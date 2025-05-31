using Licenta_v1.Data;
using Licenta_v1.Models;
using Licenta_v1.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using System.Diagnostics;
using System.Net;
using System.Text.Json.Serialization;

[ApiController]
[Route("api/[controller]")]
public class TelemetryController : ControllerBase
{
	private readonly ApplicationDbContext _db;
	private readonly RoutePlannerService _routePlanner;
	private readonly HttpClient _mlClient;

	public TelemetryController(
		ApplicationDbContext db,
		RoutePlannerService routePlanner,
		IHttpClientFactory httpFactory)
	{
		_db = db;
		_routePlanner = routePlanner;
		_mlClient = httpFactory.CreateClient("MlService");
	}

	[HttpPost]
	public async Task<IActionResult> Post([FromBody] TelemetryDto dto)
	{
		if (!ModelState.IsValid)
			return BadRequest(ModelState);

		// 1) Road context
		RoadContext roadCtx;
		try
		{
			roadCtx = await _routePlanner.GetRoadContextAsync(dto.Latitude, dto.Longitude);
		}
		catch (Exception)
		{
			return StatusCode(503, "Road-planner service failure");
		}

		// 2) Vehicle lookup
		var vehicle = await _db.Vehicles.FindAsync(dto.VehicleId);
		if (vehicle == null)
			return NotFound($"Vehicle {dto.VehicleId} not found");

		// 3) Build ML payload
		var mlPayload = new
		{
			driver_id = dto.DriverId,
			vehicle_id = dto.VehicleId,
			latitude = dto.Latitude,
			longitude = dto.Longitude,
			timestamp = dto.Timestamp,
			speed_kmh = dto.SpeedKmh,
			heading_deg = dto.HeadingDeg,
			vehicle_specs = new { weight_tons = vehicle.WeightTons },
			road_context = roadCtx
		};

		PredictionResponseDto prediction;
		try
		{
			var mlResponse = await _mlClient.PostAsJsonAsync("/predict", mlPayload);

			// READ AND PRINT RAW JSON
			var rawJson = await mlResponse.Content.ReadAsStringAsync();
			// With this corrected line:  
			Debug.WriteLine(rawJson);

			// forward 400
			if (mlResponse.StatusCode == HttpStatusCode.BadRequest)
				return BadRequest(rawJson);

			// forward any other non-success
			if (!mlResponse.IsSuccessStatusCode)
				return StatusCode((int)mlResponse.StatusCode, rawJson);

			// now bind with System.Text.Json (respects your [JsonPropertyName]s)
			prediction = await mlResponse.Content.ReadFromJsonAsync<PredictionResponseDto>()
						 ?? throw new InvalidOperationException("Empty ML response");
		}
		catch (HttpRequestException)
		{
			return StatusCode(503, "ML service unavailable");
		}

		var activeDelivery = await _db.Deliveries
			.Where(d => d.VehicleId == dto.VehicleId && d.Status == "In Progress")
			.FirstOrDefaultAsync();

		// 4) Persist event
		var evt = new AggressiveEvent
		{
			DriverId = dto.DriverId,
			VehicleId = dto.VehicleId,
			Timestamp = dto.Timestamp,
			EventType = prediction.PredictedEvent,
			SeverityScore = prediction.AggressiveScore,
			Probabilities = prediction.Proba?.ToArray(),
			Latitude = dto.Latitude,
			Longitude = dto.Longitude,
			RoadContextJson = JsonConvert.SerializeObject(roadCtx),
			DeliveryId = activeDelivery?.Id
		};

		try
		{
			_db.AggressiveEvents.Add(evt);
			await _db.SaveChangesAsync();
		}
		catch (DbUpdateException dbEx)
		{
			// include the SQL-side error if any
			var detail = dbEx.InnerException?.Message ?? dbEx.Message;
			return StatusCode(500, $"DB save failed: {detail}");
		}

		// 5) Return the saved entity
		return Ok(evt);
	}

	private class PredictionResponseDto
	{
		[JsonPropertyName("predicted_event")]
		public string PredictedEvent { get; set; } = default!;

		[JsonPropertyName("aggressive_score")]
		public double AggressiveScore { get; set; }

		[JsonPropertyName("proba")]
		public List<double>? Proba { get; set; }
	}
}

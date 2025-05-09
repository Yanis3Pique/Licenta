using Licenta_v1.Data;
using Licenta_v1.Models;
using Licenta_v1.Services;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;

[ApiController]
[Route("api/[controller]")]
public class TelemetryController : ControllerBase
{
	private readonly ApplicationDbContext _db;
	private readonly RoutePlannerService _routePlanner;
	private readonly HttpClient _httpClient;

	public TelemetryController(
		ApplicationDbContext db,
		RoutePlannerService routePlanner,
		IHttpClientFactory httpFactory)
	{
		_db = db;
		_routePlanner = routePlanner;
		_httpClient = httpFactory.CreateClient("MlService");
	}

	[HttpPost]
	public async Task<IActionResult> Post([FromBody] TelemetryDto dto)
	{
		if (!ModelState.IsValid)
			return BadRequest(ModelState);

		var roadCtx = await _routePlanner.GetRoadContextAsync(dto.Latitude, dto.Longitude);

		var vehicle = await _db.Vehicles.FindAsync(dto.VehicleId);
		if (vehicle == null) return NotFound("Vehicle not found");

		var aiPayload = new
		{
			driverId = dto.DriverId,
			vehicleId = dto.VehicleId,
			timestamp = dto.Timestamp,
			location = new { lat = dto.Latitude, lng = dto.Longitude },
			speedKmh = dto.SpeedKmh,
			headingDeg = dto.HeadingDeg,
			vehicleSpecs = new
			{
				weightTons = vehicle.WeightTons
			},
			roadContext = roadCtx
		};

		var resp = await _httpClient.PostAsJsonAsync("/detect", aiPayload);
		if (!resp.IsSuccessStatusCode)
			return StatusCode((int)resp.StatusCode, await resp.Content.ReadAsStringAsync());

		var aiResult = await resp.Content.ReadFromJsonAsync<DetectResponse>();

		var evt = new AggressiveEvent
		{
			DriverId = dto.DriverId,
			VehicleId = dto.VehicleId,
			Timestamp = dto.Timestamp,
			EventType = aiResult.EventType,
			SeverityScore = aiResult.Severity,
			Latitude = dto.Latitude,
			Longitude = dto.Longitude,
			RoadContextJson = JsonConvert.SerializeObject(roadCtx)
		};
		_db.AggressiveEvents.Add(evt);
		await _db.SaveChangesAsync();

		return Ok(evt);
	}
}

public class DetectResponse
{
	public string EventType { get; set; }
	public double Severity { get; set; }
}

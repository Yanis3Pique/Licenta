using DotNetEnv;
using Licenta_v1.Models;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using System.Text;
using System.Diagnostics;

namespace Licenta_v1.Services
{
	public class RoutePlannerService
	{
		private readonly IServiceScopeFactory scopeFactory;
		private readonly string OpenRouteServiceApiKey;
		private readonly string GoogleMapsApiKey;

		public RoutePlannerService(IServiceScopeFactory serviceScopeFactory, IConfiguration configuration)
		{
			scopeFactory = serviceScopeFactory;
			OpenRouteServiceApiKey = Env.GetString("OpenRouteServiceApiKey");
			GoogleMapsApiKey = Env.GetString("Cheie_API_Google_Maps");
		}

		// Calculeaza ruta optima in functie de Delivery
		// Ma folosesc de API-ul celor de la OpenRouteService Directions
		public async Task<RouteResult> CalculateOptimalRouteAsync(Delivery delivery)
		{
			var orders = delivery.Orders?.Where(o => o.Latitude.HasValue && o.Longitude.HasValue).ToList();
			if (orders == null || orders.Count == 0)
				throw new Exception("No valid orders found for route planning.");

			// Coordonata de start este Headquarter-ul regiunii vehiculului
			Coordinate start = new Coordinate
			{
				Latitude = delivery.Vehicle.Region.Headquarters.Latitude.Value,
				Longitude = delivery.Vehicle.Region.Headquarters.Longitude.Value
			};

			// Sortez comenzile conform proprietatii DeliverySequence
			var orderedStops = delivery.Orders.OrderBy(o => o.DeliverySequence).ToList();
			var orderIds = orderedStops.Select(o => o.Id).ToList();

			// Construiesc vectorul de coordonate pentru API (formatul [longitude, latitude])
			// Incep cu HQ, apoi adaug comenzile in ordinea stabilita (DeliverySequence), apoi intorc la HQ
			var coordinates = new List<double[]>
			{
				new double[] { start.Longitude, start.Latitude }
			};
			coordinates.AddRange(orderedStops.Select(order => new double[] { order.Longitude.Value, order.Latitude.Value }));
			coordinates.Add(new double[] { start.Longitude, start.Latitude });

			Debug.WriteLine("Request Coordinates:");
			foreach (var coord in coordinates)
			{
				Debug.WriteLine($"[{coord[0]}, {coord[1]}]");
			}

			// Construiesc request-ul pentru API-ul ORS Directions
			var profile = GetVehicleProfile(delivery.Vehicle.VehicleType);

			dynamic requestBody = new
			{
				coordinates = coordinates,
				instructions = true
			};

			// Adaug proprietatea de OPTIONS pt masinile mai mari
			if (profile == "driving-hgv")
			{
				requestBody = new
				{
					coordinates = coordinates,
					instructions = true,
					options = new
					{
						profile_params = new
						{
							restrictions = new
							{
								height = delivery.Vehicle.HeightMeters ?? 4.0,
								width = delivery.Vehicle.WidthMeters ?? 2.5,
								length = delivery.Vehicle.LengthMeters ?? 12,
								weight = delivery.Vehicle.WeightTons ?? 40
							}
						}
					}
				};
			}


			var jsonBody = JsonConvert.SerializeObject(requestBody);
			var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

			using var client = new HttpClient();
			client.DefaultRequestHeaders.Add("Authorization", OpenRouteServiceApiKey);
			string url = $"https://api.openrouteservice.org/v2/directions/{profile}";

			var response = await client.PostAsync(url, content);
			var responseJson = await response.Content.ReadAsStringAsync();
			Debug.WriteLine("Response JSON: " + responseJson);

			if (!response.IsSuccessStatusCode)
			{
				throw new Exception($"Failed to retrieve route: {response.StatusCode} - {responseJson}");
			}

			// Deserializez raspunsul primit de la ORS
			var orsResponse = JsonConvert.DeserializeObject<ORSRouteResponse>(responseJson);
			if (orsResponse == null || orsResponse.routes == null || !orsResponse.routes.Any())
				throw new Exception("No route found from OpenRouteService.");

			var route = orsResponse.routes.First();
			// Decodez polyline-ul pentru a obtine coordonatele traseului
			var decodedCoordinates = DecodePolyline(route.geometry);

			// Construiesc rezultatul final
			var routeResult = new RouteResult
			{
				Coordinates = decodedCoordinates,
				Distance = route.summary.distance,
				Duration = route.summary.duration,
				Segments = route.segments?.Select(seg => new SegmentResult
				{
					Distance = seg.distance,
					Duration = seg.duration
				}).ToList(),
				OrderIds = orderIds // ordine conform DeliverySequence
			};

			return routeResult;
		}

		// Decodez un traseu(linie) intr-o lista de coordonate
		private List<Coordinate> DecodePolyline(string polyline)
		{
			var poly = new List<Coordinate>();
			int index = 0, len = polyline.Length;
			int lat = 0, lng = 0;

			while (index < len)
			{
				int b, shift = 0, result = 0;
				do
				{
					b = polyline[index++] - 63;
					result |= (b & 0x1f) << shift;
					shift += 5;
				} while (b >= 0x20);
				int dlat = ((result & 1) != 0 ? ~(result >> 1) : (result >> 1));
				lat += dlat;

				shift = 0;
				result = 0;
				do
				{
					b = polyline[index++] - 63;
					result |= (b & 0x1f) << shift;
					shift += 5;
				} while (b >= 0x20);
				int dlng = ((result & 1) != 0 ? ~(result >> 1) : (result >> 1));
				lng += dlng;

				poly.Add(new Coordinate { Latitude = lat / 1E5, Longitude = lng / 1E5 });
			}
			return poly;
		}

		private string GetVehicleProfile(VehicleType vehicleType)
		{
			switch (vehicleType)
			{
				case VehicleType.HeavyTruck:
				case VehicleType.SmallTruck:
					return "driving-hgv";
				case VehicleType.Van:
				case VehicleType.Car:
				default:
					return "driving-car";
			}
		}
	}

	// DTO pentru rezultatul traseului (folosit pentru transmiterea catre view)
	public class RouteResult
	{
		// Lista de coordonate decodificate ale traseului
		public List<Coordinate> Coordinates { get; set; }
		// Distanta totala (in metri)
		public double Distance { get; set; }
		// Durata totala (in secunde)
		public double Duration { get; set; }
		// Lista de segmente (fiecare segment reprezinta o legatura intre opriri)
		public List<SegmentResult> Segments { get; set; }
		// Lista de OrderIds, in ordinea in care sunt parcurse comenzile (pentru primele n segmente)
		public List<int> OrderIds { get; set; }
	}

	public class SegmentResult
	{
		// Distanta segmentului (in metri)
		public double Distance { get; set; }
		// Durata segmentului (in secunde)
		public double Duration { get; set; }
	}

	public class Coordinate
	{
		// Latitudine (in grade)
		public double Latitude { get; set; }
		// Longitudine (in grade)
		public double Longitude { get; set; }
	}

	// DTO-uri conform structurii raspunsului de la OpenRouteService
	public class ORSRouteResponse
	{
		public List<Route> routes { get; set; }
	}

	public class Route
	{
		public Summary summary { get; set; }
		// Geometria traseului codificata (encoded polyline)
		public string geometry { get; set; }
		// Segmentele traseului (fiecare segment corespunzator unei legaturi intre opriri)
		public List<Segment> segments { get; set; }
	}

	public class Segment
	{
		// Distanta segmentului (in metri)
		public double distance { get; set; }
		// Durata segmentului (in secunde)
		public double duration { get; set; }
	}

	public class Summary
	{
		public double distance { get; set; }
		public double duration { get; set; }
	}
}
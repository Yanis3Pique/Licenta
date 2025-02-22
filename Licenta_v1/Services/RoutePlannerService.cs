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

		// Calculeaza ruta optima in functie de Delivery.
		// Ma folosesc de API-ul celor de la OpenRouteService Directions
		public async Task<RouteResult> CalculateOptimalRouteAsync(Delivery delivery)
		{
			var orders = delivery.Orders?.Where(o => o.Latitude.HasValue && o.Longitude.HasValue)
										 .ToList();
			if (orders == null || orders.Count == 0)
				throw new Exception("No valid orders found for route planning.");

			// Coordonata de start este Headquarter-ul
			Coordinate start = new Coordinate
			{
				Latitude = delivery.Vehicle.Region.Headquarters.Latitude.Value,
				Longitude = delivery.Vehicle.Region.Headquarters.Longitude.Value
			};

			// Ordonez comenzile cat mai eficient pentru Delivery
			var orderedStops = new List<Order>();
			var remaining = new List<Order>(orders);
			var current = start;
			while (remaining.Any())
			{
				// Selectez comanda cea mai apropiata de pozitia curenta
				var nextOrder = remaining.OrderBy(o => HaversineDistance(current.Latitude, current.Longitude, o.Latitude.Value, o.Longitude.Value)).First();
				orderedStops.Add(nextOrder);
				// Actualizez pozitia curenta cu coordonatele comenzii selectate
				current = new Coordinate { Latitude = nextOrder.Latitude.Value, Longitude = nextOrder.Longitude.Value };
				remaining.Remove(nextOrder);
			}
			// Creez lista de OrderIds in ordinea in care vor fi parcurse comenzile
			var orderIds = orderedStops.Select(o => o.Id).ToList();

			// Construiesc vectorul de coordonate pentru API (in format [longitude, latitude])
			// Incep cu HQ, apoi adaug coordonatele comenzilor in ordinea stabilita, apoi ma intorc la HQ
			var coordinates = new List<double[]>();
			coordinates.Add(new double[] { start.Longitude, start.Latitude });
			foreach (var order in orderedStops)
			{
				coordinates.Add(new double[] { order.Longitude.Value, order.Latitude.Value });
			}
			coordinates.Add(new double[] { start.Longitude, start.Latitude });

			Debug.WriteLine("Request Coordinates:");
			foreach (var coord in coordinates)
			{
				Debug.WriteLine($"[{coord[0]}, {coord[1]}]");
			}

			// Construiesc request-ul (includ "instructions = true" pentru a primi segmente)
			var requestBody = new { coordinates = coordinates, instructions = true };
			var jsonBody = JsonConvert.SerializeObject(requestBody);
			var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

			using var client = new HttpClient();
			client.DefaultRequestHeaders.Add("Authorization", OpenRouteServiceApiKey);
			string url = "https://api.openrouteservice.org/v2/directions/driving-car";

			Debug.WriteLine("Request JSON: " + jsonBody);
			var response = await client.PostAsync(url, content);
			var responseJson = await response.Content.ReadAsStringAsync();
			Debug.WriteLine("Response JSON: " + responseJson);

			if (!response.IsSuccessStatusCode)
			{
				throw new Exception($"Failed to retrieve route: {response.StatusCode} - {responseJson}");
			}

			// Deserializez raspuns (ORS returneaza un traseu encodat si segmente)
			var orsResponse = JsonConvert.DeserializeObject<ORSRouteResponse>(responseJson);
			if (orsResponse == null || orsResponse.routes == null || !orsResponse.routes.Any())
				throw new Exception("No route found from OpenRouteService.");

			var route = orsResponse.routes.First();
			// Decodific polyline-ul pentru a obtine coordonatele traseului
			var decodedCoordinates = DecodePolyline(route.geometry);

			// Creez lista de segmente
			var segmentResults = new List<SegmentResult>();
			if (route.segments != null)
			{
				foreach (var seg in route.segments)
				{
					segmentResults.Add(new SegmentResult
					{
						Distance = seg.distance,
						Duration = seg.duration
					});
				}
			}

			// Construiesc rezultatul final, inclusiv OrderIds
			var routeResult = new RouteResult
			{
				Coordinates = decodedCoordinates,
				Distance = route.summary.distance,
				Duration = route.summary.duration,
				Segments = segmentResults,
				OrderIds = orderIds // order.Ids in ordinea Orders
			};

			return routeResult;
		}

		// Calculez distanta (in metri) intre doua puncte folosind formula Haversine
		private double HaversineDistance(double lat1, double lon1, double lat2, double lon2)
		{
			const double R = 6371000; // Raza medie a Pamantului in m
			double dLat = (lat2 - lat1) * Math.PI / 180;
			double dLon = (lon2 - lon1) * Math.PI / 180;
			double a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
					   Math.Cos(lat1 * Math.PI / 180) * Math.Cos(lat2 * Math.PI / 180) *
					   Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
			double c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
			return R * c;
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
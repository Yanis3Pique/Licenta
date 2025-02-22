using DotNetEnv;
using Licenta_v1.Models;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using System.Text;
using System.Diagnostics;

namespace Licenta_v1.Services
{
	public class RoutePlannerService2
	{
		private readonly IServiceScopeFactory scopeFactory;
		private readonly string GoogleMapsApiKey;

		public RoutePlannerService2(IServiceScopeFactory serviceScopeFactory, IConfiguration configuration)
		{
			scopeFactory = serviceScopeFactory;
			GoogleMapsApiKey = Env.GetString("Cheie_API_Google_Maps");
		}

		// Calculez ruta optima pentru o livrare folosind API-ul Google Directions
		// Ruta incepe si se incheie la sediu si viziteaza toate comenzile ca puncte intermediare
		// Folosesc "optimize:true" pentru a permite Google sa reordoneze opririle
		public async Task<RouteResult> CalculateOptimalRouteAsync(Delivery delivery)
		{
			// Preiau comenzile cu locatii valide
			var orders = delivery.Orders?.Where(o => o.Latitude.HasValue && o.Longitude.HasValue).ToList();
			if (orders == null || orders.Count == 0)
				throw new Exception("Nu s-au gasit comenzi valide pentru planificarea rutei.");

			// Folosc coordonatele sediului ca origine si destinatie
			Coordinate start = new Coordinate
			{
				Latitude = delivery.Vehicle.Region.Headquarters.Latitude.Value,
				Longitude = delivery.Vehicle.Region.Headquarters.Longitude.Value
			};

			string origin = $"{start.Latitude},{start.Longitude}";
			string destination = origin;

			// Construiesc sirul de waypoints folosind "optimize:true" pentru ca Google sa le reordoneze
			// Daca exista o singura comanda se poate omite flag-ul optimize
			string waypoints = "";
			if (orders.Any())
			{
				var waypointList = orders.Select(o => $"{o.Latitude.Value},{o.Longitude.Value}").ToList();
				waypoints = "optimize:true|" + string.Join("|", waypointList);
			}

			// Construiesc URL-ul cererii
			// Mai tarziu voi adauga parametri ca &departure_time=now ca sa tin cont de traficul actual
			string url = $"https://maps.googleapis.com/maps/api/directions/json?origin={origin}&destination={destination}&mode=driving";
			if (!string.IsNullOrEmpty(waypoints))
			{
				url += $"&waypoints={Uri.EscapeDataString(waypoints)}";
			}
			url += $"&key={GoogleMapsApiKey}";

			Debug.WriteLine("URL API Google Directions: " + url);

			using var client = new HttpClient();
			var response = await client.GetAsync(url);
			var responseJson = await response.Content.ReadAsStringAsync();
			Debug.WriteLine("Raspuns JSON API Google Directions: " + responseJson);

			if (!response.IsSuccessStatusCode)
			{
				throw new Exception($"Nu s-a putut prelua ruta: {response.StatusCode} - {responseJson}");
			}

			// Deserializez raspunsul de la Google
			var googleResponse = JsonConvert.DeserializeObject<GoogleDirectionsResponse>(responseJson);
			if (googleResponse == null || googleResponse.routes == null || !googleResponse.routes.Any())
				throw new Exception("Nu s-a gasit nicio ruta de la API-ul Google Directions.");

			var route = googleResponse.routes.First();

			// Adun distanta totala si durata totala din toate segmentele
			double totalDistance = 0;
			double totalDuration = 0;
			var segmentResults = new List<SegmentResult>();
			foreach (var leg in route.legs)
			{
				totalDistance += leg.distance.value;   // in metri
				totalDuration += leg.duration.value;   // in secunde
				segmentResults.Add(new SegmentResult
				{
					Distance = leg.distance.value,
					Duration = leg.duration.value
				});
			}

			// Decodez overview_polyline intr-o lista de coordonate
			var decodedCoordinates = DecodePolyline(route.overview_polyline.points);

			// Ordinea optimizata o returnez ca un array de indici corespunzatori punctelor noastre intermediare
			List<int> orderIds;
			if (route.waypoint_order != null && route.waypoint_order.Any())
			{
				// Reordonez comenzile pe baza ordinii optimizate de Google
				orderIds = route.waypoint_order.Select(index => orders[index].Id).ToList();
			}
			else
			{
				orderIds = orders.Select(o => o.Id).ToList();
			}

			// Construiesc si returnez rezultatul final al rutei
			var routeResult = new RouteResult
			{
				Coordinates = decodedCoordinates,
				Distance = totalDistance,
				Duration = totalDuration,
				Segments = segmentResults,
				OrderIds = orderIds
			};

			return routeResult;
		}

		// Calculeaza distanta (in metri) folosind formula Haversine
		private double HaversineDistance(double lat1, double lon1, double lat2, double lon2)
		{
			const double R = 6371000; // Raza medie a Pamantului in metri
			double dLat = (lat2 - lat1) * Math.PI / 180;
			double dLon = (lon2 - lon1) * Math.PI / 180;
			double a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
					   Math.Cos(lat1 * Math.PI / 180) * Math.Cos(lat2 * Math.PI / 180) *
					   Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
			double c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
			return R * c;
		}

		// Decodez un sir de polyline codificat intr-o lista de coordonate
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

	// DTO pentru rezultatul rutei transmis catre view
	public class RouteResult2
	{
		public List<Coordinate> Coordinates { get; set; }
		public double Distance { get; set; }
		public double Duration { get; set; }
		public List<SegmentResult> Segments { get; set; }
		public List<int> OrderIds { get; set; }
	}

	public class SegmentResult2
	{
		public double Distance { get; set; }
		public double Duration { get; set; }
	}

	public class Coordinate2
	{
		public double Latitude { get; set; }
		public double Longitude { get; set; }
	}

	// DTOs pentru parsarea raspunsului de la API-ul Google Directions
	public class GoogleDirectionsResponse
	{
		public List<GoogleRoute> routes { get; set; }
		public string status { get; set; }
	}

	public class GoogleRoute
	{
		public GoogleLeg[] legs { get; set; }
		public GooglePolyline overview_polyline { get; set; }
		public int[] waypoint_order { get; set; }
	}

	public class GoogleLeg
	{
		public GoogleDistance distance { get; set; }
		public GoogleDuration duration { get; set; }
	}

	public class GoogleDistance
	{
		public string text { get; set; }
		public double value { get; set; }
	}

	public class GoogleDuration
	{
		public string text { get; set; }
		public double value { get; set; }
	}

	public class GooglePolyline
	{
		public string points { get; set; }
	}
}

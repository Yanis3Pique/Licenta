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
		private readonly string OpenWeatherMapApiKey;
		private readonly string GoogleMapsApiKey;

		public RoutePlannerService(IServiceScopeFactory serviceScopeFactory, IConfiguration configuration)
		{
			scopeFactory = serviceScopeFactory;
			OpenRouteServiceApiKey = Env.GetString("OpenRouteServiceApiKey");
			OpenWeatherMapApiKey = Env.GetString("OpenWeatherMapApiKey");
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

			dynamic? profileParams = null;

			if (profile == "driving-hgv")
			{
				profileParams = new
				{
					restrictions = new
					{
						height = delivery.Vehicle.HeightMeters,
						width = delivery.Vehicle.WidthMeters,
						length = delivery.Vehicle.LengthMeters,
						weight = delivery.Vehicle.WeightTons
					}
				};
			}

			dynamic? options = null;

			if (profileParams != null)
			{
				options = new
				{
					profile_params = profileParams
				};
			}

			// Body final
			dynamic requestBody;
			if (options != null)
			{
				requestBody = new
				{
					coordinates,
					instructions = true,
					options
				};
			}
			else
			{
				requestBody = new
				{
					coordinates,
					instructions = true
				};
			}


			Debug.WriteLine(JsonConvert.SerializeObject((object)requestBody, Formatting.Indented));
			Debug.WriteLine(delivery.Vehicle.HeightMeters.ToString(), " ", delivery.Vehicle.WidthMeters, " ",
							delivery.Vehicle.LengthMeters, " ", delivery.Vehicle.WeightTons);

			var jsonBody = JsonConvert.SerializeObject(requestBody);
			var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

			using var client = new HttpClient();
			client.DefaultRequestHeaders.Add("Authorization", OpenRouteServiceApiKey);
			string url = $"https://api.openrouteservice.org/v2/directions/{profile}/geojson";

			HttpResponseMessage response = null;
			string responseJson = null;

			try
			{
				response = await client.PostAsync(url, content);
				responseJson = await response.Content.ReadAsStringAsync();

				if (!response.IsSuccessStatusCode)
					throw new Exception($"ORS failed with avoid_polygons: {response.StatusCode} - {responseJson}");
			}
			catch (Exception ex)
			{
				Debug.WriteLine($"Fallback response body: {responseJson}");
				Debug.WriteLine($"Primary ORS routing failed: {ex.Message}");
				Debug.WriteLine("Retrying without avoid_polygons...");

				// Reincerc sa calculez ruta, dar fara restrictii de evitare a zonelor periculoase
				var fallbackBody = new
				{
					coordinates,
					instructions = true,
					profile_params = profileParams
				};

				var fallbackJson = JsonConvert.SerializeObject(fallbackBody);
				var fallbackContent = new StringContent(fallbackJson, Encoding.UTF8, "application/json");

				response = await client.PostAsync(url, fallbackContent);
				responseJson = await response.Content.ReadAsStringAsync();

				if (!response.IsSuccessStatusCode)
					throw new Exception($"Fallback ORS routing also failed: {response.StatusCode} - {responseJson}");
			}

			// Deserializez raspunsul primit de la ORS (noul format GEOJSON)
			var orsResponse = JsonConvert.DeserializeObject<ORSGeoJsonResponse>(responseJson);
			if (orsResponse?.features == null || !orsResponse.features.Any())
				throw new Exception("No route found from OpenRouteService.");

			// Extrag prima ruta (de obicei e doar una)
			var feature = orsResponse.features.First();

			// Extrag rezumatul, segmentele si coordonatele rutei
			var routeSummary = feature.properties.summary;
			var routeSegments = feature.properties.segments;
			var decodedCoordinates = feature.geometry.coordinates
				.Select(coord => new Coordinate
				{
					Longitude = coord[0],
					Latitude = coord[1]
				}).ToList();

			// Incep procesarea segmentelor si aplicarea penalizarilor
			var adjustedDuration = routeSummary.duration;
			var adjustedSegments = new List<SegmentResult>();

			for (int i = 0; i < routeSegments.Count; i++)
			{
				var segment = routeSegments[i];
				var midpointIndex = (int)((decodedCoordinates.Count / routeSegments.Count) * (i + 0.5));
				midpointIndex = Math.Min(midpointIndex, decodedCoordinates.Count - 1);
				var midpoint = decodedCoordinates[midpointIndex];

				bool isDangerous = await IsSegmentWeatherDangerousAsync(midpoint);

				double penaltyFactor = isDangerous ? 1.2 : 1.0;
				var segmentDurationAdjusted = segment.duration * penaltyFactor;
				adjustedDuration += (segmentDurationAdjusted - segment.duration);

				Debug.WriteLine($"Segment #{i + 1}: originalDuration={segment.duration}s, adjustedDuration={segmentDurationAdjusted}s, penaltyApplied={(penaltyFactor != 1)}");

				adjustedSegments.Add(new SegmentResult
				{
					Distance = segment.distance,
					Duration = segmentDurationAdjusted,
					IsWeatherDangerous = isDangerous
				});
			}

			Debug.WriteLine($"Original total duration: {routeSummary.duration}s, Adjusted total duration: {adjustedDuration}s");

			var dangerousPolygons = adjustedSegments
				.Select((segment, idx) => new { segment, idx })
				.Where(x => x.segment.IsWeatherDangerous)
				.Select(x =>
				{
					var coord = decodedCoordinates[(int)((decodedCoordinates.Count / routeSegments.Count) * (x.idx + 0.5))];
					return new List<List<double[]>>
					{
						new List<double[]>
						{
							new[] { coord.Longitude - 0.002, coord.Latitude - 0.002 },
							new[] { coord.Longitude - 0.002, coord.Latitude + 0.002 },
							new[] { coord.Longitude + 0.002, coord.Latitude + 0.002 },
							new[] { coord.Longitude + 0.002, coord.Latitude - 0.002 },
							new[] { coord.Longitude - 0.002, coord.Latitude - 0.002 }
						}
					};
				}).ToList();

			// Construiesc rezultatul final ajustat
			var routeResult = new RouteResult
			{
				Coordinates = decodedCoordinates,
				Distance = routeSummary.distance,
				Duration = adjustedDuration,
				Segments = adjustedSegments,
				OrderIds = orderIds,
				DangerousPolygons = dangerousPolygons
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
			return vehicleType switch
			{
				VehicleType.HeavyTruck or VehicleType.SmallTruck => "driving-hgv",
				_ => "driving-car",
			};
		}

		private async Task<bool> IsSegmentWeatherDangerousAsync(Coordinate coord)
		{
			string url = $"https://api.openweathermap.org/data/2.5/weather?lat={coord.Latitude}&lon={coord.Longitude}&appid={OpenWeatherMapApiKey}&units=metric";

			using var client = new HttpClient();
			var response = await client.GetStringAsync(url);
			dynamic weatherData = JsonConvert.DeserializeObject(response);

			int weatherCode = weatherData.weather[0].id;
			string weatherDescription = weatherData.weather[0].description;
			double windSpeed = weatherData.wind.speed;

			Debug.WriteLine($"Weather API response for [{coord.Latitude}, {coord.Longitude}]: code={weatherCode}, desc={weatherDescription}, windSpeed={windSpeed} m/s");

			HashSet<int> dangerousCodes = new HashSet<int> {
				200,201,202,210,211,212,221,230,231,232,
				302,312,313,314,
				502,503,504,511,522,531,
				602,621,622,
				721,741,771,781,
				804, 803
			};

			bool isDangerous = dangerousCodes.Contains((int)weatherCode) || windSpeed >= 15.0;

			Debug.WriteLine($"Segment [{coord.Latitude}, {coord.Longitude}] dangerous: {isDangerous}");

			return isDangerous;
		}

		private async Task<List<List<List<double[]>>>> GetDangerousPolygonsAsync(List<Coordinate> coordinates)
		{
			var polygons = new List<List<List<double[]>>>();

			foreach (var coord in coordinates)
			{
				if (await IsSegmentWeatherDangerousAsync(coord))
				{
					var polygon = new List<List<double[]>>
					{
						new List<double[]>
						{
							new[] { coord.Longitude - 0.002, coord.Latitude - 0.002 },
							new[] { coord.Longitude - 0.002, coord.Latitude + 0.002 },
							new[] { coord.Longitude + 0.002, coord.Latitude + 0.002 },
							new[] { coord.Longitude + 0.002, coord.Latitude - 0.002 },
							new[] { coord.Longitude - 0.002, coord.Latitude - 0.002 } // inchid poligonul
						}
					};

					polygons.Add(polygon);
				}
			}

			return polygons;
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
		// Zonele cu vreme rea
		public List<List<List<double[]>>> DangerousPolygons { get; set; }
	}

	public class SegmentResult
	{
		public double Distance { get; set; }
		public double Duration { get; set; }
		public bool IsWeatherDangerous { get; set; } // Nou adaugat
	}

	public class Coordinate
	{
		// Latitudine (in grade)
		public double Latitude { get; set; }
		// Longitudine (in grade)
		public double Longitude { get; set; }
	}

	public class ORSGeoJsonResponse
	{
		public string type { get; set; }
		public List<ORSFeature> features { get; set; }
	}

	public class ORSFeature
	{
		public string type { get; set; }
		public ORSProperties properties { get; set; }
		public ORSGeometry geometry { get; set; }
	}

	public class ORSProperties
	{
		public ORSSummary summary { get; set; }
		public List<ORSSegment> segments { get; set; }
	}

	public class ORSSegment
	{
		public double distance { get; set; }
		public double duration { get; set; }
	}

	public class ORSSummary
	{
		public double distance { get; set; }
		public double duration { get; set; }
	}

	public class ORSGeometry
	{
		public List<List<double>> coordinates { get; set; }
	}
}
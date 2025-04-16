using DotNetEnv;
using Licenta_v1.Models;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using System.Text;
using System.Diagnostics;
using System.Collections.Concurrent;

namespace Licenta_v1.Services
{
	public class RoutePlannerService
	{
		private readonly IServiceScopeFactory scopeFactory;
		private readonly string OpenRouteServiceApiKey;
		private readonly WeatherService weatherService;
		private readonly TomTomObstacleService obstacleService;

		public RoutePlannerService(IServiceScopeFactory serviceScopeFactory, IConfiguration configuration)
		{
			scopeFactory = serviceScopeFactory;
			OpenRouteServiceApiKey = Env.GetString("OpenRouteServiceApiKey");
			weatherService = new WeatherService(configuration);
			obstacleService = new TomTomObstacleService();
		}

		// Calculeaza ruta optima in functie de Delivery
		// Ma folosesc de API-ul celor de la OpenRouteService Directions
		public async Task<RouteResult> CalculateOptimalRouteAsync(Delivery delivery, double? currentLat = null, double? currentLng = null)
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

			// PRIMA CERERE fara avoid_polygons (ca sa putem obtine remainingCoords)
			dynamic initialRequestBody = options != null
				? new { coordinates, instructions = true, options }
				: new { coordinates, instructions = true };

			Debug.WriteLine(JsonConvert.SerializeObject((object)initialRequestBody, Formatting.Indented));

			var initialJson = JsonConvert.SerializeObject(initialRequestBody);
			var initialContent = new StringContent(initialJson, Encoding.UTF8, "application/json");

			using var client = new HttpClient();
			client.DefaultRequestHeaders.Add("Authorization", OpenRouteServiceApiKey);
			string url = $"https://api.openrouteservice.org/v2/directions/{profile}/geojson";

			HttpResponseMessage response = await client.PostAsync(url, initialContent);
			string responseJson = await response.Content.ReadAsStringAsync();

			if (!response.IsSuccessStatusCode)
				throw new Exception($"ORS failed (initial): {response.StatusCode} - {responseJson}");

			var orsResponse = JsonConvert.DeserializeObject<ORSGeoJsonResponse>(responseJson);
			if (orsResponse?.features == null || !orsResponse.features.Any())
				throw new Exception("No route found from OpenRouteService.");

			var feature = orsResponse.features.First();
			var routeSummary = feature.properties.summary;
			var routeSegments = feature.properties.segments;
			var decodedCoordinates = feature.geometry.coordinates
				.Select(coord => new Coordinate
				{
					Longitude = coord[0],
					Latitude = coord[1]
				}).ToList();

			List<Coordinate> remainingCoords;

			if (currentLat.HasValue && currentLng.HasValue)
			{
				var currentPos = new Coordinate
				{
					Latitude = currentLat.Value,
					Longitude = currentLng.Value
				};

				// Comenzi nelivrate inca
				var remainingOrders = delivery.Orders
					.Where(o => o.Status != OrderStatus.Delivered && o.Status != OrderStatus.FailedDelivery)
					.OrderBy(o => o.DeliverySequence)
					.ToList();

				remainingCoords = new List<Coordinate> { currentPos };

				// Adaug comenzile nelivrate inca
				foreach (var order in remainingOrders)
				{
					if (order.Latitude.HasValue && order.Longitude.HasValue)
					{
						remainingCoords.Add(new Coordinate
						{
							Latitude = order.Latitude.Value,
							Longitude = order.Longitude.Value
						});
					}
				}

				// Ne intoarcem la depozit mereu
				remainingCoords.Add(new Coordinate
				{
					Latitude = delivery.Vehicle.Region.Headquarters.Latitude.Value,
					Longitude = delivery.Vehicle.Region.Headquarters.Longitude.Value
				});
			}
			else
			{
				remainingCoords = decodedCoordinates;
			}

			object? avoidPolygons = null;
			List<AvoidPolygonWithInfo> hardPolygons = new();
			List<AvoidPolygonWithInfo> softPolygons = new();

			try
			{
				var obstacleBounds = GetRegionBoundsFromOrders(remainingCoords);
				(hardPolygons, softPolygons) = await obstacleService.GetAvoidPolygonsSeparatedAsync(
					obstacleBounds.MinLat, obstacleBounds.MinLng, obstacleBounds.MaxLat, obstacleBounds.MaxLng
				);

				avoidPolygons = new
				{
					type = "MultiPolygon",
					coordinates = hardPolygons.Select(p => p.Coordinates).ToList(),
					descriptions = hardPolygons.Select(p => p.Description).ToList()
				};
			}
			catch (Exception ex)
			{
				Debug.WriteLine($"TomTomObstacleService failed: {ex.Message}");
			}

			// A DOUA CERERE cu avoid_polygons
			if (options == null)
			{
				options = new { avoid_polygons = avoidPolygons };
			}
			else
			{
				// Convertesc options in dictionar si adaug avoid_polygons
				var optionsDict = JsonConvert.DeserializeObject<Dictionary<string, object>>(JsonConvert.SerializeObject(options));
				optionsDict["avoid_polygons"] = avoidPolygons;
				options = optionsDict;
			}

			Debug.WriteLine("Avoid polygons JSON:");
			Debug.WriteLine(JsonConvert.SerializeObject(avoidPolygons, Formatting.Indented));

			var finalRequestBody = new
			{
				coordinates,
				instructions = true,
				options
			};

			Debug.WriteLine(JsonConvert.SerializeObject((object)finalRequestBody, Formatting.Indented));

			var finalJson = JsonConvert.SerializeObject(finalRequestBody);
			var finalContent = new StringContent(finalJson, Encoding.UTF8, "application/json");

			try
			{
				response = await client.PostAsync(url, finalContent);
				responseJson = await response.Content.ReadAsStringAsync();

				if (!response.IsSuccessStatusCode)
					throw new Exception($"ORS failed (final): {response.StatusCode} - {responseJson}");

				// Actualizez coordonatele decodificate
				orsResponse = JsonConvert.DeserializeObject<ORSGeoJsonResponse>(responseJson);
				if (orsResponse?.features == null || !orsResponse.features.Any())
					throw new Exception("No route found from OpenRouteService (final).");

				feature = orsResponse.features.First();
				routeSummary = feature.properties.summary;
				routeSegments = feature.properties.segments;
				decodedCoordinates = feature.geometry.coordinates
					.Select(coord => new Coordinate
					{
						Longitude = coord[0],
						Latitude = coord[1]
					}).ToList();
			}
			catch (Exception ex)
			{
				Debug.WriteLine($"Full route with polygons failed. Switching to segment-by-segment fallback... {ex.Message}");

				List<Coordinate> combinedCoords = new();
				double totalDistance = 0;
				double totalDuration = 0;
				var segments = new List<ORSSegment>();

				for (int i = 0; i < coordinates.Count - 1; i++)
				{
					var segmentCoords = new List<double[]> { coordinates[i], coordinates[i + 1] };

					var segmentRequest = new
					{
						coordinates = segmentCoords,
						instructions = false,
						options
					};

					var segmentJson = JsonConvert.SerializeObject(segmentRequest);
					var segmentContent = new StringContent(segmentJson, Encoding.UTF8, "application/json");

					var segmentResp = await client.PostAsync(url, segmentContent);
					var segmentRespJson = await segmentResp.Content.ReadAsStringAsync();

					if (!segmentResp.IsSuccessStatusCode)
					{
						Debug.WriteLine($"Segment {i} failed. Retrying without avoid_polygons...");

						// Reincerc fara poligoanele de evitat
						var fallbackRequest = new { coordinates = segmentCoords, instructions = false };
						var fallbackJson = JsonConvert.SerializeObject(fallbackRequest);
						var fallbackContent = new StringContent(fallbackJson, Encoding.UTF8, "application/json");

						segmentResp = await client.PostAsync(url, fallbackContent);
						segmentRespJson = await segmentResp.Content.ReadAsStringAsync();

						if (!segmentResp.IsSuccessStatusCode)
							throw new Exception($"Segment {i} failed even without avoid_polygons: {segmentRespJson}");
					}

					var segResponse = JsonConvert.DeserializeObject<ORSGeoJsonResponse>(segmentRespJson);
					var segFeature = segResponse?.features?.FirstOrDefault();

					if (segFeature == null)
						throw new Exception($"Segment {i} response invalid.");

					var segCoords = segFeature.geometry.coordinates.Select(c => new Coordinate { Longitude = c[0], Latitude = c[1] }).ToList();
					if (combinedCoords.Count > 0 && combinedCoords.Last().Equals(segCoords.First()))
						segCoords.RemoveAt(0); // Pentru a evita duplicarea

					combinedCoords.AddRange(segCoords);
					totalDistance += segFeature.properties.summary.distance;
					totalDuration += segFeature.properties.summary.duration;

					segments.AddRange(segFeature.properties.segments);
				}

				decodedCoordinates = combinedCoords;
				routeSummary = new ORSSummary { distance = totalDistance, duration = totalDuration };
				routeSegments = segments;

				Debug.WriteLine("Segment-by-segment fallback succeeded.");
			}

			// Actualizez coordonatele decodificate
			orsResponse = JsonConvert.DeserializeObject<ORSGeoJsonResponse>(responseJson);
			if (orsResponse?.features == null || !orsResponse.features.Any())
				throw new Exception("No route found from OpenRouteService (final).");

			feature = orsResponse.features.First();
			routeSummary = feature.properties.summary;
			routeSegments = feature.properties.segments;
			decodedCoordinates = feature.geometry.coordinates
				.Select(coord => new Coordinate
				{
					Longitude = coord[0],
					Latitude = coord[1]
				}).ToList();

			// Incep procesarea segmentelor si aplicarea penalizarilor
			var segmentMidpoints = routeSegments.Select((segment, i) =>
			{
				double ratio = (double)decodedCoordinates.Count / routeSegments.Count;
				int midpointIndex = (int)(ratio * (i + 0.5));
				midpointIndex = Math.Min(midpointIndex, decodedCoordinates.Count - 1);
				return decodedCoordinates[midpointIndex];
			}).ToList();

			var segmentWeatherTasks = segmentMidpoints.Select(midpoint => weatherService.AnalyzeWeatherAsync(midpoint));
			var analysisResults = await Task.WhenAll(segmentWeatherTasks);

			var isDangerousResults = analysisResults.Select(r => r.isDangerous).ToArray();
			var severityResults = analysisResults.Select(r => r.severity).ToArray();
			var descriptionResults = analysisResults.Select(r => r.description).ToArray();

			var codeResults = analysisResults.Select(r => r.code).ToArray();

			var adjustedSegments = new List<SegmentResult>();
			var adjustedDuration = routeSummary.duration;

			for (int i = 0; i < routeSegments.Count; i++)
			{
				var segment = routeSegments[i];
				bool isDangerous = isDangerousResults[i];
				double penaltyFactor = isDangerous ? 1.2 : 1.0;
				double segmentDurationAdjusted = segment.duration * penaltyFactor;

				var softZones = softPolygons.Select(p => new NetTopologySuite.Geometries.Polygon(new NetTopologySuite.Geometries.LinearRing(
					p.Coordinates.First().Select(c => new NetTopologySuite.Geometries.Coordinate(c[0], c[1])).ToArray()
				))).ToList();

				var segLine = new NetTopologySuite.Geometries.LineString(new[]
				{
					new NetTopologySuite.Geometries.Coordinate(segmentMidpoints[i].Longitude, segmentMidpoints[i].Latitude),
					new NetTopologySuite.Geometries.Coordinate(segmentMidpoints[i].Longitude + 0.00001, segmentMidpoints[i].Latitude + 0.00001)
				});

				bool isInsideSoft = softZones.Any(p => p.Intersects(segLine));

				if (isInsideSoft)
				{
					segmentDurationAdjusted *= 1.2;
					Debug.WriteLine($"Segment {i} intersects soft zone → time boosted.");
				}

				adjustedDuration += (segmentDurationAdjusted - segment.duration);

				adjustedSegments.Add(new SegmentResult
				{
					Distance = segment.distance,
					Duration = segmentDurationAdjusted,
					IsWeatherDangerous = isDangerous
				});
			}

			Debug.WriteLine($"Original total duration: {routeSummary.duration}s, Adjusted total duration: {adjustedDuration}s");

			var regionCoordinates = decodedCoordinates; // HQ + comenzi
			var bounds = GetRegionBoundsFromOrders(regionCoordinates);
			double step = 0.03;
			var gridPoints = GetGridPointsNearRoute(decodedCoordinates, step: step, distanceThresholdMeters: 200);

			var weatherSeverities = await GetWeatherSeveritiesAsync(gridPoints);

			var polygonHalfSize = step / 2;
			var dangerZonesNts = weatherSeverities.Select(ws =>
			{
				double lng = ws.coord.Longitude;
				double lat = ws.coord.Latitude;

				var poly = new[]
				{
						new NetTopologySuite.Geometries.Coordinate(lng - polygonHalfSize, lat - polygonHalfSize),
						new NetTopologySuite.Geometries.Coordinate(lng - polygonHalfSize, lat + polygonHalfSize),
						new NetTopologySuite.Geometries.Coordinate(lng + polygonHalfSize, lat + polygonHalfSize),
						new NetTopologySuite.Geometries.Coordinate(lng + polygonHalfSize, lat - polygonHalfSize),
						new NetTopologySuite.Geometries.Coordinate(lng - polygonHalfSize, lat - polygonHalfSize)
					};

				return (polygon: new NetTopologySuite.Geometries.Polygon(new NetTopologySuite.Geometries.LinearRing(poly)), severity: ws.severity);
			}).ToList();

			var coloredSegments = new List<ColoredSegment>();
			for (int i = 0; i < decodedCoordinates.Count - 1; i++)
			{
				var a = decodedCoordinates[i];
				var b = decodedCoordinates[i + 1];

				var line = new NetTopologySuite.Geometries.LineString(new[]
				{
					new NetTopologySuite.Geometries.Coordinate(a.Longitude, a.Latitude),
					new NetTopologySuite.Geometries.Coordinate(b.Longitude, b.Latitude)
				});

				double maxSeverity = 0.0;

				foreach (var (polygon, severity) in dangerZonesNts)
				{
					if (polygon.Intersects(line))
					{
						maxSeverity = Math.Max(maxSeverity, severity);
					}
				}

				//double mockSeverity = 0.55;

				string description = descriptionResults[Math.Min(i, descriptionResults.Length - 1)];
				int code = codeResults[Math.Min(i, codeResults.Length - 1)];

				coloredSegments.Add(new ColoredSegment
				{
					Coordinates = new List<double[]>
					{
						new double[] { a.Latitude, a.Longitude },
						new double[] { b.Latitude, b.Longitude }
					},
					Severity = maxSeverity,
					//Severity = mockSeverity,
					WeatherDescription = description,
					WeatherCode = code
				});
			}

			// Construiesc rezultatul final ajustat
			var avoidPolygonJson = JsonConvert.SerializeObject(avoidPolygons);
			var avoidPolygonJObj = JsonConvert.DeserializeObject<Newtonsoft.Json.Linq.JObject>(avoidPolygonJson);

			var descriptions = avoidPolygonJObj?["descriptions"]?.ToObject<List<string>>() ?? new List<string>();

			var routeResult = new RouteResult
			{
				Coordinates = decodedCoordinates,
				Distance = routeSummary.distance,
				Duration = adjustedDuration,
				Segments = adjustedSegments,
				OrderIds = orderIds,
				ColoredRouteSegments = coloredSegments,
				AvoidPolygons = avoidPolygons is AvoidPolygonGeoJson geoJson
					? geoJson.coordinates
					: JsonConvert.DeserializeObject<AvoidPolygonGeoJson>(avoidPolygonJson)?.coordinates,
				AvoidDescriptions = descriptions
			};

			Debug.WriteLine("AvoidPolygons count: " + (routeResult.AvoidPolygons?.Count ?? 0));

			return routeResult;
		}

		private int FindClosestCoordinateIndex(List<Coordinate> coords, Coordinate target)
		{
			double MinDistance(Coordinate a, Coordinate b)
			{
				double dLat = a.Latitude - b.Latitude;
				double dLng = a.Longitude - b.Longitude;
				return dLat * dLat + dLng * dLng;
			}

			int index = 0;
			double minDist = double.MaxValue;

			for (int i = 0; i < coords.Count; i++)
			{
				double dist = MinDistance(coords[i], target);
				if (dist < minDist)
				{
					minDist = dist;
					index = i;
				}
			}

			return index;
		}

		public List<Coordinate> GetGridPointsNearRoute(List<Coordinate> route, double step, double distanceThresholdMeters)
		{
			var bounds = GetRegionBoundsFromOrders(route);
			var allGridPoints = GenerateGrid(bounds.MinLat, bounds.MaxLat, bounds.MinLng, bounds.MaxLng, step);
			var routeLine = new NetTopologySuite.Geometries.LineString(
				route.Select(c => new NetTopologySuite.Geometries.Coordinate(c.Longitude, c.Latitude)).ToArray()
			);

			var closePoints = new List<Coordinate>();

			foreach (var point in allGridPoints)
			{
				var pt = new NetTopologySuite.Geometries.Point(point.Longitude, point.Latitude);
				if (pt.IsWithinDistance(routeLine, distanceThresholdMeters / 111_139.0))
				{
					closePoints.Add(point);
				}
			}

			return closePoints;
		}

		private string GetVehicleProfile(VehicleType vehicleType)
		{
			return vehicleType switch
			{
				VehicleType.HeavyTruck => "driving-hgv",
				VehicleType.SmallTruck => "driving-hgv",
				VehicleType.Van => "driving-car",
				VehicleType.Car => "driving-car",
				_ => "driving-car"
			};
		}

		public List<Coordinate> GenerateGrid(double minLat, double maxLat, double minLng, double maxLng, double step = 0.0125)
		{
			var grid = new List<Coordinate>();
			for (double lat = minLat; lat <= maxLat; lat += step)
			{
				for (double lng = minLng; lng <= maxLng; lng += step)
				{
					grid.Add(new Coordinate { Latitude = lat, Longitude = lng });
				}
			}
			return grid;
		}

		public async Task<List<(Coordinate coord, double severity)>> GetWeatherSeveritiesAsync(List<Coordinate> gridPoints)
		{
			var tasks = gridPoints.Select(async coord =>
			{
				var (isDangerous, severity, description, code) = await weatherService.AnalyzeWeatherAsync(coord);
				return (coord, severity);
			});
			return (await Task.WhenAll(tasks)).ToList();
		}

		public (double MinLat, double MaxLat, double MinLng, double MaxLng) GetRegionBoundsFromOrders(List<Coordinate> coords)
		{
			if (coords == null || !coords.Any())
				throw new ArgumentException("Lista de coordonate e goală.");

			double minLat = coords.Min(c => c.Latitude);
			double maxLat = coords.Max(c => c.Latitude);
			double minLng = coords.Min(c => c.Longitude);
			double maxLng = coords.Max(c => c.Longitude);

			// Adaug un mic buffer pentru siguranță
			double buffer = 0.05;

			return (
				MinLat: minLat - buffer,
				MaxLat: maxLat + buffer,
				MinLng: minLng - buffer,
				MaxLng: maxLng + buffer
			);
		}
	}

	public class AvoidPolygonGeoJson
	{
		public string type { get; set; } = "MultiPolygon";
		public List<List<List<double[]>>> coordinates { get; set; }
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
		// Culorile pentru segmentele de ruta
		public List<ColoredSegment> ColoredRouteSegments { get; set; }
		// Poligoanele de evitat (obstacole)
		public List<List<List<double[]>>>? AvoidPolygons { get; set; }
		// Descrierea fiecarui poligon de evitat
		public List<string>? AvoidDescriptions { get; set; }
	}

	public class ColoredSegment
	{
		public List<double[]> Coordinates { get; set; }
		public double Severity { get; set; }
		public string WeatherDescription { get; set; }
		public int WeatherCode { get; set; }
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
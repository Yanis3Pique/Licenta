using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Licenta_v1.Data;
using Licenta_v1.Models;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using System.Text;
using System.Diagnostics;
using Google.OrTools.ConstraintSolver;
using Humanizer;
using System.Globalization;
using System.Net;
using System.Collections.Concurrent;

namespace Licenta_v1.Services
{
	public class OrderDeliveryOptimizer2
	{
		private readonly IServiceScopeFactory scopeFactory; // Ca sa creez mai multe instante de DbContext
		private readonly string OpenRouteServiceApiKey;
		private readonly string PtvApiKey;
		private readonly string PtVApiKeyReserve;
		private readonly string PtVApiKeyEmergency;
		private string CurrentPtvApiKey;
		private const int MaxWorkingSeconds = 6 * 3600; // 6 ore
		private const int ServiceTimePerOrderSeconds = 300; // 5 minute pe comanda

		// Semafor pentru a limita apelurile Directions ORS API
		private static SemaphoreSlim orsSemaphore = new SemaphoreSlim(5);

		// Cache-uri pentru a evita apelurile multiple pentru aceeasi comanda si vehicul
		private static ConcurrentDictionary<(int orderId, int vehicleId), bool> ptvCheckCache = new();
		private static ConcurrentDictionary<(int orderId, int vehicleId), bool> orsCheckCache = new();

		public OrderDeliveryOptimizer2(IServiceScopeFactory serviceScopeFactory, string openRouteServiceApiKey, string ptvApiKey, string ptVApiKeyReserve, string ptVApiKeyEmergency)
		{
			scopeFactory = serviceScopeFactory;
			OpenRouteServiceApiKey = openRouteServiceApiKey;
			PtvApiKey = ptvApiKey;
			PtVApiKeyReserve = ptVApiKeyReserve;
			PtVApiKeyEmergency = ptVApiKeyEmergency;
			CurrentPtvApiKey = ptvApiKey; // Default
		}

		public async Task UpdateOrderRestrictionsForHeavyVehicles(int? userRegionId = null)
		{
			using var scope = scopeFactory.CreateScope();
			var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

			// Iau comenzile care sunt inca neasignate
			var orders = db.Orders
				.Where(o => o.Status == OrderStatus.Placed &&
							o.DeliveryId == null &&
							(!userRegionId.HasValue || o.RegionId == userRegionId.Value))
				.ToList();

			// Procesez comenzile pe regiuni
			var groupedOrders = orders.GroupBy(o => o.RegionId ?? 0);
			foreach (var regionGroup in groupedOrders)
			{
				int regionId = regionGroup.Key;
				var regionOrders = regionGroup.ToList();

				// Headquarter-ul pe regiunea respectiva
				var headquarter = db.Headquarters.FirstOrDefault(hq => hq.RegionId == regionId);
				if (headquarter == null)
				{
					Debug.WriteLine($"Skipping Region {regionId} - No headquarter found.");
					continue;
				}

				var depot = new Depot2
				{
					Latitude = headquarter.Latitude ?? 0,
					Longitude = headquarter.Longitude ?? 0
				};

				// Masinile mari din regiunea respectiva
				var heavyVehicles = db.Vehicles
					.Where(v => v.RegionId == regionId &&
								v.Status == VehicleStatus.Available &&
								(v.VehicleType == VehicleType.HeavyTruck || v.VehicleType == VehicleType.SmallTruck))
					.ToList();

				Debug.WriteLine($"Region {regionId}: Processing {regionOrders.Count} orders with {heavyVehicles.Count} heavy vehicles.");

				var modifiedOrders = await PreValidateHeavyVehicleAccessAsync(regionOrders, depot, heavyVehicles);

				foreach (var order in modifiedOrders)
				{
					var existingOrder = await db.Orders.FindAsync(order.Id);
					if (existingOrder != null && existingOrder.HeavyVehicleRestricted != order.HeavyVehicleRestricted)
					{
						existingOrder.HeavyVehicleRestricted = order.HeavyVehicleRestricted;
						db.Entry(existingOrder).Property(o => o.HeavyVehicleRestricted).IsModified = true;
					}
				}
				await db.SaveChangesAsync();
			}
		}

		// Metoda principala, apelata o data pe zi de catre Admin pentru toate regiunile/Dispecer pentru regiunea sa
		public async Task RunDailyOptimization(int? userRegionId = null)
		{
			using var scope = scopeFactory.CreateScope();
			var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

			// Verific daca sunt vehicule disponibile
			var vehicles = db.Vehicles
				.Where(v => (!userRegionId.HasValue || v.RegionId == userRegionId.Value) &&
							v.Status == VehicleStatus.Available)
				.ToList();

			if (!vehicles.Any())
			{
				Debug.WriteLine("Exiting optimization: No available vehicles.");
				return;
			}

			// Actualizez comenzile in functie de vehiculele grele din regiuni
			await UpdateOrderRestrictionsForHeavyVehicles(userRegionId);

			// Iau comenzile care sunt inca neasignate
			var orders = db.Orders
				.Where(o => o.Status == OrderStatus.Placed &&
							o.DeliveryId == null &&
							(!userRegionId.HasValue || o.RegionId == userRegionId.Value))
				.ToList();

			if (!orders.Any())
			{
				Debug.WriteLine("Exiting optimization: No orders available.");
				return;
			}

			Debug.WriteLine($"Pre-flight check passed: {orders.Count} orders and {vehicles.Count} vehicles found.");

			// Grupez comenzile pe regiuni si optimizez livrarile
			var groupedOrders = orders.GroupBy(o => o.RegionId ?? 0);
			foreach (var regionGroup in groupedOrders)
			{
				int regionId = regionGroup.Key;
				var regionOrders = regionGroup.ToList();
				var regionVehicles = db.Vehicles
					.Where(v => v.RegionId == regionId && v.Status == VehicleStatus.Available)
					.ToList();

				if (!regionOrders.Any() || !regionVehicles.Any())
				{
					Debug.WriteLine($"Region {regionId}: Exiting optimization – missing orders or vehicles.");
					continue;
				}

				Debug.WriteLine($"Region {regionId}: Starting optimization with {regionOrders.Count} orders and {regionVehicles.Count} vehicles.");
				await OptimizeRegionDeliveries(regionId, regionOrders);
			}
		}

		// Optimizez Deliveries pentru o regiune specifica
		private async Task OptimizeRegionDeliveries(int regionId, List<Order> orders)
		{
			using var scope = scopeFactory.CreateScope();
			var tomorrow = DateTime.Now.AddDays(1).Date;
			var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

			// Iau Headquarter-ul regiunii respective
			var headquarter = db.Headquarters.FirstOrDefault(hq => hq.RegionId == regionId);
			if (headquarter == null)
			{
				Debug.WriteLine($"Skipping Region {regionId} - No headquarter found for this region.");
				return;
			}

			var depot = new Depot2
			{
				Latitude = headquarter.Latitude ?? 0,
				Longitude = headquarter.Longitude ?? 0
			};

			// Iau vehiculele disponibile din regiunea respectiva care nu sunt in mentenanta si nu au rute planificate pentru maine
			var vehicles = db.Vehicles
				.Where(v => v.RegionId == regionId &&
							v.Status == VehicleStatus.Available &&
							!db.Maintenances.Any(m => m.VehicleId == v.Id && m.ScheduledDate.Date == tomorrow))
				.OrderBy(v => v.FuelType == FuelType.Electric ? 0 : 1)
				.ThenBy(v => v.MaxWeightCapacity)
				.ThenBy(v => v.MaxVolumeCapacity)
				.ToList();

			var heavyVehicles = vehicles.Where(v => v.VehicleType == VehicleType.HeavyTruck || v.VehicleType == VehicleType.SmallTruck).ToList();
			var standardVehicles = vehicles.Where(v => v.VehicleType == VehicleType.Car || v.VehicleType == VehicleType.Van).ToList();

			var usedVehicleIds = new HashSet<int>();

			// Mai intai incerc sa optimizez comenzile cu masinile standard
			var lightVehicleCandidateOrders = orders
				.OrderBy(o => o.PlacedDate)
				.ThenBy(o => o.Priority == OrderPriority.High ? 0 : 1)
				.ToList();

			var standardRoutes = new List<RouteResult>();

			if (lightVehicleCandidateOrders.Any() && standardVehicles.Any())
			{
				var routes = await OptimizeRoutesWithORSForCluster(lightVehicleCandidateOrders, standardVehicles, depot);
				standardRoutes.AddRange(routes);

				foreach (var route in standardRoutes)
				{
					await AssignOrdersToDeliveriesAsync(route.Orders, standardVehicles, usedVehicleIds, depot, route.VehicleId);
				}
			}

			// Apoi incerc sa optimizez comenzile ramase(in functie de restrictii) cu masinile grele
			var deliveredOrderIds = standardRoutes.SelectMany(r => r.Orders.Select(o => o.Id)).ToHashSet();
			var remainingOrders = orders
				.Where(o => !deliveredOrderIds.Contains(o.Id))
				.OrderBy(o => o.PlacedDate)
				.ThenBy(o => o.Priority == OrderPriority.High ? 0 : 1)
				.ToList();

			var heavyAllowedOrders = remainingOrders
				.Where(o => !o.HeavyVehicleRestricted && !o.IsHeavyVehicleRestricted)
				.ToList();

			var heavyRoutes = new List<RouteResult>();

			if (heavyAllowedOrders.Any() && heavyVehicles.Any())
			{
				var routes = await OptimizeRoutesWithORSForCluster(heavyAllowedOrders, heavyVehicles, depot);
				heavyRoutes.AddRange(routes);

				foreach (var route in heavyRoutes)
				{
					await AssignOrdersToDeliveriesAsync(route.Orders, heavyVehicles, usedVehicleIds, depot, route.VehicleId);
				}
			}

			Debug.WriteLine($"Region {regionId} optimization complete: {standardRoutes.Count} standard vehicle routes and {heavyRoutes.Count} heavy vehicle routes created.");
		}

		// Verifica daca durata rutei + timpul de servire al comenzilor este mai mic decat limita de timp
		private bool IsRouteWithinTimeLimit(int routeDurationSeconds, int numberOfOrders)
		{
			int totalServiceTime = numberOfOrders * ServiceTimePerOrderSeconds;
			return (routeDurationSeconds + totalServiceTime) <= MaxWorkingSeconds;
		}

		private async Task<HttpResponseMessage> SendApiRequestWithRetriesAsync(HttpClient client, string requestUrl, HttpContent content, int maxRetries = 10)
		{
			int retry = 0;
			int delay = 1000; // delay initial de 1 secunda
			HttpResponseMessage response = null;

			while (retry < maxRetries)
			{
				await orsSemaphore.WaitAsync();
				try
				{
					response = await client.PostAsync(requestUrl, content);
				}
				finally
				{
					orsSemaphore.Release();
				}

				if (response.StatusCode == HttpStatusCode.OK)
					return response;

				string detailedResponse = await response.Content.ReadAsStringAsync();
				Debug.WriteLine($"Attempt {retry + 1}: Status {response.StatusCode}. Response: {detailedResponse}");

				if (response.StatusCode == (HttpStatusCode)429)
				{
					Debug.WriteLine($"429 Rate limit reached for ORS. Waiting 64 seconds before retry.");
					await Task.Delay(TimeSpan.FromSeconds(64)); // limita ORS-ului de 64 secunde pt retry
					retry++;
					continue;
				}

				await Task.Delay(delay);
				delay *= 2;
				retry++;
			}

			throw new Exception("Maximum retry attempts exceeded due to API rate limiting.");
		}

		// Metoda care verifica accesul vehiculelor grele la comenzi si actualizeaza statusul comenzilor
		private async Task<List<Order>> PreValidateHeavyVehicleAccessAsync(List<Order> orders, Depot2 depot, List<Vehicle> heavyVehicles)
		{
			var updatedOrders = new List<Order>();

			foreach (var order in orders)
			{
				if (order.IsHeavyVehicleRestricted)
				{
					order.HeavyVehicleRestricted = true;
					Debug.WriteLine($"[AUTO-RESTRICTED] Order {order.Id} manually restricted.");
				}
			}

			int batchSize = 5;
			foreach (var heavyVehicle in heavyVehicles)
			{
				var ordersToCheck = orders
					.Where(o => !o.HeavyVehicleRestricted)
					.ToList();

				if (!ordersToCheck.Any())
				{
					Debug.WriteLine($"[NO CHECKS] All orders are already restricted before checking with vehicle {heavyVehicle.Id}.");
					continue;
				}

				for (int i = 0; i < ordersToCheck.Count; i += batchSize)
				{
					var batch = ordersToCheck.Skip(i).Take(batchSize).ToList();

					Debug.WriteLine($"[BATCH CHECK] Checking orders {string.Join(", ", batch.Select(o => o.Id))} with Vehicle {heavyVehicle.Id}");

					foreach (var order in batch)
					{
						if (!order.Latitude.HasValue || !order.Longitude.HasValue)
						{
							Debug.WriteLine($"[SKIPPED] Order {order.Id} missing coordinates.");
							continue;
						}

						if (!ptvCheckCache.TryGetValue((order.Id, heavyVehicle.Id), out _))
						{
							bool result = await IsHeavyVehicleAccessibleAsync(order, depot, heavyVehicle);
							ptvCheckCache[(order.Id, heavyVehicle.Id)] = result;
							Debug.WriteLine($"[CHECKED] Order {order.Id}, Vehicle {heavyVehicle.Id}, Accessible: {result}");
						}
						else
						{
							Debug.WriteLine($"[CACHE HIT] Order {order.Id}, Vehicle {heavyVehicle.Id}");
						}
					}

					await Task.Delay(1000);
				}
			}

			foreach (var order in orders.Where(o => !o.HeavyVehicleRestricted))
			{
				bool accessibleByAnyHeavyVehicle = heavyVehicles.Any(hv =>
					ptvCheckCache.TryGetValue((order.Id, hv.Id), out var accessible) && accessible);

				bool wasRestricted = order.HeavyVehicleRestricted;
				order.HeavyVehicleRestricted = !accessibleByAnyHeavyVehicle;

				if (order.HeavyVehicleRestricted != wasRestricted)
				{
					updatedOrders.Add(order);
				}

				Debug.WriteLine($"[FINAL STATUS] Order {order.Id}: HeavyVehicleRestricted = {order.HeavyVehicleRestricted}");
			}

			return updatedOrders;
		}

		// Fallback in caz ca toate cheile PTV sunt folosite sau daca PTV returneaza eroare
		private async Task<bool> IsHeavyVehicleAccessibleAsync(Order order, Depot2 depot, Vehicle heavyVehicle)
		{
			var cacheKey = (order.Id, heavyVehicle.Id);

			// Mai intai verific cache-ul PTV
			if (ptvCheckCache.TryGetValue(cacheKey, out bool cachedPTVResult))
			{
				Debug.WriteLine($"[PTV CACHE] Order {order.Id}, Vehicle {heavyVehicle.Id}: {cachedPTVResult}");
				return cachedPTVResult;
			}

			if (!order.Latitude.HasValue || !order.Longitude.HasValue)
			{
				Debug.WriteLine($"[SKIP] Order {order.Id} missing coordinates.");
				ptvCheckCache[cacheKey] = false;
				return false;
			}

			var baseUrl = "https://api.myptv.com/routing/v1/routes";
			var queryParams = new List<string>
			{
				$"waypoints={depot.Latitude.ToString(CultureInfo.InvariantCulture)},{depot.Longitude.ToString(CultureInfo.InvariantCulture)}",
				$"waypoints={order.Latitude.Value.ToString(CultureInfo.InvariantCulture)},{order.Longitude.Value.ToString(CultureInfo.InvariantCulture)}",
				"profile=EUR_TRAILER_TRUCK",
				"options[trafficMode]=REALISTIC",
				$"vehicle[height]={Math.Round(heavyVehicle.HeightMeters * 100)}",
				$"vehicle[width]={Math.Round(heavyVehicle.WidthMeters * 100)}",
				$"vehicle[length]={Math.Round(heavyVehicle.LengthMeters * 100)}",
				$"vehicle[totalPermittedWeight]={Math.Round(heavyVehicle.WeightTons * 1000)}"
			};

			var queryString = string.Join("&", queryParams);
			var requestUrl = $"{baseUrl}?{queryString}";

			using var client = new HttpClient();
			var apiKeys = new List<string> { PtvApiKey, PtVApiKeyReserve, PtVApiKeyEmergency };

			foreach (var apiKey in apiKeys)
			{
				Debug.WriteLine($"[PTV] Trying key: {apiKey} for Order {order.Id}, Vehicle {heavyVehicle.Id}");
				client.DefaultRequestHeaders.Clear();
				client.DefaultRequestHeaders.Add("apiKey", apiKey);

				var response = await client.GetAsync(requestUrl);
				var responseContent = await response.Content.ReadAsStringAsync();

				if (response.StatusCode == HttpStatusCode.OK)
				{
					dynamic ptvResponse = JsonConvert.DeserializeObject(responseContent);
					bool violated = ptvResponse?.violated ?? false;
					bool isAccessible = !violated;

					ptvCheckCache[cacheKey] = isAccessible;
					Debug.WriteLine($"[PTV SUCCESS] Key: {apiKey}, Order {order.Id}, Vehicle {heavyVehicle.Id}, Accessible: {isAccessible}");
					CurrentPtvApiKey = apiKey;
					return isAccessible;
				}
				else if ((response.StatusCode == HttpStatusCode.Forbidden && responseContent.Contains("GENERAL_QUOTA_EXCEEDED")) || response.StatusCode == (HttpStatusCode)429)
				{
					Debug.WriteLine($"[PTV QUOTA] Key: {apiKey}. Moving to next key.");
					continue; // Incerc urmatorea cheie
				}
				else
				{
					Debug.WriteLine($"[PTV ERROR] Key: {apiKey}, Status: {response.StatusCode}. Trying next available key.");
					continue; // In loc sa returnez eroarea, incerc urmatoarea cheie
				}
			}

			// Daca am incercat toate cheile PTV fara success, incerc cache-ul ORS sau ORS API
			if (orsCheckCache.TryGetValue(cacheKey, out bool cachedORSResult))
			{
				Debug.WriteLine($"[ORS CACHE] Order {order.Id}, Vehicle {heavyVehicle.Id}: {cachedORSResult}");
				return cachedORSResult;
			}

			Debug.WriteLine($"[FALLBACK TO ORS] All PTV keys failed for Order {order.Id}, Vehicle {heavyVehicle.Id}. Using ORS.");

			var orsRequestUrl = "https://api.openrouteservice.org/v2/directions/driving-hgv";
			var orsRequestBody = new
			{
				coordinates = new[]
				{
					new[] { depot.Longitude, depot.Latitude },
					new[] { order.Longitude.Value, order.Latitude.Value }
				},
				options = new
				{
					profile_params = new
					{
						restrictions = new
						{
							height = heavyVehicle.HeightMeters,
							width = heavyVehicle.WidthMeters,
							length = heavyVehicle.LengthMeters,
							weight = heavyVehicle.WeightTons * 1000
						}
					}
				}
			};

			string orsJsonBody = JsonConvert.SerializeObject(orsRequestBody);
			using var orsClient = new HttpClient();
			orsClient.DefaultRequestHeaders.Add("Authorization", OpenRouteServiceApiKey);
			var orsContent = new StringContent(orsJsonBody, Encoding.UTF8, "application/json");
			var orsResponse = await SendApiRequestWithRetriesAsync(orsClient, orsRequestUrl, orsContent);

			bool orsAccessible = orsResponse.IsSuccessStatusCode;
			orsCheckCache[cacheKey] = orsAccessible;
			Debug.WriteLine($"[ORS CHECK DONE] Order {order.Id}, Vehicle {heavyVehicle.Id}, Accessible: {orsAccessible}");

			return orsAccessible;
		}

		// Metoda imparte vehiculele disponibile in submultimi (de cel mult 3 vehicule)
		// si trimite iterativ cereri de optimizare pentru comenzile ramase
		private async Task<List<RouteResult>> OptimizeRoutesWithORSForCluster(List<Order> orders, List<Vehicle> vehicles, Depot2 depot)
		{
			List<RouteResult> combinedResults = new List<RouteResult>();

			// Sortez vehiculele descrescator dupa consum, greutate maxima si volum maxim admis
			var sortedVehicles = vehicles.OrderByDescending(v => v.MaxVolumeCapacity)
										 .ThenByDescending(v => v.MaxWeightCapacity)
										 .ThenByDescending(v => v.ConsumptionRate)
										 .ToList();

			// Fac o copie a comenzilor ca sa stiu ce comenzi raman neasignate
			List<Order> remainingOrders = new List<Order>(orders);

			// Cat timp mai am comenzi de asignat si vehicule disponibile
			while (remainingOrders.Any() && sortedVehicles.Any())
			{
				// Iau un subset de cel mult 3 vehicule din lista sortata
				var subset = sortedVehicles.Take(3).ToList();

				int scaleFactor = CalculateScaleFactor(remainingOrders);

				var optimizationRequest = new
				{
					jobs = remainingOrders.Select(o => new
					{
						id = o.Id,
						location = new[] { o.Longitude ?? 0.0, o.Latitude ?? 0.0 },
						amount = new[] { (int)((o.Weight ?? 0) * scaleFactor), (int)((o.Volume ?? 0) * scaleFactor) },
					}),
					vehicles = subset.Select(v => new
					{
						id = v.Id,
						profile = GetProfileForVehicle(v),
						start = new[] { depot.Longitude, depot.Latitude },
						capacity = new[] { (int)(v.MaxWeightCapacity * scaleFactor), (int)(v.MaxVolumeCapacity * scaleFactor) },
					})
				};

				string requestUrl = "https://api.openrouteservice.org/optimization";
				string jsonBody = JsonConvert.SerializeObject(optimizationRequest);

				using var client = new HttpClient();
				client.DefaultRequestHeaders.Add("Authorization", OpenRouteServiceApiKey);
				var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

				var response = await client.PostAsync(requestUrl, content);
				var responseString = await response.Content.ReadAsStringAsync();
				Debug.WriteLine($"ORS Optimization Response (subset): {responseString}");

				if (!response.IsSuccessStatusCode)
				{
					Debug.WriteLine($"ORS Optimization Error (subset): {response.StatusCode} - {responseString}");
					// Sterg vehiculele din lista si incerc cu urmatorul subset
					sortedVehicles.RemoveAll(v => subset.Contains(v));
					continue;
				}

				var optimizationResponse = JsonConvert.DeserializeObject<ORSOptimizationResponse>(responseString);
				if (optimizationResponse?.Routes != null)
				{
					bool anyRouteFound = false;
					foreach (var route in optimizationResponse.Routes)
					{
						var orderedOrders = new List<Order>();
						// Iau Orders din opririle facute de ruta
						foreach (var step in route.Steps)
						{
							if (step.Type == "job" && step.Job.HasValue)
							{
								var order = remainingOrders.FirstOrDefault(o => o.Id == step.Job.Value);
								if (order != null)
								{
									orderedOrders.Add(order);
								}
							}
						}
						if (orderedOrders.Any())
						{
							// Iau durata totala din raspunsul ORS
							int routeDurationSeconds = route.Duration;

							// Verific daca ruta se incadreaza in limita de timp
							if (IsRouteWithinTimeLimit(routeDurationSeconds, orderedOrders.Count))
							{
								combinedResults.Add(new RouteResult
								{
									VehicleId = route.Vehicle,
									Orders = orderedOrders
								});
								anyRouteFound = true;
							}
							else
							{
								// Sterg din coada pana cand ruta se incadreaza in limita de timp
								while (orderedOrders.Count > 0)
								{
									orderedOrders.RemoveAt(orderedOrders.Count - 1); // Sterg ultima comanda adaugata

									if (!orderedOrders.Any())
										break;

									// Apoi recalculez durata pentru setul de comenzi redus ca sa vad daca se incadreaza in timp
									routeDurationSeconds = await GetRouteDurationFromORS(orderedOrders, depot);

									if (IsRouteWithinTimeLimit(routeDurationSeconds, orderedOrders.Count))
									{
										combinedResults.Add(new RouteResult
										{
											VehicleId = route.Vehicle,
											Orders = orderedOrders
										});
										anyRouteFound = true;
										break;
									}
								}
							}
						}
					}
					if (anyRouteFound)
					{
						// Sterg Orders deja asignate din remainingOrders
						var assignedOrderIds = combinedResults.SelectMany(r => r.Orders.Select(o => o.Id)).Distinct().ToList();
						remainingOrders.RemoveAll(o => assignedOrderIds.Contains(o.Id));
						// Sterg vehiculele care au fost deja folosite din sortedVehicles
						var usedVehIds = combinedResults.Select(r => r.VehicleId).Distinct().ToList();
						sortedVehicles.RemoveAll(v => usedVehIds.Contains(v.Id));
					}
					else
					{
						// Daca nu avem nicio ruta, stergem subsetul din multimea subset-urilor
						sortedVehicles.RemoveAll(v => subset.Contains(v));
					}
				}
				else
				{
					// Nu avem niciun raspuns, deci scoatem vehiculele astea din lista
					sortedVehicles.RemoveAll(v => subset.Contains(v));
				}
			}
			return combinedResults;
		}

		private async Task<int> GetRouteDurationFromORS(List<Order> orders, Depot2 depot)
		{
			var coordinates = new List<double[]> { new double[] { depot.Longitude, depot.Latitude } };
			coordinates.AddRange(orders.Select(o => new double[] { o.Longitude ?? 0.0, o.Latitude ?? 0.0 }));
			coordinates.Add(new double[] { depot.Longitude, depot.Latitude });

			var directionsRequest = new { coordinates };
			string requestUrl = "https://api.openrouteservice.org/v2/directions/driving-hgv";
			string jsonBody = JsonConvert.SerializeObject(directionsRequest);

			using (var client = new HttpClient())
			{
				client.DefaultRequestHeaders.Add("Authorization", OpenRouteServiceApiKey);
				var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
				var response = await client.PostAsync(requestUrl, content);
				var responseJson = await response.Content.ReadAsStringAsync();

				if (response.IsSuccessStatusCode)
				{
					var directionsResponse = JsonConvert.DeserializeObject<ORSRouteResponse>(responseJson);
					if (directionsResponse?.routes?.Any() == true)
					{
						return (int)directionsResponse.routes[0].summary.duration;
					}
				}

				Debug.WriteLine("Failed to recalculate duration from ORS.");
				return int.MaxValue; // Daca nu am primit raspuns de la ORS, returnez un nr mare
			}
		}

		// Data Transfer Objects pt raspunsul de la optimizarea cu ORS
		public class ORSOptimizationResponse
		{
			[JsonProperty("routes")]
			public List<ORSOptimizationRoute> Routes { get; set; }
		}

		public class ORSOptimizationRoute
		{
			[JsonProperty("vehicle")]
			public int Vehicle { get; set; }

			[JsonProperty("steps")]
			public List<ORSOptimizationStep> Steps { get; set; }

			[JsonProperty("duration")]
			public int Duration { get; set; } // secunde
		}

		public class ORSOptimizationStep
		{
			[JsonProperty("type")]
			public string Type { get; set; }

			[JsonProperty("job")]
			public int? Job { get; set; }
		}

		// Obiect pt ruta: leaga vehiculele de lista ordonata de comenzi
		public class RouteResult
		{
			public int VehicleId { get; set; }
			public List<Order> Orders { get; set; }
		}

		// Pentru o ruta anume (cluster de comenzi), alegem un vehicul disponibil care poate sa transporte comenzile
		// si un sofer disponibil pentru a crea obiectul de Delivery
		private async Task AssignOrdersToDeliveriesAsync(
			List<Order> orderedOrders,
			List<Vehicle> vehicles,
			HashSet<int> usedVehicleIds,
			Depot2 depot,
			int? assignedVehicleId = null)
		{
			using var scope = scopeFactory.CreateScope();
			var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

			if (!orderedOrders.Any())
				return;

			var plannedDate = DateTime.Now.AddDays(1).Date;

			Vehicle candidateVehicle = null;
			if (assignedVehicleId.HasValue && !usedVehicleIds.Contains(assignedVehicleId.Value))
			{
				candidateVehicle = vehicles.FirstOrDefault(v =>
					v.Id == assignedVehicleId.Value &&
					CanFitOrders(v, orderedOrders));
			}

			if (candidateVehicle == null)
				return;

			// Marchez vehiculul ales ca fiind deja folosit de un Delivery
			usedVehicleIds.Add(candidateVehicle.Id);

			// Iau un sofer disponibil(descrescator dupa Ratig = performanta)
			var driverId = (from user in db.Users
							join userRole in db.UserRoles on user.Id equals userRole.UserId
							join role in db.Roles on userRole.RoleId equals role.Id
							where role.Name == "Sofer" &&
								  user.RegionId == candidateVehicle.RegionId &&
								  (user.IsAvailable ?? false)
							orderby user.AverageRating descending
							select user.Id).FirstOrDefault();

			ApplicationUser driver = null;
			if (!string.IsNullOrEmpty(driverId))
			{
				driver = db.Users.Find(driverId);
				if (driver != null)
					driver.IsAvailable = false;
			}

			string deliveryStatus = driver == null ? "Up for Taking" : "Planned";

			var delivery = new Delivery
			{
				DriverId = driver?.Id,
				VehicleId = candidateVehicle.Id,
				PlannedStartDate = plannedDate,
				Status = deliveryStatus
			};

			db.Deliveries.Add(delivery);
			db.SaveChanges();

			// Actualizez Orders
			for (int i = 0; i < orderedOrders.Count; i++)
			{
				orderedOrders[i].DeliverySequence = i; // Numar mai mic inseamna ca se va livra mai repede(primele opriri)
				orderedOrders[i].DeliveryId = delivery.Id;
				orderedOrders[i].EstimatedDeliveryDate = DateTime.Today.AddDays(1);
				db.Orders.Update(orderedOrders[i]);
			}

			candidateVehicle.Status = VehicleStatus.Busy;
			db.Vehicles.Update(candidateVehicle);

			await CalculateRouteMetrics(db, delivery, orderedOrders, candidateVehicle, depot);
			await db.SaveChangesAsync();
		}

		// Verific daca un vehicul are capacitate suficienta pentru comenzi
		private bool CanFitOrders(Vehicle vehicle, List<Order> orders)
		{
			double totalWeight = orders.Sum(o => o.Weight ?? 0);
			double totalVolume = orders.Sum(o => o.Volume ?? 0);
			return totalWeight <= vehicle.MaxWeightCapacity && totalVolume <= vehicle.MaxVolumeCapacity;
		}

		// Metoda care calculeaza datele de ruta, distanta estimata si emisiile estimate pentru un Delivery
		private async Task CalculateRouteMetrics(ApplicationDbContext db, Delivery delivery, List<Order> orders, Vehicle vehicle, Depot2 depot)
		{
			// Lista de coordonate: Headquarter, Orders in ordinea DeliverySequence, Headquarter
			var sortedOrders = orders.OrderBy(o => o.DeliverySequence).ToList();
			var locations = new List<double[]>();
			locations.Add(new double[] { depot.Longitude, depot.Latitude });
			foreach (var order in sortedOrders)
			{
				locations.Add(new double[] { order.Longitude ?? 0, order.Latitude ?? 0 });
			}
			locations.Add(new double[] { depot.Longitude, depot.Latitude });

			// Construiesc cererea pentru ORS Directions API
			var directionsRequest = new
			{
				coordinates = locations,
			};

			string profile = GetProfileForVehicle(vehicle);
			string requestUrl = $"https://api.openrouteservice.org/v2/directions/{profile}";
			string jsonBody = JsonConvert.SerializeObject(directionsRequest);

			using (var client = new HttpClient())
			{
				client.DefaultRequestHeaders.Add("Authorization", OpenRouteServiceApiKey);
				var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
				var response = await client.PostAsync(requestUrl, content);
				if (response.IsSuccessStatusCode)
				{
					var responseJson = await response.Content.ReadAsStringAsync();
					// Deserializam raspunsul
					var directionsResponse = JsonConvert.DeserializeObject<ORSRouteResponse>(responseJson);
					if (directionsResponse?.routes?.Any() == true)
					{
						var summary = directionsResponse.routes[0].summary;
						double distance = summary.distance; // metri
						double duration = summary.duration; // secunde

						// Calcularea consumului de combustibil:
						double distanceKm = distance / 1000.0;
						double fuelConsumed = (double)((vehicle.ConsumptionRate * distanceKm) / 100.0); // in litri

						// Factorul de emisie e calculat cu metoda de mai jos
						double emissionFactor = GetEmissionFactor((FuelType)vehicle.FuelType);
						double emissions = fuelConsumed * emissionFactor; // in g CO2

						DateTime baseTime = delivery.PlannedStartDate;
						// Iau comenzile sortate dupa DeliverySequence
						var sortedOrders2 = orders.OrderBy(o => o.DeliverySequence).ToList();
						// Iau durata totala a rutei si imparte-o la numarul de comenzi
						double averageInterval = (duration / 60.0) / sortedOrders2.Count; // in minute

						// Pt fiecare comanda, calculez un timp estimat cumulativ plus 5 minute in plus per comanda
						for (int i = 0; i < sortedOrders2.Count; i++)
						{
							double estimatedMinutes = i * averageInterval + i * 5; // extra 5 minute pe comanda

							// Ca sa inceapa numararea de la ora 8, cand incepe si programul de munca
							DateTime estimatedDeliveryTime = baseTime.AddMinutes(estimatedMinutes).AddHours(8);

							// Impart intervalul de livrare in 2 intervale: 8-12, 12-18
							string interval;
							if (estimatedDeliveryTime.Hour >= 8 && estimatedDeliveryTime.Hour < 12)
							{
								interval = "8-12";
							}
							else if (estimatedDeliveryTime.Hour >= 12 && estimatedDeliveryTime.Hour < 18)
							{
								interval = "12-18";
							}
							else
							{
								interval = "N/A";
							}
							sortedOrders2[i].EstimatedDeliveryInterval = interval;
							db.Orders.Update(sortedOrders2[i]);
						}

						// Actualizez Delivery
						delivery.DistanceEstimated = distanceKm; // kilometri
						delivery.EmissionsEstimated = emissions / 1000; // kilograme
						delivery.ConsumptionEstimated = fuelConsumed; // litri
						delivery.TimeTakenForDelivery = duration / 3600; // ore
						delivery.RouteData = responseJson; // salvez raspunsul complet ca JSON

						// Marchez entitatea ca modificata
						db.Deliveries.Update(delivery);
					}
				}
				else
				{
					Debug.WriteLine($"Directions API error: {response.StatusCode}");
				}
			}
		}

		public async Task RecalculateDeliveryMetrics(ApplicationDbContext db, Delivery delivery)
		{
			// Actualizez DeliverySequence pentru fiecare Order in Delivery
			await RecalculateDeliverySequence(db, delivery);

			// Recalculez estimarile de ruta pentru Delivery
			var headquarter = db.Headquarters.FirstOrDefault(hq => hq.RegionId == delivery.Vehicle.RegionId);
			if (headquarter == null)
			{
				Debug.WriteLine("No headquarter found for the delivery's region.");
				return;
			}
			var depot = new Depot2
			{
				Latitude = headquarter.Latitude ?? 0,
				Longitude = headquarter.Longitude ?? 0
			};

			await CalculateRouteMetrics(db, delivery, delivery.Orders.ToList(), delivery.Vehicle, depot);
		}

		private async Task RecalculateDeliverySequence(ApplicationDbContext db, Delivery delivery)
		{
			var headquarter = db.Headquarters.FirstOrDefault(hq => hq.RegionId == delivery.Vehicle.RegionId);
			if (headquarter == null)
			{
				Debug.WriteLine("No headquarter found for the delivery's region.");
				return;
			}
			var depot = new Depot2
			{
				Latitude = headquarter.Latitude ?? 0,
				Longitude = headquarter.Longitude ?? 0
			};

			// Fac iar cererea de optimizare pentru a obtine o noua secventa de livrare
			// Folosesc un vehicul dummy(oricum, daca am ajuns aici, e clar ca toate comenzile au loc in masina)
			var jobs = delivery.Orders.Select(o => new {
				id = o.Id,
				location = new[] { o.Longitude ?? 0.0, o.Latitude ?? 0.0 }
			}).ToList();

			var vehicleRequest = new
			{
				id = 1,
				profile = GetProfileForVehicle(delivery.Vehicle),
				start = new[] { depot.Longitude, depot.Latitude },
				capacity = new[] { int.MaxValue, int.MaxValue }
			};

			var optimizationRequest = new
			{
				jobs = jobs,
				vehicles = new[] { vehicleRequest }
			};

			string requestUrl = "https://api.openrouteservice.org/optimization";
			string jsonBody = JsonConvert.SerializeObject(optimizationRequest);

			using (var client = new HttpClient())
			{
				client.DefaultRequestHeaders.Add("Authorization", OpenRouteServiceApiKey);
				var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
				var response = await client.PostAsync(requestUrl, content);
				if (response.IsSuccessStatusCode)
				{
					var responseString = await response.Content.ReadAsStringAsync();
					var optimizationResponse = JsonConvert.DeserializeObject<ORSOptimizationResponse>(responseString);
					if (optimizationResponse?.Routes != null && optimizationResponse.Routes.Any())
					{
						// Iau id-urile comenzilor din secventa optimizata
						var route = optimizationResponse.Routes.First();
						var optimizedOrderIds = new List<int>();
						foreach (var step in route.Steps)
						{
							if (step.Type == "job" && step.Job.HasValue)
							{
								optimizedOrderIds.Add(step.Job.Value);
							}
						}
						// Actualizez fiecare Order cu DeliverySequence-ul sau in functie de pozitia in lista optimizata
						var optimizedOrders = delivery.Orders
							.OrderBy(o => optimizedOrderIds.IndexOf(o.Id))
							.ToList();
						for (int i = 0; i < optimizedOrders.Count; i++)
						{
							optimizedOrders[i].DeliverySequence = i;
						}
					}
					else
					{
						Debug.WriteLine("No route found in the optimization response.");
					}
				}
				else
				{
					Debug.WriteLine($"Optimization API error: {response.StatusCode}");
				}
			}
		}

		// Metoda helper pentru factorul de emisie
		private double GetEmissionFactor(FuelType fuelType)
		{
			switch (fuelType)
			{
				case FuelType.Petrol:
					return 2392; // 2392 g CO2/litru
				case FuelType.Diesel:
					return 2640; // 2640 g CO2/litru
				case FuelType.Electric:
					return 0;
				case FuelType.Hybrid:
					return 1250; // medie/2
				default:
					return 2500; // medie
			}
		}

		private int CalculateScaleFactor(List<Order> orders)
		{
			int maxDecimals = 0;
			foreach (var order in orders)
			{
				maxDecimals = Math.Max(maxDecimals, GetDecimalPlaces(order.Weight ?? 0));
				maxDecimals = Math.Max(maxDecimals, GetDecimalPlaces(order.Volume ?? 0));
			}
			return (int)Math.Pow(10, maxDecimals);
		}

		private int GetDecimalPlaces(double value)
		{
			string s = value.ToString(System.Globalization.CultureInfo.InvariantCulture);
			int index = s.IndexOf('.');
			if (index >= 0)
			{
				return s.Length - index - 1;
			}
			return 0;
		}

		private string GetProfileForVehicle(Vehicle vehicle)
		{
			return (vehicle.VehicleType == VehicleType.HeavyTruck || vehicle.VehicleType == VehicleType.SmallTruck)
				? "driving-hgv"
				: "driving-car";
		}

		public void InvalidateCacheForVehicle(int vehicleId)
		{
			// Sterg din cache PTV toate cheile care contin vehicleId dat
			var ptvKeysToRemove = ptvCheckCache.Keys
				.Where(key => key.vehicleId == vehicleId)
				.ToList();

			foreach (var key in ptvKeysToRemove)
			{
				ptvCheckCache.Remove(key, out _);
				Debug.WriteLine($"PTV Cache invalidated for Order {key.orderId} and Vehicle {key.vehicleId}");
			}

			// Sterg din cache ORS toate cheile care contin vehicleId dat
			var orsKeysToRemove = orsCheckCache.Keys
				.Where(key => key.vehicleId == vehicleId)
				.ToList();

			foreach (var key in orsKeysToRemove)
			{
				orsCheckCache.Remove(key, out _);
				Debug.WriteLine($"ORS Cache invalidated for Order {key.orderId} and Vehicle {key.vehicleId}");
			}
		}
	}

	// Clasa pentru Depozit (Headquarter)
	public class Depot2
	{
		public double Latitude { get; set; }
		public double Longitude { get; set; }
	}
}
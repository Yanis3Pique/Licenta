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

namespace Licenta_v1.Services
{
	public class OrderDeliveryOptimizer2
	{
		private readonly IServiceScopeFactory scopeFactory; // Ca sa creez mai multe instante de DbContext
		private readonly string OpenRouteServiceApiKey;
		private const int MaxWorkingSeconds = 6 * 3600; // 6 ore
		private const int ServiceTimePerOrderSeconds = 300; // 5 minute pe comanda

		public OrderDeliveryOptimizer2(IServiceScopeFactory serviceScopeFactory, string apiKey)
		{
			scopeFactory = serviceScopeFactory;
			OpenRouteServiceApiKey = apiKey;
		}

		// Metoda principala, apelata o data pe zi de catre Admin pentru toate regiunile/Dispecer pentru regiunea sa
		public async Task RunDailyOptimization(int? userRegionId = null)
		{
			using var scope = scopeFactory.CreateScope();
			var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

			// Sorteaza comenzile: mai intai dupa data plasarii (crescator), iar pentru comenzile din aceeasi zi High inainte de Normal
			var orders = db.Orders
				.Where(o => o.Status == OrderStatus.Placed &&
							o.DeliveryId == null &&  // Exclud comenzile deja asignate la un Delivery
							(!userRegionId.HasValue || o.RegionId == userRegionId.Value))
				.OrderBy(o => o.PlacedDate)
				.ThenBy(o => o.Priority == OrderPriority.High ? 0 : 1)
				.ToList();

			// Grupez comenzile dupa regiune
			var groupedOrders = orders.GroupBy(o => o.RegionId ?? 0);

			foreach (var regionGroup in groupedOrders)
			{
				await OptimizeRegionDeliveries(regionGroup.Key, regionGroup.ToList());
			}
		}

		// Optimizez Deliveries pentru o regiune specifica
		private async Task OptimizeRegionDeliveries(int regionId, List<Order> orders)
		{
			using var scope = scopeFactory.CreateScope();
			var tomorrow = DateTime.Now.AddDays(1).Date;
			var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

			// Re-sortez comenzile: data plasarii ascendent si prioritate High inainte de Normal
			orders = orders.Where(o => o.DeliveryId == null)
						   .OrderBy(o => o.PlacedDate)
						   .ThenBy(o => o.Priority == OrderPriority.High ? 0 : 1)
						   .ToList();

			if (orders.Count == 0)
			{
				Debug.WriteLine($"Skipping Region {regionId} - No orders found/All orders assigned.");
				return;
			}

			// Iau Headquarter-ul din regiunea curenta
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

			// Use all orders as one cluster, sorted by placed date and priority
			// Folosesc toate comenzile ca un singur cluster, sortate dupa data plasarii si prioritate
			var clusters = new List<List<Order>>
			{
				orders.OrderBy(o => o.PlacedDate)
					  .ThenBy(o => o.Priority == OrderPriority.High ? 0 : 1)
					  .ToList()
			};

			// Iau vehiculele disponibile in regiune
			var vehicles = db.Vehicles
				.Where(v => v.RegionId == regionId &&
							v.Status == VehicleStatus.Available &&
							!db.Maintenances.Any(m => m.VehicleId == v.Id && m.ScheduledDate.Date == tomorrow))
				.OrderBy(v => v.MaxWeightCapacity)
				.ThenBy(v => v.MaxVolumeCapacity)
				.ThenBy(v => v.FuelType == FuelType.Electric ? 0 : 1)
				.ToList();

			if (vehicles.Count == 0)
			{
				Debug.WriteLine($"Skipping Region {regionId} - No available vehicles.");
				return;
			}

			// Creez un HashSet pentru a tine evidenta vehiculelor deja folosite
			var usedVehicleIds = new HashSet<int>();

			// Procesez fiecare cluster separat
			foreach (var cluster in clusters)
			{
				// Apelez metoda pentru mai mult de 3 vehicule (restrictie ORS), impartindu-le in subseturi
				var availableForOptimization = vehicles.Where(v => !usedVehicleIds.Contains(v.Id)).ToList();
				var optimizationRoutes = await OptimizeRoutesWithORSForCluster(cluster, availableForOptimization, depot);

				// Pentru fiecare ruta din optimizationRoutes, verific fezabilitatea
				List<RouteResult> clusterFeasibleRoutes = new List<RouteResult>();

				foreach (var route in optimizationRoutes)
				{
					if (!IsRouteFeasible(route.Orders, vehicles) && route.Orders.Count > 1)
					{
						// Daca ruta nu este fezabila, se face partitionare spatiala a comenzilor
						var partitions = SpatialPartitionOrders(route.Orders);
						var subRoutes1 = await OptimizeRoutesWithORSForCluster(partitions.subcluster1, availableForOptimization, depot);
						var subRoutes2 = await OptimizeRoutesWithORSForCluster(partitions.subcluster2, availableForOptimization, depot);
						var subFeasible = subRoutes1.Concat(subRoutes2)
													.Where(r => IsRouteFeasible(r.Orders, vehicles))
													.ToList();
						clusterFeasibleRoutes.AddRange(subFeasible);
					}
					else if (IsRouteFeasible(route.Orders, vehicles))
					{
						clusterFeasibleRoutes.Add(route);
					}
				}

				if (clusterFeasibleRoutes.Count == 0)
				{
					Debug.WriteLine("No valid routes generated for cluster or its subclusters.");
					continue;
				}

				// Pentru fiecare ruta fezabila gasita de ORS, creez un Delivery
				foreach (var route in clusterFeasibleRoutes)
				{
					await AssignOrdersToDeliveriesAsync(route.Orders, vehicles, usedVehicleIds, depot, route.VehicleId);
				}

			}
		}

		// Helper care verifica daca exista cel putin un vehicul care poate acoperi comenzile date
		private bool IsRouteFeasible(List<Order> orders, List<Vehicle> vehicles)
		{
			return vehicles.Any(v => CanFitOrders(v, orders));
		}

		// Helper care verifica daca ruta se incadreaza in limita de timp
		private bool IsRouteWithinTimeLimit(int routeDurationSeconds, int numberOfOrders)
		{
			int totalServiceTime = numberOfOrders * ServiceTimePerOrderSeconds;
			return (routeDurationSeconds + totalServiceTime) <= MaxWorkingSeconds;
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

				var optimizationRequest = new
				{
					jobs = remainingOrders.Select(o => new
					{
						id = o.Id,
						location = new[] { o.Longitude ?? 0.0, o.Latitude ?? 0.0 },
						amount = new[] { (int)(o.Weight ?? 0), (int)(o.Volume ?? 0) },
					}),
					vehicles = subset.Select(v => new
					{
						id = v.Id,
						profile = "driving-car",
						start = new[] { depot.Longitude, depot.Latitude },
						capacity = new[] { (int)v.MaxWeightCapacity, (int)v.MaxVolumeCapacity },
					})
				};

				string requestUrl = "https://api.openrouteservice.org/optimization";
				string jsonBody = JsonConvert.SerializeObject(optimizationRequest);
				Debug.WriteLine($"Sending ORS Optimization Request (subset): {jsonBody}");

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
			var coordinates = new List<double[]>
			{
				new double[] { depot.Longitude, depot.Latitude }
			};
			coordinates.AddRange(orders.Select(o => new double[] { o.Longitude ?? 0.0, o.Latitude ?? 0.0 }));
			coordinates.Add(new double[] { depot.Longitude, depot.Latitude });

			var directionsRequest = new { coordinates };
			string requestUrl = "https://api.openrouteservice.org/v2/directions/driving-car";
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

		// Metoda de partitionare spatiala: impart un cluster in doua subclustere pe baza dispersiei spatiale
		private (List<Order> subcluster1, List<Order> subcluster2) SpatialPartitionOrders(List<Order> cluster)
		{
			// Calculez media latitudinii si longitudinii
			double avgLat = cluster.Average(o => o.Latitude ?? 0);
			double avgLon = cluster.Average(o => o.Longitude ?? 0);

			// Calculez varianta pentru latitudine si longitudine
			double varLat = cluster.Average(o => Math.Pow((o.Latitude ?? 0) - avgLat, 2));
			double varLon = cluster.Average(o => Math.Pow((o.Longitude ?? 0) - avgLon, 2));

			List<Order> subcluster1;
			List<Order> subcluster2;

			if (varLon >= varLat)
			{
				// Impart dupa longitudine
				var medianLon = cluster.Select(o => o.Longitude ?? 0)
									   .OrderBy(lon => lon)
									   .ElementAt(cluster.Count / 2);
				subcluster1 = cluster.Where(o => (o.Longitude ?? 0) <= medianLon).ToList();
				subcluster2 = cluster.Where(o => (o.Longitude ?? 0) > medianLon).ToList();
			}
			else
			{
				// Impart dupa latitudine
				var medianLat = cluster.Select(o => o.Latitude ?? 0)
									   .OrderBy(lat => lat)
									   .ElementAt(cluster.Count / 2);
				subcluster1 = cluster.Where(o => (o.Latitude ?? 0) <= medianLat).ToList();
				subcluster2 = cluster.Where(o => (o.Latitude ?? 0) > medianLat).ToList();
			}

			// Daca oricare dintre subclustere e gol, repartitionez egal pe baza indexului
			if (!subcluster1.Any() || !subcluster2.Any())
			{
				int mid = cluster.Count / 2;
				subcluster1 = cluster.Take(mid).ToList();
				subcluster2 = cluster.Skip(mid).ToList();
			}

			return (subcluster1, subcluster2);
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

			string requestUrl = "https://api.openrouteservice.org/v2/directions/driving-car";
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
				profile = "driving-car",
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
	}

	// Clasa pentru Depozit (Headquarter)
	public class Depot2
	{
		public double Latitude { get; set; }
		public double Longitude { get; set; }
	}
}
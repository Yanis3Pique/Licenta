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

namespace Licenta_v1.Services
{
	public class OrderDeliveryOptimizer2
	{
		private readonly IServiceScopeFactory scopeFactory; // Ca sa creez mai multe instante de DbContext
		private readonly string OpenRouteServiceApiKey;

		public OrderDeliveryOptimizer2(IServiceScopeFactory serviceScopeFactory, string apiKey)
		{
			scopeFactory = serviceScopeFactory;
			OpenRouteServiceApiKey = apiKey;
		}

		// Metoda principala, apelata o data pe zi de catre Admin pt toate regiunile/Dispecer pt regiunea sa
		public async Task RunDailyOptimization(int? userRegionId = null)
		{
			using var scope = scopeFactory.CreateScope();
			var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

			var orders = db.Orders
				.Where(o => o.Status == OrderStatus.Placed &&
							o.DeliveryId == null &&  // Exclud comenzile deja asignate la un Delivery
							(!userRegionId.HasValue || o.RegionId == userRegionId.Value))
				.OrderBy(o => o.Priority == OrderPriority.High ? 0 : 1)
				.ThenBy(o => o.PlacedDate)
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

			orders = orders.Where(o => o.DeliveryId == null).ToList();
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

			// Folosesc Isochrone clustering(adica gruparea in clustere in functie de timp) pentru Orders
			var clusters = await IsochroneClusterOrders(orders, depot);
			if (clusters.Count == 0)
				clusters.Add(orders);

			// Iau vehiculele disponibile in regiune
			var vehicles = db.Vehicles
				.Where(v => v.RegionId == regionId &&
							v.Status == VehicleStatus.Available &&
							!db.Maintenances.Any(m => m.VehicleId == v.Id && m.ScheduledDate.Date == tomorrow))
				.OrderBy(v => v.MaxWeightCapacity / v.ConsumptionRate)
				.ThenByDescending(v => v.YearOfManufacture)
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
				// Apelez metoda pentru mai mult de 3 vehicule(restrictie ORS), impartindu-le in subseturi
				var optimizationRoutes = await OptimizeRoutesWithORSForCluster(cluster, vehicles, depot);
				if (optimizationRoutes.Count == 0)
				{
					Debug.WriteLine("No valid routes generated for one cluster.");
					continue;
				}
				// Pt fiecare ruta gasita de ORS creez un Delivery
				foreach (var route in optimizationRoutes)
				{
					AssignOrdersToDeliveries(route.Orders, vehicles, usedVehicleIds);
				}
			}
		}


		// Metoda imparte vehiculele disponibile in submultimi (de cel mult 3 vehicule)
		// si trimite iterativ cereri de optimizare pentru comenzile ramase
		private async Task<List<RouteResult>> OptimizeRoutesWithORSForCluster(List<Order> orders, List<Vehicle> vehicles, Depot2 depot)
		{
			List<RouteResult> combinedResults = new List<RouteResult>();

			// Sortez vehiculele descrescator dupa greutate maxima si volum maxim admis
			var sortedVehicles = vehicles.OrderByDescending(v => v.MaxWeightCapacity)
										 .ThenByDescending(v => v.MaxVolumeCapacity)
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
						amount = new[] { (int)(o.Weight ?? 0), (int)(o.Volume ?? 0) }
					}),
					vehicles = subset.Select(v => new
					{
						id = v.Id,
						profile = "driving-car",
						start = new[] { depot.Longitude, depot.Latitude },
						capacity = new[] { (int)v.MaxWeightCapacity, (int)v.MaxVolumeCapacity }
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
							combinedResults.Add(new RouteResult
							{
								VehicleId = route.Vehicle,
								Orders = orderedOrders
							});
							anyRouteFound = true;
						}
					}
					if (anyRouteFound)
					{
						// Sterg Orders deja asignate din remainingOrders
						var assignedOrderIds = combinedResults.SelectMany(r => r.Orders.Select(o => o.Id)).Distinct().ToList();
						remainingOrders.RemoveAll(o => assignedOrderIds.Contains(o.Id));
						// Sterg vehiculele care au fost deja folosite din sortedVehicles
						var usedVehicleIds = combinedResults.Select(r => r.VehicleId).Distinct().ToList();
						sortedVehicles.RemoveAll(v => usedVehicleIds.Contains(v.Id));
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
		}

		public class ORSOptimizationStep
		{
			[JsonProperty("type")]
			public string Type { get; set; }

			[JsonProperty("job")]
			public int? Job { get; set; }
		}

		// Obiect pt ruta ca sa legam vehiculele de lista ordonata de comenzi
		public class RouteResult
		{
			public int VehicleId { get; set; }
			public List<Order> Orders { get; set; }
		}

		// Folosesc endpoint-ul ORS Isochrones pentru a obtine praguri de timp de calatorie in jurul Headquarter
		// si apoi grupez comenzile in functie de cel mai mic prag de timp de calatorie care contine comanda
		private async Task<List<List<Order>>> IsochroneClusterOrders(List<Order> orders, Depot2 depot)
		{
			// Aici sunt pragurile de timp (in secunde) pentru 15, 30, 45 si 60 de minute
			int[] timeRanges = new int[] { 900, 1800, 2700, 3600 };

			var isochroneRequest = new
			{
				locations = new List<double[]> { new[] { depot.Longitude, depot.Latitude } },
				range = timeRanges,
				range_type = "time"
			};

			string requestUrl = "https://api.openrouteservice.org/v2/isochrones/driving-car";
			string jsonBody = JsonConvert.SerializeObject(isochroneRequest);
			Debug.WriteLine($"Sending Isochrone Request: {jsonBody}");

			List<IsochroneFeature> features = new List<IsochroneFeature>();

			using (var client = new HttpClient())
			{
				client.DefaultRequestHeaders.Add("Authorization", OpenRouteServiceApiKey);
				var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
				var response = await client.PostAsync(requestUrl, content);
				var responseString = await response.Content.ReadAsStringAsync();
				Debug.WriteLine($"Isochrone Response: {responseString}");

				if (response.IsSuccessStatusCode)
				{
					var isochroneResponse = JsonConvert.DeserializeObject<IsochroneResponse>(responseString);
					if (isochroneResponse?.Features != null)
					{
						// Sortez feature-urile in functie de timpul de calatorie
						features = isochroneResponse.Features.OrderBy(f => f.Properties.Value).ToList();
					}
				}
				else
				{
					Debug.WriteLine($"Isochrone API error: {response.StatusCode}");
				}
			}

			// Acum, pentru fiecare comanda, determin in care "poligon" de timp se incadreaza
			// Daca o comanda se incadreaza in mai multe poligoane, o asigneaz la cel cu cel mai mic timp de calatorie
			var clustersDict = new Dictionary<double, List<Order>>();
			// Initializez clustere pentru fiecare valoare a poligonului
			foreach (var feature in features)
			{
				clustersDict[feature.Properties.Value] = new List<Order>();
			}
			// Fac si un cluster default pentru comenzile care nu intra in niciun poligon
			const double defaultKey = -1;
			clustersDict[defaultKey] = new List<Order>();

			foreach (var order in orders)
			{
				bool assigned = false;
				// Verific fiecare poligon (de la cel mai mic la cel mai mare travel time)
				foreach (var feature in features)
				{
					// Folosesc primul inel exterior de coordonate pentru poligon 
					if (feature.Geometry?.Coordinates?.Any() == true)
					{
						var polygon = feature.Geometry.Coordinates.First(); // List<List<double>>
						if (IsPointInPolygon(order.Latitude ?? 0, order.Longitude ?? 0, polygon))
						{
							clustersDict[feature.Properties.Value].Add(order);
							assigned = true;
							break;
						}
					}
				}
				if (!assigned)
				{
					clustersDict[defaultKey].Add(order);
				}
			}

			// Clusterele returnate nu trebuie sa fie goale
			return clustersDict.Values.Where(c => c.Any()).ToList();
		}

		// Verific daca un punct(Order) se gaseste in vreun poligon in functie de coordonate(latitudine, longitudine)
		// Poligonul e o lista de puncte cu coordonate(latitudine, longitudine)
		private bool IsPointInPolygon(double lat, double lon, List<List<double>> polygon)
		{
			bool inside = false;
			int count = polygon.Count;
			for (int i = 0, j = count - 1; i < count; j = i++)
			{
				double xi = polygon[i][0], yi = polygon[i][1];
				double xj = polygon[j][0], yj = polygon[j][1];

				bool intersect = ((yi > lat) != (yj > lat)) &&
								 (lon < (xj - xi) * (lat - yi) / (yj - yi + 1e-10) + xi);
				if (intersect)
					inside = !inside;
			}
			return inside;
		}

		// DTOs pentru raspunsul de la Isochrone
		public class IsochroneResponse
		{
			[JsonProperty("features")]
			public List<IsochroneFeature> Features { get; set; }
		}

		public class IsochroneFeature
		{
			[JsonProperty("properties")]
			public IsochroneProperties Properties { get; set; }

			[JsonProperty("geometry")]
			public IsochroneGeometry Geometry { get; set; }
		}

		public class IsochroneProperties
		{
			// Prin "value" ma refer la timpul de calatorie in secunde pentru acest isochrone
			[JsonProperty("value")]
			public double Value { get; set; }
		}

		public class IsochroneGeometry
		{
			[JsonProperty("type")]
			public string Type { get; set; }

			// Coordonatele sunt o lista liniara de inele, iar fiecare inel este o lista de perechi [latitudine, longitudine]
			[JsonProperty("coordinates")]
			public List<List<List<double>>> Coordinates { get; set; }
		}

		// Pentru o ruta anume(cluster de comenzi), alegem un vehicul disponibil care poate sa transporte comenzile
		// si un sofer disponibil pentru a crea obiectul de Delivery
		private void AssignOrdersToDeliveries(List<Order> orderedOrders, List<Vehicle> vehicles, HashSet<int> usedVehicleIds)
		{
			using var scope = scopeFactory.CreateScope();
			var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

			if (!orderedOrders.Any())
				return;

			var plannedDate = DateTime.Now.AddDays(1).Date;
			// Calculez incarcatura totala pe masina
			double totalWeight = orderedOrders.Sum(o => o.Weight ?? 0);
			double totalVolume = orderedOrders.Sum(o => o.Volume ?? 0);

			// Aleg cel mai bun vehicul disponibil in care incap comenzile
			var candidateVehicle = vehicles
				.Where(v => !usedVehicleIds.Contains(v.Id) && CanFitOrders(v, orderedOrders))
				.OrderBy(v => (v.MaxWeightCapacity - totalWeight) + (v.MaxVolumeCapacity - totalVolume))
				.FirstOrDefault();

			if (candidateVehicle == null)
				return;

			// Marchez ca am folosit vehiculul ca sa nu-l mai iau data viitoare
			usedVehicleIds.Add(candidateVehicle.Id);

			// Iau un sofer disponibil, mai intai cei cu rating mai bun sunt luati primii
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

			// Actualizez tabela Orders ca sa am DeliveryId si DeliverySequene
			for (int i = 0; i < orderedOrders.Count; i++)
			{
				orderedOrders[i].DeliverySequence = i; // Numar mai mic = mai la inceput in livrare
				orderedOrders[i].DeliveryId = delivery.Id;
				db.Orders.Update(orderedOrders[i]);
			}

			candidateVehicle.Status = VehicleStatus.Busy;
			db.Vehicles.Update(candidateVehicle);

			db.SaveChanges();
		}

		// Verific daca un vehicul are capacitate suficienta pentru comenzi
		private bool CanFitOrders(Vehicle vehicle, List<Order> orders)
		{
			double totalWeight = orders.Sum(o => o.Weight ?? 0);
			double totalVolume = orders.Sum(o => o.Volume ?? 0);
			return totalWeight <= vehicle.MaxWeightCapacity && totalVolume <= vehicle.MaxVolumeCapacity;
		}
	}

	// Clasa pentru Depozit(basically Headquarter)
	public class Depot2
	{
		public double Latitude { get; set; }
		public double Longitude { get; set; }
	}
}

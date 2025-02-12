using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Licenta_v1.Data;
using Licenta_v1.Models;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using Google.OrTools.ConstraintSolver;
using System.Text;
using System.Diagnostics;

namespace Licenta_v1.Services
{
	public class OrderDeliveryOptimizer
	{
		private readonly IServiceScopeFactory _scopeFactory; // Ca sa creez mai multe instante de DbContext
		private readonly string OpenRouteServiceApiKey;

		public OrderDeliveryOptimizer(IServiceScopeFactory scopeFactory, string apiKey)
		{
			_scopeFactory = scopeFactory;
			OpenRouteServiceApiKey = apiKey;
		}

		// Metoda principala, apelata o data pe zi de catre Admin pt toate regiunile/Dispecer pt regiunea sa
		public async Task RunDailyOptimization(int? userRegionId = null)
		{
			using var scope = _scopeFactory.CreateScope();
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
			using var scope = _scopeFactory.CreateScope();
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

			// Pentru clustering spatial folosesc DBSCAN.
			int minPoints = 2;     // Valorile astea doua ramane de vazut
			double eps = 10000;    // daca vor fi dinamice sau nu(cred ca DA)
			var spatialClusters = SpatialClusterOrders(orders, eps, minPoints);
			if (spatialClusters.Count == 0)
				spatialClusters.Add(orders);

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
			foreach (var cluster in spatialClusters)
			{
				// In loc sa calculez depozitul din cluster, folosesc Headquarter-ul
				var depot = new Depot
				{
					Latitude = headquarter.Latitude ?? 0,
					Longitude = headquarter.Longitude ?? 0
				};

				var distanceMatrix = await GetDistanceMatrix(cluster, depot);
				if (distanceMatrix.GetLength(0) == 0)
				{
					Debug.WriteLine("Skipping one cluster - empty distance matrix.");
					continue;
				}
				var clusterRoutes = OptimizeRoutesWithCVRP(distanceMatrix, cluster, vehicles);
				if (clusterRoutes.Count == 0)
				{
					Debug.WriteLine("No valid routes generated for one cluster.");
					continue;
				}
				// Pentru fiecare ruta, asignez comenzile la o livrare folosind un vehicul diferit
				foreach (var route in clusterRoutes)
				{
					AssignOrdersToDeliveries(route, vehicles, usedVehicleIds);
				}
			}
		}

		// Foloseste API-ul OpenRouteService pentru a construi o matrice de distante
		// Depozitul este Heaquaters-ul din regiunea respectiva
		private async Task<double[,]> GetDistanceMatrix(List<Order> orders, Depot depot)
		{
			// Depozitul (nodul 0) este Headquarter-ul.
			var locations = new List<double[]>
			{
				new[] { depot.Longitude, depot.Latitude }
			};
			locations.AddRange(orders.Select(o => new double[] { o.Longitude ?? 0.0, o.Latitude ?? 0.0 }));

			using var client = new HttpClient();
			client.DefaultRequestHeaders.Add("Authorization", OpenRouteServiceApiKey);
			var requestUrl = "https://api.openrouteservice.org/v2/matrix/driving-car";
			var requestBody = new { locations = locations, metrics = new[] { "distance" } };
			var jsonBody = JsonConvert.SerializeObject(requestBody);
			var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

			Debug.WriteLine($"Sending Request: {jsonBody}");
			var response = await client.PostAsync(requestUrl, content);
			var responseString = await response.Content.ReadAsStringAsync();
			Debug.WriteLine($"ORS Response: {responseString}");

			if (!response.IsSuccessStatusCode)
			{
				Debug.WriteLine($"ORS Error: {response.StatusCode} - {responseString}");
				throw new HttpRequestException($"Failed to get distance matrix: {response.StatusCode}");
			}

			var data = JsonConvert.DeserializeObject<ORSMatrixResponse>(responseString);
			int size = orders.Count + 1;
			double[,] matrix = new double[size, size];
			for (int i = 0; i < size; i++)
			{
				for (int j = 0; j < size; j++)
				{
					matrix[i, j] = Math.Round(data.Distances[i][j]);
				}
			}
			return matrix;
		}

		// Rezolva problema CVRP folosind OR-Tools cu doua dimensiuni de capacitate
		// Returneaza o lista de rute, unde fiecare ruta este o lista de comenzi
		// Capacitated Vehicle Routing Problem (CVRP)
		private List<List<Order>> OptimizeRoutesWithCVRP(double[,] distanceMatrix, List<Order> orders, List<Vehicle> vehicles)
		{
			int nodeCount = orders.Count + 1; // depozitul este nodul 0
			int vehicleCount = vehicles.Count;
			int depot = 0;

			var manager = new RoutingIndexManager(nodeCount, vehicleCount, depot);
			var routing = new RoutingModel(manager);

			// Callback pentru distanta (tranzit)
			int transitCallbackIndex = routing.RegisterTransitCallback((long fromIndex, long toIndex) =>
			{
				int fromNode = manager.IndexToNode(fromIndex);
				int toNode = manager.IndexToNode(toIndex);
				return (int)Math.Round(distanceMatrix[fromNode, toNode]);
			});
			routing.SetArcCostEvaluatorOfAllVehicles(transitCallbackIndex);

			// Construiesc vectorii de Demand pentru greutate si volum (depozitul are Demand 0)
			int[] weightDemand = new int[nodeCount];
			int[] volumeDemand = new int[nodeCount];
			for (int i = 1; i < nodeCount; i++)
			{
				weightDemand[i] = (int)(orders[i - 1].Weight ?? 0);
				volumeDemand[i] = (int)(orders[i - 1].Volume ?? 0);
			}

			// Inregistez callback-urile pentru Demand
			int weightCallbackIndex = routing.RegisterUnaryTransitCallback((long index) =>
			{
				int node = manager.IndexToNode(index);
				return weightDemand[node];
			});
			int volumeCallbackIndex = routing.RegisterUnaryTransitCallback((long index) =>
			{
				int node = manager.IndexToNode(index);
				return volumeDemand[node];
			});

			long[] vehicleWeightCapacities = vehicles.Select(v => (long)v.MaxWeightCapacity).ToArray();
			long[] vehicleVolumeCapacities = vehicles.Select(v => (long)v.MaxVolumeCapacity).ToArray();

			routing.AddDimensionWithVehicleCapacity(
				weightCallbackIndex,
				0,
				vehicleWeightCapacities,
				true,
				"Weight");
			routing.AddDimensionWithVehicleCapacity(
				volumeCallbackIndex,
				0,
				vehicleVolumeCapacities,
				true,
				"Volume");

			var searchParameters = operations_research_constraint_solver.DefaultRoutingSearchParameters();
			searchParameters.FirstSolutionStrategy = FirstSolutionStrategy.Types.Value.PathCheapestArc;
			var solution = routing.SolveWithParameters(searchParameters);

			var clusters = new List<List<Order>>();
			if (solution != null)
			{
				// Pentru fiecare vehicul, extrag ruta
				for (int v = 0; v < vehicleCount; v++)
				{
					var route = new List<Order>();
					long index = routing.Start(v);
					while (!routing.IsEnd(index))
					{
						int node = manager.IndexToNode(index);
						// Sar peste depozit (nodul 0)
						if (node != depot)
							route.Add(orders[node - 1]);
						index = solution.Value(routing.NextVar(index));
					}
					if (route.Count > 0)
						clusters.Add(route);
				}
			}
			else
			{
				Debug.WriteLine("⚠️ Nu s-a gasit o solutie de catre modelul CVRP.");
			}
			return clusters;
		}

		// Pentru o ruta (cluster de comenzi), selectez un vehicul (care poate transporta incarcatura)
		// si asignez un sofer pentru a crea un Delivery
		private void AssignOrdersToDeliveries(List<Order> orders, List<Vehicle> vehicles, HashSet<int> usedVehicleIds)
		{
			using var scope = _scopeFactory.CreateScope();
			var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

			if (!orders.Any())
				return;

			var plannedDate = DateTime.Now.AddDays(1).Date;
			// Calculez incarcatura totala
			double totalWeight = orders.Sum(o => o.Weight ?? 0);
			double totalVolume = orders.Sum(o => o.Volume ?? 0);

			// Iau cel mai potrivit vehicul care nu a fost deja folosit
			var candidateVehicle = vehicles
				.Where(v => !usedVehicleIds.Contains(v.Id) && CanFitOrders(v, orders))
				.OrderBy(v => (v.MaxWeightCapacity - totalWeight) + (v.MaxVolumeCapacity - totalVolume))
				.FirstOrDefault();

			if (candidateVehicle == null)
				return;

			// Marchez acest vehicul ca folosit
			usedVehicleIds.Add(candidateVehicle.Id);

			// Iau un sofer disponibil pentru acest vehicul
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

			// Actualizez comenzile pentru a avea DeliveryId
			foreach (var order in orders)
			{
				order.DeliveryId = delivery.Id;
				db.Orders.Update(order);
			}

			candidateVehicle.Status = VehicleStatus.Busy;
			db.Vehicles.Update(candidateVehicle);

			db.SaveChanges();
		}

		// Verific daca vehiculul are loc pentru comenzile date
		private bool CanFitOrders(Vehicle vehicle, List<Order> orders)
		{
			double totalWeight = orders.Sum(o => o.Weight ?? 0);
			double totalVolume = orders.Sum(o => o.Volume ?? 0);
			return totalWeight <= vehicle.MaxWeightCapacity && totalVolume <= vehicle.MaxVolumeCapacity;
		}


		// DBSCAN care grupeaza comenzile dupa coordonatele (lat, lon)
		private List<List<Order>> SpatialClusterOrders(List<Order> orders, double eps, int minPoints)
		{
			var clusters = new List<List<Order>>();
			var visited = new HashSet<Order>();
			var noise = new List<Order>();

			foreach (var order in orders)
			{
				if (visited.Contains(order))
					continue;
				visited.Add(order);
				var neighbors = GetNeighbors(order, orders, eps);
				if (neighbors.Count < minPoints)
				{
					noise.Add(order);
				}
				else
				{
					var cluster = new List<Order>();
					ExpandCluster(order, neighbors, cluster, orders, eps, minPoints, visited);
					clusters.Add(cluster);
				}
			}
			// Ma asigur ca fiecare comanda este asignata pe cat posibil
			foreach (var n in noise)
			{
				if (!clusters.Any(c => c.Contains(n)))
				{
					clusters.Add(new List<Order> { n });
				}
			}
			return clusters;
		}

		// Gasesc vecinii unei comenzi in functie de epsilon
		private List<Order> GetNeighbors(Order order, List<Order> orders, double eps)
		{
			var neighbors = new List<Order>();
			foreach (var o in orders)
			{
				if (HaversineDistance(order.Latitude ?? 0, order.Longitude ?? 0,
										o.Latitude ?? 0, o.Longitude ?? 0) <= eps)
				{
					neighbors.Add(o);
				}
			}
			return neighbors;
		}

		// Extind cluster-ul. Functie apelata in DBSCAN
		private void ExpandCluster(Order order, List<Order> neighbors, List<Order> cluster,
									 List<Order> orders, double eps, int minPoints, HashSet<Order> visited)
		{
			cluster.Add(order);
			var neighborQueue = new Queue<Order>(neighbors);
			while (neighborQueue.Count > 0)
			{
				var current = neighborQueue.Dequeue();
				if (!visited.Contains(current))
				{
					visited.Add(current);
					var currentNeighbors = GetNeighbors(current, orders, eps);
					if (currentNeighbors.Count >= minPoints)
					{
						foreach (var n in currentNeighbors)
						{
							if (!neighborQueue.Contains(n))
								neighborQueue.Enqueue(n);
						}
					}
				}
				if (!cluster.Contains(current))
					cluster.Add(current);
			}
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
	}

	// Clasa simpla utilizata ca depozit (retine coordonatele)
	public class Depot
	{
		public double Latitude { get; set; }
		public double Longitude { get; set; }
	}
}

// Data Transfer Object simplu pentru deserializarea raspunsului de la
// OpenRouteService(matricea aia de distante)
public class ORSMatrixResponse
{
	public required List<List<double>> Distances { get; set; }
}
using System;
using System.Collections.Generic;
using System.Linq;
using Licenta_v1.Data;
using Licenta_v1.Models;
using Microsoft.EntityFrameworkCore;

namespace Licenta_v1.Services
{
	public class OrderDeliveryOptimizer
	{
		private readonly ApplicationDbContext db;
		private const double MaxClusterDistance = 2.0; // Adjustez distanta MAX intre doua comenzi din acelasi cluster
		private const int MinClusterSize = 2;

		public OrderDeliveryOptimizer(ApplicationDbContext context)
		{
			db = context;
		}

		public void RunDailyOptimization(int? userRegionId = null)
		{
			var ordersQuery = db.Orders
				.Where(o => o.Status == OrderStatus.Placed);

			// Daca userul care ruleaza optimizarea este dispecer, se va optimiza doar regiunea lui
			if (userRegionId.HasValue)
			{
				ordersQuery = ordersQuery.Where(o => o.RegionId == userRegionId.Value);
			}

			// Ordonez acum comenzile dupa prioritate si data plasarii
			ordersQuery = ordersQuery
				.OrderBy(o => o.Priority == OrderPriority.High ? 0 : 1)
				.ThenBy(o => o.PlacedDate);

			var orders = ordersQuery.ToList();
			var groupedOrders = orders.GroupBy(o => o.RegionId ?? 0);

			foreach (var regionGroup in groupedOrders)
			{
				OptimizeRegionDeliveries(regionGroup.Key, regionGroup.ToList());
			}

			db.SaveChanges();
		}

		private void OptimizeRegionDeliveries(int regionId, List<Order> orders)
		{
			var vehicles = db.Vehicles
							 .Include(v => v.Region)
							 .Where(v => v.RegionId == regionId && v.Status == VehicleStatus.Available)
							 .OrderBy(v => v.MaxWeightCapacity)
							 .ToList();

			var clusteredOrders = ClusterOrders(orders);
			var optimizedClusters = BinPackingOptimization(clusteredOrders, vehicles);

			foreach (var cluster in optimizedClusters)
			{
				AssignOrdersToDeliveries(cluster, vehicles);
			}
		}

		private List<List<Order>> ClusterOrders(List<Order> orders)
		{
			var clusters = new List<List<Order>>();
			var visited = new HashSet<Order>();
			foreach (var order in orders)
			{
				if (visited.Contains(order))
					continue;

				var cluster = new List<Order>();
				var neighbors = GetNeighbors(order, orders);
				if (neighbors.Count < MinClusterSize)
					continue;

				ExpandCluster(order, neighbors, cluster, orders, visited);
				clusters.Add(cluster);
			}
			return clusters;
		}

		private List<Order> GetNeighbors(Order order, List<Order> orders)
		{
			return orders.Where(o => GetDistance(order, o) <= MaxClusterDistance).ToList();
		}

		private void ExpandCluster(Order order, List<Order> neighbors, List<Order> cluster, List<Order> orders, HashSet<Order> visited)
		{
			cluster.Add(order);
			var newNeighborsList = new List<Order>();
			foreach (var neighbor in neighbors)
			{
				if (!visited.Contains(neighbor))
				{
					visited.Add(neighbor);
					var newNeighbors = GetNeighbors(neighbor, orders);
					if (newNeighbors.Count >= MinClusterSize)
						newNeighborsList.AddRange(newNeighbors);
				}
				if (!cluster.Contains(neighbor))
					cluster.Add(neighbor);
			}
			neighbors.AddRange(newNeighborsList);
		}

		private List<List<Order>> BinPackingOptimization(List<List<Order>> clusters, List<Vehicle> vehicles)
		{
			var optimizedClusters = new List<List<Order>>();
			foreach (var cluster in clusters)
			{
				optimizedClusters.AddRange(SplitClusterByCapacity(cluster, vehicles));
			}
			return optimizedClusters;
		}

		private List<List<Order>> SplitClusterByCapacity(List<Order> cluster, List<Vehicle> vehicles)
		{
			var subClusters = new List<List<Order>>();
			var remainingOrders = new List<Order>(cluster);

			while (remainingOrders.Any())
			{
				var subCluster = new List<Order>();
				var vehicle = vehicles.FirstOrDefault();
				if (vehicle == null)
					break;

				double weightUsed = 0, volumeUsed = 0;
				foreach (var order in remainingOrders.ToList())
				{
					if (weightUsed + order.Weight <= vehicle.MaxWeightCapacity && volumeUsed + order.Volume <= vehicle.MaxVolumeCapacity)
					{
						subCluster.Add(order);
						weightUsed += order.Weight ?? 0;
						volumeUsed += order.Volume ?? 0;
						remainingOrders.Remove(order);
					}
				}
				subClusters.Add(subCluster);
			}

			return subClusters;
		}

		private bool AssignOrdersToDeliveries(List<Order> orders, List<Vehicle> vehicles)
		{
			var vehicle = vehicles.FirstOrDefault(v => CanFitOrders(v, orders));
			if (vehicle == null) return false;

			var driver = (from user in db.Users
						  join userRole in db.UserRoles on user.Id equals userRole.UserId
						  join role in db.Roles on userRole.RoleId equals role.Id
						  where user.RegionId == vehicle.RegionId && role.Name == "Sofer"
						  select user).FirstOrDefault();
			if (driver == null) return false;

			var delivery = new Delivery
			{
				DriverId = driver.Id,
				VehicleId = vehicle.Id,
				PlannedStartDate = DateTime.Now.AddDays(1),
				Status = "Planned"
			};

			db.Deliveries.Add(delivery);
			db.SaveChanges(); // Salvez Delivery pentru a obtine DeliveryId

			// Acum pot sa actualizez Orders cu DeliveryId
			orders.ForEach(o => o.DeliveryId = delivery.Id);
			db.Orders.UpdateRange(orders);

			vehicle.Status = VehicleStatus.Busy;
			db.SaveChanges(); // Salvez modificarile comenzilor si a vehiculului

			return true;
		}

		private bool CanFitOrders(Vehicle vehicle, List<Order> orders)
		{
			double totalWeight = orders.Sum(o => o.Weight ?? 0);
			double totalVolume = orders.Sum(o => o.Volume ?? 0);
			return totalWeight <= vehicle.MaxWeightCapacity && totalVolume <= vehicle.MaxVolumeCapacity;
		}

		private double GetDistance(Order a, Order b)
		{
			double R = 6371;
			double dLat = ToRadians(b.Latitude.Value - a.Latitude.Value);
			double dLon = ToRadians(b.Longitude.Value - a.Longitude.Value);
			double aVal = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) + Math.Cos(ToRadians(a.Latitude.Value)) * Math.Cos(ToRadians(b.Latitude.Value)) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
			double c = 2 * Math.Atan2(Math.Sqrt(aVal), Math.Sqrt(1 - aVal));
			return R * c;
		}

		private double ToRadians(double angle) => angle * (Math.PI / 180);
	}
}

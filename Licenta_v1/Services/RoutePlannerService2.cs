using DotNetEnv;
using Licenta_v1.Models;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using System.Text;
using System.Diagnostics;
using Google.OrTools.ConstraintSolver;

namespace Licenta_v1.Services
{
	public class RoutePlannerService2
	{
		private readonly IServiceScopeFactory scopeFactory;

		public RoutePlannerService2(IServiceScopeFactory serviceScopeFactory, IConfiguration configuration)
		{
			scopeFactory = serviceScopeFactory;
		}

		/// <summary>
		/// Calculates the optimal route by first getting the distance matrix from OSRM,
		/// then solving the TSP with OR-Tools. The route starts and ends at the headquarters.
		/// </summary>
		public async Task<RouteResult2> CalculateOptimalRouteAsync(Delivery delivery)
		{
			// Get orders with valid coordinates.
			var orders = delivery.Orders?.Where(o => o.Latitude.HasValue && o.Longitude.HasValue).ToList();
			if (orders == null || orders.Count == 0)
				throw new Exception("No valid orders found for route planning.");

			// Use the headquarters as the start/end point.
			Coordinate2 headquarters = new Coordinate2
			{
				Latitude = delivery.Vehicle.Region.Headquarters.Latitude.Value,
				Longitude = delivery.Vehicle.Region.Headquarters.Longitude.Value
			};

			// Build a list of points: headquarters is at index 0, then the orders.
			List<Coordinate2> points = new List<Coordinate2> { headquarters };
			points.AddRange(orders.Select(o => new Coordinate2
			{
				Latitude = o.Latitude.Value,
				Longitude = o.Longitude.Value
			}));

			// Build the OSRM Table API URL.
			// OSRM expects coordinates in "longitude,latitude" order.
			string baseUrl = "http://router.project-osrm.org/table/v1/driving/";
			string coordinatesStr = string.Join(";", points.Select(p =>
				$"{p.Longitude.ToString(System.Globalization.CultureInfo.InvariantCulture)},{p.Latitude.ToString(System.Globalization.CultureInfo.InvariantCulture)}"));
			string url = $"{baseUrl}{coordinatesStr}?annotations=distance";

			using var client = new HttpClient();
			var response = await client.GetAsync(url);
			if (!response.IsSuccessStatusCode)
				throw new Exception($"OSRM API request failed: {response.StatusCode}");

			var jsonResponse = await response.Content.ReadAsStringAsync();
			var osrmResponse = JsonConvert.DeserializeObject<OsrmTableResponse>(jsonResponse);
			if (osrmResponse == null || osrmResponse.Code != "Ok" || osrmResponse.Distances == null)
				throw new Exception("Invalid response from OSRM API.");

			int numPoints = points.Count;
			int[,] distanceMatrix = new int[numPoints, numPoints];
			// Convert OSRM distances (in meters) to an integer matrix.
			for (int i = 0; i < numPoints; i++)
			{
				for (int j = 0; j < numPoints; j++)
				{
					distanceMatrix[i, j] = (int)Math.Round(osrmResponse.Distances[i][j]);
				}
			}

			// Solve the TSP using OR-Tools.
			List<int> routeOrder = SolveTSP(distanceMatrix);
			if (routeOrder == null || routeOrder.Count == 0)
				throw new Exception("TSP solver did not find a route.");

			// Calculate the total distance for the computed route.
			int totalDistance = 0;
			for (int i = 0; i < routeOrder.Count - 1; i++)
			{
				totalDistance += distanceMatrix[routeOrder[i], routeOrder[i + 1]];
			}

			// Build the route coordinates from the computed indices.
			List<Coordinate2> routeCoordinates = routeOrder.Select(index => points[index]).ToList();

			// Map the computed indices back to order IDs (skipping index 0 which is the headquarters).
			List<int> orderIds = new List<int>();
			foreach (var index in routeOrder)
			{
				if (index > 0)
				{
					// Since orders were added after headquarters, subtract 1 from the index.
					orderIds.Add(orders[index - 1].Id);
				}
			}

			var routeResult = new RouteResult2
			{
				Coordinates = routeCoordinates,
				Distance = totalDistance,
				Duration = 0, // You can compute durations if you wish to use another OSRM API endpoint.
				Segments = new List<SegmentResult2>(), // Optionally, build detailed segments.
				OrderIds = orderIds
			};

			return routeResult;
		}

		/// <summary>
		/// Uses OR-Tools to solve the TSP for the given distance matrix.
		/// </summary>
		private List<int> SolveTSP(int[,] distanceMatrix)
		{
			int size = distanceMatrix.GetLength(0);
			// Create the routing index manager.
			RoutingIndexManager manager = new RoutingIndexManager(size, 1, 0);
			// Create Routing Model.
			RoutingModel routing = new RoutingModel(manager);

			// Register a transit callback.
			int transitCallbackIndex = routing.RegisterTransitCallback((long fromIndex, long toIndex) =>
			{
				int fromNode = manager.IndexToNode(fromIndex);
				int toNode = manager.IndexToNode(toIndex);
				return distanceMatrix[fromNode, toNode];
			});
			routing.SetArcCostEvaluatorOfAllVehicles(transitCallbackIndex);

			// Set search parameters (using PathCheapestArc heuristic for the first solution).
			RoutingSearchParameters searchParameters = operations_research_constraint_solver.DefaultRoutingSearchParameters();
			searchParameters.FirstSolutionStrategy = FirstSolutionStrategy.Types.Value.PathCheapestArc;

			// Solve the problem.
			Assignment solution = routing.SolveWithParameters(searchParameters);
			if (solution == null)
				return null;

			// Extract the route from the solution.
			List<int> route = new List<int>();
			long index = routing.Start(0);
			while (!routing.IsEnd(index))
			{
				int node = manager.IndexToNode(index);
				route.Add(node);
				index = solution.Value(routing.NextVar(index));
			}
			// Add the final node to complete the loop.
			route.Add(manager.IndexToNode(index));

			return route;
		}
	}

	/// <summary>
	/// DTO for parsing the OSRM Table API response.
	/// </summary>
	public class OsrmTableResponse
	{
		[JsonProperty("code")]
		public string Code { get; set; }

		[JsonProperty("distances")]
		public double[][] Distances { get; set; }
	}

	/// <summary>
	/// DTO for the final route result.
	/// </summary>
	public class RouteResult2
	{
		public List<Coordinate2> Coordinates { get; set; }
		public int Distance { get; set; }
		public int Duration { get; set; }
		public List<SegmentResult2> Segments { get; set; }
		public List<int> OrderIds { get; set; }
	}

	public class SegmentResult2
	{
		public int Distance { get; set; }
		public int Duration { get; set; }
	}

	/// <summary>
	/// DTO representing a geographic coordinate.
	/// </summary>
	public class Coordinate2
	{
		public double Latitude { get; set; }
		public double Longitude { get; set; }
	}
}

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

		// Calculez ruta optima cu distance-matrix de la OSRM si apoi rezolv TSP cu OR-Tools
		// Ruta incepe si se termina la Headquarter
		public async Task<RouteResult2> CalculateOptimalRouteAsync2(Delivery delivery)
		{
			// Iau doar comenzile cu coordonate valide
			var orders = delivery.Orders?.Where(o => o.Latitude.HasValue && o.Longitude.HasValue).ToList();
			if (orders == null || orders.Count == 0)
				throw new Exception("No valid orders found for route planning.");

			// Folosesc coordonatele Headquarter-ului ca punct de start/sfarsit
			Coordinate2 headquarters = new Coordinate2
			{
				Latitude = delivery.Vehicle.Region.Headquarters.Latitude.Value,
				Longitude = delivery.Vehicle.Region.Headquarters.Longitude.Value
			};

			// Fac o lista de puncte: headquarters e la index 0, apoi comenzile
			List<Coordinate2> points = new List<Coordinate2> { headquarters };
			points.AddRange(orders.Select(o => new Coordinate2
			{
				Latitude = o.Latitude.Value,
				Longitude = o.Longitude.Value
			}));

			// Consturiesc URL-ul pentru OSRM Table API
			// OSRM se aspteapta la coordonatele in ordinea "longitude,latitude"
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
			// Convertesc distantele OSRM (in metri) intr-o matrice de intregi.
			for (int i = 0; i < numPoints; i++)
			{
				for (int j = 0; j < numPoints; j++)
				{
					distanceMatrix[i, j] = (int)Math.Round(osrmResponse.Distances[i][j]);
				}
			}

			// Rezolv TSP folosind OR-Tools
			List<int> routeOrder = SolveTSP(distanceMatrix);
			if (routeOrder == null || routeOrder.Count == 0)
				throw new Exception("TSP solver did not find a route.");

			// Calculez distanta totala pentru ruta calculata
			int totalDistance = 0;
			for (int i = 0; i < routeOrder.Count - 1; i++)
			{
				totalDistance += distanceMatrix[routeOrder[i], routeOrder[i + 1]];
			}

			// Construiesc lista de coordonate pentru ruta
			List<Coordinate2> routeCoordinates = routeOrder.Select(index => points[index]).ToList();

			// Pun id-urile comenzilor in ordinea rutei calculate
			List<int> orderIds = new List<int>();
			foreach (var index in routeOrder)
			{
				if (index > 0)
				{
					// Comenzile au fost adaugate dupa headquarters, deci scad 1 din index.
					orderIds.Add(orders[index - 1].Id);
				}
			}

			var routeResult = new RouteResult2
			{
				Coordinates = routeCoordinates,
				Distance = totalDistance,
				Duration = 0,
				Segments = new List<SegmentResult2>(),
				OrderIds = orderIds
			};

			return routeResult;
		}

		// Aici folosesc defapt OR-Tools pentru a rezolva TSP pentru matricea de distante
		private List<int> SolveTSP(int[,] distanceMatrix)
		{
			int size = distanceMatrix.GetLength(0);
			// Creez un manager pentru a face conversia intre index si noduri
			RoutingIndexManager manager = new RoutingIndexManager(size, 1, 0);
			// Creez un model de rutare
			RoutingModel routing = new RoutingModel(manager);

			// Acesta va returna distanta dintre doua noduri
			int transitCallbackIndex = routing.RegisterTransitCallback((long fromIndex, long toIndex) =>
			{
				int fromNode = manager.IndexToNode(fromIndex);
				int toNode = manager.IndexToNode(toIndex);
				return distanceMatrix[fromNode, toNode];
			});
			routing.SetArcCostEvaluatorOfAllVehicles(transitCallbackIndex);

			// Setez parametrii de cautare (folosind euristica PathCheapestArc pentru prima solutie)
			RoutingSearchParameters searchParameters = operations_research_constraint_solver.DefaultRoutingSearchParameters();
			searchParameters.FirstSolutionStrategy = FirstSolutionStrategy.Types.Value.PathCheapestArc;

			// Rezolv problema
			Assignment solution = routing.SolveWithParameters(searchParameters);
			if (solution == null)
				return null;

			// Extrag ruta din solutie
			List<int> route = new List<int>();
			long index = routing.Start(0);
			while (!routing.IsEnd(index))
			{
				int node = manager.IndexToNode(index);
				route.Add(node);
				index = solution.Value(routing.NextVar(index));
			}
			// Adaug ultimul nod pentru a completa ruta
			route.Add(manager.IndexToNode(index));

			return route;
		}
	}

	// DTO pentru parsarea raspunsului de la OSRM Table API
	public class OsrmTableResponse
	{
		[JsonProperty("code")]
		public string Code { get; set; }

		[JsonProperty("distances")]
		public double[][] Distances { get; set; }
	}

	// DTO pentru rezultatul final al rutei
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

	// DTO pt a reprezenta coordonatele geografica
	public class Coordinate2
	{
		public double Latitude { get; set; }
		public double Longitude { get; set; }
	}
}
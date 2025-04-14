using DotNetEnv;
using Newtonsoft.Json;
using System.Diagnostics;

namespace Licenta_v1.Services
{
	public class TomTomObstacleService
	{
		private readonly string TomTomAPIKey = Env.GetString("TomTomTrafficIncidentsApiKey");
		private readonly HttpClient _client = new();

		public async Task<object> GetAvoidPolygonsAsync(double minLat, double minLon, double maxLat, double maxLon)
		{
			string url = $"https://api.tomtom.com/traffic/services/5/incidentDetails" +
						 $"?key={TomTomAPIKey}" +
						 $"&bbox={minLon},{minLat},{maxLon},{maxLat}" +
						 $"&categoryFilter=1,2,3,4,5,6,7,8,9,10,11,14" +
						 $"&timeValidityFilter=present";

			var httpResponse = await _client.GetAsync(url);
			var content = await httpResponse.Content.ReadAsStringAsync();

			Debug.WriteLine("RAW INCIDENTS:");
			Debug.WriteLine(content);

			if (!httpResponse.IsSuccessStatusCode)
				throw new Exception($"TomTom API error: {httpResponse.StatusCode} - {content}");

			dynamic json = JsonConvert.DeserializeObject(content);

			var polygons = new List<AvoidPolygonWithInfo>();

			foreach (var incident in json?.incidents ?? new List<dynamic>())
			{
				var iconCategory = (int?)incident?.properties?.iconCategory ?? -1;
				string mainType = GetIncidentType(iconCategory);

				var events = incident?.properties?.events as IEnumerable<dynamic>;
				var eventDescriptionsList = (events != null)
					? events
						.Select(e => (string?)e?.description)
						.Where(desc => !string.IsNullOrWhiteSpace(desc))
						.ToList()
					: new List<string>();

				string eventDescriptions = eventDescriptionsList.Any()
					? string.Join("; ", eventDescriptionsList)
					: null;

				string rawDescription = ((string?)incident?.properties?.description)?.Trim();

				string from = (string?)incident?.properties?.from;
				string to = (string?)incident?.properties?.to;
				string roadNumbers = string.Join(", ", (incident?.properties?.roadNumbers as IEnumerable<dynamic> ?? new List<dynamic>()));

				string fallback = $"From: {from}, To: {to}, Road: {roadNumbers}".Trim();

				string finalDescription = $"{SplitCamelCase(mainType)}";

				Debug.WriteLine($"[Incident] Category: {iconCategory} ({mainType}), Events: {eventDescriptions}");

				if (incident?.geometry?.type == "LineString")
				{
					var coords = ((IEnumerable<dynamic>)incident.geometry.coordinates)
						.Select(c => new double[] { (double)c[0], (double)c[1] })
						.ToList();

					if (coords.Count >= 2)
					{
						var bufferedPolygons = BufferLineToPolygon(coords, 0.00025);
						foreach (var poly in bufferedPolygons)
						{
							polygons.Add(new AvoidPolygonWithInfo
							{
								Coordinates = bufferedPolygons,
								Description = finalDescription
							});
						}
					}
				}
			}

			return new
			{
				type = "MultiPolygon",
				coordinates = polygons.Select(p => p.Coordinates).ToList(),
				descriptions = polygons.Select(p => p.Description).ToList()
			};
		}

		private List<List<double[]>> BufferLineToPolygon(List<double[]> coords, double bufferDistance)
		{
			var geometryFactory = new NetTopologySuite.Geometries.GeometryFactory();

			var lineCoords = coords.Select(c => new NetTopologySuite.Geometries.Coordinate(c[0], c[1])).ToArray();
			var lineString = geometryFactory.CreateLineString(lineCoords);

			var polygonCoords = new List<List<double[]>>();

			var bufferedGeometry = lineString.Buffer(bufferDistance);

			if (bufferedGeometry is NetTopologySuite.Geometries.Polygon polygon)
			{
				polygonCoords.Add(
					polygon.ExteriorRing.Coordinates
						.Select(c => new double[] { c.X, c.Y })
						.ToList()
				);
			}
			else if (bufferedGeometry is NetTopologySuite.Geometries.MultiPolygon multiPolygon)
			{
				foreach (var geom in multiPolygon.Geometries)
				{
					if (geom is NetTopologySuite.Geometries.Polygon poly)
					{
						polygonCoords.Add(
							poly.ExteriorRing.Coordinates
								.Select(c => new double[] { c.X, c.Y })
								.ToList()
						);
					}
				}
			}

			return polygonCoords;
		}

		private string GetIncidentType(int category)
		{
			return category switch
			{
				0 => "Unknown",
				1 => "Accident",
				2 => "Fog",
				3 => "DangerousConditions",
				4 => "Rain",
				5 => "Ice",
				6 => "Jam",
				7 => "LaneClosed",
				8 => "RoadClosed",
				9 => "RoadWorks",
				10 => "Wind",
				11 => "Flooding",
				14 => "BrokenDownVehicle",
				_ => "Other"
			};
		}

		private string SplitCamelCase(string input)
		{
			return System.Text.RegularExpressions.Regex.Replace(input, "(\\B[A-Z])", " $1");
		}
	}

	public class AvoidPolygonWithInfo
	{
		public List<List<double[]>> Coordinates { get; set; }
		public string Description { get; set; }
	}
}

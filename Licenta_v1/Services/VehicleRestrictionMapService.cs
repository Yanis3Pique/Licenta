//using System.Net.Http;
//using System.Text.Json;
//using NetTopologySuite.Features;
//using NetTopologySuite.Geometries;
//using NetTopologySuite.IO;
//using System.Text.Json.Nodes;

//public class VehicleRestrictionMapService
//{
//	private readonly Dictionary<int, List<Geometry>> _restrictedZonesPerVehicle = new();

//	public void LoadRestrictedZones(int vehicleId, string geoJsonPath, double minLat, double minLng, double maxLat, double maxLng)
//	{
//		if (_restrictedZonesPerVehicle.ContainsKey(vehicleId))
//			return;

//		string folder = Path.GetDirectoryName(geoJsonPath);
//		if (!Directory.Exists(folder))
//			Directory.CreateDirectory(folder);

//		if (!File.Exists(geoJsonPath))
//		{
//			// 🔽 Call Overpass to fetch real restrictions
//			var geoJson = FetchRealRestrictionsFromOverpass(minLat, minLng, maxLat, maxLng).Result;
//			File.WriteAllText(geoJsonPath, geoJson);
//		}

//		using var reader = new StreamReader(geoJsonPath);
//		var geoJsonText = reader.ReadToEnd();
//		var serializer = new GeoJsonReader();
//		var features = serializer.Read<FeatureCollection>(geoJsonText);

//		_restrictedZonesPerVehicle[vehicleId] = features.Select(f => f.Geometry).ToList();
//	}

//	private async Task<string> FetchRealRestrictionsFromOverpass(double minLat, double minLng, double maxLat, double maxLng)
//	{
//		string bbox = $"{minLat},{minLng},{maxLat},{maxLng}";
//		string query = $"""
//			[out:json][timeout:25];
//			(
//			  way["hgv"="no"]({bbox});
//			  way["maxweight"]({bbox});
//			  way["maxheight"]({bbox});
//			);
//			out geom;
//		""";

//		string overpassUrl = "https://overpass-api.de/api/interpreter";

//		using var client = new HttpClient();
//		var content = new StringContent(query);
//		var response = await client.PostAsync(overpassUrl, content);
//		response.EnsureSuccessStatusCode();

//		string json = await response.Content.ReadAsStringAsync();

//		// OPTIONAL: Convert Overpass JSON to GeoJSON
//		string geoJson = OverpassToGeoJson(json);
//		return geoJson;
//	}

//	private string OverpassToGeoJson(string overpassJson)
//	{
//		return OsmToGeoJsonConverter.ConvertOverpassToGeoJson(overpassJson);
//	}

//	public bool IsPointRestrictedForVehicle(int vehicleId, double lat, double lng)
//	{
//		if (!_restrictedZonesPerVehicle.ContainsKey(vehicleId))
//			return false;

//		var point = new Point(lng, lat) { SRID = 4326 };
//		return _restrictedZonesPerVehicle[vehicleId].Any(zone => zone.Contains(point));
//	}
//}

//public static class OsmToGeoJsonConverter
//{
//	public static string ConvertOverpassToGeoJson(string overpassJson)
//	{
//		var doc = JsonNode.Parse(overpassJson);
//		var elements = doc["elements"].AsArray();

//		var features = new List<IFeature>();
//		var nodes = new Dictionary<long, Coordinate>();

//		foreach (var el in elements)
//		{
//			string type = el["type"]?.ToString();
//			if (type == "node")
//			{
//				long id = el["id"]!.GetValue<long>();
//				double lat = el["lat"]!.GetValue<double>();
//				double lon = el["lon"]!.GetValue<double>();
//				nodes[id] = new Coordinate(lon, lat);
//			}
//		}

//		foreach (var el in elements)
//		{
//			string type = el["type"]?.ToString();
//			if (type == "way" && el["geometry"] is JsonArray geometryArray)
//			{
//				var coordinates = geometryArray.Select(p =>
//					new Coordinate(p["lon"].GetValue<double>(), p["lat"].GetValue<double>())
//				).ToArray();

//				var line = new LineString(coordinates);
//				var tags = el["tags"]?.AsObject();

//				var attr = new AttributesTable();
//				if (tags != null)
//				{
//					foreach (var tag in tags)
//						attr.Add(tag.Key, tag.Value?.ToString());
//				}

//				var feature = new Feature(line, attr);
//				features.Add(feature);
//			}
//		}

//		var collection = new FeatureCollection();
//		foreach (var f in features)
//			collection.Add(f);

//		var writer = new GeoJsonWriter();
//		return writer.Write(collection);
//	}
//}
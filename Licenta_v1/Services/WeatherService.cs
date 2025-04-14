using Newtonsoft.Json;
using System.Collections.Concurrent;
using System.Diagnostics;


namespace Licenta_v1.Services
{
	public class WeatherService
	{
		private readonly string apiKey;
		private static readonly HttpClient httpClient = new();
		private static readonly ConcurrentDictionary<string, (DateTime timestamp, bool isDangerous, double severity, string description, int code)> cache = new();

		public WeatherService(IConfiguration config)
		{
			apiKey = DotNetEnv.Env.GetString("OpenWeatherMapApiKey");
		}

		public async Task<(bool isDangerous, double severity, string description, int code)> AnalyzeWeatherAsync(Coordinate coord)
		{
			string key = $"{coord.Latitude:F4},{coord.Longitude:F4}";
			var now = DateTime.UtcNow;
			var cacheDuration = TimeSpan.FromHours(1);

			if (cache.TryGetValue(key, out var cached) && now - cached.timestamp < cacheDuration)
				return (cached.isDangerous, cached.severity, cached.description, cached.code);

			string url = $"https://api.openweathermap.org/data/2.5/weather?lat={coord.Latitude}&lon={coord.Longitude}&appid={apiKey}&units=metric";
			var response = await httpClient.GetStringAsync(url);
			dynamic weather = JsonConvert.DeserializeObject(response);

			int code = weather.weather[0].id;
			double wind = weather.wind.speed;

			double severity = ComputeSeverity(code, wind);
			bool isDangerous = severity >= 0.5;

			string description = weather.weather[0].description;

			cache[key] = (now, isDangerous, severity, description, code);

			Debug.WriteLine($"Weather [{coord.Latitude}, {coord.Longitude}] => danger: {isDangerous}, severity: {severity}");

			return (isDangerous, severity, description, code);
		}

		public static double ComputeSeverity(int code, double wind)
		{
			double sev = 0.0;

			// Base severity by category
			if (code >= 200 && code <= 232)       // Thunderstorm
				sev += 0.6;
			else if (code >= 300 && code <= 321)  // Drizzle
				sev += 0.2;
			else if (code >= 500 && code <= 531)  // Rain
				sev += 0.4;
			else if (code >= 600 && code <= 622)  // Snow
				sev += 0.5;
			else if (code >= 700 && code <= 781)  // Atmosphere
				sev += 0.3;
			else if (code == 800)                 // Clear
				sev += 0.0;
			else if (code >= 801 && code <= 804)  // Clouds
				sev += 0.1;

			// Extra severity by specific codes
			if (HighSeverityCodes.Contains(code))
				sev += 0.4;
			else if (MediumSeverityCodes.Contains(code))
				sev += 0.2;

			// Wind contribution
			if (wind >= 20) sev += 0.4;
			else if (wind >= 15) sev += 0.3;
			else if (wind >= 10) sev += 0.15;

			return Math.Min(sev, 1.0);
		}

		private static readonly HashSet<int> HighSeverityCodes = new()
		{
			202, 212, 232, // heavy thunderstorm
			502, 503, 504, 511, 522, 531, // heavy rain
			602, 622, // heavy snow
			781 // tornado
		};

		private static readonly HashSet<int> MediumSeverityCodes = new()
		{
			200,201,210,211,221,230,231, // regular thunderstorm
			313,314,321, // shower drizzle
			500,501,520,521, // light to moderate rain
			600,601,611,612,613,615,616,620,621, // light/moderate snow/sleet
			701,711,721,731,741,751,761,762,771, // mist, smoke, haze, dust, squall
			800,801,802,803,804 // clouds and clear
		};
	}
}
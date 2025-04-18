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
			//var cacheDuration = TimeSpan.Zero;

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
			double severity = 0.0;

			// Codul meteo
			severity += GetCodeSeverity(code) * 0.6;

			// Efectele vantului (normalizate 0 - 1)
			severity += NormalizeWindSeverity(wind) * 0.3;

			// Bonus pentru vizibilitate redusa (ceata, fum, etc)
			if (IsLowVisibility(code))
				severity += 0.1;

			return Math.Min(severity, 1.0);
			//return 1.0;
		}

		private static double GetCodeSeverity(int code)
		{
			if (code >= 200 && code <= 232) return 0.9; // Thunderstorm
			if (code >= 300 && code <= 321) return 0.3; // Drizzle
			if (code >= 500 && code <= 504) return 0.5; // Rain (light/moderate)
			if (code == 511) return 0.8;                // Freezing rain
			if (code >= 520 && code <= 531) return 0.7; // Shower rain
			if (code >= 600 && code <= 622) return 0.7; // Snow
			if (code >= 701 && code <= 781) return 0.4; // Atmosphere (fog, dust, ...)
			if (code == 800) return 0.0;                // Clear
			if (code >= 801 && code <= 804) return 0.1; // Clouds
			return 0.4; // in caz de cod necunoscut
		}

		private static double NormalizeWindSeverity(double wind)
		{
			if (wind <= 0) return 0.0;
			if (wind >= 20) return 1.0;
			return wind / 20.0;
		}

		private static bool IsLowVisibility(int code)
		{
			return (code >= 701 && code <= 781); // mist, smoke, haze, fog, ...
		}
	}
}
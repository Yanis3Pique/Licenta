using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace Licenta_v1.Models
{
	public class AggressiveEvent
	{
		public int Id { get; set; }
		public string DriverId { get; set; }
		public int VehicleId { get; set; }
		public DateTime Timestamp { get; set; }
		public string EventType { get; set; }
		public double SeverityScore { get; set; }
		public string ProbabilitiesJson { get; set; }
		public double Latitude { get; set; }
		public double Longitude { get; set; }
		public string RoadContextJson { get; set; }

		[NotMapped]
		public double[] Probabilities
		{
			get => string.IsNullOrEmpty(ProbabilitiesJson)
				? Array.Empty<double>()
				: System.Text.Json.JsonSerializer.Deserialize<double[]>(ProbabilitiesJson);

			set => ProbabilitiesJson = System.Text.Json.JsonSerializer.Serialize(value);
		}
	}
}

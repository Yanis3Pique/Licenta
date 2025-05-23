using System;
using System.ComponentModel.DataAnnotations;

namespace Licenta_v1.Models
{
	public class TelemetryDto
	{
		[Required]
		public string DriverId { get; set; }

		[Required]
		public int VehicleId { get; set; }

		[Required]
		public DateTime Timestamp { get; set; }

		[Required]
		public double Latitude { get; set; }

		[Required]
		public double Longitude { get; set; }

		[Required]
		public double SpeedKmh { get; set; }

		[Required]
		public double HeadingDeg { get; set; }
	}
}

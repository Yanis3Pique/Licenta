using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Licenta_v1.Models
{
	public class AggressiveEvent
	{
		[Key]
		public int Id { get; set; }

		[Required]
		public string DriverId { get; set; }

		[Required]
		public int VehicleId { get; set; }

		[Required]
		public DateTime Timestamp { get; set; }

		[Required]
		[StringLength(50)]
		public string EventType { get; set; } // "HardBrake", "SharpTurn", "Speeding", etc

		[Range(0.0, 1.0)]
		public double SeverityScore { get; set; } // 0–1

		[Required]
		public double Latitude { get; set; }

		[Required]
		public double Longitude { get; set; }

		[Required]
		public string RoadContextJson { get; set; } // JSON cu informatii despre drum(limita de viteza, tip de drum, vreme, etc)

		[ForeignKey(nameof(DriverId))]
		public virtual ApplicationUser Driver { get; set; }

		[ForeignKey(nameof(VehicleId))]
		public virtual Vehicle Vehicle { get; set; }
	}
}

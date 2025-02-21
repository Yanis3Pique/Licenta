using System.ComponentModel.DataAnnotations;

namespace Licenta_v1.Models
{
	public class Delivery
	{
		[Key]
		public int Id { get; set; }
		public string? DriverId { get; set; }
		[Required(ErrorMessage = "The vehicle id is mandatory.")]
		public int VehicleId { get; set; }
		[Required(ErrorMessage = "The planned start date is mandatory.")]
		public DateTime PlannedStartDate { get; set; }
		public DateTime? ActualEndDate { get; set; }
		[Required(ErrorMessage = "The status is mandatory.")]
		public string Status { get; set; } = "Planned"; // (Planned, In Progress, Completed)
		public string? RouteData { get; set; } // JSON Route
		public double? DistanceEstimated { get; set; }
		public double? ConsumptionEstimated { get; set; }
		public double? EmissionsEstimated { get; set; }
		public double? TimeTakenForDelivery { get; set; }

		public virtual ApplicationUser? Driver { get; set; }
		public virtual Vehicle? Vehicle { get; set; }
		public virtual ICollection<Order>? Orders { get; set; }
		public virtual RouteHistory? RouteHistory { get; set; }
	}
}

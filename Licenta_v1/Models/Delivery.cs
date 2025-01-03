namespace Licenta_v1.Models
{
	public class Delivery
	{
		public int Id { get; set; }
		public string DriverId { get; set; }
		public int VehicleId { get; set; }
		public DateTime PlannedStartDate { get; set; }
		public DateTime? ActualEndDate { get; set; }
		public string Status { get; set; } = "Planned"; // (Planned, In Progress, Completed)
		public string? RouteData { get; set; } // JSON Route
		public double? EmissionsEstimated { get; set; }
		public double? DistanceEstimated { get; set; }

		public virtual ApplicationUser? Driver { get; set; }
		public virtual Vehicle? Vehicle { get; set; }
		public virtual ICollection<Order>? Orders { get; set; }
		public virtual ICollection<RouteHistory>? RouteHistories { get; set; }
	}

}

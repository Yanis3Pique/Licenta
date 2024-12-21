namespace Licenta_v1.Models
{
	public class Maintenance
	{
		public int Id { get; set; }
		public int VehicleId { get; set; }
		public string MaintenanceType { get; set; } = string.Empty;
		public DateTime ScheduledDate { get; set; }
		public DateTime? CompletedDate { get; set; }
		public string Status { get; set; } = "Scheduled";

		public virtual Vehicle? Vehicle { get; set; }
	}

}

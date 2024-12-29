using System.ComponentModel.DataAnnotations;

namespace Licenta_v1.Models
{
	public class Maintenance
	{
		[Key]
		public int Id { get; set; }
		[Required(ErrorMessage = "The vehicle is mandatory.")]
		public int VehicleId { get; set; }
		[Required(ErrorMessage = "The maintenance type is mandatory.")]
		public string MaintenanceType { get; set; } // Oil Change, Tire Change, Inspection, Repair, Other
		public DateTime ScheduledDate { get; set; }
		public DateTime? CompletedDate { get; set; }
		public string Status { get; set; } = "Scheduled";

		public virtual Vehicle? Vehicle { get; set; }
	}

}

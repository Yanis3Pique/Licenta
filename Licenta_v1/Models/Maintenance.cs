using Licenta_v1.Services;
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
		public MaintenanceTypes MaintenanceType { get; set; }
		public DateTime ScheduledDate { get; set; }
		public DateTime? CompletedDate { get; set; }
		public string Status { get; set; } = "Scheduled"; // (Scheduled, In Progress, Completed)

		public virtual Vehicle? Vehicle { get; set; }
	}
}

using System.ComponentModel.DataAnnotations.Schema;

namespace Licenta_v1.Models
{
	public class OrderVehicleRestriction
	{
		// These three properties form the composite key.
		public int OrderId { get; set; }
		public int VehicleId { get; set; }
		public string Source { get; set; } = string.Empty; // "PTV", "ORS", "Manual"

		public string Reason { get; set; } = string.Empty;
		public DateTime CreatedAt { get; set; } = DateTime.Now;
		public bool IsAccessible { get; set; }

		// Use [ForeignKey] to explicitly specify that OrderId is the FK for this navigation property.
		[ForeignKey("OrderId")] 
		public virtual Order Order { get; set; }

		// (Optionally, you can also decorate the Vehicle navigation property)
		[ForeignKey("VehicleId")]
		public virtual Vehicle Vehicle { get; set; }
	}
}

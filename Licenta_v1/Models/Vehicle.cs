namespace Licenta_v1.Models
{
	public class Vehicle
	{
		public int Id { get; set; }
		public string RegistrationNumber { get; set; } = string.Empty;
		public string Status { get; set; } = "Active"; // (Active, Maintenance)
		public string FuelType { get; set; } = string.Empty; // (Diesel, Benzine, LPG, Electric)
		public double MaxWeightCapacity { get; set; }
		public double MaxVolumeCapacity { get; set; }
		public int RegionId { get; set; }
		public double ConsumptionRate { get; set; }
		public double TotalDistanceTraveled { get; set; }

		public virtual Region? Region { get; set; }
		public virtual ICollection<Delivery>? Deliveries { get; set; }
		public virtual ICollection<Maintenance>? MaintenanceRecords { get; set; }
	}

}

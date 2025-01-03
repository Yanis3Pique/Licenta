using System.ComponentModel.DataAnnotations;

namespace Licenta_v1.Models
{
	public class Vehicle
	{
		[Key]
		public int Id { get; set; }
		[Required(ErrorMessage = "The brand is mandatory.")]
		[StringLength(50, ErrorMessage = "The brand must be maximum 50 characters in length.")]
		public string Brand { get; set; } = string.Empty;
		[Required(ErrorMessage = "The model is mandatory.")]
		[StringLength(50, ErrorMessage = "The model must be maximum 50 characters in length.")]
		public string Model { get; set; } = string.Empty;
		[Required(ErrorMessage = "The registration number is mandatory.")]
		[StringLength(10, ErrorMessage = "The registration number must be maximum 10 characters in length.")]
		[RegularExpression(@"^[A-Z]{1,2}\d{2,3}[A-Z]{3}$", ErrorMessage = "Invalid registration number. Example -> AG307NWA")]
		public string RegistrationNumber { get; set; } = string.Empty;
		[Required(ErrorMessage = "The year of manufacture is mandatory.")]
		[Range(1900, 2025, ErrorMessage = "The year of manufacture must be between 1900 and 2025.")]
		public int? YearOfManufacture { get; set; }
		[EnumDataType(typeof(VehicleStatus), ErrorMessage = "Invalid vehicle type.")]
		public VehicleStatus? Status { get; set; } = VehicleStatus.Available; // (Active, Busy, Maintenance, Retired)
		[Required(ErrorMessage = "The fuel type is mandatory.")]
		[EnumDataType(typeof(FuelType), ErrorMessage = "Invalid fuel type.")]
		public FuelType? FuelType { get; set; } // (Diesel, Petrol, LPG, Electric, Hybrid)
		[Required(ErrorMessage = "The max weight capacity is mandatory.")]
		[Range(0, double.MaxValue, ErrorMessage = "The max weight capacity must be a positive number.")]
		public double? MaxWeightCapacity { get; set; }
		[Required(ErrorMessage = "The max volume capacity is mandatory.")]
		[Range(0, double.MaxValue, ErrorMessage = "The max volume capacity must be a positive number.")]
		public double? MaxVolumeCapacity { get; set; }
		[Required(ErrorMessage = "The region is mandatory.")]
		public int? RegionId { get; set; }
		[Required(ErrorMessage = "The consumption rate is mandatory.")]
		[Range(0, double.MaxValue, ErrorMessage = "The consumption rate must be a positive number.")]
		public double? ConsumptionRate { get; set; }
		[Required(ErrorMessage = "The total distance traveled is mandatory.")]
		[Range(0, double.MaxValue, ErrorMessage = "The total distance traveled must be a positive number.")]
		public double? TotalDistanceTraveledKM { get; set; }
		public string? ImagePath { get; set; }

		public virtual Region? Region { get; set; }
		public virtual ICollection<Delivery>? Deliveries { get; set; }
		public virtual ICollection<Maintenance>? MaintenanceRecords { get; set; }


		// Urmarire mentenanta - ultima data sau distanta la care fiecare tip de revizie a fost facut
		public double LastEngineServiceKM { get; set; } = 0;
		public DateTime LastEngineServiceDate { get; set; } = DateTime.Now;

		public double LastTireChangeKM { get; set; } = 0;
		public DateTime LastTireChangeDate { get; set; } = DateTime.Now;

		public double LastBrakePadChangeKM { get; set; } = 0;

		public double LastSuspensionServiceKM { get; set; } = 0;
		public DateTime LastSuspensionServiceDate { get; set; } = DateTime.Now;

		public double LastGeneralInspectionKM { get; set; } = 0;
		public DateTime LastGeneralInspectionDate { get; set; } = DateTime.Now;

		// Pentru vehiculele electrice/hibride
		public double LastBatteryCheckKM { get; set; } = 0;
		public DateTime LastBatteryCheckDate { get; set; } = DateTime.Now;

		public double LastCoolantCheckKM { get; set; } = 0;
		public DateTime LastCoolantCheckDate { get; set; } = DateTime.Now;
	}
}

using System.ComponentModel.DataAnnotations;
using System.Reflection;

namespace Licenta_v1.Services
{
	public enum VehicleStatus
	{
		Available,
		Busy,
		Maintenance,
		Retired
	}

	public enum FuelType
	{
		Diesel,
		Petrol,
		Electric,
		Hybrid
	}

	public enum OrderPriority
	{
		Normal,
		High
	}

	public enum OrderStatus
	{
		Placed,
		InProgress,
		Delivered,
		FailedDelivery
	}

	public enum MaintenanceTypes
	{
		[Display(Name = "Battery Coolant Change")]
		BatteryCoolantChange,

		[Display(Name = "Battery Health Check")]
		BatteryHealthCheck,

		[Display(Name = "Brake Pad Replacement")]
		BrakePadReplacement,

		[Display(Name = "Engine (Oil & Filter)")]
		EngineOilFilter,

		[Display(Name = "General Inspection")]
		GeneralInspection,

		[Display(Name = "Suspension Service")]
		SuspensionService,

		[Display(Name = "Tire Replacement")]
		TireReplacement
	}

	public static class EnumExtensions
	{
		public static string GetDisplayName(this Enum value)
		{
			var field = value.GetType().GetField(value.ToString());
			var attribute = field?.GetCustomAttribute<DisplayAttribute>();
			return attribute?.GetName() ?? value.ToString();
		}
	}
}

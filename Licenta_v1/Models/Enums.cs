namespace Licenta_v1.Models
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
		InDelivery,
		Delivered
	}
}

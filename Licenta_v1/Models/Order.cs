namespace Licenta_v1.Models
{
	public class Order
	{
		public int Id { get; set; }
		public string ClientId { get; set; }
		public int? DeliveryId { get; set; }
		public string Priority { get; set; } = "Normal";
		public double Weight { get; set; }
		public double Volume { get; set; }
		public string Address { get; set; } = string.Empty;
		public double? Latitude { get; set; }
		public double? Longitude { get; set; }
		public string Status { get; set; } = "Placed"; // (Placed, InDelivery, Delivered)
		public DateTime PlacedDate { get; set; }
		public DateTime? EstimatedDeliveryDate { get; set; }
		public DateTime? DeliveredDate { get; set; }

		public virtual ApplicationUser? Client { get; set; }
		public virtual Delivery? Delivery { get; set; }
		public virtual Feedback? Feedback { get; set; }
	}

}

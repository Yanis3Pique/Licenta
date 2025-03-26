using Licenta_v1.Services;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using System.ComponentModel.DataAnnotations;

namespace Licenta_v1.Models
{
	public class Order
	{
		[Key]
		[BindNever]
		public int Id { get; set; }
		[Required(ErrorMessage = "The client id is mandatory.")]
		public string? ClientId { get; set; }
		public int? DeliveryId { get; set; }
		[Required(ErrorMessage = "The priority is mandatory.")]
		public OrderPriority? Priority { get; set; } // (Normal, High)
		[Required(ErrorMessage = "The weight is mandatory.")]
		[Range(0, int.MaxValue, ErrorMessage = "Only positive number allowed.")]
		public double? Weight { get; set; }
		[Required(ErrorMessage = "The volume is mandatory.")]
		[Range(0, int.MaxValue, ErrorMessage = "Only positive number allowed.")]
		public double? Volume { get; set; }
		[Required(ErrorMessage = "The address is mandatory.")]
		public string Address { get; set; } = string.Empty;
		[Required(ErrorMessage = "The latitude is mandatory.")]
		[Range(-90, 90, ErrorMessage = "Invalid latitude.")]
		public double? Latitude { get; set; }
		[Required(ErrorMessage = "The longitude is mandatory.")]
		[Range(-180, 180, ErrorMessage = "Invalid longitude.")]
		public double? Longitude { get; set; }
		[Required(ErrorMessage = "The region id is mandatory.")]
		public int? RegionId { get; set; }
		public OrderStatus Status { get; set; } = OrderStatus.Placed; // (Placed, InProgress, Delivered, (FailedDelivery))
		public DateTime PlacedDate { get; set; } = System.DateTime.Now;
		public DateTime? EstimatedDeliveryDate { get; set; }
		public string? EstimatedDeliveryInterval { get; set; }
		public DateTime? DeliveredDate { get; set; }
		public OrderStatus? LastNotifiedStatus { get; set; } // Folosit pentru mail-urile catre clienti
		public int? DeliverySequence { get; set; } // Folosit pentru ordinea in Delivery
		public List<int> InaccessibleHeavyVehicleIds { get; set; } = new();
		public List<int> ManuallyRestrictedVehicleIds { get; set; } = new();
		public virtual ApplicationUser? Client { get; set; }
		public virtual Region? Region { get; set; }
		public virtual Delivery? Delivery { get; set; }
		public virtual Feedback? Feedback { get; set; }
	}
}

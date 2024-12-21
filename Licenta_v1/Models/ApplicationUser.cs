using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Rendering;
using System.ComponentModel.DataAnnotations.Schema;

namespace Licenta_v1.Models
{
	public class ApplicationUser : IdentityUser
	{
		public string FirstName { get; set; }
		public string LastName { get; set; }
		public DateTime DateHired { get; set; }
		public double? AverageRating { get; set; } // 1-5
		public int? RegionId { get; set; }
		public DateTime? DismissalNoticeDate { get; set; }
		public string? PhotoPath { get; set; }

		public virtual Region? Region { get; set; }
		public virtual ICollection<Order>? Orders { get; set; }
		public virtual ICollection<Delivery>? Deliveries { get; set; }
		public virtual ICollection<Feedback>? FeedbacksGiven { get; set; }
		public virtual ICollection<Feedback>? FeedbacksReceived { get; set; }

		[NotMapped]
		public IEnumerable<SelectListItem>? AllRoles { get; set; }
	}
}
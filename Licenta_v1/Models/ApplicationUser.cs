using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Rendering;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Licenta_v1.Models
{
	public class ApplicationUser : IdentityUser
	{
		[Required(ErrorMessage = "Prenumele este obligatoriu")]
		[StringLength(50, ErrorMessage = "Prenumele trebuie sa fie de maxim 50 de caractere")]
		[MinLength(2, ErrorMessage = "Prenumele trebuie sa fie de minim 2 caractere")]
		public string FirstName { get; set; }
		[Required(ErrorMessage = "Numele este obligatoriu")]
		[StringLength(50, ErrorMessage = "Numele trebuie sa fie de maxim 50 de caractere")]
		[MinLength(2, ErrorMessage = "Numele trebuie sa fie de minim 2 caractere")]
		public string LastName { get; set; }
		[Required(ErrorMessage = "Data angajarii este obligatorie")]
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
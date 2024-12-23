using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Rendering;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Licenta_v1.Models
{
	public class ApplicationUser : IdentityUser
	{
		[Required(ErrorMessage = "The user name is mandatory.")]
		public override string UserName { get; set; }
		[Required(ErrorMessage = "The email is mandatory.")]
		[EmailAddress(ErrorMessage = "Invalid email address.")]
		public override string Email { get; set; }
		[Required(ErrorMessage = "The phone number is mandatory.")]
		[Phone(ErrorMessage = "Invalid phone number.")]
		[RegularExpression(@"^(\+4)?(07\d{8}|021\d{7}|02\d{8}|03\d{8})$", ErrorMessage = "Invalid phone number.")]
		public override string PhoneNumber { get; set; }
		[Required(ErrorMessage = "The first name is mandatory")]
		[MaxLength(50, ErrorMessage = "The first name must be maximum 50 characters in length")]
		[MinLength(2, ErrorMessage = "The first name must be minimum 2 characters in length")]
		public string FirstName { get; set; }
		[Required(ErrorMessage = "The last name is mandatory")]
		[StringLength(50, ErrorMessage = "The last name must be maximum 50 characters in length")]
		[MinLength(2, ErrorMessage = "The last name must be minimum 2 characters in length")]
		public string LastName { get; set; }
		[Required(ErrorMessage = "The hire date is mandatory")]
		public DateTime DateHired { get; set; }
		public double? AverageRating { get; set; } // 1-5
		[Required(ErrorMessage = "The region is mandatory")]
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
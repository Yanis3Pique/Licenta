using System.ComponentModel.DataAnnotations;

namespace Licenta_v1.Models
{
	public class Feedback
	{
		[Key]
		public int Id { get; set; }
		[Required(ErrorMessage = "The driver id is mandatory.")]
		public string DriverId { get; set; }
		[Required(ErrorMessage = "The client id is mandatory.")]
		public string ClientId { get; set; }
		[Required(ErrorMessage = "The order id is mandatory.")]
		public int OrderId { get; set; }
		[Required(ErrorMessage = "The rating is mandatory.")]
		public int Rating { get; set; } // 1-5
		public string? Comment { get; set; }
		[Required(ErrorMessage = "The feedback date is mandatory.")]
		public DateTime FeedbackDate { get; set; }

		public virtual ApplicationUser? Driver { get; set; }
		public virtual ApplicationUser? Client { get; set; }
		public virtual Order? Order { get; set; }
	}
}

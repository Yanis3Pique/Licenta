namespace Licenta_v1.Models
{
	public class Feedback
	{
		public int Id { get; set; }
		public string DriverId { get; set; }
		public string ClientId { get; set; }
		public int OrderId { get; set; }
		public int Rating { get; set; } // 1-5
		public string? Comment { get; set; }
		public DateTime FeedbackDate { get; set; }

		public virtual ApplicationUser? Driver { get; set; }
		public virtual ApplicationUser? Client { get; set; }
		public virtual Order? Order { get; set; }
	}

}

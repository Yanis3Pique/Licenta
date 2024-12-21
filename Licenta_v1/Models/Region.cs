namespace Licenta_v1.Models
{
	public class Region
	{
		public int Id { get; set; }
		public string County { get; set; } = string.Empty;

		public virtual Headquarter? Headquarters { get; set; }
		public virtual ICollection<ApplicationUser>? Users { get; set; }
		public virtual ICollection<Vehicle>? Vehicles { get; set; }
	}
}

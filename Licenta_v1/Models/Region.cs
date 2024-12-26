using System.ComponentModel.DataAnnotations;

namespace Licenta_v1.Models
{
	public class Region
	{
		[Key]
		public int Id { get; set; }
		[Required(ErrorMessage = "The region name is mandatory.")]
		public string County { get; set; } = string.Empty;

		public virtual Headquarter? Headquarters { get; set; }
		public virtual ICollection<ApplicationUser>? Users { get; set; }
		public virtual ICollection<Vehicle>? Vehicles { get; set; }
	}
}

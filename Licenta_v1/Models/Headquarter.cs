using System.ComponentModel.DataAnnotations;

namespace Licenta_v1.Models
{
	public class Headquarter
	{
		[Key]
		public int Id { get; set; }
		[Required(ErrorMessage = "The headquarter name is mandatory.")]
		public string Name { get; set; }
		[Required(ErrorMessage = "The headquarter address is mandatory.")]
		public string Address { get; set; }
		[Required(ErrorMessage = "The headquarter latitude is mandatory.")]
		public double Latitude { get; set; }
		[Required(ErrorMessage = "The headquarter longitude is mandatory.")]
		public double Longitude { get; set; }
		[Required(ErrorMessage = "The headquarter region is mandatory.")]
		public int RegionId { get; set; }

		public virtual Region? Region { get; set; }
	}
}

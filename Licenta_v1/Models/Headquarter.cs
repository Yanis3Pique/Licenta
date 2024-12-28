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
		[MaxLength(200, ErrorMessage = "The headquarter address must be maximum 200 characters in length.")]
		[MinLength(5, ErrorMessage = "The headquarter address must be minimum 5 characters in length.")]
		public string Address { get; set; }
		[Required(ErrorMessage = "The headquarter latitude is mandatory.")]
		[Range(-90, 90, ErrorMessage = "Invalid latitude.")]
		public double? Latitude { get; set; }
		[Required(ErrorMessage = "The headquarter longitude is mandatory.")]
		[Range(-180, 180, ErrorMessage = "Invalid longitude.")]
		public double? Longitude { get; set; }
		[Required(ErrorMessage = "The headquarter region is mandatory.")]
		public int? RegionId { get; set; }

		public virtual Region? Region { get; set; }
	}
}

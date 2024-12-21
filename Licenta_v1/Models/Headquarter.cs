namespace Licenta_v1.Models
{
	public class Headquarter
	{
		public int Id { get; set; }
		public string Name { get; set; } = string.Empty;
		public string Address { get; set; } = string.Empty;
		public double Latitude { get; set; }
		public double Longitude { get; set; }
		public int RegionId { get; set; }

		public virtual Region? Region { get; set; }
	}
}

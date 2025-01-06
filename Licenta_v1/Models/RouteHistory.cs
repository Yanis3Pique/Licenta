namespace Licenta_v1.Models
{
	public class RouteHistory
	{
		public int Id { get; set; }
		public int DeliveryId { get; set; }
		public double DistanceTraveled { get; set; }
		public double FuelConsumed { get; set; }
		public double Emissions { get; set; }
		public int TimeTaken { get; set; }
		public DateTime DateLogged { get; set; }

		public virtual Delivery? Delivery { get; set; }
	}
}

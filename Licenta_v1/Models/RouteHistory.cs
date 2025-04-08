using System;
using System.ComponentModel.DataAnnotations;

namespace Licenta_v1.Models
{
	public class RouteHistory
	{
		[Key]
		public int Id { get; set; }

		public int DeliveryId { get; set; }

		public DateTime DateLogged { get; set; }

		public double DistanceTraveled { get; set; }           // km
		public double FuelConsumed { get; set; }               // l
		public double Emissions { get; set; }                  // kg CO2
		public int TimeTaken { get; set; }                     // s

		public string RouteData { get; set; } // Json

		public int VehicleId { get; set; }
		public string VehicleDescription { get; set; }

		public string OrderIdsJson { get; set; }

		public string? DriverId { get; set; }
		public string? DriverName { get; set; }

		public virtual Delivery? Delivery { get; set; }
	}
}
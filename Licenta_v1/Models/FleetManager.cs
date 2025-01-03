namespace Licenta_v1.Models
{
	public static class FleetManager
	{
		// Diesel, Petrol, GPL, Hybrid(ICE) constante in KM si luni/ani
		private const double ENGINE_SERVICE_KM = 9600;
		private const int ENGINE_SERVICE_MONTHS = 6;
		private const double TIRE_SERVICE_KM = 48000;
		private const int TIRE_SERVICE_YEARS = 4;
		private const double BRAKE_SERVICE_KM = 80000;
		private const double SUSPENSION_SERVICE_KM = 112000;
		private const int SUSPENSION_SERVICE_YEARS = 7;
		private const double GENERAL_SERVICE_KM = 19000;
		private const int GENERAL_SERVICE_MONTHS = 12;

		// Electric / Hybrid(EV) constante in KM si luni/ani
		private const double BATTERY_CHECK_KM = 40000;
		private const int BATTERY_CHECK_YEARS = 2;
		private const double COOLANT_CHECK_KM = 80000;
		private const int COOLANT_CHECK_YEARS = 4;
		private const double EV_BRAKE_SERVICE_KM = 100000;
		// In cazuk EV-urilor, placutele de frana rezista mai mult

		public static List<Maintenance> CheckAndScheduleMaintenance(Vehicle vehicle)
		{
			var neededMaintenance = new List<Maintenance>();

			double currentKM = vehicle.TotalDistanceTraveledKM ?? 0;
			DateTime now = DateTime.Now;

			bool isElectric = (vehicle.FuelType == FuelType.Electric);
			bool isHybrid = (vehicle.FuelType == FuelType.Hybrid);

			if (isElectric || isHybrid)
			{
				CheckBatteryHealth(vehicle, currentKM, now, neededMaintenance);
				CheckCoolantHealth(vehicle, currentKM, now, neededMaintenance);
				CheckEVBrakePads(vehicle, currentKM, neededMaintenance);
				CheckSuspensionEV(vehicle, currentKM, now, neededMaintenance);
				CheckGeneralEV(vehicle, currentKM, now, neededMaintenance);
			}

			if (!isElectric || isHybrid)
			{
				CheckEngineOilFilter(vehicle, currentKM, now, neededMaintenance);
				CheckTires(vehicle, currentKM, now, neededMaintenance);
				CheckBrakePads(vehicle, currentKM, neededMaintenance);
				CheckSuspension(vehicle, currentKM, now, neededMaintenance);
				CheckGeneral(vehicle, currentKM, now, neededMaintenance);
			}

			return neededMaintenance;
		}

		// Fosil / Hybrid(Fosil)

		private static void CheckEngineOilFilter(Vehicle v, double currentKM, DateTime now, List<Maintenance> tasks)
		{
			double kmSinceLast = currentKM - v.LastEngineServiceKM;
			double monthsSince = (now - v.LastEngineServiceDate).TotalDays / 30.0;

			if (kmSinceLast >= ENGINE_SERVICE_KM || monthsSince >= ENGINE_SERVICE_MONTHS)
			{
				tasks.Add(new Maintenance
				{
					VehicleId = v.Id,
					MaintenanceType = MaintenanceTypes.EngineOilFilter,
					ScheduledDate = now.AddDays(7),
					Status = "Scheduled"
				});
			}
		}

		private static void CheckTires(Vehicle v, double currentKM, DateTime now, List<Maintenance> tasks)
		{
			double kmSinceLast = currentKM - v.LastTireChangeKM;
			double yearsSince = (now - v.LastTireChangeDate).TotalDays / 365.0;

			if (kmSinceLast >= TIRE_SERVICE_KM || yearsSince >= TIRE_SERVICE_YEARS)
			{
				tasks.Add(new Maintenance
				{
					VehicleId = v.Id,
					MaintenanceType = MaintenanceTypes.TireReplacement,
					ScheduledDate = now.AddDays(7),
					Status = "Scheduled"
				});
			}
		}

		private static void CheckBrakePads(Vehicle v, double currentKM, List<Maintenance> tasks)
		{
			double kmSinceLast = currentKM - v.LastBrakePadChangeKM;
			if (kmSinceLast >= BRAKE_SERVICE_KM)
			{
				tasks.Add(new Maintenance
				{
					VehicleId = v.Id,
					MaintenanceType = MaintenanceTypes.BrakePadReplacement,
					ScheduledDate = DateTime.Now.AddDays(7),
					Status = "Scheduled"
				});
			}
		}

		private static void CheckSuspension(Vehicle v, double currentKM, DateTime now, List<Maintenance> tasks)
		{
			double kmSinceLast = currentKM - v.LastSuspensionServiceKM;
			double yearsSince = (now - v.LastSuspensionServiceDate).TotalDays / 365.0;

			if (kmSinceLast >= SUSPENSION_SERVICE_KM || yearsSince >= SUSPENSION_SERVICE_YEARS)
			{
				tasks.Add(new Maintenance
				{
					VehicleId = v.Id,
					MaintenanceType = MaintenanceTypes.SuspensionService,
					ScheduledDate = now.AddDays(7),
					Status = "Scheduled"
				});
			}
		}

		private static void CheckGeneral(Vehicle v, double currentKM, DateTime now, List<Maintenance> tasks)
		{
			double kmSinceLast = currentKM - v.LastGeneralInspectionKM;
			double monthsSince = (now - v.LastGeneralInspectionDate).TotalDays / 30.0;

			if (kmSinceLast >= GENERAL_SERVICE_KM || monthsSince >= GENERAL_SERVICE_MONTHS)
			{
				tasks.Add(new Maintenance
				{
					VehicleId = v.Id,
					MaintenanceType = MaintenanceTypes.GeneralInspection,
					ScheduledDate = now.AddDays(7),
					Status = "Scheduled"
				});
			}
		}

		// Electrice / Hybrid(Electrice)

		private static void CheckBatteryHealth(Vehicle v, double currentKM, DateTime now, List<Maintenance> tasks)
		{
			double kmSinceLast = currentKM - v.LastBatteryCheckKM;
			double yearsSince = (now - v.LastBatteryCheckDate).TotalDays / 365.0;

			if (kmSinceLast >= BATTERY_CHECK_KM || yearsSince >= BATTERY_CHECK_YEARS)
			{
				tasks.Add(new Maintenance
				{
					VehicleId = v.Id,
					MaintenanceType = MaintenanceTypes.BatteryHealthCheck,
					ScheduledDate = now.AddDays(7),
					Status = "Scheduled"
				});
			}
		}

		private static void CheckCoolantHealth(Vehicle v, double currentKM, DateTime now, List<Maintenance> tasks)
		{
			double kmSinceLast = currentKM - v.LastCoolantCheckKM;
			double yearsSince = (now - v.LastCoolantCheckDate).TotalDays / 365.0;

			if (kmSinceLast >= COOLANT_CHECK_KM || yearsSince >= COOLANT_CHECK_YEARS)
			{
				tasks.Add(new Maintenance
				{
					VehicleId = v.Id,
					MaintenanceType = MaintenanceTypes.BatteryCoolantChange,
					ScheduledDate = now.AddDays(7),
					Status = "Scheduled"
				});
			}
		}

		private static void CheckEVBrakePads(Vehicle v, double currentKM, List<Maintenance> tasks)
		{
			double kmSinceLast = currentKM - v.LastBrakePadChangeKM;
			if (kmSinceLast >= EV_BRAKE_SERVICE_KM)
			{
				tasks.Add(new Maintenance
				{
					VehicleId = v.Id,
					MaintenanceType = MaintenanceTypes.BrakePadReplacement,
					ScheduledDate = DateTime.Now.AddDays(7),
					Status = "Scheduled"
				});
			}
		}

		private static void CheckSuspensionEV(Vehicle v, double currentKM, DateTime now, List<Maintenance> tasks)
		{
			double kmSinceLast = currentKM - v.LastSuspensionServiceKM;
			double yearsSince = (now - v.LastSuspensionServiceDate).TotalDays / 365.0;

			if (kmSinceLast >= SUSPENSION_SERVICE_KM || yearsSince >= SUSPENSION_SERVICE_YEARS)
			{
				tasks.Add(new Maintenance
				{
					VehicleId = v.Id,
					MaintenanceType = MaintenanceTypes.SuspensionService,
					ScheduledDate = now.AddDays(7),
					Status = "Scheduled"
				});
			}
		}

		private static void CheckGeneralEV(Vehicle v, double currentKM, DateTime now, List<Maintenance> tasks)
		{
			double kmSinceLast = currentKM - v.LastGeneralInspectionKM;
			double monthsSince = (now - v.LastGeneralInspectionDate).TotalDays / 30.0;

			if (kmSinceLast >= GENERAL_SERVICE_KM || monthsSince >= GENERAL_SERVICE_MONTHS)
			{
				tasks.Add(new Maintenance
				{
					VehicleId = v.Id,
					MaintenanceType = MaintenanceTypes.GeneralInspection,
					ScheduledDate = now.AddDays(7),
					Status = "Scheduled"
				});
			}
		}
	}
}

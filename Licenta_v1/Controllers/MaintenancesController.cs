using Licenta_v1.Data;
using Licenta_v1.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SendGrid.Helpers.Mail;

namespace Licenta_v1.Controllers
{
	[Authorize(Roles = "Admin,Dispecer")]
	public class MaintenancesController : Controller
	{
		private readonly ApplicationDbContext db;
		private readonly UserManager<ApplicationUser> _userManager;
		private readonly RoleManager<IdentityRole> _roleManager;

		public MaintenancesController(
			ApplicationDbContext context,
			UserManager<ApplicationUser> userManager,
			RoleManager<IdentityRole> roleManager
			)
		{
			db = context;
			_userManager = userManager;
			_roleManager = roleManager;
		}

		// Get - Maintenance/Index
		[Authorize(Roles = "Admin")]
		public IActionResult Index(string searchString, string sortOrder)
		{
			var tasks = db.Maintenances
				.Include(m => m.Vehicle)
				.Where(m => m.Status == "Scheduled" || m.Status == "In Progress" || m.Status == "Completed");

			// Search-ul se petrece aci
			if (!string.IsNullOrEmpty(searchString))
			{
				tasks = tasks.Where(m =>
					(m.Vehicle.Brand != null && m.Vehicle.Brand.Contains(searchString)) ||
					(m.Vehicle.Model != null && m.Vehicle.Model.Contains(searchString)) ||
					(m.Vehicle.RegistrationNumber != null && m.Vehicle.RegistrationNumber.Contains(searchString)) ||
					m.MaintenanceType.ToString().Contains(searchString) ||
					m.Status.Contains(searchString));
			}

			// Sortarea se petrece aci
			tasks = sortOrder switch
			{
				"vehicle" => tasks.OrderBy(m => m.Vehicle.Brand),
				"vehicle_desc" => tasks.OrderByDescending(m => m.Vehicle.Brand),
				"type" => tasks.OrderBy(m => m.MaintenanceType),
				"type_desc" => tasks.OrderByDescending(m => m.MaintenanceType),
				"scheduled_date" => tasks.OrderBy(m => m.ScheduledDate),
				"scheduled_date_desc" => tasks.OrderByDescending(m => m.ScheduledDate),
				"status" => tasks.OrderBy(m => m.Status),
				"status_desc" => tasks.OrderByDescending(m => m.Status),
				_ => tasks.OrderBy(m => m.ScheduledDate),
			};

			return View(tasks.ToList());
		}


		// Get - Maintenance/Complete/id
		public IActionResult Complete(int id)
		{
			var record = db.Maintenances
				.Include(m => m.Vehicle)
				.FirstOrDefault(m => m.Id == id);

			if (record == null) return NotFound();

			return View(record);
		}

		// Post - Maintenance/Complete/id
		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Roles = "Admin")]
		public IActionResult CompleteConfirmed(int id)
		{
			var record = db.Maintenances
				.Include(m => m.Vehicle)
				.FirstOrDefault(m => m.Id == id);

			if (record == null) return NotFound();

			// Ma asigur ca mentenanta poate fi completata doar dupa data programata
			if (record.ScheduledDate.Date > DateTime.Now.Date)
			{
				TempData["Error"] = "Maintenance cannot be completed before the scheduled date.";
				return RedirectToAction(nameof(Complete), new { id });
			}

			// Marchez mentenanta ca fiind completata
			record.Status = "Completed";
			record.CompletedDate = DateTime.Now;

			// Updatez ultima mentenanta efectuata pe vehicul
			var vehicle = record.Vehicle;
			if (vehicle != null)
			{
				double currentKM = vehicle.TotalDistanceTraveledKM ?? 0;
				DateTime now = DateTime.Now;

				switch (record.MaintenanceType)
				{
					case MaintenanceTypes.EngineOilFilter:
						vehicle.LastEngineServiceKM = currentKM;
						vehicle.LastEngineServiceDate = now;
						break;
					case MaintenanceTypes.TireReplacement:
						vehicle.LastTireChangeKM = currentKM;
						vehicle.LastTireChangeDate = now;
						break;
					case MaintenanceTypes.BrakePadReplacement:
						vehicle.LastBrakePadChangeKM = currentKM;
						break;
					case MaintenanceTypes.SuspensionService:
						vehicle.LastSuspensionServiceKM = currentKM;
						vehicle.LastSuspensionServiceDate = now;
						break;
					case MaintenanceTypes.GeneralInspection:
						vehicle.LastGeneralInspectionKM = currentKM;
						vehicle.LastGeneralInspectionDate = now;
						break;
					case MaintenanceTypes.BatteryHealthCheck:
						vehicle.LastBatteryCheckKM = currentKM;
						vehicle.LastBatteryCheckDate = now;
						break;
					case MaintenanceTypes.BatteryCoolantChange:
						vehicle.LastCoolantCheckKM = currentKM;
						vehicle.LastCoolantCheckDate = now;
						break;
				}
			}

			db.SaveChanges();

			TempData["Success"] = "Maintenance completed successfully!";
			return RedirectToAction(nameof(Index));
		}


		// Get - Maintenance/VehicleMaintenances/id
		[Authorize(Roles = "Admin,Dispecer")]
		public async Task<IActionResult> VehicleMaintenances(int vehicleId)
		{
			// Verific daca exista vehiculul
			var vehicle = await db.Vehicles.FindAsync(vehicleId);
			if (vehicle == null)
			{
				TempData["Error"] = "Vehicle not found!";
				return RedirectToAction("Index", "Vehicles");
			}

			// Iau toate programarile la mentenanta ale vehiculului
			var maintenances = await db.Maintenances
				.Where(m => m.VehicleId == vehicleId)
				.OrderByDescending(m => m.ScheduledDate)
				.ToListAsync();

			ViewBag.Vehicle = vehicle;
			return View(maintenances);
		}

	}
}

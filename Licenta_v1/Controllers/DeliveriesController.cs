using System.Linq;
using Licenta_v1.Data;
using Licenta_v1.Models;
using Licenta_v1.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace Licenta_v1.Controllers
{
	public class DeliveriesController : Controller
	{
		private readonly ApplicationDbContext db;
		private readonly OrderDeliveryOptimizer opt;

		public DeliveriesController(ApplicationDbContext context, OrderDeliveryOptimizer optimizer)
		{
			db = context;
			opt = optimizer;
		}

		[Authorize(Roles = "Admin,Dispecer")]
		public async Task<IActionResult> Index(
			string searchString,
			DateTime? plannedStartDate,
			DateTime? actualEndDate,
			int? regionId,
			string status)
		{
			// Obtinem utilizatorul curent
			var user = await db.ApplicationUsers.FirstOrDefaultAsync(u => u.UserName == User.Identity.Name);
			if (user == null)
			{
				return Unauthorized(); // Utilizatorul trebuie sa fie autentificat
			}

			// Adminii vad toate livrarile; Dispecerii vad doar livrarile din regiunea lor
			var deliveriesQuery = db.Deliveries
				.Include(d => d.Vehicle)
				.Include(d => d.Driver)
				.Include(d => d.Orders)
				.ThenInclude(o => o.Region)
				.Where(d => User.IsInRole("Admin") || d.Vehicle.RegionId == user.RegionId)
				.AsQueryable();

			// Search (sofer, marca/model/nr inmatriculare masina)
			if (!string.IsNullOrEmpty(searchString))
			{
				string lowerSearch = searchString.ToLower();
				deliveriesQuery = deliveriesQuery.Where(d =>
					(d.Driver != null && d.Driver.UserName.ToLower().Contains(lowerSearch)) ||
					((d.Vehicle.Brand.ToLower() + " " + d.Vehicle.Model.ToLower()).Contains(lowerSearch)) ||
					d.Vehicle.RegistrationNumber.ToLower().Contains(lowerSearch));
			}

			// Filtrare dupa PlannedStartDate
			if (plannedStartDate.HasValue)
			{
				deliveriesQuery = deliveriesQuery.Where(d => d.PlannedStartDate.Date == plannedStartDate.Value.Date);
			}

			// Filtrare dupa ActualEndDate
			if (actualEndDate.HasValue)
			{
				deliveriesQuery = deliveriesQuery.Where(d => d.ActualEndDate.HasValue && d.ActualEndDate.Value.Date == actualEndDate.Value.Date);
			}

			// Filtrare dupa Region
			if (regionId.HasValue)
			{
				deliveriesQuery = deliveriesQuery.Where(d => d.Vehicle.RegionId == regionId.Value);
			}

			// Filtrare dupa Delivery Status
			if (!string.IsNullOrEmpty(status) && status != "All")
			{
				string lowerStatus = status.ToLower();
				deliveriesQuery = deliveriesQuery.Where(d => d.Status.ToLower() == lowerStatus);
			}

			ViewBag.Regions = new SelectList(db.Regions, "Id", "County");
			ViewBag.RegionId = regionId;
			ViewBag.SearchString = searchString;
			ViewBag.PlannedStartDate = plannedStartDate?.ToString("yyyy-MM-dd");
			ViewBag.ActualEndDate = actualEndDate?.ToString("yyyy-MM-dd");
			ViewBag.Status = status;

			var deliveries = await deliveriesQuery.OrderByDescending(d => d.PlannedStartDate).ToListAsync();

			return View(deliveries);
		}

		[Authorize(Roles = "Admin,Dispecer,Sofer")]
		public IActionResult Show(int id)
		{
			var user = db.ApplicationUsers.FirstOrDefault(u => u.UserName == User.Identity.Name);
			
			if (user == null)
			{
				return Unauthorized(); // Trebuie sa fie un user autentificat
			}

			var delivery = db.Deliveries       // Adminii vad detaliile tuturor deliveries. Soferii vad doar Deliveries asignate lor.
				.Include(d => d.Vehicle)       // Dispecerii vad doar detaliile deliveries din regiunea lor.
				.ThenInclude(v => v.Region)    // Ca sa avem acces si la regiune prin Vehicle.
				.Include(d => d.Driver)
				.Include(d => d.Orders)
				.ThenInclude(o => o.Client)
				.FirstOrDefault(d => d.Id == id &&
					(User.IsInRole("Admin") ||
					(User.IsInRole("Dispecer") && d.Vehicle.RegionId == user.RegionId) ||
					(User.IsInRole("Sofer") && (d.DriverId == null || d.DriverId == user.Id))));

			if (delivery == null)
				return NotFound();

			ViewBag.CurrentUserId = user.Id;

			return View(delivery);
		}

		[Authorize(Roles = "Admin")]
		public async Task<IActionResult> OptimizeAll()
		{
			DateTime now = DateTime.Now;
			DateTime nextAllowedTime = now.Date.AddDays(1).AddHours(16); // urmatoarea zi la 16:00

			if (now < nextAllowedTime) // Intentia e de a folosi serviciul la finalul programului
			{
				TempData["Error"] = "Optimizarea nu este permisa pana la ora 16:00 a zilei urmatoare.";
				return RedirectToAction("Index");
			}

			await opt.RunDailyOptimization(); // Adminii optimizeaza toate regiunile
			return RedirectToAction("Index");
		}

		[Authorize(Roles = "Dispecer")]
		public async Task<IActionResult> OptimizeRegion()
		{
			DateTime now = DateTime.Now;
			DateTime nextAllowedTime = now.Date.AddDays(1).AddHours(16); // urmatoarea zi la 16:00

			if (now < nextAllowedTime) // Intentia e de a folosi serviciul la finalul programului
			{
				TempData["Error"] = "Optimizarea nu este permisa pana la ora 16:00 a zilei urmatoare.";
				return RedirectToAction("Index");
			}

			var user = db.ApplicationUsers.FirstOrDefault(u => u.UserName == User.Identity.Name);

			if (user?.RegionId == null)
			{
				TempData["Error"] = "You don't have a region assigned to you!";
				return RedirectToAction("Index");
			}

			await opt.RunDailyOptimization(user.RegionId); // Dispecerii optimizeaza regiunea lor
			return RedirectToAction("Index");
		}

		[Authorize(Roles = "Admin,Dispecer,Sofer")]
		public async Task<IActionResult> ShowDeliveriesOfDriver(
			string id,
			string searchString,
			DateTime? deliveryDate,
			string statusFilter,
			string sortOrder,
			int pageNumber = 1,
			int pageSize = 6)
		{
			if (string.IsNullOrEmpty(id))
			{
				return NotFound("Driver ID is required.");
			}

			var driver = await db.Users.FirstOrDefaultAsync(u => u.Id == id);
			if (driver == null)
			{
				return NotFound("Driver not found.");
			}

			var deliveriesQuery = db.Deliveries
				.Include(d => d.Vehicle)
				.Include(d => d.Orders)
				.Where(d => d.DriverId == id || d.DriverId == null &&
							d.Vehicle.RegionId == driver.RegionId)
				.AsQueryable();

			// Filtrare dupa numar de inmatriculare sau Brand + Model
			if (!string.IsNullOrEmpty(searchString))
			{
				string lowerSearch = searchString.ToLower();
				deliveriesQuery = deliveriesQuery.Where(d =>
					(d.Vehicle.Brand + " " + d.Vehicle.Model).ToLower().Contains(lowerSearch) ||
					d.Vehicle.RegistrationNumber.ToLower().Contains(lowerSearch));
			}

			// Filtrare dupa data, fara a lua in considerare ora
			if (deliveryDate.HasValue)
			{
				deliveriesQuery = deliveriesQuery.Where(d =>
					d.PlannedStartDate.Date == deliveryDate.Value.Date);
			}

			// Filtrare dupa status
			if (!string.IsNullOrEmpty(statusFilter) && statusFilter != "All")
			{
				string lowerStatusFilter = statusFilter.ToLower();
				deliveriesQuery = deliveriesQuery.Where(d => d.Status.ToLower() == lowerStatusFilter);
			}

			// Sortarea dupa data sau status
			switch (sortOrder)
			{
				case "date_asc":
					deliveriesQuery = deliveriesQuery.OrderBy(d => d.PlannedStartDate);
					break;
				case "date_desc":
					deliveriesQuery = deliveriesQuery.OrderByDescending(d => d.PlannedStartDate);
					break;
				case "status_asc":
					deliveriesQuery = deliveriesQuery.OrderBy(d => d.Status);
					break;
				case "status_desc":
					deliveriesQuery = deliveriesQuery.OrderByDescending(d => d.Status);
					break;
				default:
					deliveriesQuery = deliveriesQuery.OrderByDescending(d => d.PlannedStartDate);
					break;
			}

			// Paginarea
			int totalDeliveries = await deliveriesQuery.CountAsync();
			var deliveries = await deliveriesQuery
				.Skip((pageNumber - 1) * pageSize)
				.Take(pageSize)
				.ToListAsync();

			ViewBag.Driver = driver;
			ViewBag.SearchString = searchString;
			ViewBag.DeliveryDate = deliveryDate?.ToString("yyyy-MM-dd");
			ViewBag.StatusFilter = statusFilter;
			ViewBag.SortOrder = sortOrder;
			ViewBag.PageNumber = pageNumber;
			ViewBag.PageSize = pageSize;
			ViewBag.TotalPages = (int)Math.Ceiling(totalDeliveries / (double)pageSize);

			return View(deliveries);
		}

		[Authorize(Roles = "Sofer")]
		public IActionResult StartDelivery(int id)
		{
			var user = db.ApplicationUsers.FirstOrDefault(u => u.UserName == User.Identity.Name);
			if (user == null) return Unauthorized();

			// Verific daca soferul are deja o livrare in curs
			var existingDelivery = db.Deliveries
				.FirstOrDefault(d => d.DriverId == user.Id && d.Status == "In Progress");

			if (existingDelivery != null)
			{
				TempData["Error"] = "You already have an ongoing delivery!";
				return RedirectToAction("Show", new { id });
			}

			var delivery = db.Deliveries
				.Include(d => d.Vehicle)
				.Include(d => d.Orders)
				.FirstOrDefault(d => d.Id == id && d.DriverId == user.Id);

			if (delivery == null || delivery.Status != "Planned") return NotFound();

			// Actualizez statusul User, Vehicle, Delivery si al Orders
			user.IsAvailable = false;
			delivery.Vehicle.Status = VehicleStatus.Busy;
			delivery.Status = "In Progress";

			foreach (var order in delivery.Orders)
			{
				order.Status = OrderStatus.InProgress;
			}

			db.SaveChanges();
			return RedirectToAction("Show", new { id });
		}

		[Authorize(Roles = "Sofer")]
		public IActionResult MarkOrderDelivered(int orderId)
		{
			var user = db.ApplicationUsers.FirstOrDefault(u => u.UserName == User.Identity.Name);
			if (user == null) return Unauthorized();

			var order = db.Orders.Include(o => o.Delivery)
								 .FirstOrDefault(o => o.Id == orderId && o.Delivery.DriverId == user.Id);

			if (order == null || order.Status != OrderStatus.InProgress) return NotFound();

			// Marchez Order ca fiind livrata
			order.Status = OrderStatus.Delivered;
			order.DeliveredDate = DateTime.Now;

			db.SaveChanges();
			return RedirectToAction("Show", new { id = order.DeliveryId });
		}

		[Authorize(Roles = "Sofer")]
		public IActionResult CompleteDelivery(int id)
		{
			var user = db.ApplicationUsers.FirstOrDefault(u => u.UserName == User.Identity.Name);
			if (user == null) return Unauthorized();

			var delivery = db.Deliveries
				.Include(d => d.Vehicle)
				.Include(d => d.Orders)
				.Include(d => d.Driver)
				.FirstOrDefault(d => d.Id == id && d.DriverId == user.Id);

			if (delivery == null || delivery.Status != "In Progress") return NotFound();

			// Ma asigur ca toate Orders din Delivery au fost livrate
			if (delivery.Orders.Any(o => o.Status != OrderStatus.Delivered))
			{
				TempData["Error"] = "Not all orders have been delivered yet!";
				return RedirectToAction("Show", new { id });
			}

			// Marchez Delivery ca fiind completa
			delivery.Status = "Completed";
			delivery.ActualEndDate = DateTime.Now;

			// Fac soferul si masina disponibili din nou
			user.IsAvailable = true;
			delivery.Vehicle.Status = VehicleStatus.Available;

			db.SaveChanges();
			return RedirectToAction("Show", new { id });
		}

		[Authorize(Roles = "Sofer")]
		public IActionResult ClaimDelivery(int id, double? lat, double? lon)
		{
			var user = db.ApplicationUsers.FirstOrDefault(u => u.UserName == User.Identity.Name);
			if (user == null) return Unauthorized();

			System.Diagnostics.Debug.WriteLine($"Received ClaimDelivery Request: ID={id}, Lat={lat}, Lon={lon}");

			if (id <= 0)
			{
				TempData["Error"] = "Invalid delivery ID!";
				return RedirectToAction("ShowDeliveriesOfDriver",  new { user.Id });
			}

			if (!lat.HasValue || !lon.HasValue)
			{
				TempData["Error"] = "Invalid location coordinates!";
				return RedirectToAction("ShowDeliveriesOfDriver", new { user.Id });
			}

			double driverLat = lat.Value;
			double driverLon = lon.Value;

			bool hasUnfinishedDeliveries = db.Deliveries
				.Where(d => d.DriverId == user.Id)
				.Any(d => d.Status != "Completed");

			if (hasUnfinishedDeliveries)
			{
				TempData["Error"] = "You must complete all your current deliveries before claiming a new one!";
				return RedirectToAction("ShowDeliveriesOfDriver", new { user.Id });
			}

			var delivery = db.Deliveries
				.Include(d => d.Vehicle)
				.Include(d => d.Orders)
				.FirstOrDefault(d => d.Id == id && d.DriverId == null);

			if (delivery == null)
			{
				TempData["Error"] = "This delivery is no longer available!";
				return RedirectToAction("ShowDeliveriesOfDriver", new { user.Id });
			}

			// Iau Headquarter-ul regiunii soferului
			var headquarter = db.Headquarters.FirstOrDefault(h => h.RegionId == user.RegionId);
			if (headquarter == null)
			{
				TempData["Error"] = "No headquarter found for your region!";
				return RedirectToAction("ShowDeliveriesOfDriver", new { user.Id });
			}

			// Calculez distanta intre locatia Soferului si Headquarter
			double distance = HaversineDistance(headquarter.Latitude.Value, headquarter.Longitude.Value, lat.Value, lon.Value);

			if (distance > 1.0) // Soferul trebuie sa fie in raza de 1 km fata de Headquarter
			{
				TempData["Error"] = "You must be at the headquarter to claim this delivery!";
				return RedirectToAction("ShowDeliveriesOfDriver", new { user.Id });
			}

			// Asignez Delivery-ul soferului
			delivery.DriverId = user.Id;
			delivery.Status = "Planned";
			user.IsAvailable = false;

			db.SaveChanges();
			TempData["Success"] = "Delivery successfully claimed!";
			return RedirectToAction("ShowDeliveriesOfDriver", new { id = user.Id });
		}

		private double HaversineDistance(double lat1, double lon1, double lat2, double lon2)
		{
			double earthRadiusKm = 6371.0; // Raza medie a Pamantului in km

			double dLat = (lat2 - lat1) * (Math.PI / 180);
			double dLon = (lon2 - lon1) * (Math.PI / 180);

			double a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
					   Math.Cos(lat1 * (Math.PI / 180)) * Math.Cos(lat2 * (Math.PI / 180)) *
					   Math.Sin(dLon / 2) * Math.Sin(dLon / 2);

			double c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
			return earthRadiusKm * c;
		}
	}
}

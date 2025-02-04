using System.Linq;
using Licenta_v1.Data;
using Licenta_v1.Models;
using Licenta_v1.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
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
		public IActionResult Index()
		{
			var user = db.ApplicationUsers.FirstOrDefault(u => u.UserName == User.Identity.Name);

			if (user == null)
			{
				return Unauthorized(); // Trebuie sa fie un user autentificat
			}

			var deliveries = db.Deliveries     // Adminii vad toate deliveries.
				.Include(d => d.Vehicle)       // Dispecerii vad doar deliveries din regiunea lor.
				.Include(d => d.Driver)
				.Include(d => d.Orders)
				.ThenInclude(o => o.Region)
				.Where(d => User.IsInRole("Admin") || d.Vehicle.RegionId == user.RegionId)
				.ToList();

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
		public IActionResult OptimizeAll()
		{
			opt.RunDailyOptimization(); // Adminii optimizează toate regiunile
			return RedirectToAction("Index");
		}

		[Authorize(Roles = "Dispecer")]
		public IActionResult OptimizeRegion()
		{
			var user = db.ApplicationUsers.FirstOrDefault(u => u.UserName == User.Identity.Name);

			if (user?.RegionId == null)
			{
				TempData["Error"] = "Nu ai o regiune asignată!";
				return RedirectToAction("Index");
			}

			opt.RunDailyOptimization(user.RegionId); // Dispecerii optimizează doar regiunea lor
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
				.Where(d => d.DriverId == id || d.DriverId == null)
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
		public IActionResult ClaimDelivery(int id)
		{
			if (id <= 0)
			{
				TempData["Error"] = "Invalid delivery ID!";
				return RedirectToAction("Index");
			}

			var user = db.ApplicationUsers.FirstOrDefault(u => u.UserName == User.Identity.Name);
			if (user == null) return Unauthorized();

			bool hasUnfinishedDeliveries = db.Deliveries
				.Any(d => d.DriverId == user.Id && d.Status != "Completed");

			if (hasUnfinishedDeliveries)
			{
				TempData["Error"] = "You must complete all your current deliveries before claiming a new one!";
				return RedirectToAction("ShowDeliveriesOfDriver", new { user.Id });
			}

			var delivery = db.Deliveries
				.Include(d => d.Vehicle)
				.Include(d => d.Orders)
				.FirstOrDefault(d => d.Id == id && d.DriverId == null); // Doar Deliveries care nu au fost deja luate

			if (delivery == null)
			{
				TempData["Error"] = "This delivery is no longer available!";
				return RedirectToAction("ShowDeliveriesOfDriver", new { user.Id });
			}

			// Pun soferul la Delivery si actualizez statusul
			delivery.DriverId = user.Id;
			delivery.Status = "Planned";
			user.IsAvailable = false;

			db.SaveChanges();
			TempData["Success"] = "Delivery successfully claimed!";
			return RedirectToAction("ShowDeliveriesOfDriver", new { user.Id });
		}
	}
}

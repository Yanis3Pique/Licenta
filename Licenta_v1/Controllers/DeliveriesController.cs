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

			var deliveries = db.Deliveries     // Adminii vad toate deliveries.
				.Include(d => d.Vehicle)       // Dispecerii vad doar deliveries din regiunea lor.
				.Include(d => d.Driver)
				.Where(d => User.IsInRole("Admin") || d.Vehicle.RegionId == user.RegionId)
				.ToList();

			return View(deliveries);
		}

		[Authorize(Roles = "Admin,Dispecer")]
		public IActionResult Show(int id)
		{
			var user = db.ApplicationUsers.FirstOrDefault(u => u.UserName == User.Identity.Name);

			var delivery = db.Deliveries       // Adminii vad detaliile tuturor deliveries.
				.Include(d => d.Vehicle)       // Dispecerii vad doar detaliile deliveries din regiunea lor.
				.ThenInclude(v => v.Region)    // Ca sa avem acces si la regiune prin Vehicle.
				.Include(d => d.Driver)
				.Include(d => d.Orders)
				.FirstOrDefault(d => d.Id == id && (User.IsInRole("Admin") || d.Vehicle.RegionId == user.RegionId));

			if (delivery == null)
				return NotFound();

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
				.Where(d => d.DriverId == id)
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
	}
}

using Licenta_v1.Data;
using Licenta_v1.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace Licenta_v1.Controllers
{
	public class OrdersController : Controller
	{
		private readonly ApplicationDbContext db;
		private readonly UserManager<ApplicationUser> _userManager;
		private readonly RoleManager<IdentityRole> _roleManager;

		public OrdersController(
			ApplicationDbContext context,
			UserManager<ApplicationUser> userManager,
			RoleManager<IdentityRole> roleManager
			)
		{
			db = context;
			_userManager = userManager;
			_roleManager = roleManager;
		}

		[NonAction]
		private async Task<(List<Order>, int Count)> GetFilteredOrders(
			string searchString,
			int? regionId,
			string sortOrder,
			int pageNumber,
			int pageSize,
			string userRole,
			string userId)
		{
			var orders = db.Orders.Include(o => o.Client).Include(o => o.Delivery).Include(o => o.Feedback).AsQueryable();

			// Filtrez comenzile in functie de rolul utilizatorului
			if (userRole == "Client")
			{
				orders = orders.Where(o => o.ClientId == userId); // Clientul poate vedea doar comenzile sale
			}

			// Caut comenzi dupa numele clientului, prioritate, greutate, volum, adresa, status, data plasarii
			if (!string.IsNullOrEmpty(searchString))
			{
				orders = orders.Where(o =>
					o.Client.UserName.Contains(searchString) ||
					o.Priority.ToString().Contains(searchString) ||
					o.Weight.ToString().Contains(searchString) ||
					o.Volume.ToString().Contains(searchString) ||
					o.Address.Contains(searchString) ||
					o.Status.ToString().Contains(searchString) ||
					o.PlacedDate.ToString().Contains(searchString));
			}

			// Filtrez comenzile dupa judet
			if (regionId.HasValue)
			{
				orders = orders.Where(o => o.RegionId == regionId);
			}

			// Sortarea propriu-zisa dupa numele clientului, prioritate, greutate, volum, adresa, status, data plasarii
			orders = sortOrder switch
			{
				"client" => orders.OrderBy(o => o.Client.UserName),
				"client_desc" => orders.OrderByDescending(o => o.Client.UserName),
				"priority" => orders.OrderBy(o => o.Priority),
				"priority_desc" => orders.OrderByDescending(o => o.Priority),
				"weight" => orders.OrderBy(o => o.Weight),
				"weight_desc" => orders.OrderByDescending(o => o.Weight),
				"volume" => orders.OrderBy(o => o.Volume),
				"volume_desc" => orders.OrderByDescending(o => o.Volume),
				"address" => orders.OrderBy(o => o.Address),
				"address_desc" => orders.OrderByDescending(o => o.Address),
				"status" => orders.OrderBy(o => o.Status),
				"status_desc" => orders.OrderByDescending(o => o.Status),
				"placedDate" => orders.OrderBy(o => o.PlacedDate),
				"placedDate_desc" => orders.OrderByDescending(o => o.PlacedDate),
				_ => orders.OrderBy(o => o.Client.UserName),
			};

			var count = await orders.CountAsync();
			var pagedOrders = await orders.Skip((pageNumber - 1) * pageSize).Take(pageSize).ToListAsync();

			return (pagedOrders, count);
		}

		// Get - Orders/Index
		[Authorize(Roles = "Admin, Client")]
		public async Task<IActionResult> Index(string searchString, int? regionId, string sortOrder, int pageNumber = 1)
		{
			int pageSize = 6;

			ViewBag.CurrentSort = sortOrder;
			ViewBag.ClientSortParam = sortOrder == "client" ? "client_desc" : "client";
			ViewBag.PrioritySortParam = sortOrder == "priority" ? "priority_desc" : "priority";
			ViewBag.WeightSortParam = sortOrder == "weight" ? "weight_desc" : "weight";
			ViewBag.VolumeSortParam = sortOrder == "volume" ? "volume_desc" : "volume";
			ViewBag.AddressSortParam = sortOrder == "address" ? "address_desc" : "address";
			ViewBag.StatusSortParam = sortOrder == "status" ? "status_desc" : "status";
			ViewBag.PlacedDateSortParam = sortOrder == "placedDate" ? "placedDate_desc" : "placedDate";

			ViewBag.SearchString = searchString;
			ViewBag.RegionId = regionId;
			ViewBag.Regions = new SelectList(db.Regions, "Id", "County");

			// Iau id-ul utilizatorului curent si rolul acestuia
			var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
			var userRoles = await _userManager.GetRolesAsync(await _userManager.FindByIdAsync(userId));
			var userRole = userRoles.Contains("Admin") ? "Admin" : "Client";

			// Iau comenzile filtrate si numarul total de comenzi pt paginare
			var (pagedOrders, count) = await GetFilteredOrders(searchString, regionId, sortOrder, pageNumber, pageSize, userRole, userId);

			ViewBag.PageNumber = pageNumber;
			ViewBag.TotalPages = (int)Math.Ceiling(count / (double)pageSize);

			return View(pagedOrders);
		}

		// Get - Orders/Create
		[Authorize(Roles = "Admin,Client")]
		public IActionResult Create()
		{
			ViewBag.Regions = new SelectList(db.Regions, "Id", "County");

			ViewBag.Priorities = Enum.GetValues(typeof(OrderPriority))
								   .Cast<OrderPriority>()
								   .Select(s => new SelectListItem
								   {
									   Text = s.ToString(),
									   Value = ((int)s).ToString()
								   });

			return View();
		}

		// Post - Orders/Create
		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Roles = "Admin,Client")]
		public async Task<IActionResult> Create(Order order)
		{
			// Iau id-ul utilizatorului curent si il setez ca ClientId al comenzii
			order.ClientId = _userManager.GetUserId(User);

			if (ModelState.IsValid)
			{
				// Verific daca adresa este valida si din Romania
				if (!IsValidAddressInRomania(order.Address))
				{
					TempData["Error"] = "Invalid address. Please select an address from Romania.";
					ViewBag.Regions = new SelectList(db.Regions, "Id", "County");
					ViewBag.Priorities = Enum.GetValues(typeof(OrderPriority))
											 .Cast<OrderPriority>()
											 .Select(s => new SelectListItem
											 {
												 Text = s.ToString(),
												 Value = ((int)s).ToString()
											 });
					return View(order);
				}

				// Iau regiunea din baza de date care se potriveste cu judetul comenzii
				var region = await db.Regions.FirstOrDefaultAsync(r => r.County == order.Region.County);
				if (region == null)
				{
					TempData["Error"] = "The specified region does not exist.";
					ViewBag.Regions = new SelectList(db.Regions, "Id", "County");
					ViewBag.Priorities = Enum.GetValues(typeof(OrderPriority))
											 .Cast<OrderPriority>()
											 .Select(s => new SelectListItem { Text = s.ToString(), Value = ((int)s).ToString() }); return View(order);
				}
				// Setez regiunea comenzii
				order.RegionId = region.Id;

				db.Orders.Add(order);
				await db.SaveChangesAsync();

				TempData["Success"] = "Order created successfully.";
				return RedirectToAction("Index");
			}

			// Daca modelul nu e valid, afisez din nou formularul cu erorile
			ViewBag.Regions = new SelectList(db.Regions, "Id", "County");
			ViewBag.Priorities = Enum.GetValues(typeof(OrderPriority))
									 .Cast<OrderPriority>()
									 .Select(s => new SelectListItem
									 {
										 Text = s.ToString(),
										 Value = ((int)s).ToString()
									 });
			TempData["Error"] = "Failed to create the order. Please correct the errors and try again.";
			return View(order);
		}

		// Get - Orders/Show/id
		[Authorize(Roles = "Admin,Client")]
		public async Task<IActionResult> Show(int? id)
		{
			if (id == null)
			{
				return NotFound();
			}

			var order = await db.Orders.Include(o => o.Client).FirstOrDefaultAsync(o => o.Id == id);

			if (order == null)
			{
				return NotFound();
			}

			// Ma asigur ca doar clientul care a plasat comanda poate sa o vada
			var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
			var userRole = await _userManager.GetRolesAsync(await _userManager.FindByIdAsync(userId));
			if (userRole.Contains("Client") && order.ClientId != userId)
			{
				return Unauthorized();
			}

			return View(order);
		}

		// Get - Orders/ShowOrdersOfClient/id
		[Authorize(Roles = "Admin,Client")]
		public async Task<IActionResult> ShowOrdersOfClient(string id)
		{
			if (string.IsNullOrEmpty(id))
			{
				return NotFound();
			}

			// Ma asigur ca doar clientul care a plasat comanda poate sa vada comanda lui
			var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
			if (id != userId)
			{
				return Unauthorized();
			}
			var orders = await db.Orders.Include(o => o.Client).Where(o => o.ClientId == id).ToListAsync();

			return View(orders);
		}

		[NonAction]
		public bool IsValidAddressInRomania(string address)
		{
			if (string.IsNullOrWhiteSpace(address))
				return false;

			// Fac split dupa virgule
			var parts = address.Split(',', StringSplitOptions.RemoveEmptyEntries);

			// Daca avem cel putin 3 virgule(4 parti) e ok, daca nu, adresa e invalida
			if (parts.Length < 4)
			{
				return false;
			}

			// Ultima parte tre sa contina "Romania"
			var lastPart = parts[^1].Trim();
			if (!lastPart.Contains("Romania", StringComparison.OrdinalIgnoreCase))
			{
				return false;
			}

			return true;
		}
	}
}

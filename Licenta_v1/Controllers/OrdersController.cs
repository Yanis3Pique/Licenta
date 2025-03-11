using Licenta_v1.Data;
using Licenta_v1.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using SendGrid.Helpers.Mail;
using System.Drawing;
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
			string userId,
			string statusFilter) // Adaug parametru pentru filtrare dupa status
		{
			var orders = db.Orders.Include(o => o.Client).Include(o => o.Delivery).Include(o => o.Feedback).AsQueryable();

			// Filtrez comenzile in functie de rolul utilizatorului
			if (userRole == "Client")
			{
				orders = orders.Where(o => o.ClientId == userId); // Clientul poate vedea doar comenzile sale
			}
			// Verific daca utilizatorul este dispecer si filtrez comenzile dupa regiunea asignata
			if (userRole == "Dispecer")
			{
				var dispecer = await db.ApplicationUsers.FirstOrDefaultAsync(u => u.Id == userId); // verific daca dispecerul are regiunea asignata si filtrez comenzile dupa regiune
				if (dispecer != null && dispecer.RegionId.HasValue)
				{
					orders = orders.Where(o => o.RegionId == dispecer.RegionId);
				}
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
			if (regionId.HasValue && regionId.Value != 0)
			{
				orders = orders.Where(o => o.RegionId == regionId);
			}

			// Filtrare dupa status
			if (!string.IsNullOrEmpty(statusFilter))
			{
				// Filtrare dupa status: Placed, Assigned to Delivery sau Delivered
				if (statusFilter == "Placed")
				{
					// Placed = Order.Status este Placed si Order.Delivery este null
					orders = orders.Where(o => o.Status == Services.OrderStatus.Placed && o.Delivery == null);
				}
				else if (statusFilter == "Assigned")
				{
					// Assigned to Delivery = Order.Status este Placed, dar Order.Delivery nu este null
					orders = orders.Where(o => o.Status == Services.OrderStatus.Placed && o.Delivery != null);
				}
				else if (statusFilter == "Delivered")
				{
					// Delivered = Order.Status este Delivered
					orders = orders.Where(o => o.Status == Services.OrderStatus.Delivered);
				}
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
		[Authorize(Roles = "Admin, Client, Dispecer")]
		public async Task<IActionResult> Index(
			string searchString, 
			int? regionId, 
			string sortOrder, 
			string statusFilter, 
			int pageNumber = 1)
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
			var userRole = userRoles.Contains("Admin") ? "Admin" : (userRoles.Contains("Dispecer") ? "Dispecer" : "Client");

			ViewBag.CurrentUserId = userId; // Imi trebuie pt feedback in View

			// Iau comenzile filtrate si numarul total de comenzi pt paginare
			var (pagedOrders, count) = await GetFilteredOrders(searchString, regionId, sortOrder, pageNumber, pageSize, userRole, userId, statusFilter);

			ViewBag.PageNumber = pageNumber;
			ViewBag.TotalPages = (int)Math.Ceiling(count / (double)pageSize);

			return View(pagedOrders);
		}

		// Get - Order/Create
		public IActionResult Create()
		{
			var regions = db.Regions.ToList();

			ViewBag.RegionId = new SelectList(regions, "Id", "County");

			return View();
		}

		// Post - Order/Create
		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> Create(Order order)
		{
			// Validez clientul care plaseaza comanda
			var client = db.ApplicationUsers.Find(_userManager.GetUserId(User));
			if (client == null) return NotFound();

			// Asignez comenzii clientul care a plasat-o si data plasarii
			ModelState.Remove("ClientId");
			order.ClientId = client.Id;

			// Ma asigur ca Modelul e valid
			if (!ModelState.IsValid)
			{
				var errors = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage).ToList();
				foreach (var error in errors)
				{
					ModelState.AddModelError("", error);
				}

				// Repoopulez ViewBag-urile
				ViewBag.RegionId = new SelectList(db.Regions.ToList(), "Id", "County", order.RegionId);
				ViewBag.ClientId = client.Id;
				return View(order);
			}

			if(!IsValidAddressInRomania(order.Address))
			{
				TempData["Error"] = "The address must contain at least 4 parts and end with 'Romania'.";
				ViewBag.RegionId = new SelectList(db.Regions.ToList(), "Id", "County", order.RegionId);
				ViewBag.ClientId = client.Id;
				return View(order);
			}

			try
			{
				db.Orders.Add(order);
				await db.SaveChangesAsync();

				TempData["Success"] = "Order " + order.Id + " created successfully.";

				return RedirectToAction("Index");
			}
			catch (Exception ex)
			{
				ModelState.AddModelError("", "An error occurred while saving the order. Please try again.");

				// Repopulez ViewBag-urile
				ViewBag.RegionId = new SelectList(db.Regions.ToList(), "Id", "County", order.RegionId);
				ViewBag.ClientId = client.Id;
				return View(order);
			}
		}

		// Get - Orders/Show/id
		[Authorize(Roles = "Admin,Client,Dispecer")]
		public async Task<IActionResult> Show(int? id)
		{
			if (id == null)
			{
				return NotFound();
			}

			var order = await db.Orders
				.Include(o => o.Client)
				.Include(o => o.Region)
				.FirstOrDefaultAsync(o => o.Id == id);
			if (order == null)
			{
				return NotFound();
			}

			// Ma asigur ca doar clientul care a plasat comanda poate sa o vada
			var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
			var userRoles = await _userManager.GetRolesAsync(await _userManager.FindByIdAsync(userId));
			if (userRoles.Contains("Client") && order.ClientId != userId)
			{
				return Unauthorized();
			}
			// Verific daca utilizatorul este Dispecer(nu poate vedea comenzi din alta regiune)
			if (userRoles.Contains("Dispecer"))
			{
				var dispecer = await db.ApplicationUsers.FirstOrDefaultAsync(u => u.Id == userId);
				if (dispecer != null && dispecer.RegionId.HasValue && order.RegionId != dispecer.RegionId)
				{
					return Unauthorized();
				}
			}
			return View(order);
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

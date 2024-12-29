using DotNetEnv;
using Licenta_v1.Data;
using Licenta_v1.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Sprache;

namespace Licenta_v1.Controllers
{
	public class HeadquartersController : Controller
	{
		private readonly ApplicationDbContext db;
		private readonly UserManager<ApplicationUser> _userManager;
		private readonly RoleManager<IdentityRole> _roleManager;

		public HeadquartersController(
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
		private async Task<List<Headquarter>> GetFilteredHeadquarters(
			string searchString,
			string sortOrder)
		{
			var headquarters = db.Headquarters.Include(h => h.Region).AsQueryable();

			// Caut sediile dupa nume, adresa sau judet
			if (!string.IsNullOrEmpty(searchString))
			{
				headquarters = headquarters.Where(h =>
					h.Name.Contains(searchString) ||
					h.Address.Contains(searchString) ||
					h.Region.County.Contains(searchString));
			}

			// Sortarea propriu-zisa dupa nume, adresa sau judet
			headquarters = sortOrder switch
			{
				"name" => headquarters.OrderBy(h => h.Name),
				"name_desc" => headquarters.OrderByDescending(h => h.Name),
				"address" => headquarters.OrderBy(h => h.Address),
				"address_desc" => headquarters.OrderByDescending(h => h.Address),
				_ => headquarters.OrderBy(h => h.Name),
			};

			var totalCount = await headquarters.CountAsync();

			var filteredHeadquarters = await headquarters.ToListAsync();

			return (filteredHeadquarters);
		}

		// Get - Headquarters/Index
		[Authorize(Roles = "Admin")]
		public async Task<IActionResult> Index(string searchString, string sortOrder)
		{

			ViewBag.CurrentSort = sortOrder;
			ViewBag.NameSortParam = sortOrder == "name" ? "name_desc" : "name";
			ViewBag.AddressSortParam = sortOrder == "address" ? "address_desc" : "address";

			ViewBag.SearchString = searchString;

			var headquarters = await GetFilteredHeadquarters(searchString, sortOrder);

			return View(headquarters);
		}

		// Get - Headquarters/Create
		[Authorize(Roles = "Admin")]
		public IActionResult Create()
		{
			ViewBag.Regions = db.Regions.Select(r => new SelectListItem
			{
				Value = r.Id.ToString(),
				Text = r.County
			}).ToList();

			return View();
		}

		// Post - Headquarters/Create
		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Roles = "Admin")]
		public async Task<IActionResult> Create(Headquarter headquarter)
		{
			if (ModelState.IsValid)
			{
				// Verific daca exista deja un sediu in judetul selectat
				var existingHeadquarter = await db.Headquarters.FirstOrDefaultAsync(h => h.RegionId == headquarter.RegionId);
				if (existingHeadquarter != null)
				{
					ModelState.AddModelError("RegionId", "A headquarter already exists in the selected region.");
					TempData["Error"] = "A headquarter already exists in the selected region. Please choose a different region.";
					PopulateRegions();
					return View(headquarter);
				}

				// Verific daca exista judetul in tabelul Regions
				var region = await db.Regions.FirstOrDefaultAsync(r => r.Id == headquarter.RegionId);
				if (region == null)
				{
					ModelState.AddModelError("RegionId", "The selected region does not exist.");
					TempData["Error"] = "Invalid region. Please select a valid region.";
					PopulateRegions();
					return View(headquarter);
				}

				// Verific cu functia IsValidAddressInRomania daca adresa e valida
				if (!IsValidAddressInRomania(headquarter.Address))
				{
					TempData["Error"] = "Invalid address. Please select an address from Romania.";
					PopulateRegions();
					return View(headquarter);
				}

				// Adaug headquarter-ul in baza de date
				db.Headquarters.Add(headquarter);
				await db.SaveChangesAsync();

				TempData["Success"] = "Headquarter created successfully.";
				return RedirectToAction("Index");
			}

			PopulateRegions();
			TempData["Error"] = "Failed to create headquarter. Please correct the errors and try again.";
			return View(headquarter);
		}

		// Get - Headquarters/Show/id
		[Authorize(Roles = "Admin")]
		public async Task<IActionResult> Show(int? id)
		{
			if (id == null)
			{
				return NotFound();
			}
			var headquarter = await db.Headquarters.Include(h => h.Region).FirstOrDefaultAsync(h => h.Id == id);
			if (headquarter == null)
			{
				return NotFound();
			}

			// Luam API Key pt Google Maps sa ne ajute la Street View
			var googleMapsKey = Env.GetString("Cheie_API_Google_Maps");
			ViewBag.GoogleMapsApiKey = googleMapsKey;

			return View(headquarter);
		}

		// Get - Headquarters/Edit/id
		[Authorize(Roles = "Admin")]
		public async Task<IActionResult> Edit(int? id)
		{
			if (id == null)
			{
				return NotFound();
			}
			var headquarter = await db.Headquarters.FindAsync(id);
			if (headquarter == null)
			{
				return NotFound();
			}
			PopulateRegions(headquarter.RegionId);
			ViewBag.Latitude = headquarter.Latitude;
			ViewBag.Longitude = headquarter.Longitude;
			return View(headquarter);
		}

		// Post - Headquarters/Edit/id
		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Roles = "Admin")]
		public async Task<IActionResult> Edit(Headquarter headquarter)
		{
			if (ModelState.IsValid)
			{
				// Verific daca exista judetul in tabelul Regions
				var region = await db.Regions.FirstOrDefaultAsync(r => r.Id == headquarter.RegionId);
				if (region == null)
				{
					ModelState.AddModelError("RegionId", "The selected region does not exist.");
					TempData["Error"] = "Invalid region. Please select a valid region.";
					PopulateRegions();
					return View(headquarter);
				}
				// Verific cu functia IsValidAddressInRomania daca adresa e valida
				if (!IsValidAddressInRomania(headquarter.Address))
				{
					TempData["Error"] = "Invalid address. Please select an address from Romania.";
					PopulateRegions();
					return View(headquarter);
				}
				// Modific headquarter-ul in baza de date
				db.Headquarters.Update(headquarter);
				await db.SaveChangesAsync();
				TempData["Success"] = "Headquarter updated successfully.";
				return RedirectToAction("Index");
			}
			PopulateRegions();
			TempData["Error"] = "Failed to update headquarter. Please correct the errors and try again.";
			return View(headquarter);
		}

		// Get - Headquarters/Delete/id
		[Authorize(Roles = "Admin")]
		[HttpPost]
		public async Task<IActionResult> Delete(int? id)
		{
			if (id == null)
			{
				return NotFound();
			}

			var headquarter = await db.Headquarters.FindAsync(id);

			if (headquarter == null)
			{
				return NotFound();
			}

			db.Headquarters.Remove(headquarter);
			await db.SaveChangesAsync();

			TempData["Success"] = "Headquarter deleted successfully.";
			return RedirectToAction("Index");
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

		[NonAction]
		private void PopulateRegions(int? selectedRegionId = null)
		{
			ViewBag.Regions = db.Regions.Select(r => new SelectListItem
			{
				Value = r.Id.ToString(),
				Text = r.County,
				Selected = r.Id == selectedRegionId
			}).ToList();
		}
	}
}

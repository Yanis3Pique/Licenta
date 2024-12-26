using Licenta_v1.Data;
using Licenta_v1.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace Licenta_v1.Controllers
{
	public class VehiclesController : Controller
	{
		private readonly ApplicationDbContext db;
		private readonly UserManager<ApplicationUser> _userManager;
		private readonly RoleManager<IdentityRole> _roleManager;

		public VehiclesController(
			ApplicationDbContext context,
			UserManager<ApplicationUser> userManager,
			RoleManager<IdentityRole> roleManager
			)
		{
			db = context;
			_userManager = userManager;
			_roleManager = roleManager;
		}

		[Authorize(Roles = "Admin")]
		private async Task<(List<Vehicle> Vehicles, int Count)> GetFilteredVehicles(
			string searchString,
			int? regionId,
			string sortOrder,
			int pageNumber,
			int pageSize)
		{
			var vehicles = db.Vehicles.Include(v => v.Region).AsQueryable();

			// Caut masinile dupa numarul de inmatriculare / brand / model
			if (!string.IsNullOrEmpty(searchString))
			{
				vehicles = vehicles.Where(v =>
					v.RegistrationNumber.Contains(searchString) ||
					v.Brand.Contains(searchString) ||
					v.Model.Contains(searchString));
			}

			// Filtrez masinile dupa judet
			if (regionId.HasValue)
			{
				vehicles = vehicles.Where(v => v.RegionId == regionId);
			}

			// Sortarea propriu-zisa dupa brand sau implicit dupa numarul de inmatriculare
			switch (sortOrder)
			{
				case "brand":
					vehicles = vehicles.OrderBy(v => v.Brand);
					break;
				case "brand_desc":
					vehicles = vehicles.OrderByDescending(v => v.Brand);
					break;
				case "registration":
					vehicles = vehicles.OrderBy(v => v.RegistrationNumber);
					break;
				case "registration_desc":
					vehicles = vehicles.OrderByDescending(v => v.RegistrationNumber);
					break;
				case "model":
					vehicles = vehicles.OrderBy(v => v.Model);
					break;
				case "model_desc":
					vehicles = vehicles.OrderByDescending(v => v.Model);
					break;
				default:
					vehicles = vehicles.OrderBy(v => v.RegistrationNumber);
					break;
			}

			var count = await vehicles.CountAsync();
			var pagedVehicles = await vehicles.Skip((pageNumber - 1) * pageSize).Take(pageSize).ToListAsync();

			return (pagedVehicles, count);
		}

		// Get - Vehicles/Index
		[Authorize(Roles = "Admin")]
		public async Task<IActionResult> Index(string searchString, int? regionId, string sortOrder, int pageNumber = 1)
		{
			int pageSize = 10;

			ViewBag.CurrentSort = sortOrder;
			ViewBag.NameSortParam = sortOrder == "brand" ? "brand_desc" : "brand";
			ViewBag.RegistrationSortParam = sortOrder == "registration" ? "registration_desc" : "registration";
			ViewBag.ModelSortParam = sortOrder == "model" ? "model_desc" : "model";

			ViewBag.SearchString = searchString;
			ViewBag.RegionId = regionId;
			ViewBag.Regions = new SelectList(db.Regions, "Id", "County");

			var (vehicles, count) = await GetFilteredVehicles(searchString, regionId, sortOrder, pageNumber, pageSize);

			ViewBag.PageNumber = pageNumber;
			ViewBag.TotalPages = (int)Math.Ceiling(count / (double)pageSize);

			return View(vehicles);
		}

		// Get - Vehicles/Create
		[Authorize(Roles = "Admin")]
		public IActionResult Create()
		{
			ViewBag.Statuses = Enum.GetValues(typeof(VehicleStatus))
								   .Cast<VehicleStatus>()
								   .Select(s => new SelectListItem
								   {
									   Text = s.ToString(),
									   Value = ((int)s).ToString()
								   });

			ViewBag.FuelTypes = Enum.GetValues(typeof(FuelType))
									.Cast<FuelType>()
									.Select(f => new SelectListItem
									{
										Text = f.ToString(),
										Value = ((int)f).ToString()
									});

			ViewBag.Regions = new SelectList(db.Regions, "Id", "County");

			return View();
		}

		// Post - Vehicles/Create
		[HttpPost]
		[Authorize(Roles = "Admin")]
		public async Task<IActionResult> Create(Vehicle vehicle)
		{
			if (ModelState.IsValid)
			{
				db.Vehicles.Add(vehicle);
				await db.SaveChangesAsync();
				TempData["Success"] = "Vehicle added successfully!";
				return RedirectToAction(nameof(Index));
			}

			// Repopulez dropdown-urile daca-s erori
			ViewBag.Statuses = Enum.GetValues(typeof(VehicleStatus))
								   .Cast<VehicleStatus>()
								   .Select(s => new SelectListItem
								   {
									   Text = s.ToString(),
									   Value = ((int)s).ToString()
								   });

			ViewBag.FuelTypes = Enum.GetValues(typeof(FuelType))
									.Cast<FuelType>()
									.Select(f => new SelectListItem
									{
										Text = f.ToString(),
										Value = ((int)f).ToString()
									});

			ViewBag.Regions = new SelectList(db.Regions, "Id", "County");

			return View(vehicle);
		}

		// Get - Vehicles/Show/id
		[Authorize(Roles = "Admin")]
		public async Task<IActionResult> Show(int id)
		{
			var vehicle = await db.Vehicles.Include(v => v.Region).FirstOrDefaultAsync(v => v.Id == id);

			if (vehicle == null)
			{
				TempData["Error"] = "Vehicle not found!";
				return RedirectToAction("Index");
			}

			return View(vehicle);
		}

		// Get - Vehicles/Edit/id
		[Authorize(Roles = "Admin")]
		public async Task<IActionResult> Edit(int id)
		{
			var vehicle = await db.Vehicles.FindAsync(id);

			if (vehicle == null)
			{
				TempData["Error"] = "Vehicle not found!";
				return RedirectToAction("Index");
			}

			ViewBag.Regions = new SelectList(db.Regions, "Id", "County", vehicle.RegionId);
			return View(vehicle);
		}

		// Post - Vehicles/Edit/id
		[Authorize(Roles = "Admin")]
		[HttpPost]
		public async Task<IActionResult> Edit(int id, Vehicle updatedVehicle)
		{
			if (id != updatedVehicle.Id)
			{
				return NotFound();
			}

			if (ModelState.IsValid)
			{
				db.Vehicles.Update(updatedVehicle);
				await db.SaveChangesAsync();
				TempData["Success"] = "Vehicle updated successfully!";
				return RedirectToAction("Index");
			}

			ViewBag.Regions = new SelectList(db.Regions, "Id", "County", updatedVehicle.RegionId);
			TempData["Error"] = "There was an error updating the vehicle.";
			return View(updatedVehicle);
		}

		// Get - Vehicles/Delete/id	
		[Authorize(Roles = "Admin")]
		[HttpPost]
		public async Task<IActionResult> Delete(int id)
		{
			var vehicle = await db.Vehicles.FindAsync(id);

			if (vehicle == null)
			{
				TempData["Error"] = "Vehicle not found!";
				return RedirectToAction("Index");
			}

			db.Vehicles.Remove(vehicle);
			await db.SaveChangesAsync();

			TempData["Success"] = "Vehicle deleted successfully!";
			return RedirectToAction("Index");
		}
	}
}

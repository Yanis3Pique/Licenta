using Licenta_v1.Data;
using Licenta_v1.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using System.Linq;

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
			int pageSize = 6;

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
		public async Task<IActionResult> Create(Vehicle vehicle, IFormFile carPicture)
		{
			// Varific daca nu cumva am deja in BD o masina cu acelasi nr de inmatriculare
			if (await db.Vehicles.AnyAsync(v => v.RegistrationNumber == vehicle.RegistrationNumber))
			{
				ModelState.AddModelError("RegistrationNumber", "The Registration Number already exists in the database.");
			}

			if (!ModelState.IsValid)
			{
				ViewBag.Regions = db.Regions
					.Select(r => new SelectListItem
					{
						Value = r.Id.ToString(),
						Text = r.County
					}).ToList();

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

				return View(vehicle);
			}

			if (carPicture != null && carPicture.Length > 0)
			{
				var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif" };
				var extension = Path.GetExtension(carPicture.FileName).ToLowerInvariant();

				if (!allowedExtensions.Contains(extension))
				{
					ModelState.AddModelError("carPicture", "Invalid file type. Only JPG, JPEG, PNG, and GIF are allowed!");
					ViewBag.Regions = db.Regions
						.Select(r => new SelectListItem
						{
							Value = r.Id.ToString(),
							Text = r.County
						}).ToList();

					return View(vehicle);
				}

				var fileName = vehicle.Brand + vehicle.Model + vehicle.RegistrationNumber + extension;
				var filePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "Images", fileName);

				using (var stream = new FileStream(filePath, FileMode.Create))
				{
					await carPicture.CopyToAsync(stream);
				}

				vehicle.ImagePath = $"Images/{fileName}";
			}

			db.Vehicles.Add(vehicle);
			await db.SaveChangesAsync();

			return RedirectToAction("Index");
		}

		// Get - Vehicles/Show/id
		[Authorize(Roles = "Admin,Dispecer")]
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

		// Post - Vehicles/UploadCarPicture
		[Authorize(Roles = "Admin")]
		[HttpPost]
		public async Task<IActionResult> UploadCarPicture(int id, IFormFile carPicture)
		{
			if (carPicture == null || carPicture.Length == 0)
			{
				TempData["Error"] = "Please select a valid image!";
				return RedirectToAction("Show", new { id });
			}

			var vehicle = await db.Vehicles.FindAsync(id);
			if (vehicle == null)
			{
				TempData["Error"] = "Vehicle not found!";
				return RedirectToAction("Index");
			}

			// Verific daca poza are extensie valida
			var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif" };
			var extension = Path.GetExtension(carPicture.FileName)?.ToLowerInvariant();

			if (string.IsNullOrEmpty(extension) || !allowedExtensions.Contains(extension))
			{
				TempData["Error"] = "Invalid file type. Only JPG, JPEG, PNG, and GIF are allowed!";
				return RedirectToAction("Show", new { id });
			}

			try
			{
				// Sterg poza veche din wwwroot/Images
				if (!string.IsNullOrEmpty(vehicle.ImagePath))
				{
					var oldFilePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", vehicle.ImagePath);
					if (System.IO.File.Exists(oldFilePath))
					{
						System.IO.File.Delete(oldFilePath);
					}
				}

				// Salvez poza noua in wwwroot/Images
				var fileName = vehicle.Model + vehicle.Brand + vehicle.RegistrationNumber + extension;
				var filePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "Images", fileName);

				using (var stream = new FileStream(filePath, FileMode.Create))
				{
					await carPicture.CopyToAsync(stream);
				}

				// Updatez poza(adica path-ul ei) in BD
				vehicle.ImagePath = $"Images/{fileName}";
				db.Vehicles.Update(vehicle);
				await db.SaveChangesAsync();

				TempData["Success"] = "Vehicle picture updated successfully!";
			}
			catch (Exception ex)
			{
				TempData["Error"] = $"An error occurred while uploading the picture: {ex.Message}";
				return RedirectToAction("Show", new { id });
			}

			return RedirectToAction("Show", new { id });
		}

		// Get - Vehicles/Edit/id
		[Authorize(Roles = "Admin")]
		public async Task<IActionResult> Edit(int id)
		{
			if (id <= 0)
			{
				TempData["Error"] = "Invalid vehicle ID!";
				return RedirectToAction("Index");
			}

			var vehicle = await db.Vehicles.FindAsync(id);

			if (vehicle == null)
			{
				TempData["Error"] = "Vehicle not found!";
				return RedirectToAction("Index");
			}

			// Iau toate judetele pentru dropdown
			ViewBag.Regions = db.Regions.Select(r => new SelectListItem
			{
				Value = r.Id.ToString(),
				Text = r.County
			}).ToList();

			// Iau toate statusurile pentru dropdown
			ViewBag.Statuses = Enum.GetValues(typeof(VehicleStatus))
								   .Cast<VehicleStatus>()
								   .Select(s => new SelectListItem
								   {
									   Text = s.ToString(),
									   Value = ((int)s).ToString()
								   });

			// Iau toate tipurile de combustibil pentru dropdown
			ViewBag.FuelTypes = Enum.GetValues(typeof(FuelType))
									.Cast<FuelType>()
									.Select(f => new SelectListItem
									{
										Text = f.ToString(),
										Value = ((int)f).ToString()
									});

			return View(vehicle);
		}

		// Post - Vehicles/Edit/id
		[Authorize(Roles = "Admin")]
		[HttpPost]
		public async Task<IActionResult> Edit(int id, Vehicle updatedVehicle, [FromForm] int? newRegionId)
		{
			if (id <= 0 || id != updatedVehicle.Id)
			{
				TempData["Error"] = "Invalid vehicle ID!";
				return NotFound();
			}

			var vehicle = await db.Vehicles.FindAsync(id);
			if (vehicle == null)
			{
				TempData["Error"] = "Vehicle not found!";
				return RedirectToAction("Index");
			}

			if (ModelState.IsValid)
			{
				vehicle.Brand = updatedVehicle.Brand;
				vehicle.Model = updatedVehicle.Model;
				vehicle.RegistrationNumber = updatedVehicle.RegistrationNumber;
				vehicle.YearOfManufacture = updatedVehicle.YearOfManufacture;
				vehicle.Status = updatedVehicle.Status;
				vehicle.FuelType = updatedVehicle.FuelType;
				vehicle.ConsumptionRate = updatedVehicle.ConsumptionRate;
				vehicle.MaxVolumeCapacity = updatedVehicle.MaxVolumeCapacity;
				vehicle.MaxWeightCapacity = updatedVehicle.MaxWeightCapacity;
				vehicle.TotalDistanceTraveledKM = updatedVehicle.TotalDistanceTraveledKM;
				vehicle.ImagePath = vehicle.ImagePath;
				vehicle.RegionId = updatedVehicle.RegionId;

				try
				{
					db.Vehicles.Update(vehicle);
					await db.SaveChangesAsync();
					TempData["Success"] = "Vehicle updated successfully!";
					return RedirectToAction("Index");
				}
				catch (Exception ex)
				{
					TempData["Error"] = $"Error updating vehicle: {ex.Message}";
				}
			}

			// Dar daca ModelState nu e valid, returnez userul cu datele vechi
			ViewBag.Regions = db.Regions.Select(r => new SelectListItem
			{
				Value = r.Id.ToString(),
				Text = r.County
			}).ToList();

			TempData["Error"] = "There was an error updating the vehicle. Please check the input.";
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

			// Daca masina nu are status-ul "Retired", nu o pot sterge
			if (vehicle.Status != VehicleStatus.Retired)
			{
				TempData["Error"] = "Only retired vehicles can be deleted!";
				return RedirectToAction("Index");
			}

			// Sterg poza din wwwroot/Images
			if (!string.IsNullOrEmpty(vehicle.ImagePath))
			{
				var filePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", vehicle.ImagePath);
				if (System.IO.File.Exists(filePath))
				{
					System.IO.File.Delete(filePath);
				}
			}

			db.Vehicles.Remove(vehicle);
			await db.SaveChangesAsync();

			TempData["Success"] = "Vehicle deleted successfully!";
			return RedirectToAction("Index");
		}
	}
}

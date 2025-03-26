using Licenta_v1.Data;
using Licenta_v1.Models;
using Licenta_v1.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using SendGrid;
using SendGrid.Helpers.Mail;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Reflection;

namespace Licenta_v1.Controllers
{
	public class VehiclesController : Controller
	{
		private readonly ApplicationDbContext db;
		private readonly UserManager<ApplicationUser> _userManager;
		private readonly RoleManager<IdentityRole> _roleManager;
		private readonly OrderDeliveryOptimizer2 opt;
		private readonly IEmailSender _emailSender;

		public VehiclesController(
			ApplicationDbContext context,
			UserManager<ApplicationUser> userManager,
			RoleManager<IdentityRole> roleManager,
			IEmailSender emailSender,
			OrderDeliveryOptimizer2 optimizer)
		{
			db = context;
			_userManager = userManager;
			_roleManager = roleManager;
			_emailSender = emailSender;
			opt = optimizer;
		}

		[NonAction]
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
			vehicles = sortOrder switch
			{
				"brand" => vehicles.OrderBy(v => v.Brand),
				"brand_desc" => vehicles.OrderByDescending(v => v.Brand),
				"registration" => vehicles.OrderBy(v => v.RegistrationNumber),
				"registration_desc" => vehicles.OrderByDescending(v => v.RegistrationNumber),
				"model" => vehicles.OrderBy(v => v.Model),
				"model_desc" => vehicles.OrderByDescending(v => v.Model),
				_ => vehicles.OrderBy(v => v.RegistrationNumber),
			};
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

			ViewBag.VehicleTypes = Enum.GetValues(typeof(VehicleType))
									   .Cast<VehicleType>()
									   .Select(vt => new SelectListItem
									   {
										   Text = vt.GetType()
													.GetMember(vt.ToString())
													.First()
													.GetCustomAttribute<DisplayAttribute>()?
													.Name ?? vt.ToString(),
										   Value = ((int)vt).ToString()
									   });

			ViewBag.Regions = new SelectList(db.Regions, "Id", "County");

			return View();
		}

		// Post - Vehicles/Create
		[HttpPost]
		[Authorize(Roles = "Admin")]
		public async Task<IActionResult> Create(Vehicle vehicle, IFormFile? carPicture)
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

				ViewBag.VehicleTypes = Enum.GetValues(typeof(VehicleType))
									   .Cast<VehicleType>()
									   .Select(vt => new SelectListItem
									   {
										   Text = vt.GetType()
													.GetMember(vt.ToString())
													.First()
													.GetCustomAttribute<DisplayAttribute>()?
													.Name ?? vt.ToString(),
										   Value = ((int)vt).ToString()
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

			// Iau toate tipurile de vehicule pentru dropdown
			ViewBag.VehicleTypes = Enum.GetValues(typeof(VehicleType))
									   .Cast<VehicleType>()
									   .Select(vt => new SelectListItem
									   {
										   Text = vt.GetType()
													.GetMember(vt.ToString())
													.First()
													.GetCustomAttribute<DisplayAttribute>()?
													.Name ?? vt.ToString(),
										   Value = ((int)vt).ToString()
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
				bool shouldInvalidateCache =
					vehicle.HeightMeters != updatedVehicle.HeightMeters ||
					vehicle.LengthMeters != updatedVehicle.LengthMeters ||
					vehicle.WeightTons != updatedVehicle.WeightTons ||
					vehicle.WidthMeters != updatedVehicle.WidthMeters || 
					vehicle.MaxAxleLoadTons != updatedVehicle.MaxAxleLoadTons;

				vehicle.Brand = updatedVehicle.Brand;
				vehicle.Model = updatedVehicle.Model;
				vehicle.RegistrationNumber = updatedVehicle.RegistrationNumber;
				vehicle.YearOfManufacture = updatedVehicle.YearOfManufacture;
				vehicle.Status = updatedVehicle.Status;
				vehicle.FuelType = updatedVehicle.FuelType;
				vehicle.VehicleType = updatedVehicle.VehicleType;
				vehicle.ConsumptionRate = updatedVehicle.ConsumptionRate;
				vehicle.MaxVolumeCapacity = updatedVehicle.MaxVolumeCapacity;
				vehicle.MaxWeightCapacity = updatedVehicle.MaxWeightCapacity;
				vehicle.TotalDistanceTraveledKM = updatedVehicle.TotalDistanceTraveledKM;
				vehicle.ImagePath = vehicle.ImagePath;
				vehicle.RegionId = updatedVehicle.RegionId;
				vehicle.HeightMeters = updatedVehicle.HeightMeters;
				vehicle.WidthMeters = updatedVehicle.WidthMeters;
				vehicle.LengthMeters = updatedVehicle.LengthMeters;
				vehicle.WeightTons = updatedVehicle.WeightTons;
				vehicle.MaxAxleLoadTons = updatedVehicle.MaxAxleLoadTons;

				try
				{
					db.Vehicles.Update(vehicle);
					await db.SaveChangesAsync();

					if (shouldInvalidateCache)
					{
						opt.InvalidateCacheForVehicle(vehicle.Id);
					}

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

			ViewBag.VehicleTypes = Enum.GetValues(typeof(VehicleType))
									   .Cast<VehicleType>()
									   .Select(vt => new SelectListItem
									   {
										   Text = vt.GetType()
													.GetMember(vt.ToString())
													.First()
													.GetCustomAttribute<DisplayAttribute>()?
													.Name ?? vt.ToString(),
										   Value = ((int)vt).ToString()
									   });

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

		// Get - Vehicles/ScheduleMaintenance/id
		[Authorize(Roles = "Admin,Dispecer")]
		public async Task<IActionResult> ScheduleMaintenance(int id)
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

			// Iau toate tipurile de mentenanta la care vehiculul are drept in functie de tipul de combustibil
			var maintenanceTypes = Enum.GetValues(typeof(MaintenanceTypes))
						   .Cast<MaintenanceTypes>()
						   .Where(mt =>
						   {
							   switch (vehicle.FuelType)
							   {
								   case FuelType.Electric: // Electric
									   return mt == MaintenanceTypes.BatteryHealthCheck ||
											  mt == MaintenanceTypes.BatteryCoolantChange ||
											  mt == MaintenanceTypes.TireReplacement ||
											  mt == MaintenanceTypes.BrakePadReplacement ||
											  mt == MaintenanceTypes.SuspensionService ||
											  mt == MaintenanceTypes.GeneralInspection;

								   case FuelType.Hybrid: // ICE + Electric = Hybrid
									   return mt == MaintenanceTypes.EngineOilFilter ||
											  mt == MaintenanceTypes.BatteryHealthCheck ||
											  mt == MaintenanceTypes.BatteryCoolantChange ||
											  mt == MaintenanceTypes.TireReplacement ||
											  mt == MaintenanceTypes.BrakePadReplacement ||
											  mt == MaintenanceTypes.SuspensionService ||
											  mt == MaintenanceTypes.GeneralInspection;

								   default: // ICE
									   return mt == MaintenanceTypes.EngineOilFilter ||
											  mt == MaintenanceTypes.TireReplacement ||
											  mt == MaintenanceTypes.BrakePadReplacement ||
											  mt == MaintenanceTypes.SuspensionService ||
											  mt == MaintenanceTypes.GeneralInspection;
							   }
						   })
						   .Select(mt => new SelectListItem
						   {
							   Text = mt.GetDisplayName(),
							   Value = mt.ToString()
						   }).ToList();

			ViewBag.MaintenanceTypes = maintenanceTypes;

			return View(vehicle);
		}

		// Post - Vehicles/ScheduleMaintenance/id
		[Authorize(Roles = "Admin,Dispecer")]
		[HttpPost]
		public async Task<IActionResult> ScheduleMaintenance(int id, [FromForm] string selectedMaintenanceType)
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

			if (string.IsNullOrEmpty(selectedMaintenanceType))
			{
				TempData["Error"] = "Please select a maintenance type.";
				return RedirectToAction("ScheduleMaintenance", new { id });
			}

			// Verific sa nu mai fie o mentenanta de acelasi tip programata pentru vehicul ("Scheduled")
			if (await db.Maintenances.AnyAsync(m => m.VehicleId == id && m.MaintenanceType.ToString() == selectedMaintenanceType && m.Status == "Scheduled"))
			{
				TempData["Error"] = "A maintenance of the same type is already scheduled for this vehicle!";
				return RedirectToAction("ScheduleMaintenance", new { id });
			}

			// Adaug o noua mentenanta in BD peste 7 zile
			var maintenance = new Maintenance
			{
				VehicleId = id,
				MaintenanceType = Enum.Parse<MaintenanceTypes>(selectedMaintenanceType),
				ScheduledDate = DateTime.Now.AddDays(7),
				Status = "Scheduled"
			};

			db.Maintenances.Add(maintenance);
			await db.SaveChangesAsync();

			TempData["Success"] = $"Maintenance for {vehicle.Brand} {vehicle.Model} scheduled successfully!";

			// Dau mail la toti adminii ca s-a programat o mentenanta
			await NotifyUsers(vehicle, maintenance);

			if(User.IsInRole("Admin"))
			{
				return RedirectToAction("Index");
			}
			else
			{
				return RedirectToAction("VehicleMaintenances", "Maintenances", new { vehicleId = id });
			}
		}

		// Dau mail la toti adminii si dispecerilor din regiunea specificata ca s-a programat o mentenanta
		private async Task NotifyUsers(Vehicle vehicle, Maintenance maintenance)
		{
			// Iau mail-ul userului curent(adica cel care a creat mentenanta)
			var currentUser = await _userManager.GetUserAsync(User);
			var currentUserEmail = currentUser.Email;

			// Iau toti adminii
			var admins = await _userManager.GetUsersInRoleAsync("Admin");

			// Iau toti dispecerii din regiunea vehiculului
			var dispatchers = (await _userManager.GetUsersInRoleAsync("Dispecer"))
				.Where(d => d.RegionId == vehicle.RegionId);

			// Combin emailurile fara duplicate si eliminand pe cel care apeleaza metoda ScheduleMaintenance
			var emailAddresses = admins.Select(a => a.Email)
				.Concat(dispatchers.Select(d => d.Email))
				.Where(email => !string.IsNullOrEmpty(email) && email != currentUserEmail) // Exclud utilizatorul curent
				.Distinct() // Fara duplicate
				.ToList();

			if (emailAddresses.Count == 0) return; // N-am cui sa dau mail

			// Mail-ul propriu-zis basically
			var emailBody = $@"
			<div style='font-family: Arial, sans-serif; line-height: 1.6; max-width: 600px; margin: auto;'>
				<div style='text-align: center; padding: 20px; background-color: #f4f4f4; border-bottom: 1px solid #ddd;'>
					<h1 style='color: #333;'>New Maintenance Scheduled</h1>
				</div>
				<div style='padding: 20px; background-color: #ffffff;'>
					<p>A new maintenance task has been scheduled:</p>
					<ul style='color: #666;'>
						<li><strong>Vehicle:</strong> {vehicle.Brand} {vehicle.Model} ({vehicle.RegistrationNumber})</li>
						<li><strong>Type:</strong> {maintenance.MaintenanceType.GetDisplayName()}</li>
						<li><strong>Scheduled Date:</strong> {maintenance.ScheduledDate.ToString("g")}</li>
					</ul>
					<p style='color: #666;'>Please review the details in the system.</p>
				</div>
				<div style='text-align: center; padding: 10px; background-color: #f4f4f4; border-top: 1px solid #ddd;'>
					<p style='color: #888; font-size: 12px;'>EcoDelivery | All Rights Reserved</p>
				</div>
			</div>";

			// Trimit mail-ul destinatarilor
			foreach (var email in emailAddresses)
			{
				await _emailSender.SendEmailAsync(
					email,
					"New Maintenance Scheduled",
					emailBody
				);
			}
		}

		// Post - Vehicles/Retire/id
		[Authorize(Roles = "Admin")]
		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> Retire(int id)
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
			// Daca masina are deja statusul "Retired", nu o mai pot retrage
			if (vehicle.Status == VehicleStatus.Retired)
			{
				TempData["Error"] = "Vehicle already retired!";
				return RedirectToAction("Index");
			}
			vehicle.Status = VehicleStatus.Retired;

			// Sterg toate mentenantele programate pentru masina
			var maintenances = await db.Maintenances
				.Where(m => m.VehicleId == id)
				.ToListAsync();
			db.Maintenances.RemoveRange(maintenances);

			db.Vehicles.Update(vehicle);
			await db.SaveChangesAsync();
			TempData["Success"] = "Vehicle retired successfully!";
			return RedirectToAction("Index");
		}
	}
}

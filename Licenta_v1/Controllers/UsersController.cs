using Licenta_v1.Data;
using Licenta_v1.Models;
using Licenta_v1.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using static Microsoft.EntityFrameworkCore.DbLoggerCategory;

namespace Licenta_v1.Controllers
{
	public class UsersController : Controller
	{
		private readonly ApplicationDbContext db;
		private readonly UserManager<ApplicationUser> _userManager;
		private readonly RoleManager<IdentityRole> _roleManager;
		private readonly SignInManager<ApplicationUser> _signInManager;

		public UsersController(
			ApplicationDbContext context,
			UserManager<ApplicationUser> userManager,
			RoleManager<IdentityRole> roleManager,
			SignInManager<ApplicationUser> signInManager
			)
		{
			db = context;
			_userManager = userManager;
			_roleManager = roleManager;
			_signInManager = signInManager;
		}

		[NonAction]
		private IEnumerable<SelectListItem> GetAllRoles()
		{
			var selectList = new List<SelectListItem>();

			var roles = _roleManager.Roles;
			foreach (var role in roles)
			{
				selectList.Add(new SelectListItem
				{
					Value = role.Id,
					Text = role.Name
				});
			}

			return selectList;
		}

		[NonAction]
		private async Task<(List<ApplicationUser> Users, int Count)> GetFilteredUsers(
			string roleFilter,
			string searchString,
			int? regionId,
			string sortOrder,
			int pageNumber,
			int pageSize)
		{
			var users = db.Users.Include(u => u.Region).Where(u => !u.IsDeleted).AsQueryable();

			// Filtrez userii dupa rol
			if (!string.IsNullOrEmpty(roleFilter))
			{
				var usersInRole = await (from ur in db.UserRoles
										 join r in db.Roles on ur.RoleId equals r.Id
										 where r.Name == roleFilter
										 select ur.UserId).ToListAsync();
				users = users.Where(u => usersInRole.Contains(u.Id));
			}

			if (!string.IsNullOrEmpty(searchString))
			{
				users = users.Where(u => u.FirstName.Contains(searchString)
									  || u.LastName.Contains(searchString)
									  || u.UserName.Contains(searchString));
			}

			if (regionId.HasValue)
			{
				users = users.Where(u => u.RegionId == regionId.Value);
			}

			users = sortOrder switch
			{
				"name_desc" => users.OrderByDescending(u => u.LastName).ThenByDescending(u => u.FirstName),
				"name" => users.OrderBy(u => u.LastName).ThenBy(u => u.FirstName),
				"date_desc" => users.OrderByDescending(u => u.DateHired),
				"date" => users.OrderBy(u => u.DateHired),
				_ => users.OrderBy(u => u.UserName),
			};

			var count = await users.CountAsync();
			var pagedUsers = await users.Skip((pageNumber - 1) * pageSize).Take(pageSize).ToListAsync();

			return (pagedUsers, count);
		}

		// Get - Users/Index
		[Authorize(Roles = "Admin")]
		public async Task<IActionResult> Index(string searchString, int? regionId, string sortOrder, int pageNumber = 1)
		{
			int pageSize = 6;

			ViewBag.CurrentSort = sortOrder;
			ViewBag.NameSortParam = "name";
			ViewBag.NameSortParamDesc = "name_desc";
			ViewBag.DateSortParam = "date";
			ViewBag.DateSortParamDesc = "date_desc";

			ViewBag.SearchString = searchString;
			ViewBag.RegionId = regionId;
			ViewBag.Regions = new SelectList(db.Regions, "Id", "County");

			var (users, count) = await GetFilteredUsers(null, searchString, regionId, sortOrder, pageNumber, pageSize);

			ViewBag.PageNumber = pageNumber;
			ViewBag.TotalPages = (int)Math.Ceiling(count / (double)pageSize);

			return View(users);
		}

		// Get - Users/IndexClients
		[Authorize(Roles = "Admin")]
		public async Task<IActionResult> IndexClients(string searchString, int? regionId, string sortOrder, int pageNumber = 1)
		{
			int pageSize = 6;

			ViewBag.CurrentSort = sortOrder;
			ViewBag.NameSortParam = "name";
			ViewBag.NameSortParamDesc = "name_desc";
			ViewBag.DateSortParam = "date";
			ViewBag.DateSortParamDesc = "date_desc";

			ViewBag.SearchString = searchString;
			ViewBag.RegionId = regionId;
			ViewBag.Regions = new SelectList(db.Regions, "Id", "County");

			var (users, count) = await GetFilteredUsers("Client", searchString, regionId, sortOrder, pageNumber, pageSize);

			ViewBag.PageNumber = pageNumber;
			ViewBag.TotalPages = (int)Math.Ceiling(count / (double)pageSize);

			return View(users);
		}

		// Get - Users/IndexDrivers
		[Authorize(Roles = "Admin")]
		public async Task<IActionResult> IndexDrivers(string searchString, int? regionId, string sortOrder, int pageNumber = 1)
		{
			int pageSize = 6;

			ViewBag.CurrentSort = sortOrder;
			ViewBag.NameSortParam = "name";
			ViewBag.NameSortParamDesc = "name_desc";
			ViewBag.DateSortParam = "date";
			ViewBag.DateSortParamDesc = "date_desc";

			ViewBag.SearchString = searchString;
			ViewBag.RegionId = regionId;
			ViewBag.Regions = new SelectList(db.Regions, "Id", "County");

			var (users, count) = await GetFilteredUsers("Sofer", searchString, regionId, sortOrder, pageNumber, pageSize);

			ViewBag.PageNumber = pageNumber;

			ViewBag.TotalPages = (int)Math.Ceiling(count / (double)pageSize);
			return View(users);
		}

		// Get - Users/IndexDispatchers
		[Authorize(Roles = "Admin")]
		public async Task<IActionResult> IndexDispatchers(string searchString, int? regionId, string sortOrder, int pageNumber = 1)
		{
			int pageSize = 6;

			ViewBag.CurrentSort = sortOrder;
			ViewBag.NameSortParam = "name";
			ViewBag.NameSortParamDesc = "name_desc";
			ViewBag.DateSortParam = "date";
			ViewBag.DateSortParamDesc = "date_desc";

			ViewBag.SearchString = searchString;
			ViewBag.RegionId = regionId;
			ViewBag.Regions = new SelectList(db.Regions, "Id", "County");

			var (users, count) = await GetFilteredUsers("Dispecer", searchString, regionId, sortOrder, pageNumber, pageSize);

			ViewBag.PageNumber = pageNumber;
			ViewBag.TotalPages = (int)Math.Ceiling(count / (double)pageSize);

			return View(users);
		}

		// Get - Users/ShowDriversOfDispatcher/id
		[Authorize(Roles = "Admin,Dispecer")]
		public async Task<IActionResult> ShowDriversOfDispatcher(
			string id,
			string searchString,
			string sortOrder,
			int pageNumber = 1,
			int pageSize = 6)
		{
			if (id == null) return NotFound();

			var dispatcher = await db.ApplicationUsers
									 .Include(u => u.Region)
									 .FirstOrDefaultAsync(u => u.Id == id);
			if (dispatcher == null) return NotFound();

			string regionName = dispatcher.Region?.County ?? "Unknown Region";

			ViewBag.CurrentSort = sortOrder;
			ViewBag.NameSortParam = sortOrder == "name" ? "name_desc" : "name";
			ViewBag.DateSortParam = sortOrder == "date" ? "date_desc" : "date";

			ViewBag.SearchString = searchString;

			var driversQuery = db.ApplicationUsers
				.Include(u => u.Region)
				.Where(u => u.RegionId == dispatcher.RegionId && u.Id != dispatcher.Id);

			if (!string.IsNullOrEmpty(searchString))
			{
				driversQuery = driversQuery.Where(d =>
					d.FirstName.Contains(searchString) ||
					d.LastName.Contains(searchString) ||
					d.UserName.Contains(searchString));
			}

			var driversList = await driversQuery.ToListAsync();

			var drivers = new List<ApplicationUser>();
			foreach (var driver in driversList)
			{
				var roles = await _userManager.GetRolesAsync(driver);
				if (roles.Contains("Sofer")) // Daca-s soferi ii iau
				{
					drivers.Add(driver);
				}
			}

			// Sortez
			drivers = sortOrder switch
			{
				"name_desc" => drivers.OrderByDescending(u => u.LastName).ThenByDescending(u => u.FirstName).ToList(),
				"name" => drivers.OrderBy(u => u.LastName).ThenBy(u => u.FirstName).ToList(),
				"date_desc" => drivers.OrderByDescending(u => u.DateHired).ToList(),
				"date" => drivers.OrderBy(u => u.DateHired).ToList(),
				_ => drivers.OrderBy(u => u.UserName).ToList()
			};

			// Paginare
			int totalItems = drivers.Count;
			var paginatedDrivers = drivers.Skip((pageNumber - 1) * pageSize).Take(pageSize).ToList();

			ViewBag.PageNumber = pageNumber;
			ViewBag.TotalPages = (int)Math.Ceiling(totalItems / (double)pageSize);
			ViewBag.Title = "Drivers for " + regionName;

			return View(paginatedDrivers);
		}

		// Get - Vehicles/ShowVehiclesOfDispatcher/id
		[Authorize(Roles = "Admin,Dispecer")]
		public async Task<IActionResult> ShowVehiclesOfDispatcher(
			string id,
			string searchString,
			string sortOrder,
			int pageNumber = 1,
			int pageSize = 6)
		{
			if (id == null) return NotFound();

			var dispatcher = await db.ApplicationUsers
									 .Include(u => u.Region)
									 .FirstOrDefaultAsync(u => u.Id == id);
			if (dispatcher == null) return NotFound();

			string regionName = dispatcher.Region?.County ?? "Unknown Region";

			ViewBag.CurrentSort = sortOrder;
			ViewBag.RegistrationSortParam = sortOrder == "registration" ? "registration_desc" : "registration";
			ViewBag.BrandSortParam = sortOrder == "brand" ? "brand_desc" : "brand";
			ViewBag.ModelSortParam = sortOrder == "model" ? "model_desc" : "model";

			ViewBag.SearchString = searchString;

			var vehiclesQuery = db.Vehicles.Include(v => v.Region)
										   .Where(v => v.RegionId == dispatcher.RegionId);

			// Filtre de cautare
			if (!string.IsNullOrEmpty(searchString))
			{
				vehiclesQuery = vehiclesQuery.Where(v =>
					v.RegistrationNumber.Contains(searchString) ||
					v.Brand.Contains(searchString) ||
					v.Model.Contains(searchString));
			}

			var vehiclesList = await vehiclesQuery.ToListAsync();

			// Sortez
			var vehicles = sortOrder switch
			{
				"registration_desc" => vehiclesList.OrderByDescending(v => v.RegistrationNumber).ToList(),
				"registration" => vehiclesList.OrderBy(v => v.RegistrationNumber).ToList(),
				"brand_desc" => vehiclesList.OrderByDescending(v => v.Brand).ToList(),
				"brand" => vehiclesList.OrderBy(v => v.Brand).ToList(),
				"model_desc" => vehiclesList.OrderByDescending(v => v.Model).ToList(),
				"model" => vehiclesList.OrderBy(v => v.Model).ToList(),
				_ => vehiclesList.OrderBy(v => v.RegistrationNumber).ToList()
			};

			// Paginare
			int totalItems = vehicles.Count;
			var paginatedVehicles = vehicles.Skip((pageNumber - 1) * pageSize).Take(pageSize).ToList();

			ViewBag.PageNumber = pageNumber;
			ViewBag.TotalPages = (int)Math.Ceiling(totalItems / (double)pageSize);
			ViewBag.Title = "Vehicles for " + regionName;

			return View(paginatedVehicles);
		}

		// Post - Users/AssignDriverToDispatcher/id
		[Authorize(Roles = "Admin")]
		[HttpPost]
		public async Task<IActionResult> AssignDriverToDispatcherByRegionId(string driverId, string dispatcherId)
		{
			if (driverId == null || dispatcherId == null) return NotFound();
			var driver = await db.ApplicationUsers
			   .Include(d => d.Deliveries)
			   .FirstOrDefaultAsync(d => d.Id == driverId);

			var dispatcher = await db.ApplicationUsers.FindAsync(dispatcherId);

			if (driver == null || dispatcher == null) return NotFound();
			if (dispatcher.RegionId == null)
			{
				TempData["Error"] = "The dispatcher is not assigned to a region!";
				return RedirectToAction("Show", new { id = driverId });
			}

			// Verific daca soferul are Deliveries pe care trebuie sa le termine
			bool hasActiveDeliveries = await db.Deliveries
				.AnyAsync(d => d.DriverId == driverId && (d.Status == "Planned" || d.Status == "In Progress"));

			if (hasActiveDeliveries)
			{
				TempData["Error"] = "The driver cannot change regions while having 'Planned' or 'In Progress' deliveries.";
				return RedirectToAction("Show", new { id = driverId });
			}

			driver.RegionId = dispatcher.RegionId;
			await db.SaveChangesAsync();

			if (!(await _userManager.IsInRoleAsync(driver, "Sofer")))
			{
				await _userManager.AddToRoleAsync(driver, "Sofer");
			}

			TempData["Success"] = "Driver successfully assigned to new region.";
			return RedirectToAction("Show", new { id = driverId });
		}

		// Get - Users/Show/id
		[Authorize] // Doar pentru userii care au cont
		public async Task<IActionResult> Show(string id)
		{
			if (id == null) return NotFound();


			var loggedInUserId = _userManager.GetUserId(User); // Iau id-ul userului logat
			var loggedInUserRoles = await _userManager.GetRolesAsync(await _userManager.FindByIdAsync(loggedInUserId)); // Iau rolul userului logat

			var userToView = await db.ApplicationUsers
									 .Include(u => u.Region)
									 .Include(u => u.Orders)
									 .Include(u => u.Deliveries)
									 .Include(u => u.FeedbacksGiven)
									 .Include(u => u.FeedbacksReceived)
									 .FirstOrDefaultAsync(u => u.Id == id);

			if (userToView == null) return NotFound();
			if (userToView.IsDeleted)
			{
				TempData["Error"] = "This user has been deleted.";
				return RedirectToAction("Index");
			}

			var rolesOfUserToView = await _userManager.GetRolesAsync(userToView); // Iau rolul userului caruia tre sa-i afisez detaliile
			var roleOfUserToView = rolesOfUserToView.FirstOrDefault();

			ViewBag.IsCurrentUserLoggedIn = loggedInUserId == id;

			var feedbacks = userToView.FeedbacksReceived;
			if (feedbacks != null && feedbacks.Any())
			{
				var averageRating = feedbacks.Average(f => f.Rating);

				ViewBag.FullStars = (int)Math.Floor(averageRating);
				ViewBag.HalfStar = (averageRating - ViewBag.FullStars >= 0.5);
				ViewBag.EmptyStars = 5 - (ViewBag.FullStars + (ViewBag.HalfStar ? 1 : 0));
			}
			else
			{
				// Valori default pentru rating
				ViewBag.FullStars = 0;
				ViewBag.HalfStar = false;
				ViewBag.EmptyStars = 5;
			}

			// Admin vede tot si poate asigna un dispecer unui sofer
			if (loggedInUserRoles.Contains("Admin"))
			{
				ViewBag.UserRole = roleOfUserToView;

				// Lista cu dispeceri daca ne uitam la profilul unui sofer
				if (roleOfUserToView == "Sofer")
				{
					// Fetch all application users and filter dispatchers in memory
					var allUsers = await db.ApplicationUsers.Include(u => u.Region).ToListAsync();
					var dispatchers = allUsers.Where(u => _userManager.IsInRoleAsync(u, "Dispecer").Result).ToList();

					ViewBag.Dispatchers = dispatchers.Select(d => new SelectListItem
					{
						Value = d.Id,
						Text = $"{d.FirstName} {d.LastName} ({d.Region?.County ?? "No Region"})"
					});
				}

				return View(userToView);
			}

			// Dispecer vede doar Sofer si Client
			if (loggedInUserRoles.Contains("Dispecer") && (roleOfUserToView == "Sofer" || roleOfUserToView == "Client"))
			{
				ViewBag.UserRole = roleOfUserToView;
				return View(userToView);
			}

			// Sofer vede doar Client
			if (loggedInUserRoles.Contains("Sofer") && roleOfUserToView == "Client")
			{
				ViewBag.UserRole = roleOfUserToView;
				return View(userToView);
			}

			// Client nu vede nimic
			if (loggedInUserRoles.Contains("Client"))
			{
				return Forbid();
			}

			// Daca nu e nimic, nu avem voie(desi putin probabil sa ajungem aici)
			return Forbid();
		}

		// Get - Users/Profile
		[Authorize]
		public async Task<IActionResult> Profile()
		{
			var userId = _userManager.GetUserId(User);
			var user = await db.ApplicationUsers
							   .Include(u => u.Region)
							   .Include(u => u.Orders)
							   .Include(u => u.Deliveries)
							   .Include(u => u.FeedbacksGiven)
							   .Include(u => u.FeedbacksReceived)
							   .FirstOrDefaultAsync(u => u.Id == userId);
			if (user == null) return NotFound();
			if (user.IsDeleted)
			{
				TempData["Error"] = "This user has been deleted.";
				return RedirectToAction("Index");
			}

			var roles = await _userManager.GetRolesAsync(user);
			ViewBag.UserRole = roles.FirstOrDefault();

			if (ViewBag.UserRole == "Sofer")
			{
				// Calculez rating-ul mediu ca sa-l afisez cu stelute in View
				var feedbacks = user.FeedbacksReceived;
				if (feedbacks != null && feedbacks.Any())
				{
					var averageRating = feedbacks.Average(f => f.Rating);

					ViewBag.FullStars = (int)Math.Floor(averageRating);
					ViewBag.HalfStar = (averageRating - ViewBag.FullStars >= 0.5);
					ViewBag.EmptyStars = 5 - (ViewBag.FullStars + (ViewBag.HalfStar ? 1 : 0));
				}
				else
				{
					// Valori default pentru rating
					ViewBag.FullStars = 0;
					ViewBag.HalfStar = false;
					ViewBag.EmptyStars = 5;
				}
			}

			return View(user);
		}

		// Post - Users/UploadProfilePicture
		[HttpPost]
		[Authorize]
		public async Task<IActionResult> UploadProfilePicture(IFormFile profilePicture)
		{
			if (profilePicture == null || profilePicture.Length == 0)
			{
				TempData["Error"] = "Please select a valid image!";
				return RedirectToAction("Profile");
			}

			var userId = _userManager.GetUserId(User);
			var user = await _userManager.FindByIdAsync(userId);

			if (user == null)
			{
				TempData["Error"] = "User not found!";
				return RedirectToAction("Profile");
			}

			// Verific daca poza are extensie valida
			var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif" };
			var extension = Path.GetExtension(profilePicture.FileName).ToLowerInvariant();

			if (!allowedExtensions.Contains(extension))
			{
				TempData["Error"] = "Invalid file type. Only JPG, JPEG, PNG, and GIF are allowed!";
				return RedirectToAction("Profile");
			}

			// Sterg poza veche din wwwroot/Images
			if (!string.IsNullOrEmpty(user.PhotoPath))
			{
				var oldFilePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", user.PhotoPath);
				if (System.IO.File.Exists(oldFilePath))
				{
					System.IO.File.Delete(oldFilePath);
				}
			}

			// Salvez poza noua in wwwroot/Images
			var fileName = user.FirstName + user.LastName + extension;
			var filePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "Images", fileName);

			using (var stream = new FileStream(filePath, FileMode.Create))
			{
				await profilePicture.CopyToAsync(stream);
			}

			// Updatez poza(adica path-ul ei) in BD
			user.PhotoPath = $"Images/{fileName}";
			await _userManager.UpdateAsync(user);

			TempData["Success"] = "Profile picture updated successfully.";
			return RedirectToAction("Profile");
		}

		// Get - Users/Edit/id
		[Authorize(Roles = "Admin")]
		public async Task<IActionResult> Edit(string id)
		{
			if (string.IsNullOrEmpty(id))
				return NotFound();

			// Nu am voie sa ma editez pe mine
			if (id == _userManager.GetUserId(User))
			{
				TempData["Error"] = "You cannot edit your own account!";
				return RedirectToAction("Index");
			}

			// Nu am voie sa editez alt admin
			var loggedInUserRoles = await _userManager.GetRolesAsync(await _userManager.FindByIdAsync(_userManager.GetUserId(User)));
			if (loggedInUserRoles.Contains("Admin"))
			{
				var userRoles = await _userManager.GetRolesAsync(await _userManager.FindByIdAsync(id));
				if (userRoles.Contains("Admin"))
				{
					TempData["Error"] = "You cannot edit another admin!";
					return RedirectToAction("Index");
				}
			}

			// Gasesc userul dupa id
			var user = await db.ApplicationUsers.FindAsync(id);
			if (user == null) return NotFound();

			// Iau rolul userului curent
			var currentRoleId = db.UserRoles
				.Where(ur => ur.UserId == user.Id)
				.Select(ur => ur.RoleId)
				.FirstOrDefault();

			// Iau toate rolurile si il marchez ca selectat pe cel pe care userul il are deja
			ViewBag.AllRoles = db.Roles.Select(r => new SelectListItem
			{
				Value = r.Id,
				Text = r.Name,
				Selected = r.Id == currentRoleId // Preselectez rolul curent
			}).ToList();

			// Iau toate judetele pentru dropdown
			ViewBag.Regions = db.Regions
				.Select(r => new SelectListItem
				{
					Value = r.Id.ToString(),
					Text = r.County
				})
				.ToList();

			ViewBag.Latitude = user.Latitude;
			ViewBag.Longitude = user.Longitude;

			return View(user);
		}

		// Post - Users/Edit/id
		[Authorize(Roles = "Admin")]
		[HttpPost]
		public async Task<IActionResult> Edit(string id, ApplicationUser newData, [FromForm] string newRole, [FromForm] int? newRegionId)
		{
			if (id == null) return NotFound();

			var user = await db.ApplicationUsers.FindAsync(id);
			if (user == null)
			{
				TempData["Error"] = "User not found!";
				return NotFound();
			}

			// Mesaj custom de validare pentru rol
			if (string.IsNullOrEmpty(newRole) || newRole == "Select role")
			{
				ModelState.AddModelError("newRole", "Please select a valid role for the user!");
			}

			user.AllRoles = GetAllRoles();

			// Verific cu functia IsValidAddressInRomania daca adresa e valida
			if (!IsValidAddressInRomania(newData.HomeAddress))
			{
				TempData["Error"] = "Invalid address. Please select an address from Romania.";
				PopulateEditViewData(user, newRole, newRegionId);
				return View(user);
			}

			if (ModelState.IsValid)
			{
				// Updatez datele userului
				user.FirstName = newData.FirstName;
				user.LastName = newData.LastName;
				user.HomeAddress = newData.HomeAddress;
				user.Email = newData.Email;
				user.UserName = newData.UserName;
				user.PhoneNumber = newData.PhoneNumber;
				user.DateHired = newData.DateHired;
				user.RegionId = newData.RegionId;
				user.PhotoPath = user.PhotoPath;
				user.Latitude = newData.Latitude;
				user.Longitude = newData.Longitude;

				var roles = db.Roles.ToList();

				foreach (var role in roles)
				{
					await _userManager.RemoveFromRoleAsync(user, role.Name);
				}

				var roleName = await _roleManager.FindByIdAsync(newRole);
				await _userManager.AddToRoleAsync(user, roleName.Name);

				await db.SaveChangesAsync();
				return RedirectToAction("Index", TempData["Success"] = "User edited successfully!");
			}

			// Dar daca ModelState nu e valid, returnez userul cu datele vechi
			user.AllRoles = GetAllRoles();
			ViewBag.Regions = db.Regions.Select(r => new SelectListItem
			{
				Value = r.Id.ToString(),
				Text = r.County
			});
			TempData["Error"] = "Validation error. Please check the form.";
			return View(user);
		}

		// Get - Users/EditMyself
		[Authorize]
		public async Task<IActionResult> EditMyself()
		{
			// Iau id-ul userului logat
			var userId = _userManager.GetUserId(User);

			var user = await db.ApplicationUsers.FindAsync(userId);
			if (user == null)
			{
				TempData["Error"] = "User not found!";
				return RedirectToAction("Profile");
			}

			ViewBag.Latitude = user.Latitude;
			ViewBag.Longitude = user.Longitude;

			return View(user);
		}

		// Post - Users/EditMyself
		[Authorize]
		[HttpPost]
		public async Task<IActionResult> EditMyself(ApplicationUser newData)
		{
			var userId = _userManager.GetUserId(User);

			var user = await db.ApplicationUsers.FindAsync(userId);
			if (user == null)
			{
				TempData["Error"] = "User not found!";
				return RedirectToAction("Profile");
			}

			ModelState.Remove(nameof(ApplicationUser.DateHired));
			ModelState.Remove(nameof(ApplicationUser.RegionId));
			ModelState.Remove(nameof(ApplicationUser.AverageRating));
			ModelState.Remove(nameof(ApplicationUser.DismissalNoticeDate));
			ModelState.Remove(nameof(ApplicationUser.PhotoPath));

			if (!ModelState.IsValid)
			{
				TempData["Error"] = "Validation error. Please check the form.";
				return View(newData);
			}

			// Verific cu functia IsValidAddressInRomania daca adresa e valida
			if (!IsValidAddressInRomania(newData.HomeAddress))
			{
				TempData["Error"] = "Invalid address. Please select an address from Romania.";
				ViewBag.Latitude = user.Latitude;
				ViewBag.Longitude = user.Longitude;
				return View(user);
			}

			// Updatez doar campurile care sunt editabile
			user.FirstName = newData.FirstName;
			user.LastName = newData.LastName;
			user.HomeAddress = newData.HomeAddress;
			user.Email = newData.Email;
			user.NormalizedEmail = newData.Email.ToUpper();
			user.UserName = newData.UserName;
			user.NormalizedUserName = newData.UserName.ToUpper();
			user.PhoneNumber = newData.PhoneNumber;
			user.Latitude = newData.Latitude;
			user.Longitude = newData.Longitude;

			db.ApplicationUsers.Update(user);

			await db.SaveChangesAsync();

			TempData["Success"] = "Profile updated successfully!";
			return RedirectToAction("Profile"); // Or another appropriate action/view
		}

		// Get - Users/IssuingNoticeTermination/id
		[Authorize(Roles = "Admin")]
		[HttpPost]
		public async Task<IActionResult> IssuingNoticeTermination(string id)
		{
			if (id == null) return NotFound();

			var user = await db.ApplicationUsers
				.Include(u => u.Deliveries)
				.FirstOrDefaultAsync(u => u.Id == id);

			if (user == null) return NotFound();
			var userRole = await _userManager.GetRolesAsync(user);

			// Daca nu e sofer / nu e dispecer nu am voie
			if (userRole.Contains("Sofer") == false && userRole.Contains("Dispecer") == false)
			{
				TempData["Error"] = "Only drivers and dispatchers can be issued a termination notice!";
				return RedirectToAction("Index");
			}

			else if(userRole.Contains("Sofer") == true || userRole.Contains("Dispecer") == true)
			{
				// Daca deja am o data de concediere nu mai dam inca o data
				if (user.DismissalNoticeDate != null)
				{
					TempData["Error"] = "This driver already has a dismissal notice date.";
					return RedirectToAction("Index");
				}

				// Daca userul e dispecer sau sofer si are mai putin de 90 de zile in firma, nu poate fi concediat
				var daysSinceHired = (DateTime.Now - user.DateHired).Days;
				if (daysSinceHired < 90)
				{
					TempData["Error"] = "This user is on probation and cannot be fired!";
					return RedirectToAction("Index");
				}

				if (user.DismissalNoticeDate == null)
				{
					user.DismissalNoticeDate = DateTime.Now;
				}

				// Daca este sofer si are rating >3 nu poate fi concediat
				if (userRole.Contains("Sofer") && user.AverageRating > 3)
				{
					TempData["Error"] = "The driver cannot be fired because he has an average rating bigger than 3/5!";
					return RedirectToAction("IndexDrivers");
				}

				// Daca soferul are Deliveries Planned / In Progress, nu poate fi concediat
				if (userRole.Contains("Sofer"))
				{
					foreach (var delivery in user.Deliveries)
					{
						if (userRole.Contains("Sofer") && user.Deliveries?.Any(d => d.Status == "Planned" || d.Status == "In Progress") == true)
						{
							TempData["Error"] = "The driver cannot be fired because he has planned or in-progress deliveries!";
							return RedirectToAction("IndexDrivers");
						}
					}
				}

				// Altfel userului ii este trimisa o instiintare de concediere
				user.DismissalNoticeDate = DateTime.Now;
			}

			db.Update(user);
			await db.SaveChangesAsync();

			TempData["Success"] = "Termination notice successfully issued!";
			return RedirectToAction("Index");
		}

		// Get - Users/Delete/id
		[Authorize(Roles = "Admin")]
		[HttpPost]
		public async Task<IActionResult> Delete(string id)
		{
			if (id == null) return NotFound();

			var user = await db.ApplicationUsers
				.Include(u => u.Orders)
				.Include(u => u.Deliveries)
				.Include(u => u.FeedbacksGiven)
				.Include(u => u.FeedbacksReceived)
				.FirstOrDefaultAsync(u => u.Id == id);

			if (user == null) return NotFound();

			var role = await _userManager.GetRolesAsync(user);
			if (role == null) return NotFound();

			if (role.Contains("Admin"))
			{
				TempData["Error"] = "You cannot delete an admin!";
				return RedirectToAction("Index");
			}

			if (role.Contains("Sofer") || role.Contains("Dispecer"))
			{
				if (user.DismissalNoticeDate == null)
				{
					TempData["Error"] = "This user needs a dismissal notice before deletion.";
					return RedirectToAction("Index");
				}

				var daysSinceNotice = (DateTime.Now - user.DismissalNoticeDate.Value).Days;
				if (daysSinceNotice < 30)
				{
					TempData["Error"] = "You can only delete this user after 30 days of the dismissal notice.";
					return RedirectToAction("Index");
				}
			}

			if (role.Contains("Client"))
			{
				bool hasActiveOrders = user.Orders.Any(o =>
					o.Status == OrderStatus.Placed ||
					o.Status == OrderStatus.InProgress ||
					o.Status == OrderStatus.FailedDelivery);

				if (hasActiveOrders)
				{
					TempData["Error"] = "Client cannot be deleted because they have active or failed orders.";
					return RedirectToAction("IndexClients");
				}
			}

			// Soft Delete + Anonimizare date conform GDPR
			user.IsDeleted = true;
			user.IsAvailable = false;
			user.DeletedAt = DateTime.UtcNow;
			user.FirstName = "[Deleted]";
			user.LastName = "[User]";
			user.UserName = $"deleted_user_{Guid.NewGuid()}";
			user.Email = $"deleted_{Guid.NewGuid()}@example.com";
			user.PhoneNumber = string.Empty;
			user.HomeAddress = "Anonymized";
			user.Latitude = null;
			user.Longitude = null;
			user.NormalizedEmail = user.Email;
			user.NormalizedUserName = user.UserName;

			if (!string.IsNullOrEmpty(user.PhotoPath))
			{
				var path = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", user.PhotoPath);
				if (System.IO.File.Exists(path)) System.IO.File.Delete(path);
				user.PhotoPath = null;
			}

			await db.SaveChangesAsync();
			TempData["Success"] = "User was successfully soft-deleted and anonymized.";
			return RedirectToAction("Index");
		}

		[Authorize]
		[HttpPost]
		public async Task<IActionResult> DeleteAccount()
		{
			var userId = _userManager.GetUserId(User);
			var user = await db.ApplicationUsers
				.Include(u => u.Orders)
				.Include(u => u.Deliveries)
				.FirstOrDefaultAsync(u => u.Id == userId);

			if (user == null) return NotFound();

			var roles = await _userManager.GetRolesAsync(user);
			var role = roles.FirstOrDefault();

			if (role == "Admin")
			{
				var adminCount = await (from userRole in db.UserRoles
										join r in db.Roles on userRole.RoleId equals r.Id
										join activeUser in db.ApplicationUsers on userRole.UserId equals activeUser.Id
										where r.Name == "Admin"
											&& userRole.UserId != userId
											&& !activeUser.IsDeleted
										select userRole.UserId).CountAsync();

				if (adminCount <= 2)
				{
					TempData["Error"] = "You cannot delete your account because there must be at least two other Admins in the application.";
					return RedirectToAction("Profile");
				}
			}
			else if (role == "Dispecer")
			{
				bool hasOngoingDeliveries = db.Deliveries
					.Any(d => d.Vehicle.RegionId == user.RegionId && d.Status != "Completed");

				if (hasOngoingDeliveries)
				{
					TempData["Error"] = "You cannot delete your account while your region has active deliveries.";
					return RedirectToAction("Profile");
				}
			}
			else if (role == "Sofer")
			{
				bool hasActiveDeliveries = user.Deliveries.Any(d =>
					d.Status == "Planned" ||
					d.Status == "In Progress");

				if (hasActiveDeliveries)
				{
					TempData["Error"] = "You cannot delete your account while you have deliveries to complete.";
					return RedirectToAction("Profile");
				}
			}
			else if (role == "Client")
			{
				bool hasPendingOrders = user.Orders.Any(o =>
					o.Status == OrderStatus.Placed ||
					o.Status == OrderStatus.InProgress ||
					o.Status == OrderStatus.FailedDelivery);

				if (hasPendingOrders)
				{
					TempData["Error"] = "You cannot delete your account while you have orders that need delivery.";
					return RedirectToAction("Profile");
				}
			}

			// Soft Delete + Anonimizare date conform GDPR
			user.IsDeleted = true;
			user.IsAvailable = false;
			user.DeletedAt = DateTime.UtcNow;
			user.FirstName = "[Deleted]";
			user.LastName = "[User]";
			user.UserName = $"deleted_user_{Guid.NewGuid()}";
			user.Email = $"deleted_{Guid.NewGuid()}@example.com";
			user.PhoneNumber = string.Empty;
			user.HomeAddress = "Anonymized";
			user.Latitude = 0;
			user.Longitude = 0;
			user.NormalizedEmail = user.Email;
			user.NormalizedUserName = user.UserName;

			if (!string.IsNullOrEmpty(user.PhotoPath))
			{
				var path = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", user.PhotoPath);
				if (System.IO.File.Exists(path)) System.IO.File.Delete(path);
				user.PhotoPath = null;
			}

			await db.SaveChangesAsync();

			// Force Log Out without Session
			await _signInManager.SignOutAsync(); // Sign out the user (Identity)
			Response.Cookies.Delete(".AspNetCore.Identity.Application"); // Remove Identity Cookie

			// Set Headers
			Response.Headers["Cache-Control"] = "no-cache, no-store, must-revalidate";
			Response.Headers["Pragma"] = "no-cache";
			Response.Headers["Expires"] = "0";

			TempData["Success"] = "Your account was successfully deleted.";
			return Redirect("/Identity/Account/Login");
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

		[NonAction]
		private void PopulateEditViewData(ApplicationUser user, string selectedRole, int? selectedRegionId)
		{
			// Repopulez rolurile
			ViewBag.AllRoles = db.Roles.Select(r => new SelectListItem
			{
				Value = r.Id,
				Text = r.Name,
				Selected = r.Id == selectedRole
			}).ToList();

			// Repopulez regiunile
			ViewBag.Regions = db.Regions.Select(r => new SelectListItem
			{
				Value = r.Id.ToString(),
				Text = r.County,
				Selected = r.Id == selectedRegionId
			}).ToList();

			// Repopulez coordonatele
			ViewBag.Latitude = user.Latitude;
			ViewBag.Longitude = user.Longitude;
		}
	}
}

using Licenta_v1.Data;
using Licenta_v1.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace Licenta_v1.Controllers
{
	[Authorize(Roles = "Admin")]
	public class UsersController : Controller
	{
		private readonly ApplicationDbContext db;
		private readonly UserManager<ApplicationUser> _userManager;
		private readonly RoleManager<IdentityRole> _roleManager;

		public UsersController(
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

		private async Task<(List<ApplicationUser> Users, int Count)> GetFilteredUsers(
			string roleFilter,
			string searchString,
			int? regionId,
			string sortOrder,
			int pageNumber,
			int pageSize)
		{
			var users = db.Users.Include(u => u.Region).AsQueryable();

			// Filtrez userii dupa rol
			if (!string.IsNullOrEmpty(roleFilter))
			{
				// Aflu ce useri au rolul respectiv si le iau id-urile
				var usersInRole = await (from ur in db.UserRoles
										 join r in db.Roles on ur.RoleId equals r.Id
										 where r.Name == roleFilter
										 select ur.UserId).ToListAsync();
				users = users.Where(u => usersInRole.Contains(u.Id));
			}

			// Caut userii dupa nume, prenume sau username
			if (!string.IsNullOrEmpty(searchString))
			{
				users = users.Where(u => u.FirstName.Contains(searchString)
									  || u.LastName.Contains(searchString)
									  || u.UserName.Contains(searchString));
			}

			// Filtrez userii dupa judet
			if (regionId.HasValue)
			{
				users = users.Where(u => u.RegionId == regionId.Value);
			}

			// Sortarea propriu-zisa
			switch (sortOrder)
			{
				case "name_desc":
					users = users.OrderByDescending(u => u.LastName).ThenByDescending(u => u.FirstName);
					break;
				case "name":
					users = users.OrderBy(u => u.LastName).ThenBy(u => u.FirstName);
					break;
				case "date":
					users = users.OrderBy(u => u.DateHired);
					break;
				case "date_desc":
					users = users.OrderByDescending(u => u.DateHired);
					break;
				default:
					users = users.OrderBy(u => u.UserName);
					break;
			}

			var count = await users.CountAsync();
			var pagedUsers = await users.Skip((pageNumber - 1) * pageSize).Take(pageSize).ToListAsync();

			return (pagedUsers, count);
		}

		// Get - Users/Index
		public async Task<IActionResult> Index(string searchString, int? regionId, string sortOrder, int pageNumber = 1)
		{
			int pageSize = 10;

			ViewBag.CurrentSort = sortOrder;
			ViewBag.NameSortParam = string.IsNullOrEmpty(sortOrder) ? "name" : "";
			ViewBag.NameSortParamDesc = sortOrder == "name" ? "name_desc" : "name";
			ViewBag.DateSortParam = sortOrder == "date" ? "date_desc" : "date";

			ViewBag.SearchString = searchString;
			ViewBag.RegionId = regionId;
			ViewBag.Regions = new SelectList(db.Regions, "Id", "County");

			var (users, count) = await GetFilteredUsers(null, searchString, regionId, sortOrder, pageNumber, pageSize);

			ViewBag.PageNumber = pageNumber;
			ViewBag.TotalPages = (int)Math.Ceiling(count / (double)pageSize);

			return View(users);
		}

		// Get - Users/Index-Clienti
		public async Task<IActionResult> IndexClients(string searchString, int? regionId, string sortOrder, int pageNumber = 1)
		{
			int pageSize = 10;

			ViewBag.CurrentSort = sortOrder;
			ViewBag.NameSortParam = string.IsNullOrEmpty(sortOrder) ? "name" : "";
			ViewBag.NameSortParamDesc = sortOrder == "name" ? "name_desc" : "name";
			ViewBag.DateSortParam = sortOrder == "date" ? "date_desc" : "date";

			ViewBag.SearchString = searchString;
			ViewBag.RegionId = regionId;
			ViewBag.Regions = new SelectList(db.Regions, "Id", "County");

			var (users, count) = await GetFilteredUsers("Client", searchString, regionId, sortOrder, pageNumber, pageSize);

			ViewBag.PageNumber = pageNumber;
			ViewBag.TotalPages = (int)Math.Ceiling(count / (double)pageSize);

			return View(users);
		}

		// Get - Users/Index-Soferi
		public async Task<IActionResult> IndexDrivers(string searchString, int? regionId, string sortOrder, int pageNumber = 1)
		{
			int pageSize = 10;

			ViewBag.CurrentSort = sortOrder;
			ViewBag.NameSortParam = string.IsNullOrEmpty(sortOrder) ? "name" : "";
			ViewBag.NameSortParamDesc = sortOrder == "name" ? "name_desc" : "name";
			ViewBag.DateSortParam = sortOrder == "date" ? "date_desc" : "date";

			ViewBag.SearchString = searchString;
			ViewBag.RegionId = regionId;
			ViewBag.Regions = new SelectList(db.Regions, "Id", "County");

			var (users, count) = await GetFilteredUsers("Sofer", searchString, regionId, sortOrder, pageNumber, pageSize);

			ViewBag.PageNumber = pageNumber;

			ViewBag.TotalPages = (int)Math.Ceiling(count / (double)pageSize);
			return View(users);
		}

		// Get - Users/Index-Dispeceri
		public async Task<IActionResult> IndexDispatchers(string searchString, int? regionId, string sortOrder, int pageNumber = 1)
		{
			int pageSize = 10;

			ViewBag.CurrentSort = sortOrder;
			ViewBag.NameSortParam = string.IsNullOrEmpty(sortOrder) ? "name" : "";
			ViewBag.NameSortParamDesc = sortOrder == "name" ? "name_desc" : "name";
			ViewBag.DateSortParam = sortOrder == "date" ? "date_desc" : "date";

			ViewBag.SearchString = searchString;
			ViewBag.RegionId = regionId;
			ViewBag.Regions = new SelectList(db.Regions, "Id", "County");

			var (users, count) = await GetFilteredUsers("Dispecer", searchString, regionId, sortOrder, pageNumber, pageSize);

			ViewBag.PageNumber = pageNumber;
			ViewBag.TotalPages = (int)Math.Ceiling(count / (double)pageSize);

			return View(users);
		}

		// Get - Users/Show/id
		public async Task<IActionResult> Show(string id)
		{
			if (id == null) return NotFound();

			var user = await db.ApplicationUsers
							   .Include(u => u.Region)
							   .Include(u => u.Orders)
							   .Include(u => u.Deliveries)
							   .Include(u => u.FeedbacksGiven)
							   .Include(u => u.FeedbacksReceived)
							   .FirstOrDefaultAsync(u => u.Id == id);

			if (user == null) return NotFound();

			var roles = await _userManager.GetRolesAsync(user);
			ViewBag.UserRole = roles.FirstOrDefault();

			return View(user);
		}

		// Get - Users/Edit/id
		public async Task<IActionResult> Edit(string id)
		{
			if (id == null) return NotFound();

			var user = await db.ApplicationUsers.FindAsync(id);
			if (user == null) return NotFound();

			user.AllRoles = GetAllRoles();

			var currentRoles = await _userManager.GetRolesAsync(user);
			var currentRole = await _roleManager.FindByNameAsync(currentRoles.FirstOrDefault());

			ViewBag.UserRole = currentRole.Id;

			ViewBag.Regions = db.Regions.Select(r => new SelectListItem
			{
				Value = r.Id.ToString(),
				Text = r.County
			});

			return View(user);
		}

		// Post - Users/Edit/id
		[HttpPost]
		public async Task<IActionResult> Edit(string id, ApplicationUser newData, [FromForm] string newRole, [FromForm] int? newRegionId)
		{
			if (id == null) return NotFound();

			var user = await db.ApplicationUsers.FindAsync(id);
			if (user == null) return NotFound();

			user.AllRoles = GetAllRoles();

			if (ModelState.IsValid)
			{
				// Updatez datele userului
				user.FirstName = newData.FirstName;
				user.LastName = newData.LastName;
				user.Email = newData.Email;
				user.UserName = newData.UserName;
				user.PhoneNumber = newData.PhoneNumber;
				user.DateHired = newData.DateHired;
				user.RegionId = newRegionId;

				var roles = db.Roles.ToList();

				foreach (var role in roles)
				{
					await _userManager.RemoveFromRoleAsync(user, role.Name);
				}

				var roleName = await _roleManager.FindByIdAsync(newRole);
				await _userManager.AddToRoleAsync(user, roleName.Name);

				await db.SaveChangesAsync();
				return RedirectToAction("Index");
			}

			// Dar daca ModelState nu e valid, returnez userul cu datele vechi
			user.AllRoles = GetAllRoles();
			ViewBag.Regions = db.Regions.Select(r => new SelectListItem
			{
				Value = r.Id.ToString(),
				Text = r.County
			});
			return View(user);
		}

		// Get - Users/Delete/id
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

			// Luam rolul userului separat ca sa ne fie usor la scris
			var role = await _userManager.GetRolesAsync(user);

			// Daca un dispecer sau sofer au o vechime de < 90 de zile in firma nu pot fi stersi
			if (role.Contains("Dispecer") || role.Contains("Sofer"))
			{
				var daysSinceHired = (DateTime.Now - user.DateHired).TotalDays;
				if (daysSinceHired < 90)
				{
					ModelState.AddModelError("", "Nu poti sterge dispecerul sau soferul deoarece nu au trecut inca 90 de zile de la angajare!");
					return RedirectToAction("Index");
				}
			}

			// Daca userul e sofer nu poate fi sters decat dupa 30 de zile de la notificarea de concediere
			if (role.Contains("Sofer"))
			{
				if (user.DismissalNoticeDate.HasValue)
				{
					var daysSinceNotice = (DateTime.Now - user.DismissalNoticeDate.Value).TotalDays;
					if (daysSinceNotice < 30)
					{
						ModelState.AddModelError("", "Nu poti sterge soferul deoarece nu au trecut inca 30 de zile de la notificarea de concediere!");
						return RedirectToAction("Index");
					}
				}
				else
				{
					ModelState.AddModelError("", "Nu poti sterge soferul fara sa-l avertizezi ca va fi concediat cu 30 de zile inainte!");
					return RedirectToAction("Index");
				}
			}

			// Daca soferul este in mijlocul unei livrari, nu poate fi sters
			if (role.Contains("Sofer"))
			{
				if (user.Deliveries?.Count > 0)
				{
					foreach (var delivery in user.Deliveries)
					{
						if (delivery.Status.Contains("InProgress"))
						{
							ModelState.AddModelError("", "Nu poti sterge soferul fiindca e in mijlocul unei livrari!");
							return RedirectToAction("Index");
						}
					}
				}
			}

			// Handle-uiesc feeback-urile pe care le-a dat userul
			if (user.FeedbacksGiven?.Count > 0)
			{
				db.Feedbacks.RemoveRange(user.FeedbacksGiven);
			}

			// Handle-uiesc feeback-urile pe care le-a primit userul in cazul in care e sofer
			if (user.FeedbacksReceived?.Count > 0)
			{
				db.Feedbacks.RemoveRange(user.FeedbacksReceived);
			}

			// Handle-uiesc comenzile userului in cazul in care e client
			if (user.Orders?.Count > 0)
			{
				db.Orders.RemoveRange(user.Orders);
			}

			// Handle-uiesc livrarile userului in cazul in care e sofer
			if (user.Deliveries?.Count > 0)
			{
				db.Deliveries.RemoveRange(user.Deliveries);
			}

			db.ApplicationUsers.Remove(user); // Intr-un final, sterg userul
			await db.SaveChangesAsync();

			return RedirectToAction("Index");
		}
	}
}

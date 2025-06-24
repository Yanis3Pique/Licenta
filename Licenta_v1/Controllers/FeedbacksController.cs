using Licenta_v1.Data;
using Licenta_v1.Models;
using Licenta_v1.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace Licenta_v1.Controllers
{
	public class FeedbacksController : Controller
	{
		private readonly ApplicationDbContext db;
		private readonly UserManager<ApplicationUser> _userManager;
		private readonly RoleManager<IdentityRole> _roleManager;

		public FeedbacksController(
			ApplicationDbContext context,
			UserManager<ApplicationUser> userManager,
			RoleManager<IdentityRole> roleManager
			)
		{
			db = context;
			_userManager = userManager;
			_roleManager = roleManager;
		}

		// Get - Feedbacks/GiveFeedback
		[Authorize(Roles = "Client")]
		public async Task<IActionResult> GiveFeedback(string driverId, int orderId)
		{
			var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

			// Verific daca comanda exista, este a userului curent si este livrata
			var order = await db.Orders
				.Include(o => o.Delivery)
				.ThenInclude(d => d.Driver)
				.Include(o => o.Feedback)
				.FirstOrDefaultAsync(o => o.Id == orderId && o.ClientId == userId && o.Status == OrderStatus.Delivered);

			if (order == null || order.Delivery?.DriverId != driverId)
			{
				return NotFound();
			}

			// Verific daca userul a mai dat feedback la aceasta comanda
			if (order.Feedback != null)
			{
				TempData["Error"] = "You have already submitted feedback for this order.";
				return RedirectToAction("Index", "Orders");
			}

			var feedback = new Feedback
			{
				DriverId = driverId,
				ClientId = userId,
				OrderId = orderId,
				FeedbackDate = DateTime.Now
			};

			return View(feedback);
		}

		// Post - Feedbacks/GiveFeedback
		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Roles = "Client")]
		public async Task<IActionResult> GiveFeedback(Feedback feedback)
		{
			if (!ModelState.IsValid)
			{
				return View(feedback);
			}

			var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

			var order = await db.Orders
				.Include(o => o.Delivery)
				.FirstOrDefaultAsync(o =>
					o.Id == feedback.OrderId &&
					o.ClientId == userId &&
					o.Status == OrderStatus.Delivered);

			if (order == null || order.Delivery?.DriverId != feedback.DriverId)
			{
				TempData["Error"] = "Invalid feedback submission!";
				return RedirectToAction("Index", "Orders");
			}

			int deliveryId = order.Delivery.Id;

			const double dangerousThreshold = 0.7;
			const double basePenaltyPerEvent = 0.15;   // 0.15 stele la severitate maxima

			var events = await db.AggressiveEvents
				.Where(e => e.DeliveryId == deliveryId &&
							e.EventType != "Normal" &&
							e.SeverityScore >= dangerousThreshold)
				.ToListAsync();

			double totalPenalty = events.Sum(e =>
			{
				double weight = (e.SeverityScore - dangerousThreshold) / (1 - dangerousThreshold);
				return weight * basePenaltyPerEvent;
			});

			double adjusted = Math.Max(1.0, feedback.Rating - totalPenalty);
			feedback.Rating = (int)Math.Round(adjusted, 1);

			// 3) Now save everything normally
			db.Feedbacks.Add(feedback);
			await db.SaveChangesAsync();

			// 4) Recompute the driver's average rating

			var driver = await db.ApplicationUsers
				.Include(d => d.FeedbacksReceived)
				.FirstOrDefaultAsync(d => d.Id == feedback.DriverId);

			if (driver != null && driver.FeedbacksReceived.Any())
			{
				driver.AverageRating = driver.FeedbacksReceived.Average(f => f.Rating);
				await db.SaveChangesAsync();
			}

			TempData["Success"] = "Your feedback has been submitted successfully!";
			return RedirectToAction("Index", "Orders");
		}


		// Get - Feedbacks/Index
		[Authorize(Roles = "Admin,Dispecer")]
		public async Task<IActionResult> Index(string searchString, string sortOrder, DateTime? filterDate)
		{
			var user = db.ApplicationUsers.FirstOrDefault(u => u.UserName == User.Identity.Name);
			if (user == null) return Unauthorized();

			var feedbacksQuery = db.Feedbacks
				.Include(f => f.Driver)
				.Include(f => f.Client)
				.Include(f => f.Order)
				.AsQueryable();

			// Dispecerii vad doar Feedbacks din Regiunea lor
			if (User.IsInRole("Dispecer"))
			{
				feedbacksQuery = feedbacksQuery.Where(f => f.Driver.RegionId == user.RegionId);
			}

			// Search
			if (!string.IsNullOrEmpty(searchString))
			{
				string lowerSearch = searchString.ToLower();
				feedbacksQuery = feedbacksQuery.Where(f =>
					f.Client.UserName.ToLower().Contains(lowerSearch) ||
					f.Driver.UserName.ToLower().Contains(lowerSearch) ||
					f.Comment.ToLower().Contains(lowerSearch)
				);
			}

			// Filtrarea
			if (filterDate.HasValue)
			{
				feedbacksQuery = feedbacksQuery.Where(f => f.FeedbackDate.Date == filterDate.Value.Date);
			}

			// Sortarea
			ViewBag.CurrentSort = sortOrder;
			ViewBag.ClientSortParam = sortOrder == "client" ? "client_desc" : "client";
			ViewBag.DriverSortParam = sortOrder == "driver" ? "driver_desc" : "driver";
			ViewBag.RatingSortParam = sortOrder == "rating" ? "rating_desc" : "rating";
			ViewBag.DateSortParam = sortOrder == "date" ? "date_desc" : "date";

			feedbacksQuery = sortOrder switch
			{
				"client" => feedbacksQuery.OrderBy(f => f.Client.UserName),
				"client_desc" => feedbacksQuery.OrderByDescending(f => f.Client.UserName),
				"driver" => feedbacksQuery.OrderBy(f => f.Driver.UserName),
				"driver_desc" => feedbacksQuery.OrderByDescending(f => f.Driver.UserName),
				"rating" => feedbacksQuery.OrderBy(f => f.Rating),
				"rating_desc" => feedbacksQuery.OrderByDescending(f => f.Rating),
				"date" => feedbacksQuery.OrderBy(f => f.FeedbackDate),
				"date_desc" => feedbacksQuery.OrderByDescending(f => f.FeedbackDate),
				_ => feedbacksQuery,
			};

			var feedbacks = await feedbacksQuery.ToListAsync();

			ViewBag.SearchString = searchString;
			ViewBag.FilterDate = filterDate?.ToString("yyyy-MM-dd");
			return View(feedbacks);
		}

		[Authorize(Roles = "Admin,Dispecer,Sofer")]
		public async Task<IActionResult> ShowFeedbacksOfDriver(
			string id, 
			string searchString, 
			string sortOrder, 
			DateTime? filterDate)
		{
			if (string.IsNullOrEmpty(id))
			{
				return NotFound("Driver ID is required.");
			}

			var user = await db.ApplicationUsers.FirstOrDefaultAsync(u => u.UserName == User.Identity.Name);
			if (user == null) return Unauthorized();

			var feedbacksQuery = db.Feedbacks
				.Include(f => f.Driver)
				.Include(f => f.Client)
				.Include(f => f.Order)
				.Where(f => f.DriverId == id)
				.AsQueryable();

			// Adminii vad toate Feedbacks
			if (User.IsInRole("Admin"))
			{
				feedbacksQuery = feedbacksQuery.Where(f => f.DriverId == id);
			}
			// Dispecerii vad doar Feedbacks din Regiunea lor
			else if (User.IsInRole("Dispecer"))
			{
				feedbacksQuery = feedbacksQuery.Where(f => f.DriverId == id && f.Driver.RegionId == user.RegionId);
			}
			// Soferii vede doar Feedbacks pe care le-au primit
			else if (User.IsInRole("Sofer"))
			{
				if (id != user.Id)
				{
					return Unauthorized();
				}
				feedbacksQuery = feedbacksQuery.Where(f => f.DriverId == user.Id);
			}

			// Search
			if (!string.IsNullOrEmpty(searchString))
			{
				string lowerSearch = searchString.ToLower();
				feedbacksQuery = feedbacksQuery.Where(f =>
					f.Client.UserName.ToLower().Contains(lowerSearch) ||
					f.OrderId.ToString().Contains(lowerSearch) ||
					f.Comment.ToLower().Contains(lowerSearch));
			}

			// Filtrare
			if (filterDate.HasValue)
			{
				feedbacksQuery = feedbacksQuery.Where(f => f.FeedbackDate.Date == filterDate.Value.Date);
			}

			// Sortare
			ViewBag.CurrentSort = sortOrder;
			ViewBag.DateSortParam = sortOrder == "date" ? "date_desc" : "date";
			ViewBag.RatingSortParam = sortOrder == "rating" ? "rating_desc" : "rating";

			feedbacksQuery = sortOrder switch
			{
				"rating" => feedbacksQuery.OrderBy(f => f.Rating),
				"rating_desc" => feedbacksQuery.OrderByDescending(f => f.Rating),
				"date" => feedbacksQuery.OrderBy(f => f.FeedbackDate),
				"date_desc" => feedbacksQuery.OrderByDescending(f => f.FeedbackDate),
				_ => feedbacksQuery,
			};

			var feedbacks = await feedbacksQuery.ToListAsync();

			ViewBag.Driver = await db.Users.FindAsync(id);
			ViewBag.SearchString = searchString;
			ViewBag.FilterDate = filterDate?.ToString("yyyy-MM-dd");

			return View(feedbacks);
		}

		[Authorize(Roles = "Admin,Client")]
		public async Task<IActionResult> ShowFeedbacksGivenByClient(
			string id, 
			string searchString, 
			string sortOrder, 
			DateTime? filterDate)
		{
			if (string.IsNullOrEmpty(id))
			{
				return NotFound("Client ID is required.");
			}

			var user = await db.ApplicationUsers.FirstOrDefaultAsync(u => u.UserName == User.Identity.Name);
			if (user == null) return Unauthorized();

			var feedbacksQuery = db.Feedbacks
				.Include(f => f.Driver)
				.Include(f => f.Client)
				.Include(f => f.Order)
				.Where(f => f.ClientId == id)
				.AsQueryable();

			// Adminii vad toate Feedbacks
			if (User.IsInRole("Admin"))
			{
				feedbacksQuery = feedbacksQuery.Where(f => f.ClientId == id);
			}
			// Clientii vad doar Feedbacks date de ei
			else if (User.IsInRole("Client"))
			{
				if (id != user.Id)
				{
					return Unauthorized();
				}
				feedbacksQuery = feedbacksQuery.Where(f => f.ClientId == user.Id);
			}

			// Search
			if (!string.IsNullOrEmpty(searchString))
			{
				string lowerSearch = searchString.ToLower();
				feedbacksQuery = feedbacksQuery.Where(f =>
					f.Driver.UserName.ToLower().Contains(lowerSearch) ||
					f.OrderId.ToString().Contains(lowerSearch) ||
					f.Comment.ToLower().Contains(lowerSearch));
			}

			// Filtrare
			if (filterDate.HasValue)
			{
				feedbacksQuery = feedbacksQuery.Where(f => f.FeedbackDate.Date == filterDate.Value.Date);
			}

			// Sortare
			ViewBag.CurrentSort = sortOrder;
			ViewBag.DateSortParam = sortOrder == "date" ? "date_desc" : "date";
			ViewBag.RatingSortParam = sortOrder == "rating" ? "rating_desc" : "rating";

			feedbacksQuery = sortOrder switch
			{
				"rating" => feedbacksQuery.OrderBy(f => f.Rating),
				"rating_desc" => feedbacksQuery.OrderByDescending(f => f.Rating),
				"date" => feedbacksQuery.OrderBy(f => f.FeedbackDate),
				"date_desc" => feedbacksQuery.OrderByDescending(f => f.FeedbackDate),
				_ => feedbacksQuery,
			};

			var feedbacks = await feedbacksQuery.ToListAsync();

			ViewBag.Client = await db.Users.FindAsync(id);
			ViewBag.SearchString = searchString;
			ViewBag.FilterDate = filterDate?.ToString("yyyy-MM-dd");

			return View(feedbacks);
		}
	}
}

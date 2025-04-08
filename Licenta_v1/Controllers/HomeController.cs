using System.Diagnostics;
using Licenta_v1.Data;
using Licenta_v1.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Licenta_v1.Controllers
{
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;
		private readonly ApplicationDbContext db;
		private readonly IMemoryCache cache;

		public HomeController(ApplicationDbContext context, IMemoryCache memoryCache)
		{
			db = context;
			cache = memoryCache;
		}

		public async Task<IActionResult> Index()
		{
			var feedbacks = await GetRandomFeedbacks();
			ViewBag.Feedbacks = feedbacks;

			return View();
		}

		private async Task<List<Feedback>> GetRandomFeedbacks()
		{
			return await cache.GetOrCreateAsync("RandomFeedbacks", async entry =>
			{
				entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1); // Le iau random la fiecare ora

				var allFeedbacks = await db.Feedbacks
					.Include(f => f.Driver)
					.Include(f => f.Client)
					.Where(f => f.Client != null && !f.Client.IsDeleted)
					.ToListAsync();

				// Grupez feedback-urile dupa client si iau cel mai bun feedback per client
				var groupedFeedbacks = allFeedbacks
					.GroupBy(f => f.ClientId)
					.Select(g => g.OrderByDescending(f => !string.IsNullOrEmpty(f.Comment)) // Au prioritate cele cu comentariu
									.ThenByDescending(f => f.Rating) // Descrescator dupa rating
									.First())
					.ToList();

				// Iau 2 la intamplare
				return groupedFeedbacks.OrderBy(x => Guid.NewGuid()).Take(2).ToList();
			}) ?? new List<Feedback>();
		}


		public IActionResult Privacy()
        {
            return View();
        }

		public IActionResult LearnMore()
		{
			return View();
		}

		public IActionResult GetStarted()
		{
			return View();
		}

		[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}

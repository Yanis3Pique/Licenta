using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Licenta_v1.Data;
using Licenta_v1.Models;

public class TaskuriAutomate : BackgroundService
{
	private readonly IServiceProvider _serviceProvider;

	public TaskuriAutomate(IServiceProvider serviceProvider)
	{
		_serviceProvider = serviceProvider;
	}

	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		while (!stoppingToken.IsCancellationRequested)
		{
			// Execut metoda
			using (var scope = _serviceProvider.CreateScope())
			{
				var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

				// Verific conditiile de stergere a userilor din baza de date
				await CheckAndDeleteUsers(dbContext);
			}

			// Stau 10 minute si execut din nou metoda
			await Task.Delay(TimeSpan.FromMinutes(10), stoppingToken);
		}
	}

	private async Task CheckAndDeleteUsers(ApplicationDbContext dbContext)
	{
		var today = DateTime.Now.Date;

		// Selectez utilizatorii care au DismissalNoticeDate si este la mai mult sau chiar de 30 zile distanta fata de data de azi
		var thresholdDate = DateTime.Today.AddDays(-30);

		var usersToDelete = dbContext.ApplicationUsers
			.Where(u => u.DismissalNoticeDate.HasValue &&
						u.DismissalNoticeDate.Value.Date <= thresholdDate)
			.ToList();

		foreach (var user in usersToDelete)
		{
			// Sterg userii
			dbContext.ApplicationUsers.Remove(user);
		}

		// Salvez modificarile
		await dbContext.SaveChangesAsync();
	}
}
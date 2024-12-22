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
			// Ruleaza in fiecare zi la 12 noaptea
			await Task.Delay(TimeSpan.FromDays(1), stoppingToken);

			using (var scope = _serviceProvider.CreateScope())
			{
				var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

				// Verific conditiile de stergere a userilor din baza de date
				await CheckAndDeleteUsers(dbContext);
			}
		}
	}

	private async Task CheckAndDeleteUsers(ApplicationDbContext dbContext)
	{
		var today = DateTime.Now.Date;

		// Selectez utilizatorii care au DismissalNoticeDate si este la mai mult de 30 zile distanta fata de data de azi
		var usersToDelete = dbContext.ApplicationUsers.Where(u => u.DismissalNoticeDate.HasValue &&
						(today - u.DismissalNoticeDate.Value.Date).TotalDays > 30).ToList();

		foreach (var user in usersToDelete)
		{
			// Apelează metoda Delete existentă
			dbContext.ApplicationUsers.Remove(user);
		}

		// Salvează modificările
		await dbContext.SaveChangesAsync();
	}
}

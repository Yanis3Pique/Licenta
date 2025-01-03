using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Licenta_v1.Data;
using Licenta_v1.Models;
using Microsoft.EntityFrameworkCore;

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
			// Execut metodele
			using (var scope = _serviceProvider.CreateScope())
			{
				var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

				// Verific conditiile de stergere a userilor din baza de date
				await CheckAndDeleteUsers(db);

				// Verific conditiile de programare a mentenantei
				await CheckAndScheduleMaintenance(db);

				// Updatez vehiculele in functie de mentenantele programate
				await UpdateVehicles(db);
			}

			// Stau o ora si execut din nou metoda
			await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
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

	private async Task CheckAndScheduleMaintenance(ApplicationDbContext dbContext)
	{
		// Iau toate masinile cu mentenantele lors
		var vehicles = await dbContext.Vehicles.Include(v => v.MaintenanceRecords).ToListAsync();

		foreach (var vehicle in vehicles)
		{
			// Iau toate taskurile de mentenanta necesare pentru masina
			var tasks = FleetManager.CheckAndScheduleMaintenance(vehicle);

			foreach (var task in tasks)
			{
				// Ma asigur ca nu exista deja un task de mentenanta pentru masina
				var existingTask = vehicle.MaintenanceRecords
					.Any(m => m.MaintenanceType == task.MaintenanceType && m.Status == "Scheduled");

				if (!existingTask)
				{
					dbContext.Maintenances.Add(task);
				}
			}
		}

		// Salvez modificarile
		await dbContext.SaveChangesAsync();
	}

	private async Task UpdateVehicles(ApplicationDbContext dbContext)
	{
		var today = DateTime.Today;

		// Iau toate mentenantele programate sau in curs pentru astazi
		var todayMaintenances = await dbContext.Maintenances
			.Include(m => m.Vehicle)
			.Where(m => m.ScheduledDate.Date == today && m.Status == "Scheduled" ||
						m.Status == "In Progress")
			.ToListAsync();

		// Fac un set cu id-urile vehiculelor care au mentenante active
		var vehiclesWithActiveMaintenance = new HashSet<int>();

		foreach (var maintenance in todayMaintenances)
		{
			var vehicle = maintenance.Vehicle;

			if (vehicle != null)
			{
				vehiclesWithActiveMaintenance.Add(vehicle.Id);

				if (maintenance.Status == "Scheduled")
				{
					vehicle.Status = VehicleStatus.Maintenance;

					maintenance.Status = "In Progress";
				}
			}
		}

		// Iau toate vehiculele care sunt in mentenanta
		var vehiclesInMaintenance = await dbContext.Vehicles
			.Where(v => v.Status == VehicleStatus.Maintenance)
			.ToListAsync();

		foreach (var vehicle in vehiclesInMaintenance)
		{
			// Daca un vehicul nu are mentenante active, il fac disponibil
			if (!vehiclesWithActiveMaintenance.Contains(vehicle.Id))
			{
				vehicle.Status = VehicleStatus.Available;
			}
		}

		await dbContext.SaveChangesAsync();
	}

}
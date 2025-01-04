using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Licenta_v1.Data;
using Licenta_v1.Models;
using Microsoft.EntityFrameworkCore;
using System.Net.Mail;
using System.Net;
using Microsoft.AspNetCore.Identity.UI.Services;

public class TaskuriAutomate : BackgroundService
{
	private readonly IServiceProvider _serviceProvider;
	private readonly IEmailSender _emailSender;

	public TaskuriAutomate(IServiceProvider serviceProvider, IEmailSender emailSender)
	{
		_serviceProvider = serviceProvider;
		_emailSender = emailSender;
	}

	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		var lastUserCheck = DateTime.MinValue;

		while (!stoppingToken.IsCancellationRequested)
		{
			// Iau timpul curent
			var currentTime = DateTime.Now;

			using (var scope = _serviceProvider.CreateScope())
			{
				var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

				// Task-ul de CheckAndDeleteUsers se ruleaza zilnic
				if ((currentTime - lastUserCheck).TotalDays >= 1)
				{
					await CheckAndDeleteUsers(db);
					lastUserCheck = currentTime; // Actualizez timpul la care s-a rulat comanda
				}

				// Celelalte doua metode se ruleaza la fiecare minut
				await CheckAndScheduleMaintenance(db);
				await UpdateVehicles(db);
			}

			// Astept un minut pana la urmatoarea iteratie a while-ului
			await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
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
		// Iau toate vehiculele si mentenantele lor
		var vehicles = await dbContext.Vehicles.Include(v => v.MaintenanceRecords).ToListAsync();

		var newMaintenances = new List<Maintenance>();

		foreach (var vehicle in vehicles)
		{
			// Iau mentenantele care trebuie programate pentru vehiculul curent
			var tasks = FleetManager.CheckAndScheduleMaintenance(vehicle);

			foreach (var task in tasks)
			{
				// Ma asigur ca nu exista deja o mentenanta programata pentru acelasi tip de mentenanta
				var existingTask = vehicle.MaintenanceRecords
					.Any(m => m.MaintenanceType == task.MaintenanceType && m.Status == "Scheduled");

				if (!existingTask)
				{
					dbContext.Maintenances.Add(task);
					newMaintenances.Add(task); // Tin cont de mentenantele noi pentru notificari
				}
			}
		}

		await dbContext.SaveChangesAsync();

		// Daca exista mentenante noi, trimit notificari
		if (newMaintenances.Count != 0)
		{
			await NotifyAdminsAndDispatchers(newMaintenances, dbContext);
		}
	}

	private async Task NotifyAdminsAndDispatchers(List<Maintenance> newMaintenances, ApplicationDbContext dbContext)
	{
		// Iau toti utilizatorii care sunt Admini sau Dispeceri
		var recipients = await dbContext.ApplicationUsers
			.Join(dbContext.UserRoles,
				  user => user.Id,
				  userRole => userRole.UserId,
				  (user, userRole) => new { user, userRole })
			.Join(dbContext.Roles,
				  combined => combined.userRole.RoleId,
				  role => role.Id,
				  (combined, role) => new { combined.user, role })
			.Where(result => result.role.Name == "Admin" || result.role.Name == "Dispecer")
			.Select(result => result.user.Email)
			.ToListAsync();

		if (recipients.Count == 0) return; // No recipients to notify

		// Continutul mail-ului basically
		var emailBody = "<div style='font-family: Arial, sans-serif; line-height: 1.6; max-width: 600px; margin: auto;'>" +
						"<div style='text-align: center; padding: 20px; background-color: #f4f4f4; border-bottom: 1px solid #ddd;'>" +
						"<h1 style='color: #333;'>Vehicle Maintenance Notification</h1>" +
						"</div><div style='padding: 20px; background-color: #ffffff;'>";

		emailBody += "<h2 style='color: #555;'>Scheduled Maintenance</h2>" +
					 "<p style='color: #666;'>The following vehicles have been scheduled for maintenance:</p>" +
					 "<ul style='color: #666;'>";

		foreach (var maintenance in newMaintenances)
		{
			emailBody += $"<li><strong>Vehicle:</strong> {maintenance.Vehicle.Brand} {maintenance.Vehicle.Model} " +
						 $"[{maintenance.Vehicle.RegistrationNumber}]<br>" +
						 $"<strong>Maintenance Type:</strong> {maintenance.MaintenanceType}<br>" +
						 $"<strong>Scheduled Date:</strong> {maintenance.ScheduledDate.ToShortDateString()}</li>";
		}

		emailBody += "</ul>" +
					 "<p style='color: #666;'>Please ensure these vehicles are available for maintenance on the scheduled dates.</p>" +
					 "</div><div style='text-align: center; padding: 10px; background-color: #f4f4f4; border-top: 1px solid #ddd;'>" +
					 "<p style='color: #888; font-size: 12px;'>EcoDelivery | All Rights Reserved</p>" +
					 "</div></div>";

		// Trimit mail-uri catre toti destinatarii
		foreach (var recipient in recipients)
		{
			await _emailSender.SendEmailAsync(
				recipient,
				"Scheduled Maintenance Notification",
				emailBody
			);
		}
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
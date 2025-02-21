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
using Licenta_v1.Services;

public class TaskuriAutomate : BackgroundService
{
	private readonly IServiceProvider _serviceProvider;
	private readonly IEmailSender _emailSender;
	private DateTime _lastDeliveryCleanup = DateTime.MinValue;

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

				DateTime todayAt18 = currentTime.Date.AddHours(18);
				// Rulez doar daca am trecut de ora 18:00 si inca nu s-a rulat deja codul pe ziua de astazi
				if (currentTime >= todayAt18 && _lastDeliveryCleanup < todayAt18)
				{
					await DeletePlannedDeliveries(db);
					_lastDeliveryCleanup = currentTime;
				}

				// Celelalte trei metode se ruleaza la fiecare minut
				await CheckAndScheduleMaintenance(db);
				await UpdateVehicles(db);
				await NotifyClientOfOrderStatus(db);
			}

			// Astept un minut pana la urmatoarea iteratie a while-ului
			await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
		}
	}

	private async Task DeletePlannedDeliveries(ApplicationDbContext dbContext)
	{
		DateTime today = DateTime.Today;

		// Selectez toate Deliveries programate pentru azi sau in trecut cu statusul "Planned" sau "Up for Taking"
		var deliveriesToDelete = await dbContext.Deliveries
			.Include(d => d.Vehicle)
			.Include(d => d.Driver)
			.Where(d => d.PlannedStartDate.Date <= today &&
						(d.Status == "Planned" || d.Status == "Up for Taking"))
			.ToListAsync();

		foreach (var delivery in deliveriesToDelete)
		{
			// Resetez statusul vehiculului sa-l facem Available pt reprogramarea comenzilor de maine
			if (delivery.Vehicle != null)
			{
				delivery.Vehicle.Status = VehicleStatus.Available;
			}

			// Resetez statusul soferului sa-l facem Available pt reprogramarea comenzilor de maine
			if (delivery.Driver != null)
			{
				delivery.Driver.IsAvailable = true;
			}

			var orders = dbContext.Orders.Where(o => o.DeliveryId == delivery.Id).ToList();
			foreach (var order in orders)
			{
				order.DeliveryId = null;
				order.DeliverySequence = null; // Sterg DeliverySequence-ul curent(e expirat)
				dbContext.Orders.Update(order);
			}

			// Sterg Delivery-ul din baza de date.
			dbContext.Deliveries.Remove(delivery);
		}

		await dbContext.SaveChangesAsync();
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

		if (recipients.Count == 0) return;

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

	private async Task NotifyClientOfOrderStatus(ApplicationDbContext db)
	{
		// Iau toate comenzile care au fost abia plasate(Status == Placed && LastNotifiedStatus is null)
		// sau care si-au schimbat statusul (Status != LastNotifiedStatus)
		var ordersToNotify = await db.Orders
			.Where(o => o.ClientId != null &&
						(o.LastNotifiedStatus == null || o.LastNotifiedStatus != o.Status))
			.Include(o => o.Client)
			.ToListAsync();

		foreach (var order in ordersToNotify)
		{
			if (order.Client == null || string.IsNullOrEmpty(order.Client.Email))
				continue; // Daca clientul nu gasesc mail-ul, sar peste notificare

			string subject = $"Order #{order.Id} - Status Update";
			string messageBody = GenerateOrderStatusEmail(order);

			await _emailSender.SendEmailAsync(order.Client.Email, subject, messageBody);

			// Actualizez LastNotifiedStatus ca sa nu spamez clientii cu mail-uri
			order.LastNotifiedStatus = order.Status;
		}

		await db.SaveChangesAsync();
	}

	private string GenerateOrderStatusEmail(Order order)
	{
		string statusMessage = order.Status switch
		{
			OrderStatus.Placed => "Your order has been successfully placed. We will notify you once it's out for delivery.",
			OrderStatus.InProgress => "Your order is on the way! Our driver is working hard to get it to you.",
			OrderStatus.Delivered => "Your order has been delivered successfully! We hope you enjoy your purchase.",
			_ => "There is an update on your order. Please check your account for details."
		};

		return $@"
        <div style='font-family: Arial, sans-serif; line-height: 1.6; max-width: 600px; margin: auto;'>
            <div style='text-align: center; padding: 20px; background-color: #f4f4f4; border-bottom: 1px solid #ddd;'>
                <img src='https://cdn.pixabay.com/photo/2024/04/01/14/43/ai-generated-8669101_1280.png' alt='EcoDelivery' style='max-width: 150px;'>
                <h2 style='color: #333;'>EcoDelivery - Order Update</h2>
            </div>
            <div style='padding: 20px; background-color: #ffffff;'>
                <h3 style='color: #555;'>Order #{order.Id} - {order.Status}</h3>
                <p style='color: #666;'>{statusMessage}</p>
                <p><strong>Address:</strong> {order.Address}</p>
                <p><strong>Estimated Delivery Date:</strong> {order.EstimatedDeliveryDate?.ToString("dd/MM/yyyy") ?? "N/A"}</p>
                <p>Thank you for choosing EcoDelivery!</p>
            </div>
            <div style='text-align: center; padding: 10px; background-color: #f4f4f4; border-top: 1px solid #ddd;'>
                <p style='color: #888; font-size: 12px;'>EcoDelivery | All Rights Reserved</p>
            </div>
        </div>";
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
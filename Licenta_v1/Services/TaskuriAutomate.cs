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
	private readonly OrderDeliveryOptimizer2 _optimizer;
	private DateTime _lastDeliveryCleanup = DateTime.MinValue;

	public TaskuriAutomate(IServiceProvider serviceProvider, IEmailSender emailSender, OrderDeliveryOptimizer2 optimizer)
	{
		_serviceProvider = serviceProvider;
		_emailSender = emailSender;
		_optimizer = optimizer;
	}

	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		// PRELOAD cache la startup
		await _optimizer.LoadRestrictionCacheAsync();

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
					await AnonymizeOldRouteHistories(db);
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
				order.EstimatedDeliveryDate = null;
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
		var ordersToNotify = await db.Orders
			.Where(o => o.ClientId != null)
			.Include(o => o.Client)
			.ToListAsync();

		foreach (var order in ordersToNotify)
		{
			if (order.Client == null || string.IsNullOrEmpty(order.Client.Email))
				continue;

			bool sendEmail = false;
			string subject = $"Order #{order.Id} - Status Update";
			string messageBody = "";

			// Comanda plasata in aplicatie
			if (order.Status == OrderStatus.Placed && order.LastNotifiedStatus == null)
			{
				messageBody = GenerateOrderPlacedEmail(order);
				sendEmail = true;
				order.LastNotifiedStatus = OrderStatus.Placed;
			}
			// Comanda asignata unei livrari
			else if (order.Status == OrderStatus.Placed &&
					 order.DeliveryId != null &&
					 order.EstimatedDeliveryDate != null &&
					 order.LastDeliveryAssignmentNotified == null)
			{
				messageBody = GenerateOrderAssignedToDeliveryEmail(order);
				sendEmail = true;
				order.LastDeliveryAssignmentNotified = DateTime.Now;
			}
			// Comanda in curs de livrare
			else if (order.Status == OrderStatus.InProgress && order.LastNotifiedStatus != OrderStatus.InProgress)
			{
				messageBody = GenerateOrderInProgressEmail(order);
				sendEmail = true;
				order.LastNotifiedStatus = OrderStatus.InProgress;
			}
			// Comanda livrata
			else if (order.Status == OrderStatus.Delivered && order.LastNotifiedStatus != OrderStatus.Delivered)
			{
				messageBody = GenerateOrderDeliveredEmail(order);
				sendEmail = true;
				order.LastNotifiedStatus = OrderStatus.Delivered;
			}

			if (sendEmail)
			{
				await _emailSender.SendEmailAsync(order.Client.Email, subject, messageBody);
			}
		}

		await db.SaveChangesAsync();
	}

	private string GenerateOrderPlacedEmail(Order order) =>
		$@"
		<div style='font-family: Arial, sans-serif; line-height: 1.6; max-width: 600px; margin: auto;'>
			<div style='text-align: center; padding: 20px; background-color: #f4f4f4; border-bottom: 1px solid #ddd;'>
				<img src='https://cdn.pixabay.com/photo/2024/04/01/14/43/ai-generated-8669101_1280.png' alt='EcoDelivery' style='max-width: 150px;'>
				<h2 style='color: #333;'>EcoDelivery - Order Received</h2>
			</div>
			<div style='padding: 20px; background-color: #ffffff;'>
				<h3 style='color: #555;'>Order #{order.Id} - Placed</h3>
				<p style='color: #666;'>Thank you for your order! Our team has received it and is currently reviewing the details.</p>
				<p style='color: #666;'>You will receive a new update once your order is scheduled for delivery.</p>
				<p><strong>Delivery Address:</strong> {order.Address}</p>
				<p><strong>Estimated Delivery Date:</strong> To be scheduled</p>
				<p>Thank you for choosing EcoDelivery!</p>
			</div>
			<div style='text-align: center; padding: 10px; background-color: #f4f4f4; border-top: 1px solid #ddd;'>
				<p style='color: #888; font-size: 12px;'>EcoDelivery | All Rights Reserved</p>
			</div>
		</div>";

	private string GenerateOrderAssignedToDeliveryEmail(Order order) =>
		$@"
		<div style='font-family: Arial, sans-serif; line-height: 1.6; max-width: 600px; margin: auto;'>
			<div style='text-align: center; padding: 20px; background-color: #f4f4f4; border-bottom: 1px solid #ddd;'>
				<img src='https://cdn.pixabay.com/photo/2024/04/01/14/43/ai-generated-8669101_1280.png' alt='EcoDelivery' style='max-width: 150px;'>
				<h2 style='color: #333;'>EcoDelivery - Delivery Scheduled</h2>
			</div>
			<div style='padding: 20px; background-color: #ffffff;'>
				<h3 style='color: #555;'>Order #{order.Id} - Scheduled</h3>
				<p style='color: #666;'>Your order has been successfully scheduled for delivery. Our team is preparing your items for dispatch.</p>
				<p style='color: #666;'>We will notify you again once your delivery is on the way.</p>
				<p><strong>Delivery Address:</strong> {order.Address}</p>
				<p><strong>Estimated Delivery Date:</strong> {order.EstimatedDeliveryDate:dd/MM/yyyy}</p>
				<p><strong>Estimated Time Window:</strong> {order.EstimatedDeliveryInterval ?? "To be determined"}</p>
				<p>Thank you for choosing EcoDelivery!</p>
			</div>
			<div style='text-align: center; padding: 10px; background-color: #f4f4f4; border-top: 1px solid #ddd;'>
				<p style='color: #888; font-size: 12px;'>EcoDelivery | All Rights Reserved</p>
			</div>
		</div>";

	private string GenerateOrderInProgressEmail(Order order) =>
		$@"
		<div style='font-family: Arial, sans-serif; line-height: 1.6; max-width: 600px; margin: auto;'>
			<div style='text-align: center; padding: 20px; background-color: #f4f4f4; border-bottom: 1px solid #ddd;'>
				<img src='https://cdn.pixabay.com/photo/2024/04/01/14/43/ai-generated-8669101_1280.png' alt='EcoDelivery' style='max-width: 150px;'>
				<h2 style='color: #333;'>EcoDelivery - On the Way</h2>
			</div>
			<div style='padding: 20px; background-color: #ffffff;'>
				<h3 style='color: #555;'>Order #{order.Id} - Out for Delivery</h3>
				<p style='color: #666;'>Your order is now on its way to you. One of our drivers is en route and will arrive soon.</p>
				<p><strong>Delivery Date:</strong> {order.EstimatedDeliveryDate:dd/MM/yyyy}</p>
				<p><strong>Estimated Time Window:</strong> {order.EstimatedDeliveryInterval ?? "N/A"}</p>
				<p><strong>Delivery Address:</strong> {order.Address}</p>
				<p>Please make sure someone is available to receive the package.</p>
			</div>
			<div style='text-align: center; padding: 10px; background-color: #f4f4f4; border-top: 1px solid #ddd;'>
				<p style='color: #888; font-size: 12px;'>EcoDelivery | All Rights Reserved</p>
			</div>
		</div>";

	private string GenerateOrderDeliveredEmail(Order order) =>
		$@"
		<div style='font-family: Arial, sans-serif; line-height: 1.6; max-width: 600px; margin: auto;'>
			<div style='text-align: center; padding: 20px; background-color: #f4f4f4; border-bottom: 1px solid #ddd;'>
				<img src='https://cdn.pixabay.com/photo/2024/04/01/14/43/ai-generated-8669101_1280.png' alt='EcoDelivery' style='max-width: 150px;'>
				<h2 style='color: #333;'>EcoDelivery - Order Delivered</h2>
			</div>
			<div style='padding: 20px; background-color: #ffffff;'>
				<h3 style='color: #555;'>Order #{order.Id} - Delivered</h3>
				<p style='color: #666;'>We’re happy to let you know that your order has been delivered successfully.</p>
				<p><strong>Delivery Address:</strong> {order.Address}</p>
				<p>If anything is wrong or missing, please <a href='#'>contact support</a> right away.</p>
				<p>We appreciate your trust in EcoDelivery!</p>
			</div>
			<div style='text-align: center; padding: 10px; background-color: #f4f4f4; border-top: 1px solid #ddd;'>
				<p style='color: #888; font-size: 12px;'>EcoDelivery | All Rights Reserved</p>
			</div>
		</div>";

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

	private async Task AnonymizeOldRouteHistories(ApplicationDbContext dbContext)
	{
		var cutoffDate = DateTime.Now.AddMonths(-6);

		// Selectez rutele logate cu mai mult de 6 luni in urma si care inca au date despre sofer
		var oldHistories = await dbContext.RouteHistories
			.Where(r => r.DateLogged < cutoffDate && r.DriverId != null)
			.ToListAsync();

		foreach (var history in oldHistories)
		{
			history.DriverId = null;
			history.DriverName = null;
			dbContext.RouteHistories.Update(history);
		}

		if (oldHistories.Any())
		{
			await dbContext.SaveChangesAsync();
		}
	}
}
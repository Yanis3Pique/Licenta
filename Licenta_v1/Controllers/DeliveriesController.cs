﻿using System.Diagnostics;
using System.Linq;
using Google.OrTools.ConstraintSolver;
using Licenta_v1.Data;
using Licenta_v1.Models;
using Licenta_v1.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using SendGrid.Helpers.Mail;

namespace Licenta_v1.Controllers
{
	public class DeliveriesController : Controller
	{
		private readonly ApplicationDbContext db;
		private readonly OrderDeliveryOptimizer2 opt;
		private readonly RoutePlannerService rps;

		public DeliveriesController(ApplicationDbContext context, OrderDeliveryOptimizer2 optimizer, RoutePlannerService routePlannerService)
		{
			db = context;
			opt = optimizer;
			rps = routePlannerService;
		}

		[Authorize(Roles = "Dispecer")]
		public IActionResult Create()
		{
			// Iau Dispecerul(userul curent)
			var user = db.ApplicationUsers.FirstOrDefault(u => u.UserName == User.Identity.Name);
			if (user == null)
				return Unauthorized();

			// Iau Comenzile disponibile in regiunea Dispecerului (comenzi neasignate)
			var availableOrders = db.Orders
				.Where(o => o.Status == OrderStatus.Placed &&
							o.DeliveryId == null &&
							o.RegionId == user.RegionId)
				.ToList();

			// Iau Soferii disponibili in regiunea Dispecerului
			var availableDrivers = (from u in db.ApplicationUsers
									join ur in db.UserRoles on u.Id equals ur.UserId
									join r in db.Roles on ur.RoleId equals r.Id
									where r.Name == "Sofer" &&
										  u.IsAvailable == true &&
										  u.RegionId == user.RegionId &&
										  !u.IsDeleted &&
										  (u.DismissalNoticeDate == null || u.DismissalNoticeDate > DateTime.Now.AddDays(7))
									select u).ToList();

			// Iau Vehiculele disponibile in regiunea Dispecerului
			var availableVehicles = db.Vehicles
				.Where(v => v.Status == VehicleStatus.Available && v.RegionId == user.RegionId)
				.ToList();

			var allRestrictions = db.OrderVehicleRestrictions
				.Where(r => availableOrders.Select(o => o.Id).Contains(r.OrderId))
				.ToList();

			var restrictedMap = allRestrictions
				.Where(r => r.Source == "Manual" || !r.IsAccessible)
				.GroupBy(r => r.VehicleId)
				.ToDictionary(
				   g => g.Key,
				   g => g.Select(r => r.OrderId).Distinct().ToList()
				);

			ViewBag.RestrictedByVehicleJson = JsonConvert.SerializeObject(restrictedMap);
			ViewBag.AvailableOrders = availableOrders;
			ViewBag.AvailableDrivers = availableDrivers;
			ViewBag.AvailableVehicles = availableVehicles;

			return View();
		}

		[HttpPost]
		[Authorize(Roles = "Dispecer")]
		public async Task<IActionResult> CreateDelivery(string driverId, int vehicleId, int[] selectedOrderIds)
		{
			var user = await db.ApplicationUsers.FirstOrDefaultAsync(u => u.UserName == User.Identity.Name);
			if (user == null)
				return Unauthorized();

			// Iau Comenzile selectate de user
			var orders = db.Orders.Where(o => selectedOrderIds.Contains(o.Id) && o.DeliveryId == null).ToList();
			if (!orders.Any())
			{
				TempData["Error"] = "Please select at least one order.";
				return RedirectToAction("Create");
			}

			// Verific daca toate comenzile sunt nelivrabile
			var undeliverableOrders = await db.OrderVehicleRestrictions
				.Where(r => selectedOrderIds.Contains(r.OrderId) &&
							(r.Source == "Manual" || !r.IsAccessible))
				.Select(r => r.OrderId)
				.ToListAsync();
			if (undeliverableOrders.Count == orders.Count)
			{
				TempData["Error"] = "Cannot create a delivery because all selected orders are undeliverable.";
				return RedirectToAction("Create");
			}

			// Iau Vehiculul selectat de user
			var vehicle = db.Vehicles.FirstOrDefault(v => v.Id == vehicleId && v.Status == VehicleStatus.Available);
			if (vehicle == null)
			{
				TempData["Error"] = "The selected vehicle is not available.";
				return RedirectToAction("Create");
			}

			// Iau Soferul selectat de user
			var driver = !string.IsNullOrEmpty(driverId)
			 ? db.ApplicationUsers.FirstOrDefault(u => u.Id == driverId &&
													   u.IsAvailable == true &&
													   !u.IsDeleted &&
													   (u.DismissalNoticeDate == null || u.DismissalNoticeDate > DateTime.Now.AddDays(7)))
			 : null;

			// Verific daca comenzile selectate se incadreaza in capacitatea vehiculului
			double usedWeight = orders.Sum(o => o.Weight ?? 0);
			double usedVolume = orders.Sum(o => o.Volume ?? 0);
			if (usedWeight > vehicle.MaxWeightCapacity || usedVolume > vehicle.MaxVolumeCapacity)
			{
				TempData["Error"] = "The selected orders exceed the vehicle's capacity.";
				return RedirectToAction("Create");
			}

			// Verific daca vehiculul este restrictionat pentru una dintre comenzi
			foreach (var order in orders)
			{
				bool isRestricted = db.OrderVehicleRestrictions
					.Any(r => r.OrderId == order.Id && r.VehicleId == vehicle.Id &&
							  (r.Source == "Manual" || !r.IsAccessible));
				if (isRestricted)
				{
					TempData["Error"] = $"Order #{order.Id} is not accessible by the selected vehicle.";
					return RedirectToAction("Create");
				}
			}

			// Fac un nou Delivery
			var delivery = new Delivery
			{
				VehicleId = vehicle.Id,
				DriverId = driver?.Id,
				Status = driver != null ? "Planned" : "Up for Taking",
				PlannedStartDate = DateTime.Today.AddDays(1),
				Orders = new List<Order>()
			};

			// Pun fiecare Order in Delivery-ul nou creat
			foreach (var order in orders)
			{
				order.DeliveryId = delivery.Id;
				order.EstimatedDeliveryDate = delivery.PlannedStartDate;
				delivery.Orders.Add(order);
				db.Orders.Update(order);
			}

			// Actualizez statusurile: marchez vehiculul ca fiind ocupat si soferul ca fiind indisponibil
			vehicle.Status = VehicleStatus.Busy;
			db.Deliveries.Add(delivery);
			db.Vehicles.Update(vehicle);
			await db.SaveChangesAsync();

			var fullDelivery = await db.Deliveries
				.Include(d => d.Vehicle)
				   .ThenInclude(v => v.Region)
					   .ThenInclude(r => r.Headquarters)
				.Include(d => d.Orders)
				.FirstOrDefaultAsync(d => d.Id == delivery.Id);

			var previewRoute = await rps.CalculateOptimalRouteAsync(fullDelivery);
			if (previewRoute.FailedOrderIds.Count == fullDelivery.Orders.Count)
			{
				vehicle.Status = VehicleStatus.Available;
				db.Vehicles.Update(vehicle);
				if (driver != null)
				{
					driver.IsAvailable = true;
					db.ApplicationUsers.Update(driver);
				}
				foreach (var o in fullDelivery.Orders)
				{
					o.DeliveryId = null;
					o.DeliverySequence = null;
					o.Status = OrderStatus.Placed;
					db.Orders.Update(o);
				}
				db.Deliveries.Remove(fullDelivery);
				await db.SaveChangesAsync();

				TempData["Error"] = "No delivery created - none of the selected orders could be reached.";
				return RedirectToAction("Create");
			}

			await opt.RecalculateDeliveryMetrics(db, fullDelivery);
			await db.SaveChangesAsync();

			// Creez RouteHistory record
			var routeHistory = new RouteHistory
			{
				DeliveryId = delivery.Id,
				DateLogged = DateTime.Now,
				DistanceTraveled = delivery.DistanceEstimated ?? 0,
				FuelConsumed = delivery.ConsumptionEstimated ?? 0,
				Emissions = delivery.EmissionsEstimated ?? 0,
				TimeTaken = (int)((delivery.TimeTakenForDelivery ?? 0) * 3600),
				RouteData = delivery.RouteData,
				VehicleId = delivery.Vehicle.Id,
				VehicleDescription = $"{delivery.Vehicle.Brand} {delivery.Vehicle.Model} ({delivery.Vehicle.RegistrationNumber})",
				OrderIdsJson = JsonConvert.SerializeObject(delivery.Orders.Select(o => o.Id)),
				DriverId = delivery.DriverId, // asta poate sa fie null aici
				DriverName = driver?.UserName
			};
			db.RouteHistories.Add(routeHistory);
			await db.SaveChangesAsync();


			TempData["Success"] = "Delivery created successfully!";
			return RedirectToAction("Show", new { id = delivery.Id });
		}

		[Authorize(Roles = "Admin,Dispecer")]
		public async Task<IActionResult> Index(
			string searchString,
			DateTime? plannedStartDate,
			DateTime? actualEndDate,
			string regionId,
			string status,
			int pageNumber = 1)
		{
			int pageSize = 10;

			var user = await db.ApplicationUsers.FirstOrDefaultAsync(u => u.UserName == User.Identity.Name);
			if (user == null)
			{
				return Unauthorized();
			}

			var deliveriesQuery = db.Deliveries
				.Include(d => d.Driver)
				.Include(d => d.Vehicle)
					.ThenInclude(v => v.Region)
				.Include(d => d.Orders)
				.AsQueryable();

			// Dispecer - doar regiunea lui
			if (User.IsInRole("Dispecer"))
			{
				deliveriesQuery = deliveriesQuery.Where(d => d.Vehicle.RegionId == user.RegionId);
			}
			// Admin - tot
			else if (User.IsInRole("Admin") && int.TryParse(regionId, out int parsedRegionId))
			{
				deliveriesQuery = deliveriesQuery.Where(d => d.Vehicle.RegionId == parsedRegionId);
			}

			// Functionalitatea de Search
			if (!string.IsNullOrEmpty(searchString))
			{
				string lowerSearch = searchString.ToLower();
				deliveriesQuery = deliveriesQuery.Where(d =>
					(d.Driver != null && d.Driver.UserName != null && d.Driver.UserName.ToLower().Contains(lowerSearch)) ||
					(d.Vehicle != null &&
					 (
						((d.Vehicle.Brand ?? "") + " " + (d.Vehicle.Model ?? "")).ToLower().Contains(lowerSearch) ||
						((d.Vehicle.Brand ?? "").ToLower().Contains(lowerSearch)) ||
						((d.Vehicle.Model ?? "").ToLower().Contains(lowerSearch)) ||
						(d.Vehicle.RegistrationNumber != null && d.Vehicle.RegistrationNumber.ToLower().Contains(lowerSearch))
					 ))
				);
			}

			// Filtrare dupa PlannedStartDate
			if (plannedStartDate.HasValue)
			{
				deliveriesQuery = deliveriesQuery.Where(d => d.PlannedStartDate.Date == plannedStartDate.Value.Date);
			}

			// Filtrare dupa ActualEndDate
			if (actualEndDate.HasValue)
			{
				deliveriesQuery = deliveriesQuery.Where(d => d.ActualEndDate.HasValue && d.ActualEndDate.Value.Date == actualEndDate.Value.Date);
			}

			// Filtrare dupa Status (if not "All")
			if (!string.IsNullOrEmpty(status) && status != "All")
			{
				string lowerStatus = status.ToLower();
				deliveriesQuery = deliveriesQuery.Where(d => d.Status.ToLower() == lowerStatus);
			}

			int totalDeliveries = await deliveriesQuery.CountAsync();

			var deliveries = await deliveriesQuery
				.OrderByDescending(d => d.PlannedStartDate)
				.Skip((pageNumber - 1) * pageSize)
				.Take(pageSize)
				.ToListAsync();

			ViewBag.PageNumber = pageNumber;
			ViewBag.TotalPages = (int)Math.Ceiling(totalDeliveries / (double)pageSize);

			ViewBag.SearchString = searchString;
			ViewBag.PlannedStartDate = plannedStartDate?.ToString("yyyy-MM-dd");
			ViewBag.ActualEndDate = actualEndDate?.ToString("yyyy-MM-dd");
			ViewBag.RegionId = regionId;
			ViewBag.Status = status;
			ViewBag.Regions = new SelectList(db.Regions, "Id", "County");

			ViewBag.IsOptimizationRunning = await IsAnyOptimizationInProgress();

			return View(deliveries);
		}

		[Authorize(Roles = "Admin,Dispecer,Sofer")]
		public async Task<IActionResult> ShowAsync(int id)
		{
			var user = db.ApplicationUsers.FirstOrDefault(u => u.UserName == User.Identity.Name);

			if (user == null)
			{
				return Unauthorized(); // Trebuie sa fie un user autentificat
			}

			var delivery = db.Deliveries       // Adminii vad detaliile tuturor deliveries. Soferii vad doar Deliveries asignate lor.
				.Include(d => d.Vehicle)       // Dispecerii vad doar detaliile deliveries din regiunea lor.
				.ThenInclude(v => v.Region)    // Ca sa avem acces si la regiune prin Vehicle.
				.ThenInclude(r => r.Headquarters)
				.Include(d => d.Driver)
				.Include(d => d.Orders)
				.ThenInclude(o => o.Client)
				.FirstOrDefault(d => d.Id == id &&
					(User.IsInRole("Admin") ||
					(User.IsInRole("Dispecer") && d.Vehicle.RegionId == user.RegionId) ||
					(User.IsInRole("Sofer") && (d.DriverId == null || d.DriverId == user.Id))));

			if (delivery == null)
				return NotFound();

			ViewBag.IsOptimizationRunning = await IsAnyOptimizationInProgress();
			ViewBag.CurrentUserId = user.Id;
			ViewBag.VehicleTotalKMBefore = delivery.Vehicle.TotalDistanceTraveledKM;

			return View(delivery);
		}

		[Authorize(Roles = "Sofer,Dispecer")]
		public async Task<IActionResult> GetOptimalRoute(int deliveryId)
		{
			var user = await db.ApplicationUsers
							   .FirstOrDefaultAsync(u => u.UserName == User.Identity.Name);
			if (user == null) return Unauthorized();

			var deliveryQuery = db.Deliveries
				.Include(d => d.Orders)
				.Include(d => d.Vehicle)
					.ThenInclude(v => v.Region)
						.ThenInclude(r => r.Headquarters)
				.Where(d => d.Id == deliveryId);

			if (User.IsInRole("Sofer"))
				deliveryQuery = deliveryQuery.Where(d => d.DriverId == user.Id);
			else if (User.IsInRole("Dispecer"))
				deliveryQuery = deliveryQuery.Where(d => d.Vehicle.RegionId == user.RegionId);

			var delivery = await deliveryQuery.FirstOrDefaultAsync();
			if (delivery == null) return NotFound();

			try
			{
				// Cer ruta de la ORS
				var route = await rps.CalculateOptimalRouteAsync(delivery);

				// Daca au fost comenzi care nu pots fi livrate, le marchez ca FailedDelivery
				if (route.FailedOrderIds?.Count > 0)
				{
					foreach (var oid in route.FailedOrderIds)
					{
						var o = await db.Orders.FindAsync(oid);
						if (o != null && o.Status != OrderStatus.FailedDelivery)
						{
							o.Status = OrderStatus.FailedDelivery;
							db.Orders.Update(o);
						}
					}
					await db.SaveChangesAsync();
				}

				var stopLocations = new List<(double Latitude, double Longitude)>
				{
					(delivery.Vehicle.Region.Headquarters.Latitude ?? 0,
					 delivery.Vehicle.Region.Headquarters.Longitude ?? 0)
				};
				stopLocations.AddRange(delivery.Orders.Select(o => (o.Latitude ?? 0, o.Longitude ?? 0)));
				stopLocations.Add((delivery.Vehicle.Region.Headquarters.Latitude ?? 0,
								   delivery.Vehicle.Region.Headquarters.Longitude ?? 0));

				var stopIndices = new List<int>();
				for (int i = 0; i < stopLocations.Count; i++)
				{
					var (lat, lon) = stopLocations[i];
					int best = -1; double bestDist = double.MaxValue;
					for (int j = 0; j < route.Coordinates.Count; j++)
					{
						var c = route.Coordinates[j];
						var d = HaversineDistance(lat, lon, c.Latitude, c.Longitude);
						if (d < bestDist) { bestDist = d; best = j; }
					}
					if (best >= 0 && !stopIndices.Contains(best))
						stopIndices.Add(best);
				}
				if (!stopIndices.Contains(route.Coordinates.Count - 1))
					stopIndices.Add(route.Coordinates.Count - 1);
				stopIndices.Sort();

				// Returnez un JSON cu coordonatele, segmentele si ordinea comenzilor
				return Json(new
				{
					coordinates = route.Coordinates,
					stopIndices = stopIndices,
					segments = route.Segments,
					orderIds = route.OrderIds,
					failedOrderIds = route.FailedOrderIds,
					coloredRouteSegments = route.ColoredRouteSegments,
					AvoidPolygons = route.AvoidPolygons,
					avoidDescriptions = route.AvoidDescriptions,
					rawCoordinates = route.RawCoordinates
				});
			}
			catch (Exception ex)
			{
				return BadRequest(new { error = ex.Message });
			}
		}

		[Authorize(Roles = "Dispecer")]
		public IActionResult Edit(int id)
		{
			// Iau Dispecerul(userul curent)
			var user = db.ApplicationUsers.FirstOrDefault(u => u.UserName == User.Identity.Name);
			if (user == null)
				return Unauthorized();

			// Incarc Delivery-ul daca exista si e in aceeasi regiune cu Dispecerul
			var delivery = db.Deliveries
				.Include(d => d.Vehicle)
				.ThenInclude(v => v.Region)
						.ThenInclude(r => r.Headquarters)
				.Include(d => d.Driver)
				.Include(d => d.Orders.Where(o => o.RegionId == user.RegionId))
					.ThenInclude(o => o.Client)
				.FirstOrDefault(d => d.Id == id && d.Vehicle.RegionId == user.RegionId);

			if (delivery == null)
				return NotFound();

			// Daca Delivery-ul are alt status decat Planned, nu am voie sa o editez
			if (delivery.Status != "Planned" && delivery.Status != "Up for Taking")
			{
				TempData["Error"] = "You cannot edit this Delivery.";
				return RedirectToAction("Show", new { id });
			}

			// Fac o lista de Orders plasate si neasignate niciunui Delivery
			var deliveryVehicleId = delivery.Vehicle.Id;

			ViewBag.AvailableOrders = db.Orders
				.Where(o => o.Status == OrderStatus.Placed && o.DeliveryId == null && o.RegionId == user.RegionId)
				.Where(o => !db.OrderVehicleRestrictions.Any(r => r.OrderId == o.Id && r.VehicleId == deliveryVehicleId))
				.ToList();

			return View(delivery);
		}

		[HttpPost]
		[Authorize(Roles = "Dispecer")]
		public async Task<IActionResult> EditDelivery(int id, int[] keepOrderIds, int[] addOrderIds)
		{
			var delivery = db.Deliveries
				.Include(d => d.Vehicle)
					.ThenInclude(v => v.Region)
						.ThenInclude(r => r.Headquarters)
				.Include(d => d.Driver)
				.Include(d => d.Orders)
				.FirstOrDefault(d => d.Id == id);

			if (delivery == null)
				return NotFound();

			var vehicle = delivery.Vehicle;

			// Iau comenzile care sunt in lista de Orders curente si nu sunt in lista de Orders de pastrat
			var newOrders = db.Orders.Where(o => addOrderIds.Contains(o.Id) && o.DeliveryId == null).ToList();
			var keptOrders = delivery.Orders.Where(o => keepOrderIds.Contains(o.Id)).ToList();

			var allFinalOrders = keptOrders.Concat(newOrders).ToList();

			// Verific daca comenzile selectate se incadreaza in capacitatea masinii
			if (!CanFitOrders(vehicle, allFinalOrders))
			{
				TempData["Error"] = "The selected orders exceed the vehicle's capacity.";
				return RedirectToAction("Edit", new { id });
			}

			// Verific daca vehiculul este restrictionat pentru vreuna din comenzi
			foreach (var order in newOrders)
			{
				bool isRestricted = db.OrderVehicleRestrictions
					.Any(r => r.OrderId == order.Id && r.VehicleId == vehicle.Id &&
							  (r.Source == "Manual" || !r.IsAccessible));
				if (isRestricted)
				{
					TempData["Error"] = $"Order #{order.Id} is not accessible by the selected vehicle.";
					return RedirectToAction("Edit", new { id });
				}
			}

			// Sterg comenzile care nu sunt in lista
			var ordersToRemove = delivery.Orders.Where(o => !keepOrderIds.Contains(o.Id)).ToList();
			foreach (var order in ordersToRemove)
			{
				order.DeliveryId = null;
				order.DeliverySequence = null;
				order.Status = OrderStatus.Placed;
				db.Orders.Update(order);
			}
			foreach (var order in ordersToRemove)
			{
				delivery.Orders.Remove(order);
			}

			// Adaug comenzile noi
			foreach (var order in newOrders)
			{
				order.DeliveryId = delivery.Id;
				order.EstimatedDeliveryDate = delivery.PlannedStartDate;
				delivery.Orders.Add(order);
				db.Orders.Update(order);
			}

			// Daca nu mai sunt comenzi in Delivery, il sterg
			if (!delivery.Orders.Any())
			{
				vehicle.Status = VehicleStatus.Available;
				if (delivery.Driver != null)
				{
					delivery.Driver.IsAvailable = true;
					db.ApplicationUsers.Update(delivery.Driver);
				}

				db.Vehicles.Update(vehicle);
				db.Deliveries.Remove(delivery);
				await db.SaveChangesAsync();

				TempData["Success"] = "Delivery removed successfully!";
				return RedirectToAction("Index");
			}

			// Recalculez estimarile si DeliverySequence-ul
			await opt.RecalculateDeliveryMetrics(db, delivery);
			db.Deliveries.Update(delivery);
			await db.SaveChangesAsync();

			var routeHistory = db.RouteHistories.FirstOrDefault(r => r.DeliveryId == delivery.Id);
			if (routeHistory != null)
			{
				routeHistory.DateLogged = DateTime.Now;
				routeHistory.DistanceTraveled = delivery.DistanceEstimated ?? 0;
				routeHistory.FuelConsumed = delivery.ConsumptionEstimated ?? 0;
				routeHistory.Emissions = delivery.EmissionsEstimated ?? 0;
				routeHistory.TimeTaken = (int)((delivery.TimeTakenForDelivery ?? 0) * 3600);
				routeHistory.RouteData = delivery.RouteData;
				routeHistory.OrderIdsJson = JsonConvert.SerializeObject(delivery.Orders.Select(o => o.Id));

				db.RouteHistories.Update(routeHistory);
				await db.SaveChangesAsync();
			}

			TempData["Success"] = "Delivery updated successfully!";
			return RedirectToAction("Show", new { id });
		}

		[Authorize(Roles = "Admin")]
		public async Task<IActionResult> OptimizeAll()
		{
			//DateTime now = DateTime.Now;
			//DateTime allowedStart = now.Date.AddHours(18); // Azi ora 18:00
			//DateTime allowedEnd = now.Date.AddHours(22);   // Azi ora 22:00

			//if (now < allowedStart || now > allowedEnd)
			//{
			//	TempData["Error"] = "Optimizing Deliveries can be done only once a day, between 18:00 and 22:00.";
			//	return RedirectToAction("Index");
			//}

			var user = db.ApplicationUsers.FirstOrDefault(u => u.UserName == User.Identity.Name);

			try
			{
				await opt.RunDailyOptimization(user.Id); // Adminii optimizeaza toate regiunile
				TempData["Success"] = "Global optimization completed successfully!";
			}
			catch (InvalidOperationException ex)
			{
				TempData["Error"] = ex.Message;
			}
			catch (Exception ex)
			{
				Debug.WriteLine(ex, "Unexpected error during global optimization.");
				TempData["Error"] = "Unexpected error. Please try again later.";
			}

			return RedirectToAction("Index");
		}

		[Authorize(Roles = "Dispecer")]
		public async Task<IActionResult> OptimizeRegion()
		{
			//DateTime now = DateTime.Now;
			//DateTime allowedStart = now.Date.AddHours(18); // Azi ora 18:00
			//DateTime allowedEnd = now.Date.AddHours(22);   // Azi ora 22:00

			//if (now < allowedStart || now > allowedEnd)
			//{
			//	TempData["Error"] = "Optimizing Deliveries can be done only once a day, between 18:00 and 22:00.";
			//	return RedirectToAction("Index");
			//}

			var user = db.ApplicationUsers.FirstOrDefault(u => u.UserName == User.Identity.Name);

			if (user?.RegionId == null)
			{
				TempData["Error"] = "You don't have a region assigned to you!";
				return RedirectToAction("Index");
			}

			try
			{
				await opt.RunDailyOptimization(user.Id, user.RegionId);
				TempData["Success"] = "Optimization completed successfully!";
			}
			catch (InvalidOperationException ex)
			{
				TempData["Error"] = ex.Message;
			}
			catch (Exception ex)
			{
				Debug.WriteLine(ex, "Unexpected error during optimization.");
				TempData["Error"] = "Unexpected error. Please try again later.";
			}

			return RedirectToAction("Index");
		}

		[HttpPost]
		[Authorize(Roles = "Admin")]
		public async Task<IActionResult> CleanupDeliveries()
		{
			DateTime today = DateTime.Today;

			// Selectez toate Deliveries programate oricand cu statusul "Planned" sau "Up for Taking"
			var deliveriesToDelete = await db.Deliveries
				.Include(d => d.Vehicle)
				.Include(d => d.Driver)
				.Where(d => (d.Status == "Planned" || d.Status == "Up for Taking"))
				.ToListAsync();

			foreach (var delivery in deliveriesToDelete)
			{
				// Resetez statusul Vehiculului sa-l facem Available pentru reprogramarea comenzilor
				if (delivery.Vehicle != null)
				{
					delivery.Vehicle.Status = VehicleStatus.Available;
				}

				// Resetez statusul Soferului pt reprogramarea comenzilor
				if (delivery.Driver != null)
				{
					delivery.Driver.IsAvailable = true;
				}

				// Resetez DeliveryId-ul si DeliverySequence-ul pt Orders asociate
				var orders = db.Orders.Where(o => o.DeliveryId == delivery.Id).ToList();
				foreach (var order in orders)
				{
					order.DeliveryId = null;
					order.DeliverySequence = null;
					order.EstimatedDeliveryInterval = null;
					order.EstimatedDeliveryDate = null;
					order.Status = OrderStatus.Placed;
					db.Orders.Update(order);
				}

				db.Deliveries.Remove(delivery);
			}

			await db.SaveChangesAsync();
			TempData["Success"] = "Cleanup completed successfully!";
			return RedirectToAction("Index");
		}

		[Authorize(Roles = "Admin,Dispecer,Sofer")]
		public async Task<IActionResult> ShowDeliveriesOfDriver(
			string id,
			string searchString,
			DateTime? deliveryDate,
			string statusFilter,
			string sortOrder,
			int pageNumber = 1,
			int pageSize = 6)
		{
			if (string.IsNullOrEmpty(id))
			{
				return NotFound("Driver ID is required.");
			}

			var driver = await db.Users.FirstOrDefaultAsync(u => u.Id == id);
			if (driver == null)
			{
				return NotFound("Driver not found.");
			}

			var deliveriesQuery = db.Deliveries
				.Include(d => d.Vehicle)
				.Include(d => d.Orders)
				.Where(d => d.DriverId == id || d.DriverId == null &&
							d.Vehicle.RegionId == driver.RegionId)
				.AsQueryable();

			// Filtrare dupa numar de inmatriculare sau Brand + Model
			if (!string.IsNullOrEmpty(searchString))
			{
				string lowerSearch = searchString.ToLower();
				deliveriesQuery = deliveriesQuery.Where(d =>
					(d.Vehicle.Brand + " " + d.Vehicle.Model).ToLower().Contains(lowerSearch) ||
					d.Vehicle.RegistrationNumber.ToLower().Contains(lowerSearch));
			}

			// Filtrare dupa data, fara a lua in considerare ora
			if (deliveryDate.HasValue)
			{
				deliveriesQuery = deliveriesQuery.Where(d =>
					d.PlannedStartDate.Date == deliveryDate.Value.Date);
			}

			// Filtrare dupa status
			if (!string.IsNullOrEmpty(statusFilter) && statusFilter != "All")
			{
				string lowerStatusFilter = statusFilter.ToLower();
				deliveriesQuery = deliveriesQuery.Where(d => d.Status.ToLower() == lowerStatusFilter);
			}

			// Sortarea dupa data sau status
			switch (sortOrder)
			{
				case "date_asc":
					deliveriesQuery = deliveriesQuery.OrderBy(d => d.PlannedStartDate);
					break;
				case "date_desc":
					deliveriesQuery = deliveriesQuery.OrderByDescending(d => d.PlannedStartDate);
					break;
				case "status_asc":
					deliveriesQuery = deliveriesQuery.OrderBy(d => d.Status);
					break;
				case "status_desc":
					deliveriesQuery = deliveriesQuery.OrderByDescending(d => d.Status);
					break;
				default:
					deliveriesQuery = deliveriesQuery.OrderByDescending(d => d.PlannedStartDate);
					break;
			}

			// Paginarea
			int totalDeliveries = await deliveriesQuery.CountAsync();
			var deliveries = await deliveriesQuery
				.Skip((pageNumber - 1) * pageSize)
				.Take(pageSize)
				.ToListAsync();

			ViewBag.Driver = driver;
			ViewBag.SearchString = searchString;
			ViewBag.DeliveryDate = deliveryDate?.ToString("yyyy-MM-dd");
			ViewBag.StatusFilter = statusFilter;
			ViewBag.SortOrder = sortOrder;
			ViewBag.PageNumber = pageNumber;
			ViewBag.PageSize = pageSize;
			ViewBag.TotalPages = (int)Math.Ceiling(totalDeliveries / (double)pageSize);

			return View(deliveries);
		}

		[Authorize(Roles = "Sofer")]
		public IActionResult StartDelivery(int id)
		{
			var user = db.ApplicationUsers.FirstOrDefault(u => u.UserName == User.Identity.Name);
			if (user == null) return Unauthorized();

			// Verific daca soferul are deja o livrare in curs
			var existingDelivery = db.Deliveries
				.FirstOrDefault(d => d.DriverId == user.Id && d.Status == "In Progress");

			if (existingDelivery != null)
			{
				TempData["Error"] = "You already have an ongoing delivery!";
				return RedirectToAction("Show", new { id });
			}

			var delivery = db.Deliveries
				.Include(d => d.Vehicle)
				.Include(d => d.Orders)
				.FirstOrDefault(d => d.Id == id && d.DriverId == user.Id);

			if (delivery == null || delivery.Status != "Planned") return NotFound();

			// Actualizez statusul User, Vehicle, Delivery si al Orders
			user.IsAvailable = false;
			delivery.Vehicle.Status = VehicleStatus.Busy;
			delivery.Status = "In Progress";

			foreach (var order in delivery.Orders)
			{
				if(order.Status == OrderStatus.Placed)
				{
					order.Status = OrderStatus.InProgress;
				}
			}

			db.SaveChanges();
			return RedirectToAction("Show", new { id });
		}

		[Authorize(Roles = "Sofer")]
		public IActionResult MarkOrderDelivered(int orderId)
		{
			var user = db.ApplicationUsers.FirstOrDefault(u => u.UserName == User.Identity.Name);
			if (user == null) return Unauthorized();

			var order = db.Orders.Include(o => o.Delivery)
								 .FirstOrDefault(o => o.Id == orderId && o.Delivery.DriverId == user.Id);

			if (order == null || order.Status != OrderStatus.InProgress) return NotFound();

			// Marchez Order ca fiind livrata
			order.Status = OrderStatus.Delivered;
			order.DeliveredDate = DateTime.Now;

			db.SaveChanges();
			return RedirectToAction("Show", new { id = order.DeliveryId });
		}

		[Authorize(Roles = "Sofer")]
		public IActionResult MarkOrderFailed(int orderId)
		{
			var user = db.ApplicationUsers.FirstOrDefault(u => u.UserName == User.Identity.Name);
			if (user == null) return Unauthorized();

			var order = db.Orders.Include(o => o.Delivery)
								 .FirstOrDefault(o => o.Id == orderId && o.Delivery.DriverId == user.Id);

			if (order == null || order.Status != OrderStatus.InProgress) return NotFound();

			// Marchez Order ca fiind esuata
			order.Status = OrderStatus.FailedDelivery;

			db.SaveChanges();
			return RedirectToAction("Show", new { id = order.DeliveryId });
		}

		[HttpPost]
		[Authorize(Roles = "Sofer")]
		public IActionResult CompleteDelivery(int id, double newOdometerReading)
		{
			var user = db.ApplicationUsers.FirstOrDefault(u => u.UserName == User.Identity.Name);
			if (user == null) return Unauthorized();

			var delivery = db.Deliveries
				.Include(d => d.Vehicle)
				.Include(d => d.Orders)
				.Include(d => d.Driver)
				.FirstOrDefault(d => d.Id == id && d.DriverId == user.Id);

			if (delivery == null || delivery.Status != "In Progress") return NotFound();

			// Ma asigur ca toate Orders din Delivery au fost livrate/fail-uite
			if (delivery.Orders.Any(o => o.Status != OrderStatus.Delivered && o.Status != OrderStatus.FailedDelivery))
			{
				TempData["Error"] = "Not all orders have been delivered or failed yet!";
				return RedirectToAction("Show", new { id });
			}

			foreach (Order order in delivery.Orders)
			{
				if (order.Status == OrderStatus.FailedDelivery)
				{
					// Daca o comanda a esuat, o marchez ca fiind disponibila pentru alt Delivery
					order.Status = OrderStatus.Placed;
					order.DeliveryId = null;
					order.DeliverySequence = null;
					order.EstimatedDeliveryDate = null;
					order.EstimatedDeliveryInterval = null;
					order.DeliveredDate = null;
					order.LastNotifiedStatus = null;
					order.LastDeliveryAssignmentNotified = null;
				}
			}

			db.SaveChanges();

			// Marchez Delivery ca fiind completa
			delivery.Status = "Completed";
			delivery.ActualEndDate = DateTime.Now;

			// Fac soferul si masina disponibili din nou
			user.IsAvailable = true;
			delivery.Vehicle.Status = VehicleStatus.Available;

			// Actualizez TotalDistanceTraveledKM dacă noua valoare este mai mare
			if (newOdometerReading > delivery.Vehicle.TotalDistanceTraveledKM)
			{
				delivery.Vehicle.TotalDistanceTraveledKM = newOdometerReading;
			}
			else
			{
				TempData["Error"] = "The entered odometer reading must be higher than the current value.";
				return RedirectToAction("Show", new { id });
			}

			// Creez RoutrHistry record
			var existingHistory = db.RouteHistories.FirstOrDefault(r => r.DeliveryId == delivery.Id);
			if (existingHistory != null)
			{
				existingHistory.DateLogged = DateTime.Now;
				existingHistory.DistanceTraveled = delivery.DistanceEstimated ?? 0;
				existingHistory.FuelConsumed = delivery.ConsumptionEstimated ?? 0;
				existingHistory.Emissions = delivery.EmissionsEstimated ?? 0;
				existingHistory.TimeTaken = (int)((delivery.TimeTakenForDelivery ?? 0) * 3600); // secunde
				existingHistory.RouteData = delivery.RouteData;
				existingHistory.VehicleId = delivery.Vehicle.Id;
				existingHistory.VehicleDescription = $"{delivery.Vehicle.Brand} {delivery.Vehicle.Model} ({delivery.Vehicle.RegistrationNumber})";
				existingHistory.OrderIdsJson = JsonConvert.SerializeObject(delivery.Orders.Select(o => o.Id));
				existingHistory.DriverId = user.Id;
				existingHistory.DriverName = user.UserName;

				db.RouteHistories.Update(existingHistory);
			}
			else
			{
				var routeHistory = new RouteHistory
				{
					DeliveryId = delivery.Id,
					DateLogged = DateTime.Now,
					DistanceTraveled = delivery.DistanceEstimated ?? 0,
					FuelConsumed = delivery.ConsumptionEstimated ?? 0,
					Emissions = delivery.EmissionsEstimated ?? 0,
					TimeTaken = (int)((delivery.TimeTakenForDelivery ?? 0) * 3600),
					RouteData = delivery.RouteData,
					VehicleId = delivery.Vehicle.Id,
					VehicleDescription = $"{delivery.Vehicle.Brand} {delivery.Vehicle.Model} ({delivery.Vehicle.RegistrationNumber})",
					OrderIdsJson = JsonConvert.SerializeObject(delivery.Orders.Select(o => o.Id)),
					DriverId = user.Id,
					DriverName = user.UserName
				};

				db.RouteHistories.Add(routeHistory);
			}

			db.SaveChanges();
			TempData["Success"] = "Delivery completed successfully!";
			return RedirectToAction("Show", new { id });
		}

		[HttpPost]
		[Authorize(Roles = "Sofer")]
		public IActionResult ClaimDelivery(int id, double? lat, double? lon)
		{
			var user = db.ApplicationUsers.FirstOrDefault(u => u.UserName == User.Identity.Name);
			if (user == null) return Unauthorized();

			if (user.IsDeleted || (user.DismissalNoticeDate != null && user.DismissalNoticeDate <= DateTime.Now.AddDays(7)))
			{
				TempData["Error"] = "You are not eligible to claim deliveries at this time.";
				return RedirectToAction("ShowDeliveriesOfDriver", new { id = user.Id });
			}

			System.Diagnostics.Debug.WriteLine($"Received ClaimDelivery Request: ID={id}, Lat={lat}, Lon={lon}");

			if (id <= 0)
			{
				TempData["Error"] = "Invalid delivery ID!";
				return RedirectToAction("ShowDeliveriesOfDriver", new { user.Id });
			}

			if (!lat.HasValue || !lon.HasValue)
			{
				TempData["Error"] = "Invalid location coordinates!";
				return RedirectToAction("ShowDeliveriesOfDriver", new { user.Id });
			}

			double driverLat = lat.Value;
			double driverLon = lon.Value;

			bool hasUnfinishedDeliveries = db.Deliveries
				.Where(d => d.DriverId == user.Id)
				.Any(d => d.Status != "Completed");

			if (hasUnfinishedDeliveries)
			{
				TempData["Error"] = "You must complete all your current deliveries before claiming a new one!";
				return RedirectToAction("ShowDeliveriesOfDriver", new { user.Id });
			}

			var delivery = db.Deliveries
				.Include(d => d.Vehicle)
				.Include(d => d.Orders)
				.FirstOrDefault(d => d.Id == id && d.DriverId == null);

			if (delivery == null)
			{
				TempData["Error"] = "This delivery is no longer available!";
				return RedirectToAction("ShowDeliveriesOfDriver", new { user.Id });
			}

			// Iau Headquarter-ul regiunii soferului
			var headquarter = db.Headquarters.FirstOrDefault(h => h.RegionId == user.RegionId);
			if (headquarter == null)
			{
				TempData["Error"] = "No headquarter found for your region!";
				return RedirectToAction("ShowDeliveriesOfDriver", new { user.Id });
			}

			// Calculez distanta intre locatia Soferului si Headquarter
			double distance = HaversineDistance(headquarter.Latitude.Value, headquarter.Longitude.Value, lat.Value, lon.Value);

			if (distance > 1.0) // Soferul trebuie sa fie in raza de 1 km fata de Headquarter
			{
				TempData["Error"] = "You must be at the headquarter to claim this delivery!";
				return RedirectToAction("ShowDeliveriesOfDriver", new { user.Id });
			}

			// Asignez Delivery-ul soferului
			delivery.DriverId = user.Id;
			delivery.Status = "Planned";
			user.IsAvailable = false;

			var routeHistory = db.RouteHistories.FirstOrDefault(r => r.DeliveryId == delivery.Id);
			if (routeHistory != null)
			{
				routeHistory.DriverId = user.Id;
				routeHistory.DriverName = user.UserName;
				db.RouteHistories.Update(routeHistory);
			}

			db.SaveChanges();
			TempData["Success"] = "Delivery successfully claimed!";
			return RedirectToAction("ShowDeliveriesOfDriver", new { id = user.Id });
		}

		[HttpPost]
		[Authorize(Roles = "Admin,Dispecer")]
		public async Task<IActionResult> DeleteDelivery(int id)
		{
			// Iau Delivery-ul cu Vehicle, Driver si Orders
			var delivery = db.Deliveries
				.Include(d => d.Vehicle)
				.Include(d => d.Driver)
				.Include(d => d.Orders)
				.FirstOrDefault(d => d.Id == id);

			if (delivery == null)
			{
				TempData["Error"] = "Delivery not found.";
				return RedirectToAction("Index");
			}

			if (delivery.Status == "Completed")
			{
				TempData["Error"] = "Completed deliveries cannot be deleted.";
				return RedirectToAction("Show", new { id });
			}

			// Resetez fiecare comanda: elimin asocierea cu Delivery-ul si resetez statusul comenzii
			foreach (var order in delivery.Orders)
			{
				order.DeliveryId = null;
				order.DeliverySequence = null;
				order.Status = OrderStatus.Placed;
				order.EstimatedDeliveryInterval = null;
				order.EstimatedDeliveryDate = null;
				db.Orders.Update(order);
			}

			// Fac vehiculul disponibil
			if (delivery.Vehicle != null)
			{
				delivery.Vehicle.Status = VehicleStatus.Available;
				db.Vehicles.Update(delivery.Vehicle);
			}

			// Fac soferul disponibil
			if (delivery.Driver != null)
			{
				delivery.Driver.IsAvailable = true;
				db.ApplicationUsers.Update(delivery.Driver);
			}

			db.Deliveries.Remove(delivery);

			await db.SaveChangesAsync();

			TempData["Success"] = "Delivery deleted successfully!";
			return RedirectToAction("Index");
		}

		private double HaversineDistance(double lat1, double lon1, double lat2, double lon2)
		{
			double earthRadiusKm = 6371.0; // Raza medie a Pamantului in km

			double dLat = (lat2 - lat1) * (Math.PI / 180);
			double dLon = (lon2 - lon1) * (Math.PI / 180);

			double a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
					   Math.Cos(lat1 * (Math.PI / 180)) * Math.Cos(lat2 * (Math.PI / 180)) *
					   Math.Sin(dLon / 2) * Math.Sin(dLon / 2);

			double c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
			return earthRadiusKm * c;
		}

		private bool CanFitOrders(Vehicle vehicle, List<Order> orders)
		{
			double totalWeight = orders.Sum(o => o.Weight ?? 0);
			double totalVolume = orders.Sum(o => o.Volume ?? 0);
			return totalWeight <= vehicle.MaxWeightCapacity && totalVolume <= vehicle.MaxVolumeCapacity;
		}

		private async Task<bool> IsAnyOptimizationInProgress()
		{
			using var command = db.Database.GetDbConnection().CreateCommand();
			command.CommandText = @"
				SELECT COUNT(*) 
				FROM sys.dm_tran_locks 
				WHERE resource_type = 'APPLICATION' 
				AND resource_description LIKE 'Optimization_%'";

			if (command.Connection.State != System.Data.ConnectionState.Open)
				await command.Connection.OpenAsync();

			var count = (int)await command.ExecuteScalarAsync();
			return count > 0;
		}

		[HttpGet]
		[Route("api/weather/heatmap")]
		[Authorize(Roles = "Admin,Dispecer,Sofer")]
		public async Task<IActionResult> GetWeatherHeatmap()
		{
			var user = db.ApplicationUsers.FirstOrDefault(u => u.UserName == User.Identity.Name);
			if (user == null || user.RegionId == null)
				return Unauthorized();

			// Iau comenzile din regiunea userului + HQ
			var regionOrders = db.Orders
				.Where(o => o.RegionId == user.RegionId && o.Latitude.HasValue && o.Longitude.HasValue)
				.Select(o => new Coordinate
				{
					Latitude = o.Latitude.Value,
					Longitude = o.Longitude.Value
				})
				.ToList();

			var hq = db.Regions
				.Include(r => r.Headquarters)
				.First(r => r.Id == user.RegionId)
				.Headquarters;

			regionOrders.Add(new Coordinate
			{
				Latitude = hq.Latitude.Value,
				Longitude = hq.Longitude.Value
			});

			// Calculez limitele
			var bounds = rps.GetRegionBoundsFromOrders(regionOrders);
			var grid = rps.GenerateGrid(bounds.MinLat, bounds.MaxLat, bounds.MinLng, bounds.MaxLng, 0.05);

			// Calculez severitatea vremii pe grila
			var weatherData = await rps.GetWeatherSeveritiesAsync(grid);

			var heatmap = weatherData
				.Select(w => new[] { w.coord.Latitude, w.coord.Longitude, Math.Round(w.severity, 2) })
				.ToList();

			return Json(heatmap);
		}

		[HttpGet]
		public async Task<IActionResult> GetEvents(int deliveryId)
		{
			var delivery = await db.Deliveries
				.Include(d => d.Vehicle)
				.FirstOrDefaultAsync(d => d.Id == deliveryId);

			if (delivery == null)
				return NotFound();

			var vehicleId = delivery.VehicleId;

			// Get latest 10 events that are not "Normal"
			var events = await db.AggressiveEvents
				.Where(e => e.VehicleId == vehicleId && e.EventType != "Normal")
				.OrderByDescending(e => e.Timestamp)
				.Take(10)
				.Select(e => new
				{
					eventType = e.EventType,
					severity = e.SeverityScore,
					timestamp = e.Timestamp.ToUniversalTime().ToString("o"),
					driverId = e.DriverId,
					probabilities = e.Probabilities,
					vehicleId = e.VehicleId,
					deliveryId = e.DeliveryId
				})
				.ToListAsync();

			return Ok(events);
		}
	}
}

public static class Extensions
{
	public static bool ContainsAll<T>(this ICollection<T> source, ICollection<T> items)
	{
		return items.All(i => source.Contains(i));
	}
}
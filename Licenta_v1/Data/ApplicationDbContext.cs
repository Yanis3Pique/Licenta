using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Licenta_v1.Models;

namespace Licenta_v1.Data
{
    public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

		public DbSet<ApplicationUser> ApplicationUsers { get; set; }
		public DbSet<Feedback> Feedbacks { get; set; }
		public DbSet<Headquarter> Headquarters { get; set; }
		public DbSet<Order> Orders { get; set; }
		public DbSet<Region> Regions { get; set; }
		public DbSet<Vehicle> Vehicles { get; set; }
		public DbSet<Delivery> Deliveries { get; set; }
		public DbSet<Maintenance> Maintenances { get; set; }
		public DbSet<RouteHistory> RouteHistories { get; set; }

		protected override void OnModelCreating(ModelBuilder modelBuilder)
		{
			base.OnModelCreating(modelBuilder);

			// Relatie Region - Headquarter (1-to-1)
			modelBuilder.Entity<Region>()
				.HasOne(r => r.Headquarters)
				.WithOne(h => h.Region)
				.HasForeignKey<Headquarter>(h => h.RegionId)
				.OnDelete(DeleteBehavior.Cascade);

			// Relatie ApplicationUser - Region (m-to-1)
			modelBuilder.Entity<ApplicationUser>()
				.HasOne(u => u.Region)
				.WithMany(r => r.Users)
				.HasForeignKey(u => u.RegionId)
				.OnDelete(DeleteBehavior.Restrict);

			// Relatie Delivery - ApplicationUser (Driver) (m-to-1)
			modelBuilder.Entity<Delivery>()
				.HasOne(d => d.Driver)
				.WithMany(u => u.Deliveries)
				.HasForeignKey(d => d.DriverId)
				.OnDelete(DeleteBehavior.Restrict);

			// Relatie Delivery - Vehicle (m-to-1)
			modelBuilder.Entity<Delivery>()
				.HasOne(d => d.Vehicle)
				.WithMany(v => v.Deliveries)
				.HasForeignKey(d => d.VehicleId)
				.OnDelete(DeleteBehavior.Restrict);

			// Relatie Vehicle - Region (m-to-1)
			modelBuilder.Entity<Vehicle>()
				.HasOne(v => v.Region)
				.WithMany(r => r.Vehicles)
				.HasForeignKey(v => v.RegionId)
				.OnDelete(DeleteBehavior.Restrict);

			// Relatie Order - ApplicationUser (Client) (m-to-1)
			modelBuilder.Entity<Order>()
				.HasOne(o => o.Client)
				.WithMany(u => u.Orders)
				.HasForeignKey(o => o.ClientId)
				.OnDelete(DeleteBehavior.Restrict);

			// Relatie Order - Delivery (m-to-1)
			modelBuilder.Entity<Order>()
				.HasOne(o => o.Delivery)
				.WithMany(d => d.Orders)
				.HasForeignKey(o => o.DeliveryId)
				.OnDelete(DeleteBehavior.SetNull);

			// Relatie Feedback - Driver (ApplicationUser) (m-to-1)
			modelBuilder.Entity<Feedback>()
				.HasOne(f => f.Driver)
				.WithMany(u => u.FeedbacksReceived)
				.HasForeignKey(f => f.DriverId)
				.OnDelete(DeleteBehavior.Restrict);

			// Relatie Feedback - Client (ApplicationUser) (m-to-1)
			modelBuilder.Entity<Feedback>()
				.HasOne(f => f.Client)
				.WithMany(u => u.FeedbacksGiven)
				.HasForeignKey(f => f.ClientId)
				.OnDelete(DeleteBehavior.Restrict);

			// Relatie Feedback - Order (1-to-1)
			modelBuilder.Entity<Feedback>()
				.HasOne(f => f.Order)
				.WithOne(o => o.Feedback)
				.HasForeignKey<Feedback>(f => f.OrderId)
				.OnDelete(DeleteBehavior.Cascade);

			// Relatie Maintenance - Vehicle (m-to-1)
			modelBuilder.Entity<Maintenance>()
				.HasOne(m => m.Vehicle)
				.WithMany(v => v.MaintenanceRecords)
				.HasForeignKey(m => m.VehicleId)
				.OnDelete(DeleteBehavior.Cascade);

			// Relatie RouteHistory - Delivery (1-to-1)
			modelBuilder.Entity<RouteHistory>()
				.HasOne(rh => rh.Delivery)
				.WithOne(d => d.RouteHistory)
				.HasForeignKey<RouteHistory>(rh => rh.DeliveryId)
				.OnDelete(DeleteBehavior.Cascade);
		}
	}
}

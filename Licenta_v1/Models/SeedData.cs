using Microsoft.AspNetCore.Mvc;
using static System.Runtime.InteropServices.JavaScript.JSType;
using System.Data;
using Licenta_v1.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;

namespace Licenta_v1.Models
{
	public static class SeedData
	{
		public static void Initialize(IServiceProvider serviceProvider)
		{
			using (var context = new ApplicationDbContext(serviceProvider.GetRequiredService<DbContextOptions<ApplicationDbContext>>()))
			{
				// Daca am deja roluri ies ca nu are rost
				if (context.Roles.Any())
				{
					return;
				}

				// Creez rolurile in BD
				context.Roles.AddRange(
					new IdentityRole
					{
						Id = "b0c5cf7b-e8b2-4eea-87b5-417e70b7e5b0",
						Name = "Admin",
						NormalizedName = "Admin".ToUpper()
					},
					new IdentityRole
					{
						Id = "b0c5cf7b-e8b2-4eea-87b5-417e70b7e5b1",
						Name = "Dispecer",
						NormalizedName = "Dispecer".ToUpper()
					},
					new IdentityRole
					{
						Id = "b0c5cf7b-e8b2-4eea-87b5-417e70b7e5b2",
						Name = "Sofer",
						NormalizedName = "Sofer".ToUpper()
					},
					new IdentityRole
					{
						Id = "b0c5cf7b-e8b2-4eea-87b5-417e70b7e5b3",
						Name = "Client",
						NormalizedName = "Client".ToUpper()
					}
				);

				// Instanta pentru creare de parole pt useri(Hash-uite)
				var hasherForPasswords = new PasswordHasher<ApplicationUser>();

				// Creez userii in BD - o sa fac cate un user pentru fiecare rol pentru inceput
				context.Users.AddRange(
					new ApplicationUser
					{
						Id = "75c71560-bd1a-4284-98aa-b0af8ba69fa0",
						UserName = "admin",
						NormalizedUserName = "admin".ToUpper(),
						FirstName = "Gerard",
						LastName = "Pique",
						DateHired = new DateTime(2019, 10, 15),
						Email = "admin@test.com",
						NormalizedEmail = "ADMIN@TEST.COM",
						EmailConfirmed = true,
						PhoneNumber = "0735221044",
						PhoneNumberConfirmed = true,
						PasswordHash = hasherForPasswords.HashPassword(null, "Admin5576!"),
					},
					new ApplicationUser
					{
						Id = "75c71560-bd1a-4284-98aa-b0af8ba69fa1",
						UserName = "dispecer",
						NormalizedUserName = "dispecer".ToUpper(),
						FirstName = "Lionel",
						LastName = "Messi",
						DateHired = new DateTime(2020, 1, 1),
						Email = "dispecer@test.com",
						NormalizedEmail = "DISPECER@TEST.COM",
						EmailConfirmed = true,
						PhoneNumber = "0753502075",
						PhoneNumberConfirmed = true,
						PasswordHash = hasherForPasswords.HashPassword(null, "Dispecer5576!"),
					},
					new ApplicationUser
					{
						Id = "75c71560-bd1a-4284-98aa-b0af8ba69fa2",
						UserName = "sofer",
						NormalizedUserName = "sofer".ToUpper(),
						FirstName = "Sergio",
						LastName = "Busquets",
						DateHired = new DateTime(2020, 1, 2),
						Email = "sofer@test.com",
						NormalizedEmail = "SOFER@TEST.COM",
						EmailConfirmed = true,
						PhoneNumber = "0771292251",
						PhoneNumberConfirmed = true,
						PasswordHash = hasherForPasswords.HashPassword(null, "Sofer5576!"),
					},
					new ApplicationUser
					{
						Id = "75c71560-bd1a-4284-98aa-b0af8ba69fa3",
						UserName = "client",
						NormalizedUserName = "client".ToUpper(),
						FirstName = "Andres",
						LastName = "Iniesta",
						DateHired = new DateTime(2020, 1, 3),
						Email = "client@test.com",
						NormalizedEmail = "CLIENT@TEST.COM",
						EmailConfirmed = true,
						PhoneNumber = "0722829817",
						PhoneNumberConfirmed = true,
						PasswordHash = hasherForPasswords.HashPassword(null, "Client5576!"),
					}
				);

				// Asociez userii cu rolurile
				context.UserRoles.AddRange(
					new IdentityUserRole<string>
					{
						UserId = "75c71560-bd1a-4284-98aa-b0af8ba69fa0",
						RoleId = "b0c5cf7b-e8b2-4eea-87b5-417e70b7e5b0"
					},
					new IdentityUserRole<string>
					{
						UserId = "75c71560-bd1a-4284-98aa-b0af8ba69fa1",
						RoleId = "b0c5cf7b-e8b2-4eea-87b5-417e70b7e5b1"
					},
					new IdentityUserRole<string>
					{
						UserId = "75c71560-bd1a-4284-98aa-b0af8ba69fa2",
						RoleId = "b0c5cf7b-e8b2-4eea-87b5-417e70b7e5b2"
					},
					new IdentityUserRole<string>
					{
						UserId = "75c71560-bd1a-4284-98aa-b0af8ba69fa3",
						RoleId = "b0c5cf7b-e8b2-4eea-87b5-417e70b7e5b3"
					}
				);

				context.SaveChanges();
			}
		}
	}
}

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
		public static async void Initialize(IServiceProvider serviceProvider)
		{
			using (var context = new ApplicationDbContext(serviceProvider.GetRequiredService<DbContextOptions<ApplicationDbContext>>()))
			{
				// Adaug toate judetele in baza de date + Bucuresti cele 6 sectoare
				if (context.Regions.Any())
				{
					return;
				}

				var regions = new List<Region>
				{
					new Region { County = "Alba" },
					new Region { County = "Arad" },
					new Region { County = "Argeș" },
					new Region { County = "Bacău" },
					new Region { County = "Bihor" },
					new Region { County = "Bistrița-Năsăud" },
					new Region { County = "Botoșani" },
					new Region { County = "Brăila" },
					new Region { County = "Brașov" },
					new Region { County = "București Sector 1" },
					new Region { County = "București Sector 2" },
					new Region { County = "București Sector 3" },
					new Region { County = "București Sector 4" },
					new Region { County = "București Sector 5" },
					new Region { County = "București Sector 6" },
					new Region { County = "Buzău" },
					new Region { County = "Călărași" },
					new Region { County = "Caraș-Severin" },
					new Region { County = "Cluj" },
					new Region { County = "Constanța" },
					new Region { County = "Covasna" },
					new Region { County = "Dâmbovița" },
					new Region { County = "Dolj" },
					new Region { County = "Galați" },
					new Region { County = "Giurgiu" },
					new Region { County = "Gorj" },
					new Region { County = "Harghita" },
					new Region { County = "Hunedoara" },
					new Region { County = "Ialomița" },
					new Region { County = "Iași" },
					new Region { County = "Ilfov" },
					new Region { County = "Maramureș" },
					new Region { County = "Mehedinți" },
					new Region { County = "Mureș" },
					new Region { County = "Neamț" },
					new Region { County = "Olt" },
					new Region { County = "Prahova" },
					new Region { County = "Satu Mare" },
					new Region { County = "Sălaj" },
					new Region { County = "Sibiu" },
					new Region { County = "Suceava" },
					new Region { County = "Teleorman" },
					new Region { County = "Timiș" },
					new Region { County = "Tulcea" },
					new Region { County = "Vaslui" },
					new Region { County = "Vâlcea" },
					new Region { County = "Vrancea" }
				};

				context.Regions.AddRange(regions);
				await context.SaveChangesAsync();

				// Adaug depozitele/bazele in baza de date
				if(context.Headquarters.Any())
				{
					return;
				}

				var headquarters = new List<Headquarter>
				{
					new Headquarter
					{
						Name = "EcoDelivery Alba-Iulia",
						RegionId = regions.Single(r => r.County == "Alba").Id,
						Address = "Str. Mihai Viteazu, nr. 1, Alba-Iulia",
						Latitude = 46.0711,
						Longitude = 23.5806
					},
					new Headquarter
					{
						Name = "EcoDelivery Arad",
						RegionId = regions.Single(r => r.County == "Arad").Id,
						Address = "Str. Vasile Goldiș, nr. 1, Arad",
						Latitude = 46.1866,
						Longitude = 21.3126
					},
					new Headquarter
					{
						Name = "EcoDelivery Pitești",
						RegionId = regions.Single(r => r.County == "Argeș").Id,
						Address = "Str. Nicolae Bălcescu, nr. 1, Pitești",
						Latitude = 44.8606,
						Longitude = 24.8678
					},
					new Headquarter
					{
						Name = "EcoDelivery Bacău",
						RegionId = regions.Single(r => r.County == "Bacău").Id,
						Address = "Str. Mărășești, nr. 1, Bacău",
						Latitude = 46.5712,
						Longitude = 26.9136
					},
					new Headquarter
					{
						Name = "EcoDelivery Oradea",
						RegionId = regions.Single(r => r.County == "Bihor").Id,
						Address = "Str. Independenței, nr. 1, Oradea",
						Latitude = 47.0722,
						Longitude = 21.9217
					},
					new Headquarter
					{
						Name = "EcoDelivery Bistrița",
						RegionId = regions.Single(r => r.County == "Bistrița-Năsăud").Id,
						Address = "Str. 1 Decembrie 1918, nr. 1, Bistrița",
						Latitude = 47.1333,
						Longitude = 24.4833
					},
					new Headquarter
					{
						Name = "EcoDelivery Botoșani",
						RegionId = regions.Single(r => r.County == "Botoșani").Id,
						Address = "Str. Ștefan cel Mare, nr. 1, Botoșani",
						Latitude = 47.7489,
						Longitude = 26.6597
					},
					new Headquarter
					{
						Name = "EcoDelivery Brăila",
						RegionId = regions.Single(r => r.County == "Brăila").Id,
						Address = "Str. Mihai Eminescu, nr. 1, Brăila",
						Latitude = 45.2692,
						Longitude = 27.9575
					},
					new Headquarter
					{
						Name = "EcoDelivery Brașov",
						RegionId = regions.Single(r => r.County == "Brașov").Id,
						Address = "Str. Mureșenilor, nr. 1, Brașov",
						Latitude = 45.6572,
						Longitude = 25.6012
					},
					new Headquarter
					{
						Name = "EcoDelivery București Sector 1",
						RegionId = regions.Single(r => r.County == "București Sector 1").Id,
						Address = "Str. Calea Victoriei, nr. 1, București Sector 1",
						Latitude = 44.4396,
						Longitude = 26.0963
					},
					new Headquarter
					{
						Name = "EcoDelivery București Sector 2",
						RegionId = regions.Single(r => r.County == "București Sector 2").Id,
						Address = "Str. Bulevardul Unirii, nr. 1, București Sector 2",
						Latitude = 44.4325,
						Longitude = 26.1063
					},
					new Headquarter
					{
						Name = "EcoDelivery București Sector 3",
						RegionId = regions.Single(r => r.County == "București Sector 3").Id,
						Address = "Str. Calea Moșilor, nr. 1, București Sector 3",
						Latitude = 44.4356,
						Longitude = 26.1246
					},
					new Headquarter
					{
						Name = "EcoDelivery București Sector 4",
						RegionId = regions.Single(r => r.County == "București Sector 4").Id,
						Address = "Str. Bulevardul Tineretului, nr. 1, București Sector 4",
						Latitude = 44.3989,
						Longitude = 26.1236
					},
					new Headquarter
					{
						Name = "EcoDelivery București Sector 5",
						RegionId = regions.Single(r => r.County == "București Sector 5").Id,
						Address = "Str. Calea Rahovei, nr. 1, București Sector 5",
						Latitude = 44.4236,
						Longitude = 26.0425
					},
					new Headquarter
					{
						Name = "EcoDelivery București Sector 6",
						RegionId = regions.Single(r => r.County == "București Sector 6").Id,
						Address = "Str. Bulevardul Iuliu Maniu, nr. 1, București Sector 6",
						Latitude = 44.4312,
						Longitude = 26.0312
					},
					new Headquarter
					{
						Name = "EcoDelivery Buzău",
						RegionId = regions.Single(r => r.County == "Buzău").Id,
						Address = "Str. Mihai Eminescu, nr. 1, Buzău",
						Latitude = 45.1489,
						Longitude = 26.8236
					},
					new Headquarter
					{
						Name = "EcoDelivery Călărași",
						RegionId = regions.Single(r => r.County == "Călărași").Id,
						Address = "Str. Alexandru Ioan Cuza, nr. 1, Călărași",
						Latitude = 44.2025,
						Longitude = 27.3306
					},
					new Headquarter
					{
						Name = "EcoDelivery Reșița",
						RegionId = regions.Single(r => r.County == "Caraș-Severin").Id,
						Address = "Str. 1 Decembrie 1918, nr. 1, Reșița",
						Latitude = 45.2975,
						Longitude = 21.8897
					},
					new Headquarter
					{
						Name = "EcoDelivery Cluj-Napoca",
						RegionId = regions.Single(r => r.County == "Cluj").Id,
						Address = "Str. Eroilor, nr. 1, Cluj-Napoca",
						Latitude = 46.7712,
						Longitude = 23.6236
					},
					new Headquarter
					{
						Name = "EcoDelivery Constanța",
						RegionId = regions.Single(r => r.County == "Constanța").Id,
						Address = "Str. Mircea cel Bătrân, nr. 1, Constanța",
						Latitude = 44.1769,
						Longitude = 28.6529
					},
					new Headquarter
					{
						Name = "EcoDelivery Sfântu Gheorghe",
						RegionId = regions.Single(r => r.County == "Covasna").Id,
						Address = "Str. 1 Decembrie 1918, nr. 1, Sfântu Gheorghe",
						Latitude = 45.8689,
						Longitude = 25.7836
					},
					new Headquarter
					{
						Name = "EcoDelivery Târgoviște",
						RegionId = regions.Single(r => r.County == "Dâmbovița").Id,
						Address = "Str. 1 Decembrie 1918, nr. 1, Târgoviște",
						Latitude = 44.9347,
						Longitude = 25.4593
					},
					new Headquarter
					{
						Name = "EcoDelivery Craiova",
						RegionId = regions.Single(r => r.County == "Dolj").Id,
						Address = "Str. Calea București, nr. 1, Craiova",
						Latitude = 44.3306,
						Longitude = 23.7947
					},
					new Headquarter
					{
						Name = "EcoDelivery Galați",
						RegionId = regions.Single(r => r.County == "Galați").Id,
						Address = "Str. Domnească, nr. 1, Galați",
						Latitude = 45.4389,
						Longitude = 28.0456
					},
					new Headquarter
					{
						Name = "EcoDelivery Giurgiu",
						RegionId = regions.Single(r => r.County == "Giurgiu").Id,
						Address = "Str. 1 Decembrie 1918, nr. 1, Giurgiu",
						Latitude = 43.9036,
						Longitude = 25.9736
					},
					new Headquarter
					{
						Name = "EcoDelivery Târgu Jiu",
						RegionId = regions.Single(r => r.County == "Gorj").Id,
						Address = "Str. 1 Decembrie 1918, nr. 1, Târgu Jiu",
						Latitude = 45.0456,
						Longitude = 23.2747
					},
					new Headquarter
					{
						Name = "EcoDelivery Miercurea Ciuc",
						RegionId = regions.Single(r => r.County == "Harghita").Id,
						Address = "Str. 1 Decembrie 1918, nr. 1, Miercurea Ciuc",
						Latitude = 46.3612,
						Longitude = 25.8025
					},
					new Headquarter
					{
						Name = "EcoDelivery Deva",
						RegionId = regions.Single(r => r.County == "Hunedoara").Id,
						Address = "Str. 1 Decembrie 1918, nr. 1, Deva",
						Latitude = 45.8836,
						Longitude = 22.9025
					},
					new Headquarter
					{
						Name = "EcoDelivery Slobozia",
						RegionId = regions.Single(r => r.County == "Ialomița").Id,
						Address = "Str. 1 Decembrie 1918, nr. 1, Slobozia",
						Latitude = 44.5669,
						Longitude = 27.3669
					},
					new Headquarter
					{
						Name = "EcoDelivery Iași",
						RegionId = regions.Single(r => r.County == "Iași").Id,
						Address = "Str. 1 Decembrie 1918, nr. 1, Iași",
						Latitude = 47.1589,
						Longitude = 27.6012
					},
					new Headquarter
					{
						Name = "EcoDelivery Buftea",
						RegionId = regions.Single(r => r.County == "Ilfov").Id,
						Address = "Str. 1 Decembrie 1918, nr. 1, Buftea",
						Latitude = 44.5589,
						Longitude = 25.9486
					},
					new Headquarter
					{
						Name = "EcoDelivery Baia Mare",
						RegionId = regions.Single(r => r.County == "Maramureș").Id,
						Address = "Str. 1 Decembrie 1918, nr. 1, Baia Mare",
						Latitude = 47.6589,
						Longitude = 23.5686
					},
					new Headquarter
					{
						Name = "EcoDelivery Drobeta-Turnu Severin",
						RegionId = regions.Single(r => r.County == "Mehedinți").Id,
						Address = "Str. 1 Decembrie 1918, nr. 1, Drobeta-Turnu Severin",
						Latitude = 44.6312,
						Longitude = 22.6569
					},
					new Headquarter
					{
						Name = "EcoDelivery Târgu Mureș",
						RegionId = regions.Single(r => r.County == "Mureș").Id,
						Address = "Str. 1 Decembrie 1918, nr. 1, Târgu Mureș",
						Latitude = 46.5406,
						Longitude = 24.5625
					},
					new Headquarter
					{
						Name = "EcoDelivery Piatra Neamț",
						RegionId = regions.Single(r => r.County == "Neamț").Id,
						Address = "Str. 1 Decembrie 1918, nr. 1, Piatra Neamț",
						Latitude = 46.9286,
						Longitude = 26.3547
					},
					new Headquarter
					{
						Name = "EcoDelivery Slatina",
						RegionId = regions.Single(r => r.County == "Olt").Id,
						Address = "Str. 1 Decembrie 1918, nr. 1, Slatina",
						Latitude = 44.4312,
						Longitude = 24.3656
					},
					new Headquarter
					{
						Name = "EcoDelivery Ploiești",
						RegionId = regions.Single(r => r.County == "Prahova").Id,
						Address = "Str. 1 Decembrie 1918, nr. 1, Ploiești",
						Latitude = 44.9469,
						Longitude = 26.0369
					},
					new Headquarter
					{
						Name = "EcoDelivery Satu Mare",
						RegionId = regions.Single(r => r.County == "Satu Mare").Id,
						Address = "Str. 1 Decembrie 1918, nr. 1, Satu Mare",
						Latitude = 47.7925,
						Longitude = 22.8856
					},
					new Headquarter
					{
						Name = "EcoDelivery Zalău",
						RegionId = regions.Single(r => r.County == "Sălaj").Id,
						Address = "Str. 1 Decembrie 1918, nr. 1, Zalău",
						Latitude = 47.1836,
						Longitude = 23.0556
					},
					new Headquarter
					{
						Name = "EcoDelivery Sibiu",
						RegionId = regions.Single(r => r.County == "Sibiu").Id,
						Address = "Str. 1 Decembrie 1918, nr. 1, Sibiu",
						Latitude = 45.7986,
						Longitude = 24.1256
					},
					new Headquarter
					{
						Name = "EcoDelivery Suceava",
						RegionId = regions.Single(r => r.County == "Suceava").Id,
						Address = "Str. 1 Decembrie 1918, nr. 1, Suceava",
						Latitude = 47.6547,
						Longitude = 26.2556
					},
					new Headquarter
					{
						Name = "EcoDelivery Alexandria",
						RegionId = regions.Single(r => r.County == "Teleorman").Id,
						Address = "Str. 1 Decembrie 1918, nr. 1, Alexandria",
						Latitude = 43.9836,
						Longitude = 25.3456
					},
					new Headquarter
					{
						Name = "EcoDelivery Timișoara",
						RegionId = regions.Single(r => r.County == "Timiș").Id,
						Address = "Str. 1 Decembrie 1918, nr. 1, Timișoara",
						Latitude = 45.7486,
						Longitude = 21.2256
					},
					new Headquarter
					{
						Name = "EcoDelivery Tulcea",
						RegionId = regions.Single(r => r.County == "Tulcea").Id,
						Address = "Str. 1 Decembrie 1918, nr. 1, Tulcea",
						Latitude = 45.1786,
						Longitude = 28.8056
					},
					new Headquarter
					{
						Name = "EcoDelivery Vaslui",
						RegionId = regions.Single(r => r.County == "Vaslui").Id,
						Address = "Str. 1 Decembrie 1918, nr. 1, Vaslui",
						Latitude = 46.6386,
						Longitude = 27.7297
					},
					new Headquarter
					{
						Name = "EcoDelivery Râmnicu Vâlcea",
						RegionId = regions.Single(r => r.County == "Vâlcea").Id,
						Address = "Str. 1 Decembrie 1918, nr. 1, Râmnicu Vâlcea",
						Latitude = 45.0986,
						Longitude = 24.3669
					},
					new Headquarter
					{
						Name = "EcoDelivery Focșani",
						RegionId = regions.Single(r => r.County == "Vrancea").Id,
						Address = "Str. 1 Decembrie 1918, nr. 1, Focșani",
						Latitude = 45.6969,
						Longitude = 27.1869
					}
				};

				context.Headquarters.AddRange(headquarters);
				await context.SaveChangesAsync();

				// Acum ma ocup de vehicule
				if(context.Vehicles.Any())
				{
					return;
				}

				var vehicles = new List<Vehicle>
				{
					new Vehicle
					{
						Brand = "Mercedes-Benz",
						Model = "Sprinter",
						RegistrationNumber = "B123XYZ",
						YearOfManufacture = 2018,
						Status = VehicleStatus.Available,
						FuelType = FuelType.Diesel,
						MaxWeightCapacity = 3500,
						MaxVolumeCapacity = 15,
						RegionId = regions.Single(r => r.County == "Argeș").Id,
						ConsumptionRate = 9.5,
						TotalDistanceTraveledKM = 120000
					},
					new Vehicle
					{
						Brand = "Ford",
						Model = "Transit",
						RegistrationNumber = "CJ45AAA",
						YearOfManufacture = 2019,
						Status = VehicleStatus.Busy,
						FuelType = FuelType.Diesel,
						MaxWeightCapacity = 3000,
						MaxVolumeCapacity = 12,
						RegionId = regions.Single(r => r.County == "Bihor").Id,
						ConsumptionRate = 8.8,
						TotalDistanceTraveledKM = 95000
					},
					new Vehicle
					{
						Brand = "Volkswagen",
						Model = "Crafter",
						RegistrationNumber = "TM76BBY",
						YearOfManufacture = 2020,
						Status = VehicleStatus.Available,
						FuelType = FuelType.Diesel,
						MaxWeightCapacity = 3500,
						MaxVolumeCapacity = 14,
						RegionId = regions.Single(r => r.County == "Cluj").Id,
						ConsumptionRate = 9.2,
						TotalDistanceTraveledKM = 80000
					},
					new Vehicle
					{
						Brand = "Renault",
						Model = "Master",
						RegistrationNumber = "AR99CCC",
						YearOfManufacture = 2021,
						Status = VehicleStatus.Busy,
						FuelType = FuelType.Diesel,
						MaxWeightCapacity = 3000,
						MaxVolumeCapacity = 11,
						RegionId = regions.Single(r => r.County == "Constanța").Id,
						ConsumptionRate = 9.0,
						TotalDistanceTraveledKM = 50000
					},
					new Vehicle
					{
						Brand = "Tesla",
						Model = "Semi",
						RegistrationNumber = "B567DEF",
						YearOfManufacture = 2022,
						Status = VehicleStatus.Available,
						FuelType = FuelType.Electric,
						MaxWeightCapacity = 18000,
						MaxVolumeCapacity = 60,
						RegionId = regions.Single(r => r.County == "Dolj").Id,
						ConsumptionRate = 20.0,
						TotalDistanceTraveledKM = 10000
					},
					new Vehicle
					{
						Brand = "Fiat",
						Model = "Ducato",
						RegistrationNumber = "IS34EEE",
						YearOfManufacture = 2019,
						Status = VehicleStatus.Maintenance,
						FuelType = FuelType.Diesel,
						MaxWeightCapacity = 3300,
						MaxVolumeCapacity = 12,
						RegionId = regions.Single(r => r.County == "Galați").Id,
						ConsumptionRate = 10.0,
						TotalDistanceTraveledKM = 70000
					},
					new Vehicle
					{
						Brand = "Iveco",
						Model = "Daily",
						RegistrationNumber = "BV12FFF",
						YearOfManufacture = 2020,
						Status = VehicleStatus.Available,
						FuelType = FuelType.Diesel,
						MaxWeightCapacity = 3500,
						MaxVolumeCapacity = 14,
						RegionId = regions.Single(r => r.County == "Iași").Id,
						ConsumptionRate = 10.5,
						TotalDistanceTraveledKM = 60000
					},
					new Vehicle
					{
						Brand = "Toyota",
						Model = "Hiace",
						RegistrationNumber = "GL56GGG",
						YearOfManufacture = 2017,
						Status = VehicleStatus.Busy,
						FuelType = FuelType.Diesel,
						MaxWeightCapacity = 2800,
						MaxVolumeCapacity = 10,
						RegionId = regions.Single(r => r.County == "Ilfov").Id,
						ConsumptionRate = 8.5,
						TotalDistanceTraveledKM = 130000
					},
					new Vehicle
					{
						Brand = "Peugeot",
						Model = "Boxer",
						RegistrationNumber = "CL88HHH",
						YearOfManufacture = 2016,
						Status = VehicleStatus.Available,
						FuelType = FuelType.Diesel,
						MaxWeightCapacity = 3200,
						MaxVolumeCapacity = 12,
						RegionId = regions.Single(r => r.County == "Maramureș").Id,
						ConsumptionRate = 10.2,
						TotalDistanceTraveledKM = 150000
					},
					new Vehicle
					{
						Brand = "Citroen",
						Model = "Jumper",
						RegistrationNumber = "CJ67III",
						YearOfManufacture = 2018,
						Status = VehicleStatus.Maintenance,
						FuelType = FuelType.Diesel,
						MaxWeightCapacity = 3100,
						MaxVolumeCapacity = 13,
						RegionId = regions.Single(r => r.County == "Mehedinți").Id,
						ConsumptionRate = 9.8,
						TotalDistanceTraveledKM = 110000
					},
					new Vehicle
					{
						Brand = "MAN",
						Model = "TGE",
						RegistrationNumber = "MS45JJJ",
						YearOfManufacture = 2021,
						Status = VehicleStatus.Busy,
						FuelType = FuelType.Diesel,
						MaxWeightCapacity = 3500,
						MaxVolumeCapacity = 15,
						RegionId = regions.Single(r => r.County == "Neamț").Id,
						ConsumptionRate = 9.5,
						TotalDistanceTraveledKM = 45000
					},
					new Vehicle
					{
						Brand = "Hyundai",
						Model = "H350",
						RegistrationNumber = "BH89KKK",
						YearOfManufacture = 2019,
						Status = VehicleStatus.Available,
						FuelType = FuelType.Diesel,
						MaxWeightCapacity = 3400,
						MaxVolumeCapacity = 14,
						RegionId = regions.Single(r => r.County == "Olt").Id,
						ConsumptionRate = 10.0,
						TotalDistanceTraveledKM = 90000
					},
					new Vehicle
					{
						Brand = "Nissan",
						Model = "NV400",
						RegistrationNumber = "VN23LLL",
						YearOfManufacture = 2018,
						Status = VehicleStatus.Maintenance,
						FuelType = FuelType.Diesel,
						MaxWeightCapacity = 3200,
						MaxVolumeCapacity = 13,
						RegionId = regions.Single(r => r.County == "Argeș").Id,
						ConsumptionRate = 10.3,
						TotalDistanceTraveledKM = 80000
					},
					new Vehicle
					{
						Brand = "Opel",
						Model = "Movano",
						RegistrationNumber = "AG56MMM",
						YearOfManufacture = 2017,
						Status = VehicleStatus.Available,
						FuelType = FuelType.Diesel,
						MaxWeightCapacity = 3100,
						MaxVolumeCapacity = 12,
						RegionId = regions.Single(r => r.County == "Argeș").Id,
						ConsumptionRate = 9.7,
						TotalDistanceTraveledKM = 120000
					},
					new Vehicle
					{
						Brand = "DAF",
						Model = "LF",
						RegistrationNumber = "BR98NNN",
						YearOfManufacture = 2020,
						Status = VehicleStatus.Busy,
						FuelType = FuelType.Diesel,
						MaxWeightCapacity = 12000,
						MaxVolumeCapacity = 45,
						RegionId = regions.Single(r => r.County == "Argeș").Id,
						ConsumptionRate = 15.0,
						TotalDistanceTraveledKM = 40000
					}
				};

				context.Vehicles.AddRange(vehicles);
				await context.SaveChangesAsync();

				// Acum ma ocup de roluri, useri si legatura dintre ei
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
						PhotoPath = "Images/Pique.jpg",
						RegionId = regions.Single(r => r.County == "Dolj").Id,
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
						PhotoPath = "Images/Messi.jpg",
						RegionId = regions.Single(r => r.County == "Argeș").Id,
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
						PhotoPath = "Images/Busquets.jpg",
						RegionId = regions.Single(r => r.County == "Argeș").Id,
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
						PhotoPath = "Images/Iniesta.jpg",
						RegionId = regions.Single(r => r.County == "Olt").Id,
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

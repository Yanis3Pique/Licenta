// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
#nullable disable

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Licenta_v1.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging;
using Licenta_v1.Data;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace Licenta_v1.Areas.Identity.Pages.Account
{
	public class RegisterModel : PageModel
	{
		private readonly SignInManager<ApplicationUser> _signInManager;
		private readonly UserManager<ApplicationUser> _userManager;
		private readonly IUserStore<ApplicationUser> _userStore;
		private readonly IUserEmailStore<ApplicationUser> _emailStore;
		private readonly ILogger<RegisterModel> _logger;
		private readonly IEmailSender _emailSender;
		private readonly ApplicationDbContext _dbContext;

		public RegisterModel(
			UserManager<ApplicationUser> userManager,
			IUserStore<ApplicationUser> userStore,
			SignInManager<ApplicationUser> signInManager,
			ILogger<RegisterModel> logger,
			IEmailSender emailSender,
			ApplicationDbContext dbContext)
		{
			_userManager = userManager;
			_userStore = userStore;
			_emailStore = GetEmailStore();
			_signInManager = signInManager;
			_logger = logger;
			_emailSender = emailSender;
			_dbContext = dbContext;
		}

		[BindProperty]
		public InputModel Input { get; set; }

		public string ReturnUrl { get; set; }

		public IList<AuthenticationScheme> ExternalLogins { get; set; }

		public IEnumerable<SelectListItem> Regions { get; set; }

		public class InputModel
		{
			[Required(ErrorMessage = "The user name is mandatory!")]
			[Display(Name = "User Name")]
			public string UserName { get; set; }

			[Required(ErrorMessage = "The first name is mandatory!")]
			[MaxLength(50, ErrorMessage = "The first name must be maximum 50 characters in length!")]
			[MinLength(2, ErrorMessage = "The first name must be minimum 2 characters in length!")]
			[Display(Name = "First Name")]
			public string FirstName { get; set; }

			[Required(ErrorMessage = "The last name is mandatory!")]
			[StringLength(50, ErrorMessage = "The last name must be maximum 50 characters in length!")]
			[MinLength(2, ErrorMessage = "The last name must be minimum 2 characters in length!")]
			[Display(Name = "Last Name")]
			public string LastName { get; set; }

			[Required(ErrorMessage = "The home address is mandatory.")]
			[MaxLength(200, ErrorMessage = "The home address must be maximum 200 characters in length.")]
			[MinLength(5, ErrorMessage = "The home address must be minimum 5 characters in length.")]
			[Display(Name = "Home Address")]
			public string HomeAddress { get; set; }

			[Required(ErrorMessage = "The latitude is mandatory.")]
			[Range(-90, 90, ErrorMessage = "Invalid latitude.")]
			public double? Latitude { get; set; }

			[Required(ErrorMessage = "The longitude is mandatory.")]
			[Range(-180, 180, ErrorMessage = "Invalid longitude.")]
			public double? Longitude { get; set; }

			[Required(ErrorMessage = "The email is mandatory!")]
			[EmailAddress(ErrorMessage = "Invalid email address.")]
			[Display(Name = "Email")]
			public string Email { get; set; }

			[Required(ErrorMessage = "The phone number is mandatory!")]
			[Phone(ErrorMessage = "Invalid phone number!")]
			[RegularExpression(@"^(\+4)?(07\d{8}|021\d{7}|02\d{8}|03\d{8})$", ErrorMessage = "Invalid phone number!")]
			[Display(Name = "Phone Number")]
			public string PhoneNumber { get; set; }

			[Required(ErrorMessage = "The region is mandatory!")]
			[Display(Name = "Region")]
			public int? RegionId { get; set; }

			[Required(ErrorMessage = "The password is mandatory!")]
			[StringLength(50, ErrorMessage = "The {0} must be at least {2} and at max {1} characters long!", MinimumLength = 6)]
			[RegularExpression(@"^(?=.*[A-Za-z])(?=.*\d)(?=.*[^a-zA-Z\d]).+$",
				ErrorMessage = "The password must be alphanumeric and contain at least one non-alphanumeric character (e.g. symbols)!")]
			[DataType(DataType.Password)]
			[Display(Name = "Password")]
			public string Password { get; set; }

			[Required(ErrorMessage = "The password confirmation is mandatory!")]
			[DataType(DataType.Password)]
			[Display(Name = "Confirm password")]
			[Compare("Password", ErrorMessage = "The password and confirmation password do not match!")]
			public string ConfirmPassword { get; set; }

		}

		public async Task OnGetAsync(string returnUrl = null)
		{
			ReturnUrl = returnUrl;
			ExternalLogins = (await _signInManager.GetExternalAuthenticationSchemesAsync()).ToList();

			// Iau judetele din baza de date si le pun in dropdown
			PopulateRegions();
		}

		public async Task<IActionResult> OnPostAsync(string returnUrl = null)
		{
			returnUrl ??= Url.Content("~/");
			ExternalLogins = (await _signInManager.GetExternalAuthenticationSchemesAsync()).ToList();

			// Repopulez judetele daca imi da eroare
			PopulateRegions();

			if (ModelState.IsValid)
			{
				var user = CreateUser();

				user.UserName = Input.UserName;
				user.FirstName = Input.FirstName;
				user.LastName = Input.LastName;
				user.PhoneNumber = Input.PhoneNumber;
				user.RegionId = Input.RegionId;
				user.DateHired = DateTime.Now;
				user.HomeAddress = Input.HomeAddress;
				user.Latitude = Input.Latitude;
				user.Longitude = Input.Longitude;

				// Verific cu functia IsValidAddressInRomania daca adresa e valida
				if (!IsValidAddressInRomania(user.HomeAddress))
				{
					TempData["Error"] = "The address is not valid. Please enter a valid address in Romania.";
					return Page();
				}

				await _userStore.SetUserNameAsync(user, Input.UserName, CancellationToken.None);
				await _emailStore.SetEmailAsync(user, Input.Email, CancellationToken.None);

				// Verific daca avem email duplicat in BD
				if (_userManager.Users.Any(u => u.Email == Input.Email))
				{
					TempData["Error"] = "This email is already registered. Please use a different email.";
					return Page();
				}

				// Verific daca avem username duplicat in BD
				if (await _userManager.FindByNameAsync(Input.UserName) != null)
				{
					TempData["Error"] = "This username is already taken. Please choose another username.";
					return Page();
				}

				// Verific daca UserName-ul are spatii
				if (Input.UserName.Contains(" "))
				{
					TempData["Error"] = "The username must not contain spaces.";
					return Page();
				}

				// Creez userul si ma ocup de erori
				var result = await _userManager.CreateAsync(user, Input.Password);

				if (result.Succeeded)
				{
					_logger.LogInformation("User created a new account with password.");
					await _userManager.AddToRoleAsync(user, "Client");

					var userId = await _userManager.GetUserIdAsync(user);
					var code = await _userManager.GenerateEmailConfirmationTokenAsync(user);
					code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(code));
					var callbackUrl = Url.Page(
						"/Account/ConfirmEmail",
						pageHandler: null,
						values: new { area = "Identity", userId = userId, code = code, returnUrl = returnUrl },
						protocol: Request.Scheme);

					// Trimit mail de confirmare
					await _emailSender.SendEmailAsync(
					Input.Email,
					"Confirm Your Email - EcoDelivery",
					$@"
						<div style='font-family: Arial, sans-serif; line-height: 1.6; max-width: 600px; margin: auto;'>
							<div style='text-align: center; padding: 20px; background-color: #f4f4f4; border-bottom: 1px solid #ddd;'>
								<img src='https://cdn.pixabay.com/photo/2024/04/01/14/43/ai-generated-8669101_1280.png' alt='EcoDelivery Logo' style='width: 120px; margin-bottom: 10px;' />
								<h1 style='color: #333;'>EcoDelivery</h1>
							</div>
							<div style='padding: 20px; background-color: #ffffff;'>
								<h2 style='color: #555;'>Welcome to EcoDelivery!</h2>
								<p style='color: #666;'>Thank you for signing up with EcoDelivery. We're thrilled to have you onboard!</p>
								<p style='color: #666;'>Please confirm your account by clicking the button below:</p>
								<div style='text-align: center; margin: 20px 0;'>
									<a href='{HtmlEncoder.Default.Encode(callbackUrl)}' style='display: inline-block; padding: 12px 20px; background-color: #28a745; color: #fff; text-decoration: none; border-radius: 5px; font-size: 16px;'>
										Confirm Your Email
									</a>
								</div>
								<p style='color: #666;'>If you did not sign up for EcoDelivery, please ignore this email or contact us at yanispavel.popescu@gmail.com.</p>
							</div>
							<div style='text-align: center; padding: 10px; background-color: #f4f4f4; border-top: 1px solid #ddd;'>
								<p style='color: #888; font-size: 12px;'>EcoDelivery, Inc. | Delivering Sustainably | All Rights Reserved</p>
								<p style='color: #888; font-size: 12px;'>Str. G-ral Eremia Grigorescu, Pitești, Argeș, nr. 23</p>
							</div>
						</div>
					");

					if (_userManager.Options.SignIn.RequireConfirmedAccount)
					{
						return RedirectToPage("RegisterConfirmation", new { email = Input.Email, returnUrl = returnUrl });
					}
					else
					{
						await _signInManager.SignInAsync(user, isPersistent: false);
						return LocalRedirect(returnUrl);
					}
				}

				// Mesaje de eroare pt diverse scenarii
				foreach (var error in result.Errors)
				{
					if (error.Code == "PasswordTooShort")
					{
						ModelState.AddModelError(nameof(Input.Password), "The password is too short. It must be at least 6 characters long.");
					}
					else if (error.Code == "PasswordRequiresNonAlphanumeric")
					{
						ModelState.AddModelError(nameof(Input.Password), "The password must contain at least one non-alphanumeric character.");
					}
					else if (error.Code == "PasswordRequiresUpper")
					{
						ModelState.AddModelError(nameof(Input.Password), "The password must contain at least one uppercase letter.");
					}
					else if (error.Code == "PasswordRequiresLower")
					{
						ModelState.AddModelError(nameof(Input.Password), "The password must contain at least one lowercase letter.");
					}
					else
					{
						ModelState.AddModelError(string.Empty, error.Description);
					}
				}
			}

			// Daca am ajuns aici, nu-i bine si reiau
			ModelState.AddModelError(string.Empty, "There was an error with your registration. Please review the form and try again.");
			return Page();
		}


		// Helper pentru popularea dropdown-ului cu judete
		private void PopulateRegions()
		{
			Regions = _dbContext.Regions
				.Select(r => new SelectListItem
				{
					Value = r.Id.ToString(),
					Text = r.County
				})
				.ToList();
		}

		[NonAction]
		public bool IsValidAddressInRomania(string address)
		{
			if (string.IsNullOrWhiteSpace(address))
				return false;

			// Fac split dupa virgule
			var parts = address.Split(',', StringSplitOptions.RemoveEmptyEntries);

			// Daca avem cel putin 3 virgule(4 parti) e ok, daca nu, adresa e invalida
			if (parts.Length < 4)
			{
				return false;
			}

			// Ultima parte tre sa contina "Romania"
			var lastPart = parts[^1].Trim();
			if (!lastPart.Contains("Romania", StringComparison.OrdinalIgnoreCase))
			{
				return false;
			}

			return true;
		}

		private ApplicationUser CreateUser()
		{
			try
			{
				return Activator.CreateInstance<ApplicationUser>();
			}
			catch
			{
				throw new InvalidOperationException($"Can't create an instance of '{nameof(ApplicationUser)}'. Ensure that '{nameof(ApplicationUser)}' is not an abstract class and has a parameterless constructor, or override the register page.");
			}
		}

		private IUserEmailStore<ApplicationUser> GetEmailStore()
		{
			if (!_userManager.SupportsUserEmail)
			{
				throw new NotSupportedException("The default UI requires a user store with email support.");
			}
			return (IUserEmailStore<ApplicationUser>)_userStore;
		}
	}

}

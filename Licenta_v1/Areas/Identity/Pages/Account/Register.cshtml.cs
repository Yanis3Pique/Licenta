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
			[Required(ErrorMessage = "The user name is mandatory.")]
			[Display(Name = "User Name")]
			public string UserName { get; set; }

			[Required(ErrorMessage = "The first name is mandatory")]
			[MaxLength(50, ErrorMessage = "The first name must be maximum 50 characters in length")]
			[MinLength(2, ErrorMessage = "The first name must be minimum 2 characters in length")]
			[Display(Name = "First Name")]
			public string FirstName { get; set; }

			[Required(ErrorMessage = "The last name is mandatory")]
			[StringLength(50, ErrorMessage = "The last name must be maximum 50 characters in length")]
			[MinLength(2, ErrorMessage = "The last name must be minimum 2 characters in length")]
			[Display(Name = "Last Name")]
			public string LastName { get; set; }

			[Required(ErrorMessage = "The email is mandatory.")]
			[EmailAddress(ErrorMessage = "Invalid email address.")]
			[Display(Name = "Email")]
			public string Email { get; set; }

			[Required(ErrorMessage = "The phone number is mandatory.")]
			[Phone(ErrorMessage = "Invalid phone number.")]
			[RegularExpression(@"^(\+4)?(07\d{8}|021\d{7}|02\d{8}|03\d{8})$", ErrorMessage = "Invalid phone number.")]
			[Display(Name = "Phone Number")]
			public string PhoneNumber { get; set; }

			[Required(ErrorMessage = "The region is mandatory")]
			[Display(Name = "Region")]
			public int? RegionId { get; set; }

			[Required]
			[StringLength(50, ErrorMessage = "The {0} must be at least {2} and at max {1} characters long.", MinimumLength = 6)]
			[DataType(DataType.Password)]
			[Display(Name = "Password")]
			public string Password { get; set; }

			[DataType(DataType.Password)]
			[Display(Name = "Confirm password")]
			[Compare("Password", ErrorMessage = "The password and confirmation password do not match.")]
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

			// Reiau judetele din baza de date si le pun in dropdown in caz de eroare
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

				await _userStore.SetUserNameAsync(user, Input.UserName, CancellationToken.None);
				await _emailStore.SetEmailAsync(user, Input.Email, CancellationToken.None);
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

					// Trimit cu API-ul, un email de confirmare
					await _emailSender.SendEmailAsync(Input.Email, "Confirm your email",
						$"Please confirm your account by <a href='{HtmlEncoder.Default.Encode(callbackUrl)}'>clicking here</a>.");

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
				foreach (var error in result.Errors)
				{
					ModelState.AddModelError(string.Empty, error.Description);
				}
			}

			// Daca ajunsei aici, ceva nu a mers bine, reafisez pagina
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

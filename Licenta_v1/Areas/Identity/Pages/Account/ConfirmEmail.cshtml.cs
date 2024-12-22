using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Licenta_v1.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;

namespace Licenta_v1.Areas.Identity.Pages.Account
{
	public class ConfirmEmailModel : PageModel
	{
		private readonly UserManager<ApplicationUser> _userManager;

		public ConfirmEmailModel(UserManager<ApplicationUser> userManager)
		{
			_userManager = userManager;
		}

		[TempData]
		public string StatusMessage { get; set; }

		public async Task<IActionResult> OnGetAsync(string userId, string code)
		{
			if (userId == null || code == null)
			{
				return RedirectToPage("/Index");
			}

			var user = await _userManager.FindByIdAsync(userId);
			if (user == null)
			{
				return NotFound($"Unable to load user with ID '{userId}'.");
			}

			var decodedCode = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(code));
			var result = await _userManager.ConfirmEmailAsync(user, decodedCode);

			if (result.Succeeded)
			{
				// Redirect la pagina de login dupa ce s-a confirmat mail-ul
				return RedirectToPage("/Account/Login", new { area = "Identity" });
			}
			else
			{
				StatusMessage = "Error confirming your email.";
				return Page();
			}
		}
	}
}

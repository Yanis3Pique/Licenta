using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.Extensions.Configuration;
using SendGrid;
using SendGrid.Helpers.Mail;
using System;
using System.Threading.Tasks;

namespace Licenta_v1.Services
{
	public class EmailConfirmationSender : IEmailSender
	{
		private readonly string cheieAPI;
		private readonly string emailSender;
		private readonly string numeSender;

		public EmailConfirmationSender(string apiKey, string senderEmail, string senderName)
		{
			cheieAPI = apiKey;
			emailSender = senderEmail;
			numeSender = senderName;
		}

		public async Task SendEmailAsync(string email, string subject, string htmlMessage)
		{
			var client = new SendGridClient(cheieAPI);
			var from = new EmailAddress(emailSender, numeSender);
			var to = new EmailAddress(email);
			var plainTextContent = "Please confirm your email.";
			var htmlContent = htmlMessage;

			// Folosesc subiectul mail-ului de la parametru
			var msg = MailHelper.CreateSingleEmail(from, to, subject, plainTextContent, htmlContent);
			var response = await client.SendEmailAsync(msg);

			if ((int)response.StatusCode >= 400)
			{
				throw new Exception($"Failed to send email. Status Code: {response.StatusCode}, Body: {await response.Body.ReadAsStringAsync()}");
			}
		}
	}
}

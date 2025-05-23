using DotNetEnv;
using Licenta_v1.Data;
using Licenta_v1.Models;
using Licenta_v1.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.EntityFrameworkCore;
using SendGrid;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHostedService<TaskuriAutomate>();

Env.Load();
var cheie_API_confirmare_email_Sendgrid = Env.GetString("Cheie_API_confirmare_email_SendGrid");
var email_personal = Env.GetString("Email_personal");
var nume_personal = Env.GetString("Nume_personal");
var openRouteServiceApiKey = Env.GetString("OpenRouteServiceApiKey");
var ptvApiKey = Env.GetString("PTV_ApiKey");
var ptvApiKeyReserve = Env.GetString("PTV_ApiKeyReserve");
var ptvApiKeyEmergency = Env.GetString("PTV_ApiKeyEmergency");
var fastApiBaseUrl = Env.GetString("FastApiBaseUrl");

builder.Services.AddHttpClient("MlService", client =>
  {
	// Adjust host/port to whatever uvicorn is bound to:
	client.BaseAddress = new Uri("http://localhost:8000");
	client.Timeout = TimeSpan.FromSeconds(5);
});

builder.Services.AddScoped<RoutePlannerService>();

builder.Services.AddHttpClient("ApiClient", client =>
{
	client.Timeout = TimeSpan.FromSeconds(30); // Set global timeout
});
builder.Services.AddSingleton<OrderDeliveryOptimizer2>(provider =>
{
	var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();
	var httpClientFactory = provider.GetRequiredService<IHttpClientFactory>();
	return new OrderDeliveryOptimizer2(scopeFactory, httpClientFactory, openRouteServiceApiKey, ptvApiKey, ptvApiKeyReserve, ptvApiKeyEmergency);
});

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") ??
	throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
builder.Services.AddDbContext<ApplicationDbContext>(options => options.UseSqlServer(connectionString));
builder.Services.AddDatabaseDeveloperPageExceptionFilter();

builder.Services.AddDefaultIdentity<ApplicationUser>(options => options.SignIn.RequireConfirmedAccount = true)
	.AddRoles<IdentityRole>().AddEntityFrameworkStores<ApplicationDbContext>();
builder.Services.AddControllersWithViews();

builder.Services.AddSingleton<ISendGridClient>(provider =>
	new SendGridClient(cheie_API_confirmare_email_Sendgrid));

builder.Services.AddTransient<IEmailSender, EmailConfirmationSender>(provider =>
    new EmailConfirmationSender(cheie_API_confirmare_email_Sendgrid, email_personal, nume_personal));

builder.Services.AddMemoryCache();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    SeedData.Initialize(services);
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseMigrationsEndPoint();
}
else
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthorization();

app.MapControllers();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");
app.MapRazorPages();

app.Run();

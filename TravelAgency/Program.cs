using Microsoft.EntityFrameworkCore;
using TravelAgency.Models;
using TravelAgency.Services; 

var builder = WebApplication.CreateBuilder(args);

// MVC
builder.Services.AddControllersWithViews();

builder.Services.AddHttpContextAccessor();



// Sessions (required for cart, login temp data, booking progress)
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(60);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});
builder.Services.AddScoped<TravelAgency.Services.NotificationService>();
builder.Services.AddScoped<EmailService>();
builder.Services.AddHostedService<TravelAgency.Services.CartCleanupHostedService>();
// DB Connection
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(connectionString));

// Authentication (Optional now – Needed later)
builder.Services.AddAuthentication();

// Email service will be added later
// builder.Services.AddTransient<IEmailSender, MyEmailSender>();

var app = builder.Build();
QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

// Error handling
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();
// Sessions
app.UseSession();
app.UseMiddleware<TravelAgency.Middleware.BadgeCountsMiddleware>();

// Auth
app.UseAuthentication();
app.UseAuthorization();



// Routing
// 🛑 התיקון: ניתוב ברירת המחדל הוא עכשיו Home/Index (דף הבית הציבורי)
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
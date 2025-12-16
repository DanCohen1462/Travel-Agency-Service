using Microsoft.EntityFrameworkCore;
using TravelAgency.Models;

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

// DB Connection
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(connectionString));

// Authentication (Optional now – Needed later)
builder.Services.AddAuthentication();

// Email service will be added later
// builder.Services.AddTransient<IEmailSender, MyEmailSender>();

var app = builder.Build();

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
// Auth
app.UseAuthentication();
app.UseAuthorization();



// Routing
// 🛑 התיקון: ניתוב ברירת המחדל הוא עכשיו Home/Index (דף הבית הציבורי)
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
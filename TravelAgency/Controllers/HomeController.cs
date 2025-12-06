using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using TravelAgency.Models;

namespace TravelAgency.Controllers
{
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;
        private readonly string _connectionString;

        public HomeController(ILogger<HomeController> logger, IConfiguration config)
        {
            _logger = logger;
            _connectionString = config.GetConnectionString("DefaultConnection");
        }

        public IActionResult Index()
        {
            return View();
        }

        public IActionResult Privacy()
        {
            return View();
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }

        // ---------- Contact Us ----------

        [HttpGet]
        public IActionResult ContactUs()
        {
            var model = new ContactFormViewModel();

            var userIdStr = HttpContext.Session.GetString("UserId");
            if (!string.IsNullOrEmpty(userIdStr))
            {
                using (var conn = new SqlConnection(_connectionString))
                {
                    conn.Open();

                    string sql = @"SELECT Id, firstName, lastName, email, phoneNumber
                                   FROM Users
                                   WHERE Id = @Id AND inactive = 0";

                    using (var cmd = new SqlCommand(sql, conn))
                    {
                        cmd.Parameters.AddWithValue("@Id", int.Parse(userIdStr));

                        using (var reader = cmd.ExecuteReader())
                        {
                            if (reader.Read())
                            {
                                model.UserId = reader.GetInt32(0);
                                var firstName = reader.GetString(1);
                                var lastName = reader.GetString(2);
                                model.FullName = firstName + " " + lastName;
                                model.Email = reader.GetString(3);
                                model.Phone = reader.IsDBNull(4) ? "" : reader.GetString(4);
                            }
                        }
                    }
                }
            }

            return View(model);
        }

        [HttpPost]
        public IActionResult ContactUs(ContactFormViewModel model)
        {
            if (!ModelState.IsValid)
            {
                // מחזיר את הנתונים והודעות השגיאה
                return View(model);
            }

            // כאן בעתיד אפשר להכניס שמירה לטבלת פניות / שליחת מייל
            ViewBag.Success = "Your message has been sent. Thank you!";

            return View(model);
        }

        // ---------- דפים נוספים ----------

        public IActionResult AboutUs()
        {
            return View();
        }

        public IActionResult Cart(int id)
        {
            ViewBag.Id = id; // איזה חבילה נוספה לעגלה
            return View();
        }

        public IActionResult Payment()
        {
            return View();
        }
    }
}

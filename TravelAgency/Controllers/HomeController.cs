using System.Diagnostics;
using System.Text.Json;
using System.Linq;
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
    var vm = new HomeIndexViewModel();

    using (var conn = new SqlConnection(_connectionString))
    {
        conn.Open();

        // 1) Website feedbacks
        string fbSql = @"
            SELECT
                f.Id,
                ISNULL(u.firstName + ' ' + u.lastName, u.Username) as UserFullName,
                ISNULL(f.Description,'') as Description,
                f.Rate
            FROM feedBack1 f
            LEFT JOIN Users u ON u.Id = f.userId
            WHERE f.inactive = 0
              AND f.feedbackType = 'Website'
            ORDER BY f.Id DESC;
        ";

        using (var cmd = new SqlCommand(fbSql, conn))
        using (var r = cmd.ExecuteReader())
        {
            while (r.Read())
            {
                vm.WebsiteFeedbacks.Add(new WebsiteFeedbackVM
                {
                    Id = r.GetInt32(0),
                    UserFullName = r.IsDBNull(1) ? "Anonymous" : r.GetString(1),
                    Description = r.IsDBNull(2) ? "" : r.GetString(2),
                    Rate = r.GetInt32(3)
                });
            }
        }

        vm.WebsiteReviewsCount = vm.WebsiteFeedbacks.Count;
        vm.AvgWebsiteRate = vm.WebsiteReviewsCount == 0 ? 0 : vm.WebsiteFeedbacks.Average(x => x.Rate);

        // 2) Popular destinations Top 4 (לפי SUM(numPersons))
        string popSql = @"
            SELECT TOP (4)
                p.Id as PackageId,
                p.destination,
                ISNULL(p.country,'') as country,
                ISNULL(h.PopularityScore, 0) as PopularityScore,
                ISNULL(img.ImageLocation, '') as ImageLocation
            FROM Package p
            INNER JOIN (
                SELECT
                    PackageId,
                    SUM(ISNULL(numPersons,1)) as PopularityScore
                FROM HistoryReservation
                WHERE inactive = 0
                GROUP BY PackageId
            ) h ON h.PackageId = p.Id
            OUTER APPLY (
                SELECT TOP (1) ImageLocation
                FROM ImagesPackage
                WHERE PackageId = p.Id
                ORDER BY Id
            ) img
            WHERE p.inactive = 0
            ORDER BY h.PopularityScore DESC, p.Id DESC;
        ";

        using (var cmd = new SqlCommand(popSql, conn))
        using (var r = cmd.ExecuteReader())
        {
            while (r.Read())
            {
                string imgLoc = r.IsDBNull(4) ? "" : r.GetString(4);

                vm.PopularDestinations.Add(new PopularDestinationVM
                {
                    PackageId = r.GetInt32(0),
                    Destination = r.GetString(1),
                    Country = r.GetString(2),
                    PopularityScore = r.GetInt32(3),
                    ImageUrl = string.IsNullOrWhiteSpace(imgLoc) ? "/images/default.jpg" : imgLoc
                });
            }
        }
    }

    return View(vm);
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
        

    }
}

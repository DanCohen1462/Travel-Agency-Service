using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using TravelAgency.Models;

namespace TravelAgency.Controllers
{
    public class PackageController : Controller
    {
        private readonly string _connectionString;

        public PackageController(IConfiguration config)
        {
            _connectionString = config.GetConnectionString("DefaultConnection");
        }

        public IActionResult Index()
        {
            return View();
        }

        public IActionResult Gallery()
        {
            var packages = new List<Package>();

            using (SqlConnection conn = new SqlConnection(_connectionString))
            {
                conn.Open();

                string sql = @"
                    SELECT Id, destination, StartDate, EndDate, sum, ageLimit,
                           image, numFreePlaces, idCategory, UserId, information, inactive
                    FROM Package
                    WHERE inactive = 0";

                using (SqlCommand cmd = new SqlCommand(sql, conn))
                using (SqlDataReader reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        var p = new Package
                        {
                            Id = reader.GetInt32(0),
                            destination = reader.GetString(1),
                            StartDate = reader.GetDateTime(2),
                            EndDate = reader.GetDateTime(3),
                            sum = reader.GetInt32(4),
                            ageLimit = reader.GetInt32(5),
                            image = reader.GetString(6),
                            numFreePlaces = reader.GetInt32(7),
                            idCategory = reader.GetInt32(8),
                            UserId = reader.GetInt32(9),
                            information = reader.GetString(10),
                            inactive = reader.GetBoolean(11)
                        };

                        packages.Add(p);
                    }
                }

                string sqlCat = "SELECT Id, name FROM Category WHERE inactive = 0";
                var categories = new List<Category>();

                using (SqlCommand cmdCat = new SqlCommand(sqlCat, conn))
                using (SqlDataReader readerCat = cmdCat.ExecuteReader())
                {
                    while (readerCat.Read())
                    {
                        categories.Add(new Category
                        {
                            Id = readerCat.GetInt32(0),
                            name = readerCat.GetString(1)
                        });
                    }
                }

                ViewBag.Categories = categories;
                ViewBag.Destinations = packages
                    .Select(p => p.destination)
                    .Distinct()
                    .ToList();
            }

            return View(packages);
        }

        // --------- Package Details (NEW) ---------
        public IActionResult PackageDetails(int id)
        {
            Package? pkg = null;
            string categoryName = "";

            using (var conn = new SqlConnection(_connectionString))
            {
                conn.Open();

                string sql = @"
                    SELECT p.Id, p.destination, p.StartDate, p.EndDate, p.sum,
                           p.ageLimit, p.image, p.numFreePlaces, p.idCategory,
                           p.UserId, p.information, p.inactive,
                           c.name AS CategoryName
                    FROM Package p
                    LEFT JOIN Category c ON p.idCategory = c.Id
                    WHERE p.Id = @Id AND p.inactive = 0";

                using (var cmd = new SqlCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@Id", id);

                    using (var reader = cmd.ExecuteReader())
                    {
                        if (!reader.Read())
                        {
                            // לא נמצא – חוזרים לגלריה
                            return RedirectToAction("Gallery");
                        }

                        pkg = new Package
                        {
                            Id = reader.GetInt32(0),
                            destination = reader.GetString(1),
                            StartDate = reader.GetDateTime(2),
                            EndDate = reader.GetDateTime(3),
                            sum = reader.GetInt32(4),
                            ageLimit = reader.GetInt32(5),
                            image = reader.GetString(6),
                            numFreePlaces = reader.GetInt32(7),
                            idCategory = reader.GetInt32(8),
                            UserId = reader.GetInt32(9),
                            information = reader.GetString(10),
                            inactive = reader.GetBoolean(11)
                        };

                        if (!reader.IsDBNull(12))
                            categoryName = reader.GetString(12);
                    }
                }
            }

            var model = new PackageDetailsViewModel
            {
                Package = pkg!,
                CategoryName = categoryName
            };

            ViewData["Title"] = "Package Details";
            return View(model);
        }
    }
}

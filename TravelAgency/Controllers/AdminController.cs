using Microsoft.AspNetCore.Mvc;
using System.Security.Cryptography;
using System.Text;
using TravelAgency.Models;
using Microsoft.Data.SqlClient; // <<< חשוב!

namespace TravelAgency.Controllers;

public class AdminController : Controller
{
    private readonly string _connectionString;
    public AdminController(IConfiguration config)
    {
        _connectionString = config.GetConnectionString("DefaultConnection");
    }

    // GET
    public IActionResult Index()
    {
        return View();
    }
    public IActionResult CreatePackage()
    {
        List<Category> categories = new List<Category>();
        

        using (SqlConnection conn = new SqlConnection(_connectionString))
        {
            conn.Open();

            string query = "SELECT Id, name FROM Category WHERE inactive = 0";

            using (SqlCommand cmd = new SqlCommand(query, conn))
            using (SqlDataReader reader = cmd.ExecuteReader())
            {
                while (reader.Read())
                {
                    categories.Add(new Category
                    {
                        Id = reader.GetInt32(0),
                        name = reader.GetString(1)
                    });
                }
            }
        }

        ViewBag.Categories = categories;
        return View();
    }
    
    [HttpPost]
    public IActionResult CreatePackage(Package model)
    {
        if (!ModelState.IsValid)
            return View(model);

        using (SqlConnection conn = new SqlConnection(_connectionString))
        {
            conn.Open();

            string query = @"INSERT INTO Package
                        (destination, startDate, endDate, sum, ageLimit, image, numFreePlaces, idCategory, UserId, Information)
                        VALUES
                        (@dest, @start, @end, @sum, @age, @image, @free, @cat, 0, @info)";

            using (SqlCommand cmd = new SqlCommand(query, conn))
            {
                cmd.Parameters.AddWithValue("@dest", model.destination);
                cmd.Parameters.AddWithValue("@start", model.StartDate);
                cmd.Parameters.AddWithValue("@end", model.EndDate);
                cmd.Parameters.AddWithValue("@sum", model.sum);
                cmd.Parameters.AddWithValue("@age", model.ageLimit);
                cmd.Parameters.AddWithValue("@image", model.image);
                cmd.Parameters.AddWithValue("@free", model.numFreePlaces);
                cmd.Parameters.AddWithValue("@cat", model.idCategory);
                      // cmd.Parameters.AddWithValue("@user", HttpContext.Session.GetString("UserId"));
                cmd.Parameters.AddWithValue("@info", model.information);

                cmd.ExecuteNonQuery();
            }
        }

        return RedirectToAction("Packages");
    }

    public IActionResult AssignGuide()
    {
        List<User> guides = new List<User>();
        List<Package> unassignedPackages = new List<Package>();

        using (SqlConnection conn = new SqlConnection(_connectionString))
        {
            conn.Open();

            // --- שליפת מדריכים ---
            string queryGuides = "SELECT Id, firstName, lastName FROM Users WHERE inactive = 0 AND type = 2";

            using (SqlCommand cmd = new SqlCommand(queryGuides, conn))
            using (SqlDataReader reader = cmd.ExecuteReader())
            {
                while (reader.Read())
                {
                    guides.Add(new User
                    {
                        Id = reader.GetInt32(0),
                        firstName = reader.GetString(1),
                        lastName = reader.GetString(2),
                        IsAvailable = true
                    });
                }
            }

            // --- שליפת חבילות ללא מדריך ---
            string queryPackages =
                "SELECT Id, destination, StartDate, EndDate FROM Package WHERE inactive = 0 AND (UserId = 0 OR UserId IS NULL)";

            using (SqlCommand cmd = new SqlCommand(queryPackages, conn))
            using (SqlDataReader reader = cmd.ExecuteReader())
            {
                while (reader.Read())
                {
                    unassignedPackages.Add(new Package
                    {
                        Id = reader.GetInt32(0),
                        destination = reader.GetString(1),
                        StartDate = reader.GetDateTime(2),
                        EndDate = reader.GetDateTime(3)
                    });
                }
            }
            conn.Close();
            conn.Open();
            foreach (var guide in guides)
            {
                string conflictQuery = @"
                    SELECT COUNT(*)
                    FROM Package
                    WHERE UserId = @guideId
                    AND (
                            (StartDate <= @newEnd AND EndDate >= @newStart)
                        )";

                foreach (var pkg in unassignedPackages)
                {
                    using (SqlCommand conflictCmd = new SqlCommand(conflictQuery, conn))
                    {
                        conflictCmd.Parameters.AddWithValue("@guideId", guide.Id);
                        conflictCmd.Parameters.AddWithValue("@newStart", pkg.StartDate);
                        conflictCmd.Parameters.AddWithValue("@newEnd", pkg.EndDate);

                        int conflicts = (int)conflictCmd.ExecuteScalar();

                        if (conflicts > 0)
                        {
                            guide.IsAvailable = false;
                        }
                    }
                }
            }
        }

        
   
        AssignGuideViewModel model = new AssignGuideViewModel
        {
            Guides = guides,
            UnassignedPackages = unassignedPackages
        };

        return View(model);
    }
    [HttpPost]
    public IActionResult AssignGuideToPackage(int packageId, int guideId)
    {
        using (SqlConnection conn = new SqlConnection(_connectionString))
        {
            conn.Open();

            string query = "UPDATE Package SET UserId = @guide WHERE Id = @package";

            using (SqlCommand cmd = new SqlCommand(query, conn))
            {
                cmd.Parameters.AddWithValue("@guide", guideId);
                cmd.Parameters.AddWithValue("@package", packageId);

                cmd.ExecuteNonQuery();
            }
        }

        return RedirectToAction("AssignGuide");
    }
    public IActionResult Packages()
    {
        List<Package> packages = new List<Package>();

        using (SqlConnection conn = new SqlConnection(_connectionString))
        {
            conn.Open();

            string query = "SELECT Id, destination, image, StartDate, EndDate, sum FROM Package WHERE inactive = 0";

            using (SqlCommand cmd = new SqlCommand(query, conn))
            using (SqlDataReader reader = cmd.ExecuteReader())
            {
                while (reader.Read())
                {
                    packages.Add(new Package
                    {
                        Id = reader.GetInt32(0),
                        destination = reader.GetString(1),
                        image = reader.GetString(2),
                        StartDate = reader.GetDateTime(3),
                        EndDate = reader.GetDateTime(4),
                        sum = reader.GetInt32(5)
                    });
                }
            }
        }

        return View(packages);
    }
    [HttpPost]
    public IActionResult DeletePackage(int id)
    {
        using (SqlConnection conn = new SqlConnection(_connectionString))
        {
            conn.Open();

            string query = "UPDATE Package SET inactive = 1 WHERE Id = @id";

            using (SqlCommand cmd = new SqlCommand(query, conn))
            {
                cmd.Parameters.AddWithValue("@id", id);
                cmd.ExecuteNonQuery();
            }
        }

        return RedirectToAction("Packages");
    }
    public IActionResult PackageDetails(int id)
    {
        Package package = null;
        User guide = null;
        Category category = null;

        using (SqlConnection conn = new SqlConnection(_connectionString))
        {
            conn.Open();

            string query = @"
                SELECT p.Id, p.destination, p.image, p.StartDate, p.EndDate, p.sum, 
                       p.ageLimit, p.numFreePlaces, p.information, p.UserId, c.name
                FROM Package p
                JOIN Category c ON p.idCategory = c.Id
                WHERE p.Id = @id AND p.inactive = 0";

            using (SqlCommand cmd = new SqlCommand(query, conn))
            {
                cmd.Parameters.AddWithValue("@id", id);

                using (SqlDataReader reader = cmd.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        package = new Package
                        {
                            Id = reader.GetInt32(0),
                            destination = reader.GetString(1),
                            image = reader.GetString(2),
                            StartDate = reader.GetDateTime(3),
                            EndDate = reader.GetDateTime(4),
                            sum = reader.GetInt32(5),
                            ageLimit = reader.GetInt32(6),
                            numFreePlaces = reader.GetInt32(7),
                            information = reader.GetString(8),
                            UserId = reader.GetInt32(9)
                        };

                        category = new Category
                        {
                            name = reader.GetString(10)
                        };
                    }
                }
            }

            // שולף מידע על המדריך
            if (package != null && package.UserId != 0)
            {
                string guideQuery = "SELECT firstName, lastName FROM Users WHERE Id = @uid";

                using (SqlCommand cmd = new SqlCommand(guideQuery, conn))
                {
                    cmd.Parameters.AddWithValue("@uid", package.UserId);

                    using (SqlDataReader reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            guide = new User
                            {
                                firstName = reader.GetString(0),
                                lastName = reader.GetString(1)
                            };
                        }
                    }
                }
            }
        }

        ViewBag.Category = category?.name;
        ViewBag.Guide = guide;
        return View(package);
    }
    public IActionResult EditPackage(int id)
    {
        Package package = null;
        List<Category> categories = new List<Category>();

        using (SqlConnection conn = new SqlConnection(_connectionString))
        {
            conn.Open();

            // --- שליפת חבילה ---
            string query = @"
                SELECT Id, destination, StartDate, EndDate, sum, ageLimit, 
                       numFreePlaces, image, idCategory, information 
                FROM Package 
                WHERE Id = @id AND inactive = 0";

            using (SqlCommand cmd = new SqlCommand(query, conn))
            {
                cmd.Parameters.AddWithValue("@id", id);

                using (SqlDataReader reader = cmd.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        package = new Package
                        {
                            Id = reader.GetInt32(0),
                            destination = reader.GetString(1),
                            StartDate = reader.GetDateTime(2),
                            EndDate = reader.GetDateTime(3),
                            sum = reader.GetInt32(4),
                            ageLimit = reader.GetInt32(5),
                            numFreePlaces = reader.GetInt32(6),
                            image = reader.GetString(7),
                            idCategory = reader.GetInt32(8),
                            information = reader.GetString(9)
                        };
                    }
                }
            }

            // --- שליפת קטגוריות ---
            string queryCategories = "SELECT Id, name FROM Category WHERE inactive = 0";

            using (SqlCommand cmd = new SqlCommand(queryCategories, conn))
            using (SqlDataReader reader = cmd.ExecuteReader())
            {
                while (reader.Read())
                {
                    categories.Add(new Category
                    {
                        Id = reader.GetInt32(0),
                        name = reader.GetString(1)
                    });
                }
            }
        }

        ViewBag.Categories = categories;
        return View(package);
    }
    [HttpPost]
    public IActionResult EditPackage(Package model)
    {
        if (!ModelState.IsValid)
            return View(model);

        using (SqlConnection conn = new SqlConnection(_connectionString))
        {
            conn.Open();

            string query = @"
            UPDATE Package
            SET destination = @dest,
                StartDate = @start,
                EndDate = @end,
                sum = @sum,
                ageLimit = @age,
                numFreePlaces = @free,
                image = @image,
                idCategory = @cat,
                information = @info
            WHERE Id = @id";

            using (SqlCommand cmd = new SqlCommand(query, conn))
            {
                cmd.Parameters.AddWithValue("@dest", model.destination);
                cmd.Parameters.AddWithValue("@start", model.StartDate);
                cmd.Parameters.AddWithValue("@end", model.EndDate);
                cmd.Parameters.AddWithValue("@sum", model.sum);
                cmd.Parameters.AddWithValue("@age", model.ageLimit);
                cmd.Parameters.AddWithValue("@free", model.numFreePlaces);
                cmd.Parameters.AddWithValue("@image", model.image);
                cmd.Parameters.AddWithValue("@cat", model.idCategory);
                cmd.Parameters.AddWithValue("@info", model.information);
                cmd.Parameters.AddWithValue("@id", model.Id);

                cmd.ExecuteNonQuery();
            }
        }

        return RedirectToAction("PackageDetails", new { id = model.Id });
    }




}
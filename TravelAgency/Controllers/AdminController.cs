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
        return View("AdminHome");
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
                        (destination, startDate, endDate, sum, ageLimit, image, numFreePlaces, idCategory, UserId, Information,country)
                        VALUES
                        (@dest, @start, @end, @sum, @age, @image, @free, @cat, 0, @info, @country)";

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
                cmd.Parameters.AddWithValue("@country", model.country ?? (object)DBNull.Value);

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

            string query = @"
                SELECT 
                    p.Id,
                    p.destination,
                    p.image,
                    p.StartDate,
                    p.EndDate,
                    p.sum,
                    (
                        SELECT TOP 1 discountPercent
                        FROM Discount d
                        WHERE d.packageId = p.Id
                        AND GETDATE() BETWEEN d.startDate AND d.endDate
                    ) AS ActiveDiscount,
                    p.country
                FROM Package p
                WHERE p.inactive = 0";


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
                        sum = reader.GetInt32(5),
                        ActiveDiscount = reader.IsDBNull(6) ? 0 : reader.GetInt32(6),
                        country = reader.IsDBNull(7) ? null : reader.GetString(7),
                        

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
                       p.ageLimit, p.numFreePlaces, p.information, p.UserId, c.name,p.country
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
                            UserId = reader.GetInt32(9),
                            country =  reader.IsDBNull(11) ? null : reader.GetString(11),
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
            List<User> registeredUsers = new List<User>();

            string reservationsQuery = @"
            SELECT u.Id, u.firstName, u.lastName, u.email
            FROM HistoryReservation h
            JOIN Users u ON h.userId = u.Id
            WHERE h.packageId = @pid";

            using (SqlCommand cmd = new SqlCommand(reservationsQuery, conn))
            {
                cmd.Parameters.AddWithValue("@pid", id);

                using (SqlDataReader reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        registeredUsers.Add(new User
                        {
                            Id = reader.GetInt32(0),
                            firstName = reader.GetString(1),
                            lastName = reader.GetString(2),
                            email = reader.GetString(3)
                        });
                    }
                }
            }
     
            ViewBag.Registered = registeredUsers;
        
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
                       numFreePlaces, image, idCategory, information,country 
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
                            information = reader.GetString(9),
                            country = reader.IsDBNull(10) ? null : reader.GetString(7),

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
                information = @info,
                country = @country
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
                cmd.Parameters.AddWithValue("@country", model.country );


                cmd.ExecuteNonQuery();
            }
        }

        return RedirectToAction("PackageDetails", new { id = model.Id });
    }
    [HttpPost]
    public IActionResult AddDiscount(int packageId, int discountPercent, DateTime startDate, DateTime endDate)
    {
        using (SqlConnection conn = new SqlConnection(_connectionString))
        {
            conn.Open();

            string query = @"INSERT INTO Discount (packageId, discountPercent, startDate, endDate)
                         VALUES (@pid, @percent, @start, @end)";

            using (SqlCommand cmd = new SqlCommand(query, conn))
            {
                cmd.Parameters.AddWithValue("@pid", packageId);
                cmd.Parameters.AddWithValue("@percent", discountPercent);
                cmd.Parameters.AddWithValue("@start", startDate);
                cmd.Parameters.AddWithValue("@end", endDate);

                cmd.ExecuteNonQuery();
            }
        }

        return RedirectToAction("PackageDetails", new { id = packageId });
    }

    public IActionResult Users()
    {
        List<User> users = new List<User>();

        using (SqlConnection conn = new SqlConnection(_connectionString))
        {
            conn.Open();

            // string query = @"SELECT Id, firstName, lastName, email, phoneNumber, type, inactive 
            //              FROM Users 
            //              ORDER BY Id";
            string query = @"SELECT U.Id, U.firstName, U.lastName, U.email, U.phoneNumber, T.name as type, U.inactive 
                         FROM Users U, types T
                         where T.id=U.[type]
                         ORDER BY Id";

            using (SqlCommand cmd = new SqlCommand(query, conn))
            using (SqlDataReader r = cmd.ExecuteReader())
            {
                while (r.Read())
                {
                    users.Add(new User
                    {
                        Id = r.GetInt32(0),
                        firstName = r.GetString(1),
                        lastName = r.GetString(2),
                        email = r.GetString(3),
                        phoneNumber = r.GetString(4),
                        typeName = r.GetString(5),
                        // inactive = r.GetInt32(6)
                    });
                }
            }
        }

        return View(users);
    }
    
    public IActionResult EditUser(int id)
    {
        User? user = null;

        List<Type1> types = new List<Type1>();

        using (SqlConnection conn = new SqlConnection(_connectionString))
        {
            conn.Open();

            // --- שליפת המשתמש ---
            string q = @"SELECT Id, firstName, lastName, email, phoneNumber, type
                     FROM Users WHERE Id = @id";

            using (SqlCommand cmd = new SqlCommand(q, conn))
            {
                cmd.Parameters.AddWithValue("@id", id);

                using (SqlDataReader r = cmd.ExecuteReader())
                {
                    if (r.Read())
                    {
                        user = new User
                        {
                            Id = r.GetInt32(0),
                            firstName = r.GetString(1),
                            lastName = r.GetString(2),
                            email = r.GetString(3),
                            phoneNumber = r.GetString(4),
                            type = r.GetInt32(5)
                        };
                    }
                }
            }

            // --- שליפת כל סוגי המשתמשים ---
            string q2 = @"SELECT Id, name FROM Types";

            using (SqlCommand cmd = new SqlCommand(q2, conn))
            using (SqlDataReader r = cmd.ExecuteReader())
            {
                while (r.Read())
                {
                    types.Add(new Type1
                    {
                        Id = r.GetInt32(0),
                        name = r.GetString(1),
                       // inActive = r.GetBoolean(2)

                    });
                }
            }
        }

        // שולחים:
        ViewBag.Types = types;

        return View(user);
    }

    [HttpPost]
    public IActionResult EditUser(User model)
    {
        using (SqlConnection conn = new SqlConnection(_connectionString))
        {
            conn.Open();

            string query = @"UPDATE Users
                         SET firstName=@f, lastName=@l, email=@e, phoneNumber=@p,type=@t 
                         WHERE Id = @id";

            using (SqlCommand cmd = new SqlCommand(query, conn))
            {
                cmd.Parameters.AddWithValue("@f", model.firstName);
                cmd.Parameters.AddWithValue("@l", model.lastName);
                cmd.Parameters.AddWithValue("@e", model.email);
                cmd.Parameters.AddWithValue("@p", model.phoneNumber);
                cmd.Parameters.AddWithValue("@t", model.type);
                cmd.Parameters.AddWithValue("@id", model.Id);

                cmd.ExecuteNonQuery();
            }
        }

        return RedirectToAction("Users");
    }

    public IActionResult Analytics()
    {
        Dictionary<string, int> usersByType = new();
        Dictionary<string, int> packagesByCategory = new();
        Dictionary<string, int> reservationsByMonth = new();

        using (SqlConnection conn = new SqlConnection(_connectionString))
        {
            conn.Open();

            // ----- Users by Type -----
            string q1 = @"SELECT T.name, COUNT(*) 
                      FROM Users U
                      JOIN Types T ON U.type = T.Id
                      GROUP BY T.name";

            using (SqlCommand cmd = new SqlCommand(q1, conn))
            using (SqlDataReader r = cmd.ExecuteReader())
            {
                while (r.Read())
                {
                    usersByType[r.GetString(0)] = r.GetInt32(1);
                }
            }

            // ----- Packages by Category -----
            string q2 = @"SELECT C.name, COUNT(*)
                      FROM Package P
                      JOIN Category C ON P.idCategory = C.Id
                      GROUP BY C.name";

            using (SqlCommand cmd = new SqlCommand(q2, conn))
            using (SqlDataReader r = cmd.ExecuteReader())
            {
                while (r.Read())
                {
                    packagesByCategory[r.GetString(0)] = r.GetInt32(1);
                }
            }

            // ----- Reservations per Month -----
            // string q3 = @"
            // SELECT FORMAT(date,'yyyy-MM') AS Month, COUNT(*) 
            // FROM HistoryReservation
            // GROUP BY FORMAT(date,'yyyy-MM')
            // ORDER BY Month";
            //
            // using (SqlCommand cmd = new SqlCommand(q3, conn))
            // using (SqlDataReader r = cmd.ExecuteReader())
            // {
            //     while (r.Read())
            //     {
            //         reservationsByMonth[r.GetString(0)] = r.GetInt32(1);
            //     }
            // }
        }

        ViewBag.UsersByType = usersByType;
        ViewBag.PackagesByCategory = packagesByCategory;
        // ViewBag.ReservationsPerMonth = reservationsByMonth;

        return View();
    }

}
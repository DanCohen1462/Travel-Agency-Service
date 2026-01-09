using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

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
    // public override void OnActionExecuting(ActionExecutingContext context)
    // {
    //     // אם אין סשן משתמש — שלח לדף התחברות
    //     if (HttpContext.Session.GetInt32("UserId") == null)
    //     {
    //         context.Result = RedirectToAction("Login", "Auth");
    //         return;
    //     }
    //
    //     // אם המשתמש לא אדמין – חסום גישה
    //     int userType = HttpContext.Session.GetInt32("UserType") ?? 0;
    //     if (userType != 1) // נניח: 1 = Admin
    //     {
    //         context.Result = RedirectToAction("AccessDenied", "Auth");
    //         return;
    //     }
    //
    //     base.OnActionExecuting(context);
    // }

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
    private List<Category> LoadCategories()
    {
        List<Category> categories = new List<Category>();

        using (SqlConnection conn = new SqlConnection(_connectionString))
        {
            conn.Open();

            string query = "SELECT * FROM Category";

            using (SqlCommand cmd = new SqlCommand(query, conn))
            using (SqlDataReader reader = cmd.ExecuteReader())
            {
                while (reader.Read())
                {
                    categories.Add(new Category
                    {
                        Id = (int)reader["Id"],
                        name = reader["name"].ToString()
                    });
                }
            }
        }

        return categories;
    }

    [HttpPost]
    public IActionResult CreatePackage(Package model, List<IFormFile> images)
    {
        if (!ModelState.IsValid)
        {
            // Load categories again
             List<Category> categories = LoadCategories();
            ViewBag.Categories = categories;

            return View(model);
        }

        int newPackageId = 0;

        using (SqlConnection conn = new SqlConnection(_connectionString))
        {
            conn.Open();

          
            string query = @"
    INSERT INTO Package (destination, startDate, endDate, sum, ageLimit, numFreePlaces, idCategory, UserId, Information, country, cancelationDays)
    OUTPUT INSERTED.Id
    VALUES (@dest, @start, @end, @sum, @age, @free, @cat, 0, @info, @country, @cancellationDay);";


            using (SqlCommand cmd = new SqlCommand(query, conn))
            {
                cmd.Parameters.AddWithValue("@dest", model.destination);
                cmd.Parameters.AddWithValue("@start", model.StartDate);
                cmd.Parameters.AddWithValue("@end", model.EndDate);
                cmd.Parameters.AddWithValue("@sum", model.sum);
                cmd.Parameters.AddWithValue("@age", model.ageLimit);
                cmd.Parameters.AddWithValue("@free", model.numFreePlaces);
                cmd.Parameters.AddWithValue("@cat", model.idCategory);
                cmd.Parameters.AddWithValue("@info", model.information);
                cmd.Parameters.AddWithValue("@country", model.country ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("@cancellationDay", (object?)model.cancelationDays ?? DBNull.Value);
                newPackageId = (int)cmd.ExecuteScalar();
            }
            foreach (var img in images)
            {
                if (img.Length > 0)
                {
                    string fileName = Guid.NewGuid().ToString() + Path.GetExtension(img.FileName);
                    string path = Path.Combine("wwwroot/uploads/packages/", fileName);

                    using (var stream = new FileStream(path, FileMode.Create))
                    {
                        img.CopyTo(stream);
                    }

                    // שמירת נתיב התמונה בטבלה ImagesPackage
                    string q2 = "INSERT INTO ImagesPackage (packageId, imageLocation) VALUES (@pid, @loc)";
                    using (SqlCommand cmd2 = new SqlCommand(q2, conn))
                    {
                        cmd2.Parameters.AddWithValue("@pid", newPackageId);
                        cmd2.Parameters.AddWithValue("@loc", "/uploads/packages/" + fileName);
                        cmd2.ExecuteNonQuery();
                    }
                }
            }
            
            var duplicatedImagePaths = Request.Form["duplicateImgPaths"];

            foreach (var imgPath in duplicatedImagePaths)
            {
                string insert = @"INSERT INTO ImagesPackage (packageId, imageLocation)
                      VALUES (@pid, @loc)";

                using (SqlCommand cmd = new SqlCommand(insert, conn))
                {
                    cmd.Parameters.AddWithValue("@pid", newPackageId);
                    cmd.Parameters.AddWithValue("@loc", imgPath);
                    cmd.ExecuteNonQuery();
                }
            }


            return RedirectToAction("Packages");
        }
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
    public IActionResult Packages(string search)
    {
        List<Package> packages = new List<Package>();

        using (SqlConnection conn = new SqlConnection(_connectionString))
        {
            conn.Open();

            string query = @"
                SELECT 
                    p.Id,
                    p.destination,
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

            if (!string.IsNullOrEmpty(search))
            {
                query += " AND (p.destination LIKE @search OR p.country LIKE @search)";
            }

            using (SqlCommand cmd = new SqlCommand(query, conn))
            {
                if (!string.IsNullOrEmpty(search))
                    cmd.Parameters.AddWithValue("@search", "%" + search + "%");
            

                using (SqlDataReader reader = cmd.ExecuteReader())
                {

                    while (reader.Read())
                    {
                        packages.Add(new Package
                        {
                            Id = reader.GetInt32(0),
                            destination = reader.GetString(1),
                            //image = reader.GetString(2),
                            StartDate = reader.GetDateTime(2),
                            EndDate = reader.GetDateTime(3),
                            sum = reader.GetInt32(4),
                            ActiveDiscount = reader.IsDBNull(5) ? 0 : reader.GetInt32(5),
                            country = reader.IsDBNull(6) ? null : reader.GetString(6),


                        });
                    }
                }
        }

        foreach (var pkg in packages)
            {
                string q2 = @"SELECT TOP 5 imageLocation FROM ImagesPackage WHERE packageId = @pid";

                List<string> imgs = new List<string>();

                using (SqlCommand cmd2 = new SqlCommand(q2, conn))
                {
                    cmd2.Parameters.AddWithValue("@pid", pkg.Id);

                    using (SqlDataReader r2 = cmd2.ExecuteReader())
                    {
                        while (r2.Read())
                        {
                            imgs.Add(r2.GetString(0));
                        }
                    }
                }

                // אם אין תמונות — נשים תמונה ברירת מחדל
                if (imgs.Count == 0)
                {
                    pkg.RandomImage = "/images/default.jpg";  
                }
                else
                {
                    Random rand = new Random();
                    pkg.RandomImage = imgs[rand.Next(imgs.Count)];
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
        Package package = new Package();
        User guide = null;
        Category category = null;

        using (SqlConnection conn = new SqlConnection(_connectionString))
        {
            conn.Open();

            string query = @"
                SELECT p.Id, p.destination, p.StartDate, p.EndDate, p.sum, 
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
                            StartDate = reader.GetDateTime(2),
                            EndDate = reader.GetDateTime(3),
                            sum = reader.GetInt32(4),
                            ageLimit = reader.GetInt32(5),
                            numFreePlaces = reader.GetInt32(6),
                            information = reader.IsDBNull(7) ? null : reader.GetString(7),
                            UserId = reader.GetInt32(8),
                            country = reader.IsDBNull(10) ? null : reader.GetString(10),
                        };


                        category = new Category
                        {
                            name = reader.GetString(9)
                        };
                    }
                }
            }
            List<string> packageImages = new List<string>();

            string imgQuery = @"SELECT imageLocation 
                    FROM ImagesPackage 
                    WHERE packageId = @pid";

            using (SqlCommand cmd = new SqlCommand(imgQuery, conn))
            {
                cmd.Parameters.AddWithValue("@pid", id);

                using (SqlDataReader r = cmd.ExecuteReader())
                {
                    while (r.Read())
                    {
                        packageImages.Add(r.GetString(0));
                    }
                }
            }

            ViewBag.Images = packageImages;
 


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

            int registeredCount = 0;

                    string queryAmountRegister = @"
            SELECT COALESCE(SUM(numPersons), 0)
            FROM HistoryReservation
            WHERE packageId = @pid AND inactive = 0";

                    using (SqlCommand cmd = new SqlCommand(queryAmountRegister, conn))
                    {
                        cmd.Parameters.AddWithValue("@pid", id);

                        object result = cmd.ExecuteScalar();
                        registeredCount = (result == null || result == DBNull.Value) ? 0 : Convert.ToInt32(result);
                    }

        // מחשבים כמה מקומות נשארו
                    int remainingPlaces = package.numFreePlaces - registeredCount;
                    if (remainingPlaces < 0) remainingPlaces = 0;

        // מעבירים ל־ViewBag
                    ViewBag.RegisteredCount = registeredCount;
                    ViewBag.RemainingPlaces = remainingPlaces;
                    
            
            List<waitingList> registeredUsers = new List<waitingList>();

            string reservationsQuery = @"
            SELECT u.Id, u.firstName, u.lastName, u.email,h.numPersons
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
                        registeredUsers.Add(new waitingList
                        {
                            Id = reader.GetInt32(0),
                            firstName = reader.GetString(1),
                            lastName = reader.GetString(2),
                            email = reader.GetString(3),
                            numPersons = reader.GetInt32(4),
                        });
                    }
                }
            }
     
            ViewBag.Registered = registeredUsers;
            
            
            
            
            List<waitingList> waitingList1 = new List<waitingList>();

            string qWait = @"
                SELECT u.Id, u.firstName, u.lastName, u.email, w.numPersons
                FROM WaitingList w
                JOIN Users u ON w.UserId = u.Id
                
                WHERE w.PackageId = @pid and w.inactive = 0 and w.Reason='full'
                ORDER BY w.JoinDate";

            using (SqlCommand cmd = new SqlCommand(qWait, conn))
            {
                cmd.Parameters.AddWithValue("@pid", id);

                using (SqlDataReader r = cmd.ExecuteReader())
                {
                    while (r.Read())
                    {
                        waitingList1.Add(new waitingList
                        {
                            Id = r.GetInt32(0),
                            firstName = r.GetString(1),
                            lastName = r.GetString(2),
                            email = r.GetString(3),
                            numPersons=r.GetInt32(4)
                        });
                    }
                }
            }

            ViewBag.WaitingList = waitingList1;
            List<waitingList> waitingList2 = new List<waitingList>();

            string qWait2 = @"
                SELECT u.Id, u.firstName, u.lastName, u.email, w.numPersons
                FROM WaitingList w
                JOIN Users u ON w.UserId = u.Id
                
                WHERE w.PackageId = @pid and w.inactive = 0 and w.Reason='cart'
                ORDER BY w.JoinDate";
            using (SqlCommand cmd = new SqlCommand(qWait2, conn))
            {
                cmd.Parameters.AddWithValue("@pid", id);

                using (SqlDataReader r = cmd.ExecuteReader())
                {
                    while (r.Read())
                    {
                        waitingList2.Add(new waitingList
                        {
                            Id = r.GetInt32(0),
                            firstName = r.GetString(1),
                            lastName = r.GetString(2),
                            email = r.GetString(3),
                            numPersons=r.GetInt32(4)
                        });
                    }
                }
            }

            ViewBag.WaitingList2 = waitingList2;
        
        }
        

        ViewBag.Category = category?.name;
        ViewBag.Guide = guide;
        return View(package);
    }
    [HttpPost]
    public IActionResult DeleteImage(int imageId, int packageId)
    {
        Console.WriteLine(imageId);

        using (SqlConnection conn = new SqlConnection(_connectionString))
        {
            conn.Open();

            string q = "DELETE FROM ImagesPackage WHERE Id = @id";
            using (SqlCommand cmd = new SqlCommand(q, conn))
            {
                cmd.Parameters.AddWithValue("@id", imageId);
                cmd.ExecuteNonQuery();
            }
        }

        return RedirectToAction("EditPackage", new { id = packageId });
    }
    public IActionResult EditPackage(int id)    
    {
        PackageView model = null;
        List<Category> categories = new List<Category>();
        List<ImagePackage> packageImages = new();

        using (SqlConnection conn = new SqlConnection(_connectionString))
        {
            conn.Open();

            // --- שליפת החבילה ---
            string query = @"
                SELECT Id, destination, StartDate, EndDate, sum, ageLimit, 
                       numFreePlaces, idCategory, information, userid,country,cancelationDays
                FROM Package 
                WHERE Id = @id AND inactive = 0";

            using (SqlCommand cmd = new SqlCommand(query, conn))
            {
                cmd.Parameters.AddWithValue("@id", id);

                using (SqlDataReader reader = cmd.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        model = new PackageView
                        {
                            Id = reader.GetInt32(0),
                            destination = reader.GetString(1),
                            StartDate = reader.GetDateTime(2),
                            EndDate = reader.GetDateTime(3),
                            sum = reader.GetInt32(4),
                            ageLimit = reader.GetInt32(5),
                            numFreePlaces = reader.GetInt32(6),

                            idCategory = reader.GetInt32(7),
                            information = reader.IsDBNull(8) ? null : reader.GetString(8),
                            UserId = reader.IsDBNull(9) ? 0 : reader.GetInt32(9),
                            country = reader.IsDBNull(10) ? null : reader.GetString(10),
                            cancelationDays = reader.IsDBNull(11) ? (int?)null : reader.GetInt32(11),
                        };



                    }
                }
            }
            string queryCategories = @"SELECT Id, name FROM Category WHERE inactive = 0";

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
            // --- שליפת תמונות ---
            string imgQuery = @"SELECT Id, packageId, imageLocation 
                                FROM ImagesPackage 
                                WHERE packageId = @pid";

            using (SqlCommand cmd = new SqlCommand(imgQuery, conn))
            {
                cmd.Parameters.AddWithValue("@pid", id);

                using (SqlDataReader r = cmd.ExecuteReader())
                {
                    while (r.Read())
                    {
                        packageImages.Add(new ImagePackage
                        {
                            Id = r.GetInt32(0),
                            PackageId = r.GetInt32(1),
                            ImageLocation = r.GetString(2)
                        });
                    }
                }
            }

            // --- שליפת קטגוריות ---
            
        }

        ViewBag.Images = packageImages;
        
        ViewBag.Categories = categories;
       

        return View(model);

    }
    
    [HttpPost] 
    public IActionResult EditPackage(PackageView model,List<IFormFile> images)
    {
        
        if (!ModelState.IsValid)
        {   
            Console.WriteLine("❌ MODEL STATE INVALID!");
            foreach (var err in ModelState)
            {
                foreach (var e in err.Value.Errors)
                {
                   
                    Console.WriteLine($"FIELD: {err.Key} → ERROR: {e.ErrorMessage}");
                }
            }
            using (SqlConnection conn = new SqlConnection(_connectionString))
            {
                conn.Open();

                // --- שליפת קטגוריות ---
                List<Category> categories = new List<Category>();
                string qCat = "SELECT Id, name FROM Category WHERE inactive = 0";

                using (SqlCommand cmd = new SqlCommand(qCat, conn))
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

                ViewBag.Categories = categories;

                // --- שליפת תמונות של החבילה ---
                List<ImagePackage> imgs = new List<ImagePackage>();
                string qImg = "SELECT Id, packageId, imageLocation FROM ImagesPackage WHERE packageId = @pid";

                using (SqlCommand cmd = new SqlCommand(qImg, conn))
                {
                    cmd.Parameters.AddWithValue("@pid", model.Id);

                    using (SqlDataReader r = cmd.ExecuteReader())
                    {
                        while (r.Read())
                        {
                            imgs.Add(new ImagePackage
                            {
                                Id = r.GetInt32(0),
                                PackageId = r.GetInt32(1),
                                ImageLocation = r.GetString(2)
                            });
                        }
                    }
                }

                ViewBag.Images = imgs;
            }

            return View(model);
        }

        

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
                idCategory = @cat,
                information = @info,
                country = @country,
                CancelationDays = @CancellationDay
            WHERE Id = @id";
           

            using (SqlCommand cmd = new SqlCommand(query, conn))
            {
                cmd.Parameters.AddWithValue("@dest", model.destination);
                cmd.Parameters.AddWithValue("@start", model.StartDate);
                cmd.Parameters.AddWithValue("@end", model.EndDate);
                cmd.Parameters.AddWithValue("@sum", model.sum);
                cmd.Parameters.AddWithValue("@age", model.ageLimit);
                cmd.Parameters.AddWithValue("@free", model.numFreePlaces);
           
                cmd.Parameters.AddWithValue("@cat", model.idCategory);
                // cmd.Parameters.AddWithValue("@info", model.information);
                cmd.Parameters.AddWithValue("@info", model.information ?? (object)DBNull.Value);

                cmd.Parameters.AddWithValue("@id", model.Id);
                cmd.Parameters.AddWithValue("@country", model.country );
                cmd.Parameters.AddWithValue("@CancellationDay", (object?)model.cancelationDays ?? DBNull.Value);


                cmd.ExecuteNonQuery();
            }
            foreach (var file in images)
            {
                if (file.Length > 0)
                {
                    string fileName = Guid.NewGuid() + Path.GetExtension(file.FileName);
                    string path = Path.Combine("wwwroot/uploads/packages/", fileName);

                    using (var stream = new FileStream(path, FileMode.Create))
                        file.CopyTo(stream);

                    string insertImg = @"INSERT INTO ImagesPackage (packageId, imageLocation)
                             VALUES (@pid, @loc)";

                    using (SqlCommand cmd = new SqlCommand(insertImg, conn))
                    {
                        cmd.Parameters.AddWithValue("@pid", model.Id);
                        cmd.Parameters.AddWithValue("@loc", "/uploads/packages/" + fileName);
                        cmd.ExecuteNonQuery();
                    }
                }
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
    
    
    
    public IActionResult UserTrips(int id) // id = userId
    {
        List<UserTripViewModel> trips = new();

        using (SqlConnection conn = new SqlConnection(_connectionString))
        {
            conn.Open();

            string query = @"
            SELECT 
                h.Id AS ReservationId,
                p.destination,
                p.StartDate,
                p.EndDate,
                h.numPersons,
                h.sum,(
            SELECT TOP 1 imageLocation 
            FROM ImagesPackage 
            WHERE packageId = p.Id
        ) AS ImageUrl
                
            FROM HistoryReservation h
            JOIN Package p ON h.packageId = p.Id
            WHERE h.userId = @uid
            ORDER BY p.StartDate DESC";

            using (SqlCommand cmd = new SqlCommand(query, conn))
            {
                cmd.Parameters.AddWithValue("@uid", id);

                using (SqlDataReader r = cmd.ExecuteReader())
                {
                    while (r.Read())
                    {
                        trips.Add(new UserTripViewModel
                        {
                            ReservationId = r.GetInt32(0),
                            Destination = r.GetString(1),
                            StartDate = r.GetDateTime(2),
                            EndDate = r.GetDateTime(3),
                            NumPersons = r.GetInt32(4),
                            TotalPrice = r.GetInt32(5),
                            ImageUrl = r.IsDBNull(6) ? "/images/default.jpg" : r.GetString(6)
                        });
                    }
                }
            }
        }

        ViewBag.UserId = id;
        return View(trips);
    }
    public IActionResult DuplicatePackage(int id)
    {
        Package package = null;
        List<Category> categories = new();
        List<ImagePackage> images = new();

        using (SqlConnection conn = new SqlConnection(_connectionString))
        {
            conn.Open();

            // --- שליפת החבילה המקורית ---
            string query = @"
                SELECT Id, destination, country, sum, ageLimit, numFreePlaces, 
                       idCategory, information, cancelationDays
                FROM Package
                WHERE Id = @id";

            using (SqlCommand cmd = new SqlCommand(query, conn))
            {
                cmd.Parameters.AddWithValue("@id", id);

                using (SqlDataReader r = cmd.ExecuteReader())
                {
                    if (r.Read())
                    {
                        package = new Package
                        {
                            destination = r.GetString(1),
                            country = r.IsDBNull(2) ? null : r.GetString(2),
                            sum = r.GetInt32(3),
                            ageLimit = r.GetInt32(4),
                            numFreePlaces = r.GetInt32(5),
                            idCategory = r.GetInt32(6),
                            information = r.GetString(7),
                            cancelationDays = r.IsDBNull(8) ? null : r.GetInt32(8),

                            // חשוב – התאריכים לא מועתקים
                            StartDate = DateTime.MinValue,
                            EndDate = DateTime.MinValue
                        };
                    }
                }
            }

            // --- קטגוריות ---
            string qCat = @"SELECT Id, name FROM Category WHERE inactive = 0";
            using (SqlCommand cmd = new SqlCommand(qCat, conn))
            using (SqlDataReader r = cmd.ExecuteReader())
            {
                while (r.Read())
                {
                    categories.Add(new Category { Id = r.GetInt32(0), name = r.GetString(1) });
                }
            }

            // --- שליפת תמונות ---
            string qImg = @"SELECT imageLocation FROM ImagesPackage WHERE packageId = @pid";
            using (SqlCommand cmd = new SqlCommand(qImg, conn))
            {
                cmd.Parameters.AddWithValue("@pid", id);

                using (SqlDataReader r = cmd.ExecuteReader())
                {
                    while (r.Read())
                    {
                        images.Add(new ImagePackage { ImageLocation = r.GetString(0) });
                    }
                }
            }
        }

        ViewBag.Categories = categories;
        ViewBag.Images = images;    // להצגה בטופס ושכפול בהמשך
        ViewBag.Duplicated = true;  // כדי להציג הודעה מיוחדת בטופס

        return View("CreatePackage", package);
    }

    public IActionResult CategoryIndex()
    {
        List<Category> categories = new();

        using (SqlConnection conn = new SqlConnection(_connectionString))
        {
            conn.Open();
            string query = "SELECT Id, name, inactive FROM Category";
            SqlCommand cmd = new SqlCommand(query, conn);
            SqlDataReader reader = cmd.ExecuteReader();

            while (reader.Read())
            {
                categories.Add(new Category
                {
                    Id = reader.GetInt32(0),
                    name = reader.GetString(1),
                    inactive = reader.GetBoolean(2)
                });
            }
        }

        return View(categories);
    }
    public IActionResult CreateCategory()
    {
        return View();
    }
    [HttpPost]
    public IActionResult CreateCategory(Category model)
    {
        if (!ModelState.IsValid)
            return View(model);

        using (SqlConnection conn = new SqlConnection(_connectionString))
        {
            conn.Open();
            string query = "INSERT INTO Category (name, inactive) VALUES (@n, 0)";
            SqlCommand cmd = new SqlCommand(query, conn);

            cmd.Parameters.AddWithValue("@n", model.name);
            

            cmd.ExecuteNonQuery();
        }
    
        return RedirectToAction("CategoryIndex");
    }
    public IActionResult EditCategory(int id)
    {
        Category cat = null;

        using (SqlConnection conn = new SqlConnection(_connectionString))
        {
            conn.Open();
            string query = "SELECT Id, name FROM Category WHERE Id = @id and active = 0";
            SqlCommand cmd = new SqlCommand(query, conn);
            cmd.Parameters.AddWithValue("@id", id);

            SqlDataReader r = cmd.ExecuteReader();

            if (r.Read())
            {
                cat = new Category
                {
                    Id = r.GetInt32(0),
                    name = r.GetString(1),
                    
                };
            }
        }

        return View(cat);
    }
    [HttpPost]
    public IActionResult EditCategory(Category model)
    {
        if (!ModelState.IsValid)
            return View(model);

        using (SqlConnection conn = new SqlConnection(_connectionString))
        {
            conn.Open();
            string query = "UPDATE Category SET name=@n WHERE Id=@id and active = 0";

            SqlCommand cmd = new SqlCommand(query, conn);

            cmd.Parameters.AddWithValue("@id", model.Id);
            cmd.Parameters.AddWithValue("@n", model.name);
            

            cmd.ExecuteNonQuery();
        }

        return RedirectToAction("CategoryIndex");
    }
    [HttpPost]
    public IActionResult DeleteCategory(int id)
    {
        bool hasFuturePackages = false;

        using (SqlConnection conn = new SqlConnection(_connectionString))
        {
            conn.Open();

            string checkQuery = @"
            SELECT COUNT(*) 
            FROM Package 
            WHERE idCategory = @id 
              AND startDate > GETDATE()";

            SqlCommand checkCmd = new SqlCommand(checkQuery, conn);
            checkCmd.Parameters.AddWithValue("@id", id);

            int count = (int)checkCmd.ExecuteScalar();

            hasFuturePackages = count > 0;

            if (hasFuturePackages)
            {
                TempData["Error"] = "Cannot delete category — it has upcoming trips.";
                return RedirectToAction("CategoryIndex");
            }

            // Safe to delete
            string deleteQuery = "UPDATE Category SET inactive=1 WHERE Id=@id and inactive = 0";
            SqlCommand deleteCmd = new SqlCommand(deleteQuery, conn);
            deleteCmd.Parameters.AddWithValue("@id", id);

            deleteCmd.ExecuteNonQuery();
        }

        TempData["Success"] = "Category deleted successfully.";
        return RedirectToAction("CategoryIndex");
    }
    [HttpPost]
    public IActionResult ActiveCategory(int id)
    {
        bool hasFuturePackages = false;

        using (SqlConnection conn = new SqlConnection(_connectionString))
        {
            conn.Open();

         
          
       
            
            // Safe to Active
            string ActiveQuery = "UPDATE Category SET inactive=0 WHERE Id=@id and inactive = 1";
            SqlCommand ActiveCmd = new SqlCommand(ActiveQuery, conn);
            ActiveCmd.Parameters.AddWithValue("@id", id);

            ActiveCmd.ExecuteNonQuery();
        }

        TempData["Success"] = "Category Active successfully.";
        return RedirectToAction("CategoryIndex");
    }
    //Deactivate



}
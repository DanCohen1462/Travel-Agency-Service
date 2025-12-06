using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using TravelAgency.Models;

namespace TravelAgency.Controllers
{
    public class UsersController : Controller
    {
        private readonly string _connectionString;

        public UsersController(IConfiguration config)
        {
            _connectionString = config.GetConnectionString("DefaultConnection");
        }

        // ---------- Profile (GET) ----------
        public IActionResult Profile()
        {
            var userIdStr = HttpContext.Session.GetString("UserId");
            if (string.IsNullOrEmpty(userIdStr))
                return RedirectToAction("Login", "Auth");

            var model = new UserProfileViewModel();

            using (var conn = new SqlConnection(_connectionString))
            {
                conn.Open();

                string sql = @"SELECT Id, Username, firstName, lastName,
                                      birthDate, gender, phoneNumber, email, type
                               FROM Users
                               WHERE Id = @Id AND inactive = 0";

                using (var cmd = new SqlCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@Id", int.Parse(userIdStr));

                    using (var reader = cmd.ExecuteReader())
                    {
                        if (!reader.Read())
                            return RedirectToAction("Login", "Auth");

                        int type = reader.GetInt32(8);

                        // אדמין לא עורך פרופיל – מפנים אותו לדשבורד
                        if (type == 1)
                        {
                            return RedirectToAction("Dashboard", "Admin");
                        }

                        model.Id          = reader.GetInt32(0);
                        model.Username    = reader.GetString(1);
                        model.FirstName   = reader.GetString(2);
                        model.LastName    = reader.GetString(3);
                        model.BirthDate   = reader.IsDBNull(4) ? (DateTime?)null : reader.GetDateTime(4);
                        model.Gender      = reader.IsDBNull(5) ? "" : reader.GetString(5);
                        model.PhoneNumber = reader.IsDBNull(6) ? "" : reader.GetString(6);
                        model.Email       = reader.GetString(7);
                        model.Type        = type;
                    }
                }
            }

            ViewData["Title"] = "Profile";
            return View(model);
        }

        // ---------- Profile (POST) ----------
        [HttpPost]
        public IActionResult Profile(UserProfileViewModel model)
        {
            var userIdStr = HttpContext.Session.GetString("UserId");
            if (string.IsNullOrEmpty(userIdStr))
                return RedirectToAction("Login", "Auth");

            if (!ModelState.IsValid)
            {
                ViewData["Title"] = "Profile";
                return View(model);
            }

            using (var conn = new SqlConnection(_connectionString))
            {
                conn.Open();

                // מעדכנים רק Worker / Customer, לא אדמין
                string sql = @"
                    UPDATE Users
                    SET firstName   = @FirstName,
                        lastName    = @LastName,
                        birthDate   = @BirthDate,
                        gender      = @Gender,
                        phoneNumber = @PhoneNumber,
                        email       = @Email
                    WHERE Id = @Id AND (type = 2 OR type = 3) AND inactive = 0";

                using (var cmd = new SqlCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@FirstName",   model.FirstName);
                    cmd.Parameters.AddWithValue("@LastName",    model.LastName);
                    cmd.Parameters.AddWithValue("@BirthDate",   (object?)model.BirthDate ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@Gender",      (object?)model.Gender ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@PhoneNumber", (object?)model.PhoneNumber ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@Email",       model.Email);
                    cmd.Parameters.AddWithValue("@Id",          model.Id);

                    cmd.ExecuteNonQuery();
                }
            }

            // מעדכן את השם המלא בסשן אם השתנה
            HttpContext.Session.SetString("FullName", model.FirstName + " " + model.LastName);

            ViewBag.Success = "Profile updated successfully.";
            ViewData["Title"] = "Profile";

            return View(model);
        }

        // ---------- MyTrips ----------
        public IActionResult MyTrips()
        {
            if (HttpContext.Session.GetString("UserId") == null)
                return RedirectToAction("Login", "Auth");

            // בהמשך תטעני כאן את הטיולים של המשתמש מהדאטהבייס
            return View();
        }
    }
}

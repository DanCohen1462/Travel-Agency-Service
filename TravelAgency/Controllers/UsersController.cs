using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using TravelAgency.Models;
using Microsoft.Extensions.Configuration; 
using Microsoft.AspNetCore.Http; 
using System;
using System.Collections.Generic;

namespace TravelAgency.Controllers
{
    public class UsersController : Controller
    {
        private readonly string _connectionString;

        public UsersController(IConfiguration config)
        {
            _connectionString = config.GetConnectionString("DefaultConnection")
                                ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
        }

        // ---------- Dashboard (GET) - דף נחיתה ללקוח רגיל (המלבנים) ----------
        [HttpGet]
        public IActionResult Dashboard()
        {
            var userIdStr = HttpContext.Session.GetString("UserId");
            if (string.IsNullOrEmpty(userIdStr))
            {
                // אם אין סשן, הפנה להתחברות
                return RedirectToAction("Login", "Auth");
            }

            // קריאה ל-DB כדי לקבל את סוג המשתמש (למקרה שהסשן לא עדכני)
            int userType = 0;
            using (var conn = new SqlConnection(_connectionString))
            {
                conn.Open();
                string sql = "SELECT type FROM Users WHERE Id = @Id AND inactive = 0";

                using (var cmd = new SqlCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@Id", int.Parse(userIdStr));
                    var result = cmd.ExecuteScalar();
                    if (result != null)
                    {
                        userType = (int)result;
                    }
                }
            }

            // אם אדמין (type=1), שלח אותו ללוח הבקרה של אדמין
            if (userType == 1)
            {
                return RedirectToAction("Dashboard", "Admin");
            }
            
            // אם עובד (type=2), שלח אותו ללוח הבקרה של עובד
            if (userType == 2)
            {
                return RedirectToAction("Panel", "Worker");
            }

            // אם לקוח (type=3), נציג את דף המלבנים
            ViewData["Title"] = "Customer Dashboard";
            HttpContext.Session.SetString("UserType", userType.ToString()); 

            return View();
        }
        
        // ---------- Profile (GET) ----------
        [HttpGet]
        public IActionResult Profile()
        {
            var userIdStr = HttpContext.Session.GetString("UserId");
            if (string.IsNullOrEmpty(userIdStr))
                return RedirectToAction("Login", "Auth");

            var model = new UserProfileViewModel(); // בהנחה ש-UserProfileViewModel מוגדר

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
        [ValidateAntiForgeryToken]
        public IActionResult Profile(UserProfileViewModel model)
        {
            var userIdStr = HttpContext.Session.GetString("UserId");
            if (string.IsNullOrEmpty(userIdStr))
                return RedirectToAction("Login", "Auth");

            var userId = int.Parse(userIdStr);
            model.Id = userId;

            if (!ModelState.IsValid)
            {
                ViewData["Title"] = "Profile";
                return View(model);
            }

            using (var conn = new SqlConnection(_connectionString))
            {
                conn.Open();

                string checkSql = @"SELECT COUNT(*) FROM Users
                                    WHERE Username = @Username AND Id <> @Id AND inactive = 0";

                using (var checkCmd = new SqlCommand(checkSql, conn))
                {
                    checkCmd.Parameters.AddWithValue("@Username", model.Username);
                    checkCmd.Parameters.AddWithValue("@Id", userId);

                    int exists = (int)checkCmd.ExecuteScalar();
                    if (exists > 0)
                    {
                        ModelState.AddModelError("Username", "Username already exists");
                        ViewData["Title"] = "Profile";
                        return View(model);
                    }
                }

                string sql = @"
                    UPDATE Users
                    SET Username   = @Username,
                        firstName  = @FirstName,
                        lastName   = @LastName,
                        birthDate  = @BirthDate,
                        gender     = @Gender,
                        phoneNumber= @PhoneNumber,
                        email      = @Email
                    WHERE Id = @Id AND (type = 2 OR type = 3) AND inactive = 0";

                using (var cmd = new SqlCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@Username",   model.Username);
                    cmd.Parameters.AddWithValue("@FirstName",  model.FirstName);
                    cmd.Parameters.AddWithValue("@LastName",   model.LastName);
                    cmd.Parameters.AddWithValue("@BirthDate",  (object?)model.BirthDate ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@Gender",     (object?)model.Gender ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@PhoneNumber", (object?)model.PhoneNumber ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@Email",      model.Email);
                    cmd.Parameters.AddWithValue("@Id",         userId);

                    cmd.ExecuteNonQuery();
                }
            }

            HttpContext.Session.SetString("Username", model.Username);
            HttpContext.Session.SetString("FullName", model.FirstName + " " + model.LastName);

            ViewBag.Success = "Profile updated successfully.";
            ViewData["Title"] = "Profile";

            return View(model);
        }

        // ---------- MyTrips ----------
        public IActionResult MyTrips()
        {
            // 1. בדיקת התחברות
            var userIdStr = HttpContext.Session.GetString("UserId");
            if (string.IsNullOrEmpty(userIdStr))
                return RedirectToAction("Login", "Auth");

            int userId = int.Parse(userIdStr);
            List<UserTripViewModel> myTrips = new List<UserTripViewModel>();

            using (var conn = new SqlConnection(_connectionString))
            {
                conn.Open();

                // שאילתה שמחברת בין טבלת ההזמנות (h) לטבלת החבילות (p)
                // ולוקחת את המחיר וכמות האנשים מההזמנה, ואת היעד והתאריכים מהחבילה
                string sql = @"
                    SELECT 
                        h.Id AS ReservationId,
                        h.numPersons,
                        h.sum,
                        p.destination,
                        p.startDate,
                        p.endDate
                    FROM HistoryReservation h
                    INNER JOIN Package p ON h.PackageId = p.Id
                    WHERE h.UserId = @UserId AND h.inactive = 0";

                using (var cmd = new SqlCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@UserId", userId);

                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            var trip = new UserTripViewModel();

                            // מילוי הנתונים מהדאטה-בייס
                            trip.ReservationId = reader.GetInt32(reader.GetOrdinal("ReservationId"));
                            
                            // בדיקת NULLים ליתר ביטחון
                            trip.NumPersons = reader.IsDBNull(reader.GetOrdinal("numPersons")) ? 1 : reader.GetInt32(reader.GetOrdinal("numPersons"));
                            trip.TotalPrice = reader.IsDBNull(reader.GetOrdinal("sum")) ? 0 : reader.GetInt32(reader.GetOrdinal("sum"));
                            
                            trip.Destination = reader.GetString(reader.GetOrdinal("destination"));
                            trip.StartDate = reader.GetDateTime(reader.GetOrdinal("startDate"));
                            trip.EndDate = reader.GetDateTime(reader.GetOrdinal("endDate"));

                            // תמונה דיפולטיבית (כי אין בטבלה הזו תמונה)
                            trip.ImageUrl = "/images/default.jpg";

                            myTrips.Add(trip);
                        }
                    }
                }
            }

            ViewData["Title"] = "MyTrips";
            return View();
        }
    }
}
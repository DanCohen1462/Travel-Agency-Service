using Microsoft.AspNetCore.Mvc;
using TravelAgency.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration; 
using Microsoft.AspNetCore.Http; 
using System; 
// ... (usings נוספים אם ישנם)

public class AuthController : Controller
{
    private readonly string _connectionString;

    public AuthController(IConfiguration config)
    {
        _connectionString = config.GetConnectionString("DefaultConnection")
                            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
    }

    // ---------- Register (GET) ----------
    public IActionResult Register() => View();

    // ---------- Register (POST) ----------
    [HttpPost]
    public IActionResult Register(User model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        using (SqlConnection conn = new SqlConnection(_connectionString))
        {
            conn.Open();

            string checkQuery = "SELECT COUNT(*) FROM Users WHERE Username = @Username";
            using (SqlCommand cmd = new SqlCommand(checkQuery, conn))
            {
                cmd.Parameters.AddWithValue("@Username", model.Username);

                int exists = (int)cmd.ExecuteScalar();
                if (exists > 0)
                {
                    ModelState.AddModelError("Username", "Username already exists");
                    return View(model);
                }
            }

            string insertQuery = @"INSERT INTO Users 
            (Username, firstName, lastName, birthDate, gender, phoneNumber, email, Password, type)
            VALUES (@Username, @firstName, @lastName, @birthDate, @gender, @phoneNumber, @email, @Password, 3)";

            using (SqlCommand cmd = new SqlCommand(insertQuery, conn))
            {
                cmd.Parameters.AddWithValue("@Username", model.Username);
                cmd.Parameters.AddWithValue("@firstName", model.firstName);
                cmd.Parameters.AddWithValue("@lastName", model.lastName);
                cmd.Parameters.AddWithValue("@birthDate", (object?)model.birthDate ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@gender", (object?)model.gender ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@phoneNumber", (object?)model.phoneNumber ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@email", model.email);
                cmd.Parameters.AddWithValue("@Password", model.Password);

                cmd.ExecuteNonQuery();
            }
        }

        return RedirectToAction("Login");
    }

    // ---------- Login (GET) ----------
    public IActionResult Login() => View();

    // ---------- Login (POST) ----------
    [HttpPost]
    public IActionResult Login(string username, string password)
    {
        using (SqlConnection conn = new SqlConnection(_connectionString))
        {
            conn.Open();

            string query =
                "SELECT Id, type, firstName, lastName FROM Users WHERE Username = @Username AND Password = @Password AND inactive = 0";

            using (SqlCommand cmd = new SqlCommand(query, conn))
            {
                cmd.Parameters.AddWithValue("@Username", username);
                cmd.Parameters.AddWithValue("@Password", password);

                using (SqlDataReader reader = cmd.ExecuteReader())
                {
                    if (!reader.Read())
                    {
                        ViewBag.Error = "Invalid username or password";
                        return View();
                    }

                    int userId = reader.GetInt32(0);
                    int userType = reader.GetInt32(1);
                    string firstName = reader.GetString(2);
                    string lastName = reader.GetString(3);

                    HttpContext.Session.SetString("UserId", userId.ToString());
                    HttpContext.Session.SetString("Username", username);
                    HttpContext.Session.SetString("FullName", firstName + " " + lastName);
                    HttpContext.Session.SetString("UserType", userType.ToString());

                    if (userType == 1) // Admin
                        return RedirectToAction("index", "Admin");

                    if (userType == 2) // Worker
                        return RedirectToAction("Panel", "Worker");

                    // 🛑 התיקון: הפניה ל-Dashboard המלבנים במקום לגלריה
                    // Customer (Type 3)
                    return RedirectToAction("Dashboard", "Users");
                }
            }
        }
    }

    // ---------- Change Password ----------
    [HttpGet]
    public IActionResult ChangePassword()
    {
        var userIdStr = HttpContext.Session.GetString("UserId");
        if (string.IsNullOrEmpty(userIdStr))
            return RedirectToAction("Login");

        return View(new ChangePasswordViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult ChangePassword(ChangePasswordViewModel model)
    {
        var userIdStr = HttpContext.Session.GetString("UserId");
        if (string.IsNullOrEmpty(userIdStr))
            return RedirectToAction("Login");

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        int userId = int.Parse(userIdStr);

        using (SqlConnection conn = new SqlConnection(_connectionString))
        {
            conn.Open();

            // בודקים שהסיסמה הנוכחית נכונה
            string checkSql = "SELECT Password FROM Users WHERE Id = @Id AND inactive = 0";
            string currentPasswordFromDb = null;

            using (var cmd = new SqlCommand(checkSql, conn))
            {
                cmd.Parameters.AddWithValue("@Id", userId);
                var result = cmd.ExecuteScalar();
                currentPasswordFromDb = result as string;
            }

            if (currentPasswordFromDb == null || currentPasswordFromDb != model.CurrentPassword)
            {
                ModelState.AddModelError("CurrentPassword", "Current password is incorrect");
                return View(model);
            }

            // מעדכנים לסיסמה החדשה
            string updateSql = "UPDATE Users SET Password = @NewPassword WHERE Id = @Id AND inactive = 0";
            using (var cmd = new SqlCommand(updateSql, conn))
            {
                cmd.Parameters.AddWithValue("@NewPassword", model.NewPassword);
                cmd.Parameters.AddWithValue("@Id", userId);
                cmd.ExecuteNonQuery();
            }
        }

        ViewBag.Success = "Password changed successfully.";
        return View(new ChangePasswordViewModel());
    }

    // ---------- Logout ----------
    public IActionResult Logout()
    {
        HttpContext.Session.Clear();
        return RedirectToAction("Index", "Home");
    }
}
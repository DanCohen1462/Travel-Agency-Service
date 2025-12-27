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

    private void RefreshNotifCount(int userId)
    {
        using var conn = new SqlConnection(_connectionString);
        conn.Open();

        using var cmd = new SqlCommand(@"
       SELECT COUNT(*)
        FROM Notifications
        WHERE UserId = @uid
          AND IsRead = 0
          AND inactive = 0;
        -- AND inactive = 0  -- אם יש עמודה inactive ב-Notifications תשאירי, אם אין - למחוק
    ", conn);

        cmd.Parameters.AddWithValue("@uid", userId);

        int count = (int)cmd.ExecuteScalar();
        HttpContext.Session.SetInt32("NotifCount", count);
    }

    // ---------- Register (GET) ----------
    public IActionResult Register() => View();

    // ---------- Register (POST) ----------
    [HttpPost]
    public IActionResult Register(UserView model)
    {
        if (!ModelState.IsValid)
        {
            foreach (var kvp in ModelState)
            {
                foreach (var error in kvp.Value.Errors)
                {
                    Console.WriteLine($"❌ FIELD: {kvp.Key} → ERROR: {error.ErrorMessage}");
                }
            }
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

bool createdWelcomeNow = false;

using (var conn2 = new SqlConnection(_connectionString))
{
    conn2.Open();


    using (var checkCmd = new SqlCommand(@"
        SELECT COUNT(*)
        FROM dbo.Notifications
        WHERE UserId = @uid
          AND inactive = 0
          AND Title = 'Welcome!';", conn2))
    {
        checkCmd.Parameters.AddWithValue("@uid", userId);
        int hasWelcome = (int)checkCmd.ExecuteScalar();

        if (hasWelcome == 0)
        {
            using (var insCmd = new SqlCommand(@"
                INSERT INTO dbo.Notifications
                    (UserId, Title, Message, Type, LinkUrl, IsRead, CreatedAt, inactive)
                VALUES
                    (@uid, 'Welcome!', @msg, 'success', '/Users/Dashboard', 0, GETDATE(), 0);", conn2))
            {
                insCmd.Parameters.AddWithValue("@uid", userId);
                insCmd.Parameters.AddWithValue("@msg", $"Welcome {firstName}! Enjoy your next adventure ✈️");
                insCmd.ExecuteNonQuery();
                createdWelcomeNow = true;
            }
        }
    }

    // ✅ refresh unread badge once
    using (var countCmd = new SqlCommand(@"
        SELECT COUNT(*)
        FROM dbo.Notifications
        WHERE UserId = @uid
          AND inactive = 0
          AND IsRead = 0;", conn2))
    {
        countCmd.Parameters.AddWithValue("@uid", userId);
        int notifCount = (int)countCmd.ExecuteScalar();
        HttpContext.Session.SetInt32("NotifCount", notifCount);
    }
}

// ✅ show toast only if we created welcome now
if (createdWelcomeNow)
{
    TempData["WelcomeToast"] = $"Hello {firstName}! Welcome to TravelAgency ✨";
}

if (userType == 1) // Admin
    return RedirectToAction("index", "Admin");

if (userType == 2) // Worker
    return RedirectToAction("EmployeeDashboard", "Employee");

return RedirectToAction("Dashboard", "Users");

                }
            }
        }
    }

 
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

            //update new password
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
    
    
    
    public IActionResult AccessDenied()
    {
        return View();
    }

    // ---------- Logout ----------
    public IActionResult Logout()
    {
        HttpContext.Session.Clear();
        return RedirectToAction("Index", "Home");
    }
}
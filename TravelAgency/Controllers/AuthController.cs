
using Microsoft.AspNetCore.Mvc;
using System.Security.Cryptography;
using System.Text;
using TravelAgency.Models;
using Microsoft.Data.SqlClient; // <<< חשוב!

public class AuthController : Controller
{
    private readonly string _connectionString;

    public AuthController(IConfiguration config)
    {
        _connectionString = config.GetConnectionString("DefaultConnection");
    }



    public IActionResult Register() => View();

    [HttpPost]
    public IActionResult Register(User model)
    {
        if (!ModelState.IsValid)
            return View(model);

        using (SqlConnection conn = new SqlConnection(_connectionString))
        {
            conn.Open();

            // בדיקה אם המשתמש קיים
            string checkQuery = "SELECT COUNT(*) FROM Users WHERE Username = @Username";
            using (SqlCommand cmd = new SqlCommand(checkQuery, conn))
            {
                cmd.Parameters.AddWithValue("@Username", model.Username);

                int exists = (int)cmd.ExecuteScalar();
                if (exists > 0)
                {
                    ModelState.AddModelError("", "Username already exists");
                    return View(model);
                }
            }

            // הצפנת סיסמה
        

            // הכנסת המשתמש
            string insertQuery =
                "INSERT INTO Users (Username, Password) VALUES (@Username, @Password)";

            using (SqlCommand cmd = new SqlCommand(insertQuery, conn))
            {
                cmd.Parameters.AddWithValue("@Username", model.Username);
                cmd.Parameters.AddWithValue("@Password", model.Password);

                cmd.ExecuteNonQuery();
            }
        }

        return RedirectToAction("Login");
    }

    public IActionResult Login() => View();

    [HttpPost]
    public IActionResult Login(string username, string password)
    {
       // string hashed = HashPassword(password);

        using (SqlConnection conn = new SqlConnection(_connectionString))
        {
            conn.Open();

            string query =
                "SELECT Id FROM Users WHERE Username = @Username AND Password = @Password";

            using (SqlCommand cmd = new SqlCommand(query, conn))
            {
                cmd.Parameters.AddWithValue("@Username", username);
                cmd.Parameters.AddWithValue("@Password", password);

                var result = cmd.ExecuteScalar();

                if (result == null)
                {
                    ViewBag.Error = "Invalid username or password";
                    return View();
                }

                HttpContext.Session.SetString("UserId", result.ToString());
                HttpContext.Session.SetString("Username", username);
            }
        }

        return RedirectToAction("Index", "Home");
    }

    public IActionResult Logout()
    {
        HttpContext.Session.Clear();
        return RedirectToAction("Login");
    }
}

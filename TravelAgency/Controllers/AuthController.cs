
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
        {
            return View(model);
        }

        // חיפוש אם המשתמש קיים
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
            (Username, firstName, lastName, birthDate, gender, phoneNumber, email, Password,type)
            VALUES (@Username, @firstName, @lastName, @birthDate, @gender, @phoneNumber, @email, @Password,1)";

            using (SqlCommand cmd = new SqlCommand(insertQuery, conn))
            {
                cmd.Parameters.AddWithValue("@Username", model.Username);
                cmd.Parameters.AddWithValue("@firstName", model.firstName);
                cmd.Parameters.AddWithValue("@lastName", model.lastName);
                cmd.Parameters.AddWithValue("@birthDate", model.birthDate);
                cmd.Parameters.AddWithValue("@gender", model.gender);
                cmd.Parameters.AddWithValue("@phoneNumber", model.phoneNumber);
                cmd.Parameters.AddWithValue("@email", model.email);
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
        using (SqlConnection conn = new SqlConnection(_connectionString))
        {
            conn.Open();

            string query = 
                "SELECT Id, type FROM Users WHERE Username = @Username AND Password = @Password";

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

                    HttpContext.Session.SetString("UserId", userId.ToString());
                    HttpContext.Session.SetString("Username", username);
                    HttpContext.Session.SetString("UserType", userType.ToString());

                    
                    if (userType == 1) // 1 = Admin
                        return RedirectToAction("Dashboard", "Admin");

                    if (userType == 2) // 2 = Worker
                        return RedirectToAction("Panel", "Worker");

                    // 3 = Customer
                    return RedirectToAction("Index", "Home");
                }
            }
        }
    }


    public IActionResult Logout()
    {
        HttpContext.Session.Clear();
        return RedirectToAction("Login");
    }
}

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
                        (@dest, @start, @end, @sum, @age, @image, @free, @cat, @user, @info)";

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
                cmd.Parameters.AddWithValue("@user", HttpContext.Session.GetString("UserId"));
                cmd.Parameters.AddWithValue("@info", model.information);

                cmd.ExecuteNonQuery();
            }
        }

        return RedirectToAction("Packages");
    }


}
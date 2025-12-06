using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using TravelAgency.Models;

namespace TravelAgency.Controllers;

public class PackageController : Controller
{
    private readonly string _connectionString;

    public PackageController(IConfiguration config)
    {
        _connectionString = config.GetConnectionString("DefaultConnection");
    }
    // GET
    public IActionResult Index()
    {
        return View();
    }
   public IActionResult Gallery()
{
    var packages = new List<Package>();

    using (SqlConnection conn = new SqlConnection(_connectionString))
    {
        conn.Open();

        string sql = @"
            SELECT Id, destination, StartDate, EndDate, sum, ageLimit,
                   image, numFreePlaces, idCategory, UserId, information, inactive
            FROM Package
            WHERE inactive = 0";

        using (SqlCommand cmd = new SqlCommand(sql, conn))
        using (SqlDataReader reader = cmd.ExecuteReader())
        {
            while (reader.Read())
            {
                var p = new Package
                {
                    Id = reader.GetInt32(0),
                    destination = reader.GetString(1),
                    StartDate = reader.GetDateTime(2),
                    EndDate = reader.GetDateTime(3),
                    sum = reader.GetInt32(4),
                    ageLimit = reader.GetInt32(5),
                    image = reader.GetString(6),
                    numFreePlaces = reader.GetInt32(7),
                    idCategory = reader.GetInt32(8),
                    UserId = reader.GetInt32(9),
                    information = reader.GetString(10),
                    inactive = reader.GetBoolean(11)
                };

                packages.Add(p);
            }
        }

        string sqlCat = "SELECT Id, name FROM Category WHERE inactive = 0";
        var categories = new List<Category>();

        using (SqlCommand cmdCat = new SqlCommand(sqlCat, conn))
        using (SqlDataReader readerCat = cmdCat.ExecuteReader())
        {
            while (readerCat.Read())
            {
                categories.Add(new Category
                {
                    Id = readerCat.GetInt32(0),
                    name = readerCat.GetString(1)
                });
            }
        }

        ViewBag.Categories = categories;
        ViewBag.Destinations = packages
            .Select(p => p.destination)
            .Distinct()
            .ToList();
    }

    return View(packages);
}


    public IActionResult PackageDetails(int id)
    {
        ViewBag.Id = id;
        return View();
    }
}
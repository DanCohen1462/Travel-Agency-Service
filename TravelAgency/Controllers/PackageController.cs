using Microsoft.AspNetCore.Mvc;

namespace TravelAgency.Controllers;

public class PackageController : Controller
{
    // GET
    public IActionResult Index()
    {
        return View();
    }
    public IActionResult Gallery(string destination)
    {
        return View();
    }
    public IActionResult PackageDetails(int id)
    {
        ViewBag.Id = id;
        return View();
    }
}
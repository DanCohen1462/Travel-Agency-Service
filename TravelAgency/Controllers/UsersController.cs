using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;

public class UsersController : Controller
{
    public IActionResult Profile()
    {
        if (HttpContext.Session.GetString("UserId") == null)
            return RedirectToAction("Login", "Auth");

        ViewBag.Username = HttpContext.Session.GetString("Username");
        return View();
    }

    public IActionResult MyTrips()
    {
        if (HttpContext.Session.GetString("UserId") == null)
            return RedirectToAction("Login", "Auth");

        // בהמשך נטען כאן מהדאטהבייס את הטיולים של המשתמש
        return View();
    }
}
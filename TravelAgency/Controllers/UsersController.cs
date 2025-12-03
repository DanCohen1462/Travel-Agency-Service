using Microsoft.AspNetCore.Mvc;

public class UsersController : Controller
{
    public IActionResult Profile()
    {
        // ודא שהמשתמש מחובר
        if (HttpContext.Session.GetString("UserId") == null)
            return RedirectToAction("Login", "Auth");

        ViewBag.Username = HttpContext.Session.GetString("Username");
        return View();
    }
}
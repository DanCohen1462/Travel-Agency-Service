using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;

namespace TravelAgency.Controllers
{
    public class EmployeeController : Controller
    {
        // פעולה שמציגה את דף הבית של העובד
        public IActionResult EmployeeHome()
        {
            // בדיקת אבטחה קטנה: רק עובד יכול לראות את הדף הזה
            var role = HttpContext.Session.GetString("Role");
            if (role != "Employee")
            {
                return RedirectToAction("Index", "Home");
            }

            return View();
        }
    }
}
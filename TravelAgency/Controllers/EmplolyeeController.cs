using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;

namespace TravelAgency.Controllers
{
    public class EmployeeController : Controller
    {
        // ... שאר הקוד בקונטרולר ...

        // הפעולה שמציגה את דף הבית של העובד
        public IActionResult EmployeeDashboard()
        {
            return View();
        }
    }
}
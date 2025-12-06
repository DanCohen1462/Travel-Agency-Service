using Microsoft.AspNetCore.Mvc;

namespace TravelAgency.Controllers
{
    public class NotificationsController : Controller
    {
        public IActionResult Index()
        {
            return View();
        }
    }
}
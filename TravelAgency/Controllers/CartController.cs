using Microsoft.AspNetCore.Mvc;

namespace TravelAgency.Controllers
{
    public class CartController : Controller
    {
        public IActionResult Index()
        {
            // בהמשך תהיה כאן רשימת חבילות בעגלה, כפתורי הסרה וכו'
            return View();
        }
    }
}
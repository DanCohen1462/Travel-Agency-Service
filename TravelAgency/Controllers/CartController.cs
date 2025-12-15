using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using TravelAgency.Models;

namespace TravelAgency.Controllers
{
    public class CartController : Controller
    {
        private readonly string _connectionString;

        public CartController(IConfiguration config)
        {
            _connectionString = config.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
        }

        private int? GetUserId()
        {
            var userIdStr = HttpContext.Session.GetString("UserId");
            if (int.TryParse(userIdStr, out int id)) return id;
            return null;
        }

        // ✅ CartCount = מספר פריטים פעילים בעגלה (לא אנשים)
        private void RefreshCartCount(int userId)
        {
            using var conn = new SqlConnection(_connectionString);
            conn.Open();

            using var cmd = new SqlCommand(@"
                SELECT COUNT(*)
                FROM shoppingcart
                WHERE userId = @uid AND inactive = 0;", conn);

            cmd.Parameters.AddWithValue("@uid", userId);

            int count = (int)cmd.ExecuteScalar();
            HttpContext.Session.SetInt32("CartCount", count);
        }

        private int GetCartTotal(int userId)
        {
            using var conn = new SqlConnection(_connectionString);
            conn.Open();

            using var cmd = new SqlCommand(@"
                SELECT ISNULL(SUM(sum),0)
                FROM shoppingcart
                WHERE userId=@uid AND inactive=0;", conn);

            cmd.Parameters.AddWithValue("@uid", userId);
            return (int)cmd.ExecuteScalar();
        }

        // ---------------------------------------------------------
        // BOOK TRIP: מוסיף לעגלה ונשאר באותו עמוד (Details/Gallery)
        // ---------------------------------------------------------
        [HttpPost]
        public IActionResult Add(int packageId, int adults = 1, int children = 0)
        {
            var userId = GetUserId();
            if (!userId.HasValue)
                return RedirectToAction("Login", "Auth");

            int totalPersons = Math.Max(1, adults + children);

            // מחיר לאדם + בדיקת מקומות
            int pricePerPerson;
            int freePlaces;

            using (var conn = new SqlConnection(_connectionString))
            {
                conn.Open();
                using var cmd = new SqlCommand(@"
                    SELECT sum, numFreePlaces
                    FROM Package
                    WHERE Id = @pid AND inactive = 0;", conn);

                cmd.Parameters.AddWithValue("@pid", packageId);

                using var r = cmd.ExecuteReader();
                if (!r.Read())
                {
                    TempData["CartError"] = "Trip not found.";
                    return RedirectToAction("Gallery", "Package");
                }

                pricePerPerson = r.GetInt32(0);
                freePlaces = r.GetInt32(1);
            }

            if (freePlaces < totalPersons)
            {
                TempData["CartError"] = "Not enough free places for this number of passengers.";
                return RedirectToAction("Gallery", "Package");
            }

            int totalSum = pricePerPerson * totalPersons;

            // ✅ מוסיף שורה לעגלה (כל Book מוסיף פריט חדש -> CartCount עולה ב1)
            using (var conn = new SqlConnection(_connectionString))
            {
                conn.Open();
                using var ins = new SqlCommand(@"
                    INSERT INTO shoppingcart(userId, PackageId, sum, inactive, numPersons)
                    VALUES(@uid, @pid, @sum, 0, @n);", conn);

                ins.Parameters.AddWithValue("@uid", userId.Value);
                ins.Parameters.AddWithValue("@pid", packageId);
                ins.Parameters.AddWithValue("@sum", totalSum);
                ins.Parameters.AddWithValue("@n", totalPersons);

                ins.ExecuteNonQuery();
            }

            RefreshCartCount(userId.Value);

            // ✅ נשארים באותו עמוד
            var referer = Request.Headers["Referer"].ToString();
            if (!string.IsNullOrEmpty(referer))
                return Redirect(referer);

            return RedirectToAction("Gallery", "Package");
        }

        // ---------------------------------------------------------
        // BOOK NOW: מוסיף לעגלה ומעביר ישר לעגלה
        // ---------------------------------------------------------
        [HttpPost] // ✅ תיקון: היה לך פעמיים [HttpPost]
        public IActionResult BuyNow(int packageId, int adults = 1, int children = 0)
        {
            var userId = GetUserId();
            if (!userId.HasValue)
                return RedirectToAction("Login", "Auth");

            int totalPersons = Math.Max(1, adults + children);

            int pricePerPerson;
            int freePlaces;

            using (var conn = new SqlConnection(_connectionString))
            {
                conn.Open();
                using var cmd = new SqlCommand(@"
                    SELECT sum, numFreePlaces
                    FROM Package
                    WHERE Id = @pid AND inactive = 0;", conn);

                cmd.Parameters.AddWithValue("@pid", packageId);

                using var r = cmd.ExecuteReader();
                if (!r.Read())
                {
                    TempData["CartError"] = "Trip not found.";
                    return RedirectToAction("Gallery", "Package");
                }

                pricePerPerson = r.GetInt32(0);
                freePlaces = r.GetInt32(1);
            }

            if (freePlaces < totalPersons)
            {
                TempData["CartError"] = "Not enough free places for this number of passengers.";
                return RedirectToAction("Gallery", "Package");
            }

            int totalSum = pricePerPerson * totalPersons;

            using (var conn = new SqlConnection(_connectionString))
            {
                conn.Open();
                using var ins = new SqlCommand(@"
                    INSERT INTO shoppingcart(userId, PackageId, sum, inactive, numPersons)
                    VALUES(@uid, @pid, @sum, 0, @n);", conn);

                ins.Parameters.AddWithValue("@uid", userId.Value);
                ins.Parameters.AddWithValue("@pid", packageId);
                ins.Parameters.AddWithValue("@sum", totalSum);
                ins.Parameters.AddWithValue("@n", totalPersons);

                ins.ExecuteNonQuery();
            }

            RefreshCartCount(userId.Value);

            // ✅ BuyNow תמיד הולך לעגלה
            return RedirectToAction("Index");
        }

        // ---------------------------------------------------------
        // CART PAGE
        // ---------------------------------------------------------
        [HttpGet]
        public IActionResult Index()
        {
            var userId = GetUserId();
            if (!userId.HasValue)
                return RedirectToAction("Login", "Auth");

            var items = new List<CartItem>();

            using (var conn = new SqlConnection(_connectionString))
            {
                conn.Open();
                using var cmd = new SqlCommand(@"
                    SELECT sc.Id, sc.PackageId, sc.sum, sc.numPersons,
                           p.destination, p.country, p.startDate, p.endDate,
                           (SELECT TOP 1 ImageLocation FROM ImagesPackage WHERE PackageId = p.Id ORDER BY Id) as ImageLocation
                    FROM shoppingcart sc
                    INNER JOIN Package p ON p.Id = sc.PackageId
                    WHERE sc.userId = @uid AND sc.inactive = 0
                    ORDER BY sc.Id DESC;", conn);

                cmd.Parameters.AddWithValue("@uid", userId.Value);

                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    items.Add(new CartItem
                    {
                        PackageId = r.GetInt32(1),
                        Destination = r.IsDBNull(4) ? "" : r.GetString(4),
                        StartDate = r.GetDateTime(6),
                        EndDate = r.GetDateTime(7),
                        Quantity = r.GetInt32(3),
                        Price = (r.GetInt32(2) / Math.Max(1, r.GetInt32(3))),
                        ImageUrl = r.IsDBNull(8) ? "/images/default.jpg" : r.GetString(8),
                        ShoppingCartRowId = r.GetInt32(0),
                        TotalSum = r.GetInt32(2)
                    });
                }
            }

            RefreshCartCount(userId.Value);

            var vm = new CartViewModel { Items = items };
            return View(vm);
        }

        // ---------------------------------------------------------
        // REMOVE ITEM (X) -> inactive=1 לשורה הספציפית
        // ---------------------------------------------------------
        [HttpPost]
        public IActionResult RemoveRow(int rowId)
        {
            var userId = GetUserId();
            if (!userId.HasValue)
                return RedirectToAction("Login", "Auth");

            using (var conn = new SqlConnection(_connectionString))
            {
                conn.Open();
                using var cmd = new SqlCommand(@"
                    UPDATE shoppingcart
                    SET inactive = 1
                    WHERE Id = @rid AND userId = @uid;", conn);

                cmd.Parameters.AddWithValue("@rid", rowId);
                cmd.Parameters.AddWithValue("@uid", userId.Value);

                cmd.ExecuteNonQuery();
            }

            RefreshCartCount(userId.Value);
            return RedirectToAction("Index");
        }

        // ---------------------------------------------------------
        // PAYMENT PAGE (GET) -> מציג Payment view
        // ---------------------------------------------------------
        [HttpGet]
        public IActionResult Payment()
        {
            var userId = GetUserId();
            if (!userId.HasValue)
                return RedirectToAction("Login", "Auth");

            // ✅ אם אין פריטים בעגלה - לא נותנים להיכנס לתשלום
            int total = GetCartTotal(userId.Value);
            if (total <= 0)
            {
                TempData["CartError"] = "Your cart is empty. Please add a trip before payment.";
                return RedirectToAction("Index");
            }

            return View(new PaymentViewModel());
        }

        // ---------------------------------------------------------
        // PAYMENT (POST) -> ולידציה + Success/Failure
        // אם הצלחה: מסמן inactive=1 לעגלה -> עובר לפידבק Website
        // ---------------------------------------------------------
        [HttpPost]
        public IActionResult Payment(PaymentViewModel model)
        {
            var userId = GetUserId();
            if (!userId.HasValue)
                return RedirectToAction("Login", "Auth");

            int total = GetCartTotal(userId.Value);
            if (total <= 0)
            {
                TempData["PaymentError"] = "Payment failed: your cart is empty.";
                return RedirectToAction("Payment");
            }

            if (!ModelState.IsValid)
            {
                TempData["PaymentError"] = "Payment failed: please fix the highlighted fields.";
                return View(model); // נשאר באותו עמוד ויראו אדום ליד השדות
            }

            try
            {
                using (var conn = new SqlConnection(_connectionString))
                {
                    conn.Open();
                    using var cmd = new SqlCommand(@"
                        UPDATE shoppingcart
                        SET inactive = 1
                        WHERE userId = @uid AND inactive = 0;", conn);

                    cmd.Parameters.AddWithValue("@uid", userId.Value);
                    cmd.ExecuteNonQuery();
                }

                RefreshCartCount(userId.Value);

                TempData["PaymentSuccess"] = "Payment completed successfully!";
                // ✅ אחרי תשלום עוברים לדירוג אתר
                return RedirectToAction("Website", "Feedback");
            }
            catch
            {
                TempData["PaymentError"] = "Payment failed due to a server error. Please try again.";
                return RedirectToAction("Payment");
            }
        }

        // ---------------------------------------------------------
        // (ישן) MarkPaid נשאר אם את עדיין משתמשת בו איפשהו.
        // אני ממליץ לא להשתמש בו יותר כי עכשיו Payment() עושה את זה.
        // ---------------------------------------------------------
        [HttpPost]
        public IActionResult MarkPaid()
        {
            var userId = GetUserId();
            if (!userId.HasValue)
                return RedirectToAction("Login", "Auth");

            using (var conn = new SqlConnection(_connectionString))
            {
                conn.Open();
                using var cmd = new SqlCommand(@"
                    UPDATE shoppingcart
                    SET inactive = 1
                    WHERE userId = @uid AND inactive = 0;", conn);

                cmd.Parameters.AddWithValue("@uid", userId.Value);
                cmd.ExecuteNonQuery();
            }

            RefreshCartCount(userId.Value);
            TempData["PaymentSuccess"] = "Payment completed successfully!";
            return RedirectToAction("Index");
        }
    }
}

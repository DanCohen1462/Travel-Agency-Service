using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using TravelAgency.Models;

namespace TravelAgency.Controllers
{
    public class CartController : Controller
    {
        private readonly string _connectionString;
        private const string CART_KEY = "Cart";

        public CartController(IConfiguration config)
        {
            _connectionString = config.GetConnectionString("DefaultConnection");
        }

        // ---------- עזר לקריאת/שמירת העגלה ----------

        private List<CartItem> GetCart()
        {
            var json = HttpContext.Session.GetString(CART_KEY);
            if (string.IsNullOrEmpty(json))
                return new List<CartItem>();

            try
            {
                return JsonSerializer.Deserialize<List<CartItem>>(json)
                       ?? new List<CartItem>();
            }
            catch
            {
                return new List<CartItem>();
            }
        }

        private void SaveCart(List<CartItem> cart)
        {
            var json = JsonSerializer.Serialize(cart);
            HttpContext.Session.SetString(CART_KEY, json);

            int count = cart.Sum(c => c.Quantity);
            HttpContext.Session.SetInt32("CartCount", count);
        }

        // ---------- הוספה לעגלה ----------

        [HttpPost]
        public IActionResult Add(int packageId)
        {
            var userIdStr = HttpContext.Session.GetString("UserId");
            if (string.IsNullOrEmpty(userIdStr))
            {
                return RedirectToAction("Login", "Auth");
            }

            Package? pkg = null;

            using (var conn = new SqlConnection(_connectionString))
            {
                conn.Open();

                string sql = @"
                    SELECT Id, destination, StartDate, EndDate, sum, numFreePlaces
                    FROM Package
                    WHERE Id = @Id AND inactive = 0";

                using (var cmd = new SqlCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@Id", packageId);

                    using (var reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            pkg = new Package
                            {
                                Id = reader.GetInt32(0),
                                destination = reader.GetString(1),
                                StartDate = reader.GetDateTime(2),
                                EndDate = reader.GetDateTime(3),
                                sum = reader.GetInt32(4),
                                numFreePlaces = reader.GetInt32(5)
                            };
                        }
                    }
                }
            }

            if (pkg == null)
            {
                TempData["CartError"] = "Trip not found.";
                return RedirectToAction("Gallery", "Package");
            }

            if (pkg.numFreePlaces <= 0)
            {
                TempData["CartError"] = "This trip is full and cannot be added to the cart.";
                return RedirectToAction("Gallery", "Package");
            }

            var cart = GetCart();
            var existing = cart.FirstOrDefault(c => c.PackageId == packageId);

            if (existing != null)
            {
                existing.Quantity += 1;
            }
            else
            {
                cart.Add(new CartItem
                {
                    PackageId = pkg.Id,
                    Destination = pkg.destination,
                    StartDate = pkg.StartDate,
                    EndDate = pkg.EndDate,
                    Price = pkg.sum,
                    Quantity = 1
                });
            }

            SaveCart(cart);

            // >>> כאן השינוי: חזרה לאותו דף שממנו באת <<<
            var referer = Request.Headers["Referer"].ToString();
            if (!string.IsNullOrEmpty(referer))
            {
                return Redirect(referer);        // יחזיר לגלריה או לדף פרטים
            }

            // גיבוי – אם אין Referer
            return RedirectToAction("Gallery", "Package");
        }

        // ---------- הצגת העגלה ----------

        [HttpGet]
        public IActionResult Index()
        {
            var cart = GetCart();
            var model = new CartViewModel { Items = cart };
            return View(model);
        }

        // ---------- עדכון כמות ----------

        [HttpPost]
        public IActionResult UpdateQuantity(int packageId, int quantity)
        {
            var cart = GetCart();
            var item = cart.FirstOrDefault(c => c.PackageId == packageId);

            if (item != null)
            {
                if (quantity <= 0)
                {
                    cart.Remove(item);
                }
                else
                {
                    item.Quantity = quantity;
                }
            }

            SaveCart(cart);
            return RedirectToAction("Index");
        }

        // ---------- הסרה: מוריד יחידה אחת ----------

        [HttpPost]
        public IActionResult Remove(int packageId)
        {
            var cart = GetCart();
            var item = cart.FirstOrDefault(c => c.PackageId == packageId);

            if (item != null)
            {
                if (item.Quantity > 1)
                {
                    item.Quantity -= 1;
                }
                else
                {
                    cart.Remove(item);
                }
            }

            SaveCart(cart);
            return RedirectToAction("Index");
        }

        // ---------- ניקוי מלא ----------

        [HttpPost]
        public IActionResult Clear()
        {
            SaveCart(new List<CartItem>());
            return RedirectToAction("Index");
        }
    }
}

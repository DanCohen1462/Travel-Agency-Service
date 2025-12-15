using System;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using TravelAgency.Models;

namespace TravelAgency.Controllers
{
    public class FeedbackController : Controller
    {
        private readonly string _connectionString;

        public FeedbackController(IConfiguration config)
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

        // ---------------------------------------------------------
        // WEBSITE FEEDBACK (GET)
        // ---------------------------------------------------------
        [HttpGet]
        public IActionResult Website()
        {
            var userId = GetUserId();
            if (!userId.HasValue)
                return RedirectToAction("Login", "Auth");

            return View();
        }

        // ---------------------------------------------------------
        // WEBSITE FEEDBACK (POST) -> INSERT to feedBack1
        // ---------------------------------------------------------
        [HttpPost]
        public IActionResult Website(int rate, string description)
        {
            var userId = GetUserId();
            if (!userId.HasValue)
                return RedirectToAction("Login", "Auth");

            // ✅ ולידציה בסיסית
            if (rate < 1 || rate > 5)
            {
                TempData["FeedbackError"] = "Please choose a rate between 1 and 5.";
                return RedirectToAction("Website");
            }

            description = description ?? "";

            using (var conn = new SqlConnection(_connectionString))
            {
                conn.Open();
                using var cmd = new SqlCommand(@"
                    INSERT INTO feedBack1(userId, Description, Rate, feedbackType, inactive)
                    VALUES(@uid, @desc, @rate, 'Website', 0);", conn);

                cmd.Parameters.AddWithValue("@uid", userId.Value);
                cmd.Parameters.AddWithValue("@desc", description);
                cmd.Parameters.AddWithValue("@rate", rate);
                cmd.ExecuteNonQuery();
            }

            TempData["FeedbackSuccess"] = "Thanks for your feedback!";
            return RedirectToAction("Dashboard", "Users");
        }
    }
}

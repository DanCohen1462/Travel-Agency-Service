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

// ✅ Rate validation
            if (rate < 1 || rate > 5)
            {
                TempData["Error"] = "Please choose a rate between 1 and 5.";
                return RedirectToAction("Website");
            }

// ✅ Description required (no empty / whitespace)
            description = (description ?? "").Trim();
            if (string.IsNullOrWhiteSpace(description) || description.Length < 3)
            {
                TempData["Error"] = "Please write a short feedback (at least 3 characters).";
                return RedirectToAction("Website");
            }

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

            TempData["Success"] = "Thanks! Your rating was submitted.";

// ✅ After website feedback: send employee to employee home
            var userType = HttpContext.Session.GetString("UserType") ?? "";
            if (userType == "2")
                return RedirectToAction("EmployeeDashboard", "Employee");

            return RedirectToAction("Dashboard", "Users");


        }
        
// ---------------------------------------------------------
// PACKAGE FEEDBACK (GET)  /Feedback/Package?reservationId=123
// ---------------------------------------------------------
[HttpGet]
public IActionResult Package(int reservationId)
{
    var userId = GetUserId();
    if (!userId.HasValue)
        return RedirectToAction("Login", "Auth");

    // basic ownership check: reservation belongs to this user + active
    // ✅ ownership + fetch trip info for display (Destination/Country/Category)
    using (var conn = new SqlConnection(_connectionString))
    {
        conn.Open();

        using var cmd = new SqlCommand(@"
    SELECT TOP 1
        h.Id AS ReservationId,
        h.PackageId,
        p.destination,
        ISNULL(p.country,'') AS country,
        p.idCategory,
        ISNULL(c.name,'') AS CategoryName
    FROM HistoryReservation h
    INNER JOIN Package p ON p.Id = h.PackageId
    LEFT JOIN [Category] c ON c.Id = p.idCategory AND c.inactive = 0
    WHERE h.Id = @rid
      AND h.UserId = @uid
      AND h.inactive = 0
      AND p.inactive = 0;", conn);


        cmd.Parameters.AddWithValue("@rid", reservationId);
        cmd.Parameters.AddWithValue("@uid", userId.Value);

        using var r = cmd.ExecuteReader();
        if (!r.Read())
        {
            TempData["Error"] = "Trip not found.";
            return RedirectToAction("MyTrips", "Users");
        }

        ViewBag.ReservationId = reservationId;
        ViewBag.PackageId = Convert.ToInt32(r["PackageId"]);
        ViewBag.Destination = r["destination"]?.ToString() ?? "";
        ViewBag.Country = r["country"]?.ToString() ?? "";
        ViewBag.CategoryId = Convert.ToInt32(r["idCategory"]);
        ViewBag.CategoryName = r["CategoryName"]?.ToString() ?? "";

// ✅ clean old error TempData so it won't show when the trip was found
        TempData.Remove("Error");


    }

    return View();

}


// ---------------------------------------------------------
// PACKAGE FEEDBACK (POST)
// ---------------------------------------------------------
[HttpPost]
public IActionResult Package(int reservationId, int rate, string description)
{
    var userId = GetUserId();
    if (!userId.HasValue)
        return RedirectToAction("Login", "Auth");

    if (rate < 1 || rate > 5)
    {
        TempData["Error"] = "Please choose a rate between 1 and 5.";
        return RedirectToAction("Package", new { reservationId });
    }


    description = (description ?? "").Trim();
    if (string.IsNullOrWhiteSpace(description) || description.Length < 3)
    {
        TempData["Error"] = "Please write a short feedback (at least 3 characters).";
        return RedirectToAction("Package", new { reservationId });
    }

    using (var conn = new SqlConnection(_connectionString))
    {
        conn.Open();


        // 1) Validate ownership and pull PackageId + grouping fields (dest/country/category)
        int packageId = 0;
        string dest = "";
        string country = "";
        int categoryId = 0;

        using (var cmd = new SqlCommand(@"
            SELECT TOP 1
                h.PackageId,
                p.destination,
                ISNULL(p.country,'') as country,
                p.idCategory
            FROM HistoryReservation h
            INNER JOIN Package p ON p.Id = h.PackageId
            WHERE h.Id = @rid
              AND h.UserId = @uid
              AND h.inactive = 0
              AND p.inactive = 0;", conn))
        {
            cmd.Parameters.AddWithValue("@rid", reservationId);
            cmd.Parameters.AddWithValue("@uid", userId.Value);

            using var r = cmd.ExecuteReader();
            if (!r.Read())
            {
                TempData["Error"] = "Trip not found.";
                return RedirectToAction("MyTrips", "Users");

            }

            packageId = Convert.ToInt32(r["PackageId"]);
            dest = r["destination"]?.ToString() ?? "";
            country = r["country"]?.ToString() ?? "";
            categoryId = Convert.ToInt32(r["idCategory"]);
        }

        // 2) Prevent duplicate feedback per user (ONE per Destination+Country+Category)
        using (var chk = new SqlCommand(@"
            SELECT COUNT(*)
            FROM feedBack1 f
            INNER JOIN PackageFeedback pf ON pf.FeedbackId = f.Id AND pf.inactive = 0
            WHERE f.userId = @uid
              AND f.feedbackType = 'Package'
              AND f.inactive = 0
              AND ISNULL(pf.Destination,'') = ISNULL(@dest,'')
              AND ISNULL(pf.Country,'') = ISNULL(@ctry,'')
              AND pf.CategoryId = @cat;", conn))
        {
            chk.Parameters.AddWithValue("@uid", userId.Value);
            chk.Parameters.AddWithValue("@dest", dest);
            chk.Parameters.AddWithValue("@ctry", country);
            chk.Parameters.AddWithValue("@cat", categoryId);

            int exists = (int)chk.ExecuteScalar();
            if (exists > 0)
            {
                TempData["Error"] = "You have already submitted feedback for this trip group.";
                return RedirectToAction("MyTrips", "Users", new { tab = "history" });
            }

        }


        // 3) Insert into feedBack1
        int feedbackId;
        using (var insFb = new SqlCommand(@"
            INSERT INTO feedBack1(userId, Description, Rate, feedbackType, inactive)
            OUTPUT INSERTED.Id
            VALUES(@uid, @desc, @rate, 'Package', 0);", conn))
        {
            insFb.Parameters.AddWithValue("@uid", userId.Value);
            insFb.Parameters.AddWithValue("@desc", description);
            insFb.Parameters.AddWithValue("@rate", rate);

            feedbackId = (int)insFb.ExecuteScalar();
        }

        // 4) Link to package + store grouping fields for the “Destination+Country+Category” logic
               using (var insPf = new SqlCommand(@"
            INSERT INTO PackageFeedback(PackageId, FeedbackId, inactive, CategoryId, Destination, Country)
            VALUES(@pid, @fid, 0, @cat, @dest, @ctry);", conn))
        {
            insPf.Parameters.AddWithValue("@pid", packageId);
            insPf.Parameters.AddWithValue("@fid", feedbackId);
            insPf.Parameters.AddWithValue("@cat", categoryId);
            insPf.Parameters.AddWithValue("@dest", dest);
            insPf.Parameters.AddWithValue("@ctry", country);

            insPf.ExecuteNonQuery();
        }

        // ✅ deactivate related "Rate your trip" notification (if exists)
        try
        {
            using var cmdN = new SqlCommand(@"
                UPDATE dbo.Notifications
                SET inactive = 1
                WHERE UserId = @uid
                  AND inactive = 0
                  AND LinkUrl = @link;", conn);

            cmdN.Parameters.AddWithValue("@uid", userId.Value);
            cmdN.Parameters.AddWithValue("@link",
                $"/Users/MyTrips?tab=history&highlightReservationId={reservationId}");

            cmdN.ExecuteNonQuery();
        }
        catch { }
    }

    TempData["Success"] = "Thanks! Your rating was submitted.";
    return RedirectToAction("MyTrips", "Users", new { tab = "history" });

}
    }
}

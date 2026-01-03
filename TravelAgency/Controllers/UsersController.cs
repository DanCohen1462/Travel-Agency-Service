using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using TravelAgency.Models;
using System.Linq;
using Microsoft.Extensions.Configuration; 
using Microsoft.AspNetCore.Http; 
using System;
using System.Collections.Generic;
using TravelAgency.Services; 
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;



namespace TravelAgency.Controllers
{
    public class UsersController : Controller
    {
        private readonly string _connectionString;
        private readonly NotificationService _notificationService;
        private readonly IWebHostEnvironment _env;

        public UsersController(IConfiguration config, NotificationService notificationService, IWebHostEnvironment env)
        {
            _connectionString = config.GetConnectionString("DefaultConnection")
                                ?? throw new InvalidOperationException(
                                    "Connection string 'DefaultConnection' not found.");
            _notificationService = notificationService;
            _env = env;
        }

        // ---------- Dashboard (GET) - דף נחיתה ללקוח רגיל (המלבנים) ----------
        [HttpGet]
        public IActionResult Dashboard()
        {
            var userIdStr = HttpContext.Session.GetString("UserId");
            if (string.IsNullOrEmpty(userIdStr))
            {
                // אם אין סשן, הפנה להתחברות
                return RedirectToAction("Login", "Auth");
            }

            // קריאה ל-DB כדי לקבל את סוג המשתמש (למקרה שהסשן לא עדכני)
            int userType = 0;
            using (var conn = new SqlConnection(_connectionString))
            {
                conn.Open();
                string sql = "SELECT type FROM Users WHERE Id = @Id AND inactive = 0";

                using (var cmd = new SqlCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@Id", int.Parse(userIdStr));
                    var result = cmd.ExecuteScalar();
                    if (result != null)
                    {
                        userType = (int)result;
                    }
                }
            }

            // אם אדמין (type=1), שלח אותו ללוח הבקרה של אדמין
            if (userType == 1)
            {
                return RedirectToAction("index", "Admin");
            }

            // אם עובד (type=2), שלח אותו ללוח הבקרה של עובד
            if (userType == 2)
            {
                return RedirectToAction("Panel", "Worker");
            }

            // אם לקוח (type=3), נציג את דף המלבנים
            ViewData["Title"] = "Customer Dashboard";
            HttpContext.Session.SetString("UserType", userType.ToString());

            return View();
        }
// ---------- Profile (GET) ----------
        [HttpGet]
        public IActionResult Profile()
        {
            var userIdStr = HttpContext.Session.GetString("UserId");
            if (string.IsNullOrEmpty(userIdStr))
                return RedirectToAction("Login", "Auth");

            int userId = int.Parse(userIdStr);

            var model = new UserProfileViewModel();

            using (var conn = new SqlConnection(_connectionString))
            {
                conn.Open();

                string sql = @"
            SELECT Id, Username, firstName, lastName,
                   birthDate, gender, phoneNumber, email, type
            FROM Users
            WHERE Id = @Id AND inactive = 0;
        ";

                using (var cmd = new SqlCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@Id", userId);

                    using (var reader = cmd.ExecuteReader())
                    {
                        if (!reader.Read())
                            return RedirectToAction("Login", "Auth");

                        int type = reader.GetInt32(8);

                        if (type == 1) return RedirectToAction("Index", "Admin");
                        if (type == 2) return RedirectToAction("Panel", "Worker");

                        model.Id = reader.GetInt32(0);
                        model.Username = reader.GetString(1);
                        model.FirstName = reader.GetString(2);
                        model.LastName = reader.GetString(3);
                        model.BirthDate = reader.IsDBNull(4) ? (DateTime?)null : reader.GetDateTime(4);
                        model.Gender = reader.IsDBNull(5) ? "" : reader.GetString(5);
                        model.PhoneNumber = reader.IsDBNull(6) ? "" : reader.GetString(6);
                        model.Email = reader.GetString(7);
                        model.Type = type;
                    }
                }
            }

            ViewData["Title"] = "Profile";
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Profile(UserProfileViewModel model)
        {

    var userIdStr = HttpContext.Session.GetString("UserId");
    if (string.IsNullOrEmpty(userIdStr))
        return RedirectToAction("Login", "Auth");

    int userId = int.Parse(userIdStr);
    model.Id = userId;

    if (!ModelState.IsValid)
    {
        ViewData["Title"] = "Profile";
        return View(model);
    }

    using (var conn = new SqlConnection(_connectionString))
    {
        conn.Open();

        // load current username + last change
        string loadSql = @"
            SELECT Username, LastUsernameChangeAt
            FROM Users
            WHERE Id = @Id AND inactive = 0;
        ";

        string currentUsernameFromDb = "";
        DateTime? lastUsernameChangeAt = null;

        using (var loadCmd = new SqlCommand(loadSql, conn))
        {
            loadCmd.Parameters.AddWithValue("@Id", userId);

            using (var r = loadCmd.ExecuteReader())
            {
                if (!r.Read())
                    return RedirectToAction("Login", "Auth");

                currentUsernameFromDb = r["Username"]?.ToString() ?? "";
                lastUsernameChangeAt = r.IsDBNull(r.GetOrdinal("LastUsernameChangeAt"))
                    ? (DateTime?)null
                    : r.GetDateTime(r.GetOrdinal("LastUsernameChangeAt"));
            }
        }

        model.Username = (model.Username ?? "").Trim();

        bool usernameChanged =
            !string.Equals(currentUsernameFromDb, model.Username, StringComparison.OrdinalIgnoreCase);

        // 30 days rule + unique only if changed
        if (usernameChanged)
        {
            if (lastUsernameChangeAt.HasValue)
            {
                var nextAllowed = lastUsernameChangeAt.Value.AddDays(30);
                if (DateTime.Now < nextAllowed)
                {
                    ModelState.AddModelError("Username",
                        $"You can change your username again on {nextAllowed:dd/MM/yyyy}.");
                    ViewData["Title"] = "Profile";
                    return View(model);
                }
            }

            string checkSql = @"
                SELECT COUNT(*)
                FROM Users
                WHERE Username = @Username AND Id <> @Id AND inactive = 0;
            ";

            using (var checkCmd = new SqlCommand(checkSql, conn))
            {
                checkCmd.Parameters.AddWithValue("@Username", model.Username);
                checkCmd.Parameters.AddWithValue("@Id", userId);

                int exists = (int)checkCmd.ExecuteScalar();
                if (exists > 0)
                {
                    ModelState.AddModelError("Username", "Username already exists");
                    ViewData["Title"] = "Profile";
                    return View(model);
                }
            }
        }

        string updateSql = @"
            UPDATE Users
            SET Username   = @Username,
                firstName  = @FirstName,
                lastName   = @LastName,
                birthDate  = @BirthDate,
                gender     = @Gender,
                phoneNumber= @PhoneNumber,
                email      = @Email,
                LastUsernameChangeAt = CASE WHEN @UsernameChanged = 1 THEN GETDATE() ELSE LastUsernameChangeAt END
            WHERE Id = @Id AND (type = 2 OR type = 3) AND inactive = 0;
        ";

        using (var cmd = new SqlCommand(updateSql, conn))
        {
            cmd.Parameters.AddWithValue("@Username", model.Username);
            cmd.Parameters.AddWithValue("@FirstName", model.FirstName);
            cmd.Parameters.AddWithValue("@LastName", model.LastName);
            cmd.Parameters.AddWithValue("@BirthDate", (object?)model.BirthDate ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Gender", (object?)model.Gender ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@PhoneNumber", (object?)model.PhoneNumber ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Email", model.Email);
            cmd.Parameters.AddWithValue("@Id", userId);
            cmd.Parameters.AddWithValue("@UsernameChanged", usernameChanged ? 1 : 0);

            cmd.ExecuteNonQuery();
        }
    }

    HttpContext.Session.SetString("Username", model.Username);
    HttpContext.Session.SetString("FullName", model.FirstName + " " + model.LastName);

    ViewBag.Success = "Profile updated successfully.";
    ViewData["Title"] = "Profile";
    return View(model);
}


        public IActionResult MyTrips()
        {
            var userIdStr = HttpContext.Session.GetString("UserId");
            if (string.IsNullOrEmpty(userIdStr))
                return RedirectToAction("Login", "Auth");

            int userId = int.Parse(userIdStr);
            List<UserTripViewModel> myTrips = new List<UserTripViewModel>();

            using (var conn = new SqlConnection(_connectionString))
            {
                conn.Open();

         
                string sql = @"
    SELECT 
        h.Id AS ReservationId,
        h.PackageId,
        h.numPersons,
        h.sum,
        p.destination,
        ISNULL(p.country,'') as country,
        p.startDate,
        p.endDate,
        ISNULL(p.cancelationDays, 0) AS cancelationDays,
        p.idCategory as CategoryId,
        ISNULL(c.name,'') as CategoryName,
        ISNULL((
            SELECT TOP 1 ImageLocation 
            FROM ImagesPackage 
            WHERE PackageId = p.Id 
            ORDER BY Id
        ), '/images/default.jpg') AS ImageUrl
    FROM HistoryReservation h
    INNER JOIN Package p ON h.PackageId = p.Id
    INNER JOIN Category c ON c.Id = p.idCategory AND c.inactive = 0
    WHERE h.UserId = @UserId AND h.inactive = 0
    ORDER BY p.startDate DESC";




                using (var cmd = new SqlCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@UserId", userId);

                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            var trip = new UserTripViewModel();

                            trip.ReservationId = reader.GetInt32(reader.GetOrdinal("ReservationId"));
                            trip.PackageId = reader.GetInt32(reader.GetOrdinal("PackageId")); // ✅ חדש

                            trip.NumPersons = reader.IsDBNull(reader.GetOrdinal("numPersons"))
                                ? 1
                                : reader.GetInt32(reader.GetOrdinal("numPersons"));

                            var sumObj = reader["sum"];
                            trip.TotalPrice = (sumObj == DBNull.Value) ? 0 : Convert.ToInt32(sumObj);

                            trip.Destination = reader["destination"].ToString();
                            trip.Country = reader["country"]?.ToString() ?? "";
                            trip.StartDate = Convert.ToDateTime(reader["startDate"]);
                            trip.EndDate = Convert.ToDateTime(reader["endDate"]);

                            trip.CancelationDays = reader.IsDBNull(reader.GetOrdinal("cancelationDays"))
                                ? 0
                                : reader.GetInt32(reader.GetOrdinal("cancelationDays")); 
                            
                            trip.CategoryId = reader.IsDBNull(reader.GetOrdinal("CategoryId"))
                                ? 0
                                : reader.GetInt32(reader.GetOrdinal("CategoryId"));

                            trip.CategoryName = reader["CategoryName"]?.ToString() ?? "";

                            trip.IsUpcoming = trip.StartDate > DateTime.Now;

                            trip.ImageUrl = reader["ImageUrl"].ToString();



                            myTrips.Add(trip);
                        }
                    }
                }
            }

            // ✅ GROUPING for UI: Destination + Country + CategoryId + StartDate + EndDate
            var grouped = myTrips
                .GroupBy(t => new
                {
                    Dest = (t.Destination ?? "").Trim(),
                    Country = (t.Country ?? "").Trim(),          // <-- we'll add Country property in viewmodel? see note below
                    CatId = t.CategoryId,
                    Start = t.StartDate.Date,
                    End = t.EndDate.Date
                })
                .Select(g =>
                {
                    var first = g.OrderByDescending(x => x.ReservationId).First();

                    return new UserTripViewModel
                    {
                        ReservationId = first.ReservationId,
                        PackageId = first.PackageId,
                        Destination = first.Destination,
                        Country = first.Country,
                        StartDate = first.StartDate,
                        EndDate = first.EndDate,
                        CancelationDays = first.CancelationDays,
                        CategoryId = first.CategoryId,
                        CategoryName = first.CategoryName,
                        ImageUrl = first.ImageUrl,
                        NumPersons = g.Sum(x => Math.Max(1, x.NumPersons)),
                        TotalPrice = g.Sum(x => Math.Max(0, x.TotalPrice)),
                        IsUpcoming = first.StartDate > DateTime.Now
                        
                        
                    };
                })
                .OrderByDescending(x => x.StartDate)
                .ToList();
            
            // ✅ Fill HasRated per group (Destination+Country+Category) for this user
            try
            {
                var ratedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                using (var connR = new SqlConnection(_connectionString))
                {
                    connR.Open();
                    using var cmdR = new SqlCommand(@"
            SELECT
                ISNULL(pf.Destination,'') as Destination,
                ISNULL(pf.Country,'') as Country,
                pf.CategoryId
            FROM feedBack1 f
            INNER JOIN PackageFeedback pf ON pf.FeedbackId = f.Id AND pf.inactive = 0
            WHERE f.userId = @uid
              AND f.inactive = 0
              AND f.feedbackType = 'Package';", connR);

                    cmdR.Parameters.AddWithValue("@uid", userId);

                    using var rr = cmdR.ExecuteReader();
                    while (rr.Read())
                    {
                        string d = (rr["Destination"]?.ToString() ?? "").Trim();
                        string c = (rr["Country"]?.ToString() ?? "").Trim();
                        int cat = Convert.ToInt32(rr["CategoryId"]);

                        ratedKeys.Add($"{d}||{c}||{cat}");
                    }
                }

                foreach (var t in grouped)
                {
                    string key = $"{(t.Destination ?? "").Trim()}||{(t.Country ?? "").Trim()}||{t.CategoryId}";
                    t.HasRated = ratedKeys.Contains(key);
                }
            }
            catch
            {
                // if something fails, keep HasRated default (false) – but no crash
            }

            
// ✅ CREATE "Rate your trip" notifications (ONE per PAST TRIP CARD / group)
            try
            {
                foreach (var trip in grouped.Where(t => !t.IsUpcoming && !t.HasRated))
                {
                    string dest = trip.Destination ?? "";
                    string ctry = trip.Country ?? "";

                    string title = $"Rate your trip: {dest}{(string.IsNullOrWhiteSpace(ctry) ? "" : $" ({ctry})")}";

                    // ✅ Open should lead to History tab (past trips)
                    string linkUrl = $"/Users/MyTrips?tab=history&highlightReservationId={trip.ReservationId}";

                    using var conn3 = new SqlConnection(_connectionString);
                    conn3.Open();

                    using var existsCmd = new SqlCommand(@"
            SELECT COUNT(*)
            FROM dbo.Notifications
            WHERE UserId = @uid
              AND inactive = 0
              AND LinkUrl = @link;", conn3);

                    existsCmd.Parameters.AddWithValue("@uid", userId);
                    existsCmd.Parameters.AddWithValue("@link", linkUrl);

                    int exists = (int)existsCmd.ExecuteScalar();
                    if (exists > 0) continue;

                    _notificationService.Create(
                        userId,
                        title: title,
                        message: "Your trip has ended. Please rate your experience!",
                        type: "info",
                        linkUrl: linkUrl
                    );
                }
            }
            catch { }

            
            return View(grouped);
            
        }
        
        [HttpGet]
public IActionResult DownloadTripPdf(int reservationId)
{
    var userIdStr = HttpContext.Session.GetString("UserId");
    if (string.IsNullOrEmpty(userIdStr)) return RedirectToAction("Login", "Auth");
    int userId = int.Parse(userIdStr);

    using var conn = new SqlConnection(_connectionString);
    conn.Open();

    // Pull everything we need in one query (reservation + package + user)
    var sql = @"
SELECT TOP (1)
    h.Id as ReservationId,
    h.numPersons,
    h.sum as TotalSum,
    p.Id as PackageId,
    p.destination,
    ISNULL(p.country,'') as country,
    p.startDate,
    p.endDate,
    p.sum as PricePerPerson,
    p.ageLimit,
    p.numFreePlaces,
    ISNULL(p.Information,'') as Information,
    ISNULL(c.name,'') as CategoryName,
    ISNULL(u.firstName,'') as FirstName,
    ISNULL(u.lastName,'') as LastName,
    ISNULL(u.Username,'') as Username
FROM HistoryReservation h
INNER JOIN Package p ON p.Id = h.PackageId
LEFT JOIN Category c ON c.Id = p.idCategory
INNER JOIN Users u ON u.Id = h.UserId
WHERE h.inactive = 0
  AND h.UserId = @uid
  AND h.Id = @rid;
";

    using var cmd = new SqlCommand(sql, conn);
    cmd.Parameters.AddWithValue("@uid", userId);
    cmd.Parameters.AddWithValue("@rid", reservationId);

    using var r = cmd.ExecuteReader();
    if (!r.Read())
    {
        TempData["CartError"] = "Trip reservation not found.";
        return RedirectToAction("MyTrips");
    }

    // Read values
    var destination = r["destination"]?.ToString() ?? "";
    var country = r["country"]?.ToString() ?? "";
    var categoryName = r["CategoryName"]?.ToString() ?? "";
    var startDate = Convert.ToDateTime(r["startDate"]);
    var endDate = Convert.ToDateTime(r["endDate"]);
    var info = r["Information"]?.ToString() ?? "";

    int numPersons = Convert.ToInt32(r["numPersons"]);
    int pricePerPerson = Convert.ToInt32(r["PricePerPerson"]);
    int totalSum = Convert.ToInt32(r["TotalSum"]);

    var firstName = r["FirstName"]?.ToString() ?? "";
    var lastName = r["LastName"]?.ToString() ?? "";
    var username = r["Username"]?.ToString() ?? "";
    var fullName = (firstName + " " + lastName).Trim();
    if (string.IsNullOrWhiteSpace(fullName)) fullName = username;

    // Make the Information look nice (keep paragraphs)
    info = NormalizeInfo(info);
    
    QuestPDF.Settings.License = LicenseType.Community;

    // Build PDF
    byte[] pdfBytes = Document.Create(container =>
    {
        container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(32);
            page.DefaultTextStyle(x => x.FontSize(12));

            page.Header()
                .Row(row =>
                {
                    row.RelativeItem()
                        .Column(col =>
                        {
                            col.Item().Text("Trip Summary").FontSize(22).SemiBold();
                            col.Item().Text($"{destination} — {country}").FontSize(14).FontColor(Colors.Grey.Darken2);
                            if (!string.IsNullOrWhiteSpace(categoryName))
                                col.Item().Text($"Category: {categoryName}").FontSize(12).FontColor(Colors.Grey.Darken2);
                        });

                    row.ConstantItem(120)
                        .AlignRight()
                        .Text($"{DateTime.Now:dd/MM/yyyy}")
                        .FontSize(10)
                        .FontColor(Colors.Grey.Darken2);
                });

            page.Content().PaddingTop(18).Column(col =>
            {
                col.Spacing(12);

                // Details box
                col.Item().Element(box =>
                {
                    box.Border(1).BorderColor("#E6E6F0").Background("#FAFAFF").Padding(14).Column(c =>
                    {
                        c.Spacing(6);

                        c.Item().Row(rr =>
                        {
                            rr.RelativeItem().Text("Travel dates:").SemiBold();
                            rr.RelativeItem().AlignRight().Text($"{startDate:dd/MM/yyyy}  –  {endDate:dd/MM/yyyy}");
                        });

                        c.Item().Row(rr =>
                        {
                            rr.RelativeItem().Text("Passengers:").SemiBold();
                            rr.RelativeItem().AlignRight().Text($"{numPersons}");
                        });

                        c.Item().Row(rr =>
                        {
                            rr.RelativeItem().Text("Price per person:").SemiBold();
                            rr.RelativeItem().AlignRight().Text($"${pricePerPerson}");
                        });

                        c.Item().Row(rr =>
                        {
                            rr.RelativeItem().Text("Total:").SemiBold();
                            rr.RelativeItem().AlignRight().Text($"${totalSum}").SemiBold();
                        });
                    });
                });

                // Information
                col.Item().Text("Itinerary & Information").FontSize(15).SemiBold().FontColor("#5A189A");

                col.Item().Element(e =>
                {
                    e.Border(1).BorderColor("#EFE7F7").Padding(14).Column(c =>
                    {
                        c.Spacing(6);

                        foreach (var paragraph in SplitParagraphs(info))
                        {
                            if (string.IsNullOrWhiteSpace(paragraph)) continue;
                            c.Item().Text(paragraph);
                        }
                    });
                });

                col.Item().PaddingTop(10).LineHorizontal(1).LineColor("#E6E6F0");

                // Footer user
                col.Item().Row(rr =>
                {
                    rr.RelativeItem().Text($"Booked by: ").SemiBold();
                    rr.RelativeItem().Text(fullName);
                });

                col.Item().Row(rr =>
                {
                    rr.RelativeItem().Text($"Party size: ").SemiBold();
                    rr.RelativeItem().Text($"{numPersons} traveler(s)");
                });
            });

            page.Footer()
                .AlignCenter()
                .Text(x =>
                {
                    x.Span("TravelAgency • ").FontSize(9).FontColor(Colors.Grey.Darken2);
                    x.Span("PDF generated automatically").FontSize(9).FontColor(Colors.Grey.Darken2);
                });
        });
    }).GeneratePdf();

    var safeName = Regex.Replace(destination ?? "Trip", @"[^\w\-]+", "_");
    var fileName = $"Trip_{safeName}_{startDate:yyyyMMdd}.pdf";

    return File(pdfBytes, "application/pdf", fileName);
}

// ✅ GROUP PDF (Destination+Country+CategoryId+Dates)
[HttpGet]
public IActionResult DownloadTripPdfGroup(
    string destination,
    string country,
    int categoryId,
    string startDate,
    string endDate)
{
    var userIdStr = HttpContext.Session.GetString("UserId");
    if (string.IsNullOrEmpty(userIdStr)) return RedirectToAction("Login", "Auth");
    int userId = int.Parse(userIdStr);

    if (!DateTime.TryParse(startDate, out DateTime sd) || !DateTime.TryParse(endDate, out DateTime ed))
    {
        TempData["CartError"] = "Invalid trip dates for PDF.";
        return RedirectToAction("MyTrips");
    }

    using var conn = new SqlConnection(_connectionString);
    conn.Open();

    // 1) User name
    string firstName = "", lastName = "", username = "";
    using (var cmdU = new SqlCommand(@"
        SELECT ISNULL(firstName,''), ISNULL(lastName,''), ISNULL(Username,'')
        FROM Users
        WHERE Id = @uid AND inactive = 0;", conn))
    {
        cmdU.Parameters.AddWithValue("@uid", userId);
        using var r = cmdU.ExecuteReader();
        if (r.Read())
        {
            firstName = r.GetString(0);
            lastName = r.GetString(1);
            username = r.GetString(2);
        }
    }

    string fullName = (firstName + " " + lastName).Trim();
    if (string.IsNullOrWhiteSpace(fullName)) fullName = username;

    // 2) Package info (exact trip of this group)
    string info = "";
    string categoryName = "";
    int pricePerPerson = 0;
    int ageLimit = 0;
    int cancelDays = 0;
    string imageLocation = "";

    using (var cmdP = new SqlCommand(@"
SELECT TOP (1)
    ISNULL(p.Information,'') as Information,
    ISNULL(c.name,'') as CategoryName,
    p.sum as PricePerPerson,
    p.ageLimit,
    ISNULL(p.cancelationDays,0) as cancelationDays,
    ISNULL((
        SELECT TOP 1 ImageLocation
        FROM ImagesPackage
        WHERE PackageId = p.Id
        ORDER BY Id
    ), '') as ImageLocation
FROM Package p
INNER JOIN Category c ON c.Id = p.idCategory AND c.inactive = 0
WHERE p.inactive = 0
  AND p.destination = @dest
  AND ISNULL(p.country,'') = ISNULL(@ctry,'')
  AND p.idCategory = @cat
  AND CAST(p.startDate AS date) = CAST(@sd AS date)
  AND CAST(p.endDate AS date) = CAST(@ed AS date);
", conn))
    {
        cmdP.Parameters.AddWithValue("@dest", (destination ?? "").Trim());
        cmdP.Parameters.AddWithValue("@ctry", (country ?? "").Trim());
        cmdP.Parameters.AddWithValue("@cat", categoryId);
        cmdP.Parameters.AddWithValue("@sd", sd.Date);
        cmdP.Parameters.AddWithValue("@ed", ed.Date);

        using var r = cmdP.ExecuteReader();
        if (!r.Read())
        {
            TempData["CartError"] = "Trip not found for PDF.";
            return RedirectToAction("MyTrips");
        }

        info = r.GetString(0);
        categoryName = r.GetString(1);
        pricePerPerson = Convert.ToInt32(r["PricePerPerson"]);
        ageLimit = Convert.ToInt32(r["ageLimit"]);
        cancelDays = Convert.ToInt32(r["cancelationDays"]);
        imageLocation = r["ImageLocation"]?.ToString() ?? "";

    }

    // 3) Group totals (sum persons + sum total)
    int totalPersons = 0;
    int totalSum = 0;

    using (var cmdH = new SqlCommand(@"
        SELECT
            ISNULL(SUM(ISNULL(h.numPersons,1)),0) as TotalPersons,
            ISNULL(SUM(ISNULL(h.sum,0)),0) as TotalSum
        FROM HistoryReservation h
        INNER JOIN Package p ON p.Id = h.PackageId
        WHERE h.inactive = 0
          AND h.UserId = @uid
          AND p.inactive = 0
          AND p.destination = @dest
          AND ISNULL(p.country,'') = ISNULL(@ctry,'')
          AND p.idCategory = @cat
          AND CAST(p.startDate AS date) = CAST(@sd AS date)
          AND CAST(p.endDate AS date) = CAST(@ed AS date);", conn))
    {
        cmdH.Parameters.AddWithValue("@uid", userId);
        cmdH.Parameters.AddWithValue("@dest", (destination ?? "").Trim());
        cmdH.Parameters.AddWithValue("@ctry", (country ?? "").Trim());
        cmdH.Parameters.AddWithValue("@cat", categoryId);
        cmdH.Parameters.AddWithValue("@sd", sd.Date);
        cmdH.Parameters.AddWithValue("@ed", ed.Date);

        using var r = cmdH.ExecuteReader();
        if (r.Read())
        {
            totalPersons = r.GetInt32(0);
            totalSum = r.GetInt32(1);
        }
    }

    // Normalize info for paragraphs
    info = NormalizeInfo(info);

    // Simple sentence for cancellation
    string cancelText = cancelDays <= 0
        ? "Cancellation is not available for this trip."
        : $"Cancellation is available up to {cancelDays} day(s) before departure.";

    // Build PDF
    QuestPDF.Settings.License = LicenseType.Community;

    byte[] pdfBytes = Document.Create(container =>
    {
        container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(32);
            page.DefaultTextStyle(x => x.FontSize(12));

            page.Header().Column(h =>
{
    h.Item().Row(row =>
    {
        // Left: titles
        row.RelativeItem().Column(col =>
        {
            col.Item().Text("Trip Summary").FontSize(22).SemiBold().FontColor("#5A189A");
            col.Item().Text($"{destination} — {country}").FontSize(14).FontColor(Colors.Grey.Darken2);

            if (!string.IsNullOrWhiteSpace(categoryName))
                col.Item().Text($"Category: {categoryName}").FontSize(12).FontColor(Colors.Grey.Darken2);

            col.Item().Text($"{sd:dd/MM/yyyy}  –  {ed:dd/MM/yyyy}")
                .FontSize(11).FontColor(Colors.Grey.Darken2);
        });

        // Right: image (if exists)
        row.ConstantItem(150).AlignRight().Element(imgBox =>
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(imageLocation))
                {
                    var rel = imageLocation.Trim();

                    rel = rel.TrimStart('~');
                    if (rel.StartsWith("/")) rel = rel.Substring(1);

                    var physical = System.IO.Path.Combine(
                        _env.WebRootPath,
                        rel.Replace("/", System.IO.Path.DirectorySeparatorChar.ToString())
                    );

                    if (System.IO.File.Exists(physical))
                    {
                        var bytes = System.IO.File.ReadAllBytes(physical);

                        imgBox
                            .Height(90)
                            .Border(1).BorderColor("#E6E6F0")
                            .Background("#FFFFFF")
                            .Padding(3)
                            .Image(bytes, ImageScaling.FitArea);
                        return;
                    }
                }
            }
            catch { }

            imgBox.Height(90).Border(1).BorderColor("#E6E6F0").Background("#FAFAFF");
        });
    });

    // ✅ divider line under the header (same header call)
    h.Item().PaddingTop(10).LineHorizontal(1).LineColor("#E6E6F0");
});


            page.Content().PaddingTop(18).Column(col =>
            {
                col.Spacing(12);

                // Details box
                col.Item().Element(box =>
                {
                    box.Border(1).BorderColor("#E6E6F0").Background("#FAFAFF").Padding(14).Column(c =>
                    {
                        c.Spacing(6);

                        c.Item().Row(rr =>
                        {
                            rr.RelativeItem().Text("Passengers:").SemiBold();
                            rr.RelativeItem().AlignRight().Text($"{totalPersons}");
                        });

                        c.Item().Row(rr =>
                        {
                            rr.RelativeItem().Text("Price per person:").SemiBold();
                            rr.RelativeItem().AlignRight().Text($"${pricePerPerson}");
                        });

                        c.Item().Row(rr =>
                        {
                            rr.RelativeItem().Text("Total paid:").SemiBold();
                            rr.RelativeItem().AlignRight().Text($"${totalSum}").SemiBold();
                        });

                        c.Item().Row(rr =>
                        {
                            rr.RelativeItem().Text("Age limit:").SemiBold();
                            rr.RelativeItem().AlignRight().Text($"{ageLimit}+");
                        });

                        c.Item().Row(rr =>
                        {
                            rr.RelativeItem().Text("Cancellation:").SemiBold();
                            rr.RelativeItem().AlignRight().Text(cancelText);
                        });
                    });
                });

                // Information
                col.Item().Text("Itinerary & Information").FontSize(15).SemiBold().FontColor("#5A189A");

                col.Item().Element(e =>
                {
                    e.Border(1).BorderColor("#EFE7F7").Padding(14).Column(c =>
                    {
                        c.Spacing(6);

                        foreach (var paragraph in SplitParagraphs(info))
                        {
                            if (string.IsNullOrWhiteSpace(paragraph)) continue;
                            c.Item().Text(paragraph);
                        }
                    });
                });

                col.Item().PaddingTop(10).LineHorizontal(1).LineColor("#E6E6F0");

                // Footer user
                col.Item().Row(rr =>
                {
                    rr.RelativeItem().Text("Booked by: ").SemiBold();
                    rr.RelativeItem().Text(fullName);
                });

                col.Item().Row(rr =>
                {
                    rr.RelativeItem().Text("Party size: ").SemiBold();
                    rr.RelativeItem().Text($"{totalPersons} traveler(s)");
                });
            });

            page.Footer()
                .AlignCenter()
                .Text(x =>
                {
                    x.Span("TravelAgency • ").FontSize(9).FontColor(Colors.Grey.Darken2);
                    x.Span("PDF generated automatically").FontSize(9).FontColor(Colors.Grey.Darken2);
                });
        });
    }).GeneratePdf();

    var safeName = Regex.Replace(destination ?? "Trip", @"[^\w\-]+", "_");
    var fileName = $"Trip_{safeName}_{sd:yyyyMMdd}.pdf";

    return File(pdfBytes, "application/pdf", fileName);
}


private static string NormalizeInfo(string text)
{
    if (string.IsNullOrWhiteSpace(text)) return "";
    // normalize line breaks and remove excessive blank lines
    text = text.Replace("\r\n", "\n").Replace("\r", "\n");
    text = Regex.Replace(text, @"\n{3,}", "\n\n");
    return text.Trim();
}



private static IEnumerable<string> SplitParagraphs(string text)
{
    if (string.IsNullOrWhiteSpace(text)) yield break;
    foreach (var p in text.Split(new[] { "\n\n" }, StringSplitOptions.None))
        yield return p.Trim();
}



   // ---------------------------------------------------------
// CANCEL GROUP (by Destination+Country+CategoryId+Dates)
// Cancels ALL matching reservations for this user
// Returns places per package + creates 60-min offers
// ---------------------------------------------------------
[HttpPost]
public IActionResult CancelReservationGroup(
    string destination,
    string country,
    int categoryId,
    DateTime startDate,
    DateTime endDate)
{
    var userIdStr = HttpContext.Session.GetString("UserId");
    if (string.IsNullOrEmpty(userIdStr))
        return RedirectToAction("Login", "Auth");

    int userId = int.Parse(userIdStr);

    using var conn = new SqlConnection(_connectionString);
    conn.Open();

    using var tx = conn.BeginTransaction();

    try
    {
        // 1) find all active reservations in this group (and their packages + persons)
        var rows = new List<(int ReservationId, int PackageId, int NumPersons)>();

        using (var cmd = new SqlCommand(@"
SELECT h.Id, h.PackageId, ISNULL(h.numPersons,1) as numPersons
FROM HistoryReservation h
INNER JOIN Package p ON p.Id = h.PackageId
WHERE h.UserId = @uid
  AND h.inactive = 0
  AND p.inactive = 0
  AND p.destination = @dest
  AND ISNULL(p.country,'') = ISNULL(@ctry,'')
  AND p.idCategory = @catId
  AND CAST(p.startDate as date) = CAST(@start as date)
  AND CAST(p.endDate as date) = CAST(@end as date);
", conn, tx))
        {
            cmd.Parameters.AddWithValue("@uid", userId);
            cmd.Parameters.AddWithValue("@dest", (destination ?? "").Trim());
            cmd.Parameters.AddWithValue("@ctry", (country ?? "").Trim());
            cmd.Parameters.AddWithValue("@catId", categoryId);
            cmd.Parameters.AddWithValue("@start", startDate.Date);
            cmd.Parameters.AddWithValue("@end", endDate.Date);

            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                rows.Add((
                    r.GetInt32(0),
                    r.GetInt32(1),
                    r.IsDBNull(2) ? 1 : r.GetInt32(2)
                ));
            }
        }

        if (rows.Count == 0)
        {
            tx.Rollback();
            TempData["Error"] = "Reservation not found.";
            return RedirectToAction("MyTrips");
        }

        // 2) mark all matching reservations inactive
        using (var cmd = new SqlCommand(@"
UPDATE HistoryReservation
SET inactive = 1
WHERE UserId = @uid
  AND inactive = 0
  AND Id IN (" + string.Join(",", rows.Select(x => x.ReservationId)) + @");
", conn, tx))
        {
            cmd.Parameters.AddWithValue("@uid", userId);
            cmd.ExecuteNonQuery();
        }

        // 3) return places per package (grouped by packageId)
        foreach (var grp in rows.GroupBy(x => x.PackageId))
        {
            int pid = grp.Key;
            int totalPersons = grp.Sum(x => Math.Max(1, x.NumPersons));

            using (var back = new SqlCommand(@"
UPDATE Package
SET numFreePlaces = numFreePlaces + @n
WHERE Id = @pid;
", conn, tx))
            {
                back.Parameters.AddWithValue("@pid", pid);
                back.Parameters.AddWithValue("@n", totalPersons);
                back.ExecuteNonQuery();
            }
        }

        tx.Commit();

        // 4) create offers AFTER commit (new connection, no tx)
        foreach (var pid in rows.Select(x => x.PackageId).Distinct())
        {
            try
            {
                using var c2 = new SqlConnection(_connectionString);
                c2.Open();
                CreateOffersFromWaitlist(c2, pid, reason: "cancel");
            }
            catch (Exception ex2)
            {
                Console.WriteLine("CreateOffersFromWaitlist after CancelGroup failed: " + ex2);
            }
        }

        // create notification for the cancelling user
        try
        {
            string title = "Trip cancelled successfully";
            string message = "Your reservation was cancelled successfully.";
            string linkUrl = "/Users/MyTrips?tab=upcoming";

            _notificationService.Create(
                userId,
                title: title,
                message: message,
                type: "success",
                linkUrl: linkUrl
            );
        }
        catch { }
        
        TempData["Success"] = "Reservation cancelled successfully.";
        return RedirectToAction("MyTrips");
    }
    catch (Exception ex)
    {
        try { tx.Rollback(); } catch { }
        Console.WriteLine("CancelReservationGroup failed: " + ex);
        TempData["Error"] = "Could not cancel reservation. Please try again.";
        return RedirectToAction("MyTrips");
    }
}




private void CreateOffersFromWaitlist(SqlConnection conn, int packageId, string reason)
{
    // offer window by reason
    int minutes = (reason == "cancel") ? 60 : 15;

    // Loop: while there are free places, keep offering to the next suitable waiter
    while (true)
    {
        // current free places
        int freePlaces;
        using (var cmdFree = new SqlCommand(@"
            SELECT numFreePlaces
            FROM Package
            WHERE Id = @pid;
        ", conn))
        {
            cmdFree.Parameters.AddWithValue("@pid", packageId);
            freePlaces = (int)cmdFree.ExecuteScalar();
        }

        if (freePlaces <= 0) break;

        // next waiter that fits (skip those who don't fit)
        int waitId, userId, numPersons;
        using (var cmd = new SqlCommand(@"
            SELECT TOP 1 Id, UserId, numPersons
            FROM WaitingList
            WHERE PackageId = @pid
              AND inactive = 0
              AND numPersons <= @free
            ORDER BY JoinDate ASC, Id ASC;
        ", conn))
        {
            cmd.Parameters.AddWithValue("@pid", packageId);
            cmd.Parameters.AddWithValue("@free", freePlaces);

            using var r = cmd.ExecuteReader();
            if (!r.Read()) break;

            waitId = r.GetInt32(0);
            userId = r.GetInt32(1);
            numPersons = r.IsDBNull(2) ? 1 : r.GetInt32(2);
        }

        // ✅ close the WaitingList row so the user won't receive another offer
        using (var cmdInact = new SqlCommand(@"
    UPDATE WaitingList
    SET inactive = 1, notificationDate = GETDATE()
    WHERE Id = @wid AND inactive = 0;
", conn))
        {
            cmdInact.Parameters.AddWithValue("@wid", waitId);

            int okWl = cmdInact.ExecuteNonQuery();
            if (okWl == 0)
            {
                // someone else handled it, continue safely
                continue;
            }
        }

// IMPORTANT:
// reserve places for this offer so nobody else can take them
        using (var cmdHold = new SqlCommand(@"
    UPDATE Package
    SET numFreePlaces = numFreePlaces - @n
    WHERE Id = @pid AND numFreePlaces >= @n;
", conn))
        {
            cmdHold.Parameters.AddWithValue("@n", numPersons);
            cmdHold.Parameters.AddWithValue("@pid", packageId);

            int ok = cmdHold.ExecuteNonQuery();
            if (ok == 0) break; // race condition safety
        }

// insert offer (match the same structure as CartController)
        using (var cmdOffer = new SqlCommand(@"
    INSERT INTO WaitlistOffers (PackageId, UserId, NumPersons, Reason, OfferStart, OfferEnd)
    VALUES (@pid, @uid, @n, @reason, GETDATE(), DATEADD(minute, @mins, GETDATE()));
", conn))
        {
            cmdOffer.Parameters.AddWithValue("@pid", packageId);
            cmdOffer.Parameters.AddWithValue("@uid", userId);
            cmdOffer.Parameters.AddWithValue("@n", numPersons);
            cmdOffer.Parameters.AddWithValue("@reason", reason);
            cmdOffer.Parameters.AddWithValue("@mins", minutes);
            cmdOffer.ExecuteNonQuery();
        }


      
        _notificationService.Create(userId,
            title: "Spot available!",
            message: $"A spot is available for a trip you are waiting for. You have {minutes} minutes to add it to your cart.",
            type: "success",
            linkUrl: "/Users/MyTrips"
        );
    }
}

    }
    
}
    

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;            
using Microsoft.Extensions.Configuration;   
using Microsoft.Data.SqlClient;
using TravelAgency.Models;
using TravelAgency.Services; 
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using System.Net;
using System.Net.Mail;
using Microsoft.AspNetCore.Hosting;


namespace TravelAgency.Controllers
{
    public class CartController : Controller
    {
        private readonly string _connectionString;
        private readonly NotificationService _notificationService;
        private readonly EmailService _emailService;
        private readonly IWebHostEnvironment _env;


        public CartController(IConfiguration config, NotificationService notificationService, EmailService emailService, IWebHostEnvironment env)
        {
            _connectionString = config.GetConnectionString("DefaultConnection")
                                ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
            _notificationService = notificationService;
            _emailService = emailService;
            _env = env;
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
                WHERE userId = @uid
                  AND inactive = 0
                  AND ExpiresAt > GETDATE();", conn);


            cmd.Parameters.AddWithValue("@uid", userId);

            int count = (int)cmd.ExecuteScalar();
            HttpContext.Session.SetInt32("CartCount", count);
            
        }
        
        private void RefreshNotifCount(int userId)
        {
            using var conn = new SqlConnection(_connectionString);
            conn.Open();

            using var cmd = new SqlCommand(@"
        SELECT COUNT(*)
        FROM Notifications
        WHERE UserId = @uid AND IsRead = 0 AND inactive = 0
    ", conn);
      
            cmd.Parameters.AddWithValue("@uid", userId);

            int count = (int)cmd.ExecuteScalar();
            HttpContext.Session.SetInt32("NotifCount", count);
            
        }
        


        private int GetCartTotal(int userId)
        {
            using var conn = new SqlConnection(_connectionString);
            conn.Open();

            using var cmd = new SqlCommand(@"
                SELECT ISNULL(SUM(sum),0)
                FROM shoppingcart
                WHERE userId=@uid AND inactive=0 AND ExpiresAt > GETDATE();", conn);

            
            
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

            int totalPersons = Math.Max(1, adults) + Math.Max(0, children);
            string returnUrl = Request.Headers["Referer"].ToString();

            // If this package is already in cart (SAME passenger count) -> don't add duplicate row
            using (var preConn = new SqlConnection(_connectionString))
            {
                preConn.Open();

                using var preDup = new SqlCommand(@"
                    SELECT TOP (1) Id
                    FROM shoppingcart
                    WHERE userId = @uid
                      AND PackageId = @pid
                      AND numPersons = @n
                      AND inactive = 0
                      AND ExpiresAt > GETDATE();
                ", preConn);

                preDup.Parameters.AddWithValue("@uid", userId.Value);
                preDup.Parameters.AddWithValue("@pid", packageId);
                preDup.Parameters.AddWithValue("@n", totalPersons);

                var existingSame = preDup.ExecuteScalar();
                if (existingSame != null)
                {
                    // Toast (non-modal, auto-hide)
                    TempData["BookToast"] = "This trip is already in your cart (reserved for 15 minutes).";

                    if (!string.IsNullOrEmpty(returnUrl))
                        return Redirect(returnUrl);

                    return RedirectToAction("Gallery", "Package");
                }
            }


            // מחיר לאדם + בדיקת מקומות
            // מחיר לאדם + בדיקת Offer/מקומות
            int pricePerPerson;

int freePlaces;

int? activeOfferId = null;
int? offerPersons = null;

using (var conn = new SqlConnection(_connectionString))
{
    conn.Open();

    // 1) בדיקת Offer פעיל למשתמש על החבילה הזו
    using (var offerCmd = new SqlCommand(@"
        SELECT TOP (1) Id, NumPersons
        FROM WaitlistOffers
       WHERE PackageId = @pid
  AND UserId = @uid
  AND IsUsed = 0
  AND OfferEnd > GETDATE()
  AND ExpiredAt IS NULL

        ORDER BY OfferEnd ASC, Id ASC;
    ", conn))
    {
        offerCmd.Parameters.AddWithValue("@pid", packageId);
        offerCmd.Parameters.AddWithValue("@uid", userId.Value);

        using var or = offerCmd.ExecuteReader();
        if (or.Read())
        {
            activeOfferId = or.GetInt32(0);
            offerPersons = or.IsDBNull(1) ? 1 : or.GetInt32(1);
        }
    }

    // 2) מחיר + numFreePlaces (למקרה שאין Offer)
    using (var cmd = new SqlCommand(@"
        SELECT sum, numFreePlaces
        FROM Package
        WHERE Id = @pid AND inactive = 0;
    ", conn))
    {
        cmd.Parameters.AddWithValue("@pid", packageId);

        using var r = cmd.ExecuteReader();
        if (!r.Read())
        {
            TempData["CartError"] = "This trip is no longer available.";
            return RedirectToAction("Gallery", "Package");
        }


        pricePerPerson = r.GetInt32(0);
        freePlaces = r.GetInt32(1);
    }
}

// אם יש Offer — חייבים להתאים לכמות האנשים של ה-Offer
if (activeOfferId.HasValue)
{
    if (!offerPersons.HasValue) offerPersons = 1;
    
    if (offerPersons.Value != totalPersons)
    {
        TempData["CartError"] = $"This offer is for {offerPersons.Value} passenger(s). Please book with the same number of passengers.";
        return RedirectToAction("PackageDetails", "Package", new { id = packageId, adults = offerPersons.Value, children = 0 });
    }

}
else
{
    // אם אין Offer — בדיקת מקומות רגילה
    if (freePlaces < totalPersons)
    {
        TempData["CartError"] = "Not enough free places for this number of passengers.";
        return RedirectToAction("Gallery", "Package");
    }
}

            int totalSum = pricePerPerson * totalPersons;

            using (var conn = new SqlConnection(_connectionString))
            {
                conn.Open();

                using var tx = conn.BeginTransaction();

                try
                {
                    
                    // ✅ 1) Max 3 active cart items per user
                    using (var cntCmd = new SqlCommand(@"
    SELECT COUNT(*)
    FROM shoppingcart sc
    INNER JOIN Package p ON p.Id = sc.PackageId AND p.inactive = 0
    WHERE sc.userId = @uid
      AND sc.inactive = 0
      AND sc.ExpiresAt > GETDATE();
", conn, tx))

                    {
                        cntCmd.Parameters.AddWithValue("@uid", userId.Value);
                        int activeCount = (int)cntCmd.ExecuteScalar();

                        if (activeCount >= 3)
                        {
                            tx.Rollback();

                            TempData["CartError"] = "You can have up to 3 active trips in your cart.";

                            if (!string.IsNullOrEmpty(returnUrl))
                                return Redirect(returnUrl);

                            return RedirectToAction("Gallery", "Package");

                        
                    }

                    }
                    using (var dupCmd = new SqlCommand(@"
    SELECT TOP (1) Id
    FROM shoppingcart
    WHERE userId = @uid
      AND PackageId = @pid
      AND numPersons = @n
      AND inactive = 0
      AND ExpiresAt > GETDATE();
", conn, tx))

                    {
                        dupCmd.Parameters.AddWithValue("@uid", userId.Value);
                        dupCmd.Parameters.AddWithValue("@pid", packageId);
                        dupCmd.Parameters.AddWithValue("@n", totalPersons);

                        var existing = dupCmd.ExecuteScalar();
                        if (existing != null)
                        {
                            tx.Rollback();

                            // Book(Add): don't add again, stay here, show toast
                            TempData["BookToast"] = "This trip is already in your cart (reserved for 15 minutes).";

                            if (!string.IsNullOrEmpty(returnUrl))
                                return Redirect(returnUrl);

                            return RedirectToAction("Gallery", "Package");

                        }
                    }

                    
                    if (!activeOfferId.HasValue)
                    {
                        using (var hold = new SqlCommand(@"
        UPDATE Package
        SET numFreePlaces = numFreePlaces - @n
        WHERE Id = @pid AND inactive = 0 AND numFreePlaces >= @n;
    ", conn, tx))
                        {
                            hold.Parameters.AddWithValue("@pid", packageId);
                            hold.Parameters.AddWithValue("@n", totalPersons);

                            int ok = hold.ExecuteNonQuery();
                            if (ok == 0)
                            {
                                tx.Rollback();
                                TempData["CartError"] = "Not enough free places for this number of passengers.";
                                return RedirectToAction("Gallery", "Package");
                            }
                        }
                    }
                    else
                    {
                        // יש Offer — מסמנים אותו כ-Used (כדי שלא יישאר פתוח)
                        using (var useCmd = new SqlCommand(@"
UPDATE WaitlistOffers
SET IsUsed = 1, UsedAt = GETDATE()
WHERE Id = @oid
  AND IsUsed = 0
  AND OfferEnd > GETDATE()
  AND ExpiredAt IS NULL;

    ", conn, tx))
                        {
                            useCmd.Parameters.AddWithValue("@oid", activeOfferId.Value);
                            int ok = useCmd.ExecuteNonQuery();
                            if (ok == 0)
                            {
                                tx.Rollback();
                                TempData["CartError"] = "This offer is no longer available.";
                                return RedirectToAction("Gallery", "Package");
                            }
                        }
                    }

// 2) הכנסת שורה לעגלה + טיימר 15 דקות
                    using (var ins = new SqlCommand(@"
    INSERT INTO shoppingcart(userId, PackageId, sum, inactive, numPersons, CreatedAt, ExpiresAt, OfferId)
VALUES(@uid, @pid, @sum, 0, @n, GETDATE(), DATEADD(MINUTE, 15, GETDATE()), @offerId);

", conn, tx))
                    {
                        ins.Parameters.AddWithValue("@uid", userId.Value);
                        ins.Parameters.AddWithValue("@pid", packageId);
                        ins.Parameters.AddWithValue("@sum", totalSum);
                        ins.Parameters.AddWithValue("@n", totalPersons);
                        
                        ins.Parameters.AddWithValue("@offerId", (object?)activeOfferId ?? DBNull.Value);
                        ins.ExecuteNonQuery();
                    }


                    tx.Commit();
                    TempData["BookToast"] = "The trip was added to your cart and reserved for 15 minutes.";
                }
                catch (Exception ex)
                {
                    try { tx.Rollback(); } catch { }

                    Console.WriteLine("ADD FAILED: " + ex);
                    TempData["CartError"] = "Could not add to cart. Please try again.";

                    return RedirectToAction("Gallery", "Package");


                    
                }

            }


            RefreshCartCount(userId.Value);
            RefreshNotifCount(userId.Value);
            
            // ✅ נשארים באותו עמוד
            if (!string.IsNullOrEmpty(returnUrl))
                return Redirect(returnUrl);

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

            int totalPersons = Math.Max(1, adults) + Math.Max(0, children);

            // If this package is already in cart (SAME passenger count) -> go to cart, don't add duplicate row
            using (var preConn = new SqlConnection(_connectionString))
            {
                preConn.Open();

                using var preDup = new SqlCommand(@"
                    SELECT TOP (1) Id
                    FROM shoppingcart
                    WHERE userId = @uid
                      AND PackageId = @pid
                      AND numPersons = @n
                      AND inactive = 0
                      AND ExpiresAt > GETDATE();
                ", preConn);

                preDup.Parameters.AddWithValue("@uid", userId.Value);
                preDup.Parameters.AddWithValue("@pid", packageId);
                preDup.Parameters.AddWithValue("@n", totalPersons);

                var existingSame = preDup.ExecuteScalar();
                if (existingSame != null)
                {
                    TempData["BookToast"] = "This trip is already in your cart (reserved for 15 minutes).";
                    return RedirectToAction("Cart");
                }
            }


            // מחיר לאדם + בדיקת Offer/מקומות
            int pricePerPerson;

int freePlaces;

int? activeOfferId = null;
int? offerPersons = null;

using (var conn = new SqlConnection(_connectionString))
{
    conn.Open();

    // 1) בדיקת Offer פעיל למשתמש על החבילה הזו
    using (var offerCmd = new SqlCommand(@"
        SELECT TOP (1) Id, NumPersons
        FROM WaitlistOffers
       WHERE PackageId = @pid
  AND UserId = @uid
  AND IsUsed = 0
  AND OfferEnd > GETDATE()
  AND ExpiredAt IS NULL

        ORDER BY OfferEnd ASC, Id ASC;
    ", conn))
    {
        offerCmd.Parameters.AddWithValue("@pid", packageId);
        offerCmd.Parameters.AddWithValue("@uid", userId.Value);

        using var or = offerCmd.ExecuteReader();
        if (or.Read())
        {
            activeOfferId = or.GetInt32(0);
            offerPersons = or.IsDBNull(1) ? 1 : or.GetInt32(1);
        }
    }

    // 2) מחיר + numFreePlaces (למקרה שאין Offer)
    using (var cmd = new SqlCommand(@"
        SELECT sum, numFreePlaces
        FROM Package
        WHERE Id = @pid AND inactive = 0;
    ", conn))
    {
        cmd.Parameters.AddWithValue("@pid", packageId);

        using var r = cmd.ExecuteReader();
        if (!r.Read())
        {
            TempData["CartError"] = "This trip is no longer available.";
            return RedirectToAction("Gallery", "Package");
        }


        pricePerPerson = r.GetInt32(0);
        freePlaces = r.GetInt32(1);
    }
}

// אם יש Offer — חייבים להתאים לכמות האנשים של ה-Offer
if (activeOfferId.HasValue)
{
    if (!offerPersons.HasValue) offerPersons = 1;

    if (offerPersons.Value != totalPersons)
    {
        TempData["CartError"] = $"This offer is for {offerPersons.Value} passenger(s). Please book with the same number of passengers.";
        return RedirectToAction("PackageDetails", "Package", new { id = packageId, adults = offerPersons.Value, children = 0 });
    }

}
else
{
    // אם אין Offer — בדיקת מקומות רגילה
    if (freePlaces < totalPersons)
    {
        TempData["CartError"] = "Not enough free places for this number of passengers.";
        return RedirectToAction("Gallery", "Package");
    }
}


            int totalSum = pricePerPerson * totalPersons;

            using (var conn = new SqlConnection(_connectionString))
            {
                conn.Open();

                using var tx = conn.BeginTransaction();

                try
                {
                    
                    // ✅ 1) Max 3 active cart items per user
                    using (var cntCmd = new SqlCommand(@"
    SELECT COUNT(*)
    FROM shoppingcart sc
    INNER JOIN Package p ON p.Id = sc.PackageId AND p.inactive = 0
    WHERE sc.userId = @uid
      AND sc.inactive = 0
      AND sc.ExpiresAt > GETDATE();
", conn, tx))

                    {
                        cntCmd.Parameters.AddWithValue("@uid", userId.Value);
                        int activeCount = (int)cntCmd.ExecuteScalar();

                        if (activeCount >= 3)
                        {
                            tx.Rollback();
                            TempData["CartError"] = "You can have up to 3 active trips in your cart. Please remove one item to continue.";
                            return RedirectToAction("Cart");
                        }


                    }

                    // ✅ Prevent duplicate active row for same package + same passengers (BuyNow)
                    using (var dupCmd = new SqlCommand(@"
    SELECT TOP (1) Id
    FROM shoppingcart
    WHERE userId = @uid
      AND PackageId = @pid
      AND numPersons = @n
      AND inactive = 0
      AND ExpiresAt > GETDATE();
", conn, tx))
                    {
                        dupCmd.Parameters.AddWithValue("@uid", userId.Value);
                        dupCmd.Parameters.AddWithValue("@pid", packageId);
                        dupCmd.Parameters.AddWithValue("@n", totalPersons);

                        var existing = dupCmd.ExecuteScalar();
                        if (existing != null)
                        {
                            tx.Rollback();

                            // BuyNow: go to cart, don't add another duplicate row
                            TempData["BookToast"] = "This trip is already in your cart (reserved for 15 minutes).";
                            return RedirectToAction("Cart");
                        }
                    }

                    // 1) תפיסת מקום אמיתית ל-15 דקות (מוריד מקומות)
                    if (!activeOfferId.HasValue)
                    {
                        // אין Offer -> עושים hold רגיל
                        using (var hold = new SqlCommand(@"
        UPDATE Package
        SET numFreePlaces = numFreePlaces - @n
        WHERE Id = @pid AND inactive = 0 AND numFreePlaces >= @n;
    ", conn, tx))
                        {
                            hold.Parameters.AddWithValue("@pid", packageId);
                            hold.Parameters.AddWithValue("@n", totalPersons);

                            int ok = hold.ExecuteNonQuery();
                            if (ok == 0)
                            {
                                tx.Rollback();
                                TempData["CartError"] = "Not enough free places for this number of passengers.";
                                return RedirectToAction("Gallery", "Package");
                            }
                        }
                    }
                    else
                    {
                        // יש Offer -> מסמנים Used ולא עושים hold
                        using (var useCmd = new SqlCommand(@"
        UPDATE WaitlistOffers
        SET IsUsed = 1, UsedAt = GETDATE()
        WHERE Id = @oid
          AND IsUsed = 0
          AND OfferEnd > GETDATE()
          AND ExpiredAt IS NULL;
    ", conn, tx))
                        {
                            useCmd.Parameters.AddWithValue("@oid", activeOfferId.Value);

                            int ok = useCmd.ExecuteNonQuery();
                            if (ok == 0)
                            {
                                tx.Rollback();
                                TempData["CartError"] = "This offer is no longer available.";
                                return RedirectToAction("Gallery", "Package");
                            }
                        }
                    

                    }


                    // 2) הכנסת שורה לעגלה + טיימר 15 דקות
                    using (var ins = new SqlCommand(@"
            INSERT INTO shoppingcart(userId, PackageId, sum, inactive, numPersons, CreatedAt, ExpiresAt, OfferId)
VALUES(@uid, @pid, @sum, 0, @n, GETDATE(), DATEADD(MINUTE, 15, GETDATE()), @offerId);

        ", conn, tx))
                    {
                        ins.Parameters.AddWithValue("@uid", userId.Value);
                        ins.Parameters.AddWithValue("@pid", packageId);
                        ins.Parameters.AddWithValue("@sum", totalSum);
                        ins.Parameters.AddWithValue("@n", totalPersons);
                        
                        ins.Parameters.AddWithValue("@offerId", (object?)activeOfferId ?? DBNull.Value);

                        ins.ExecuteNonQuery();
                    }

                    tx.Commit();
                }
                catch (Exception ex)
                {
                    try { tx.Rollback(); } catch { }

                    Console.WriteLine("BUYNOW FAILED: " + ex);
                    TempData["CartError"] = "Could not add to cart. Please try again.";

                    return RedirectToAction("Gallery", "Package");

                }

            }


            RefreshCartCount(userId.Value);
            RefreshNotifCount(userId.Value);
            
            return RedirectToAction("Cart");

        }
        [IgnoreAntiforgeryToken]
        [HttpPost]
        public IActionResult ExpireNow()
        {
            var userId = GetUserId();
            if (!userId.HasValue)
                return Unauthorized();

            // ✅ Cleanup is handled by CartCleanupHostedService (single source of truth)
            int removed = 0;

            RefreshCartCount(userId.Value);
            RefreshNotifCount(userId.Value);


            var newCartCount = HttpContext.Session.GetInt32("CartCount") ?? 0;
            var newNotifCount = HttpContext.Session.GetInt32("NotifCount") ?? 0;

            return Json(new { removed, cartCount = newCartCount, notifCount = newNotifCount });
        }

        [HttpGet]
        public IActionResult BadgeCounts()
        {
            var userId = GetUserId();
            if (!userId.HasValue)
                return Json(new { cartCount = 0, notifCount = 0 });

            // מעדכן Session לפי DB (הפונקציות שלך כבר נכונות)
            RefreshCartCount(userId.Value);
            RefreshNotifCount(userId.Value);

            var cartCount = HttpContext.Session.GetInt32("CartCount") ?? 0;
            var notifCount = HttpContext.Session.GetInt32("NotifCount") ?? 0;

            return Json(new { cartCount, notifCount });
        }

        
        
        [HttpGet]
        public IActionResult Index()
        {
            return RedirectToAction("Cart");
        }


        // ---------------------------------------------------------
        // CART PAGE
        // ---------------------------------------------------------
        [HttpGet]
        public IActionResult Cart()
        {
            var userId = GetUserId();
            if (!userId.HasValue)
                return RedirectToAction("Login", "Auth");

            // ✅ ניקוי פגי-תוקף מתבצע ברקע ע"י CartCleanupHostedService
            // כדי למנוע ניקוי כפול (החזרת מקומות פעמיים / Offers כפולים / התראות כפולות)
            
            var items = new List<CartItem>();


            using (var conn = new SqlConnection(_connectionString))
            {
                conn.Open();
                using var cmd = new SqlCommand(@"
    SELECT sc.Id, sc.PackageId, sc.sum, sc.numPersons, sc.ExpiresAt,
           p.destination, p.country, p.startDate, p.endDate,
           (SELECT TOP 1 ImageLocation FROM ImagesPackage WHERE PackageId = p.Id ORDER BY Id) as ImageLocation
    FROM shoppingcart sc
    INNER JOIN Package p ON p.Id = sc.PackageId
    WHERE sc.userId = @uid
      AND sc.inactive = 0
      AND sc.ExpiresAt > GETDATE()
    ORDER BY sc.Id DESC;
", conn);

                cmd.Parameters.AddWithValue("@uid", userId.Value);

                using var r = cmd.ExecuteReader();

                while (r.Read())
                {
                    items.Add(new CartItem
                    {
                        ShoppingCartRowId = r.GetInt32(0),
                        PackageId = r.GetInt32(1),
                        TotalSum = r.GetInt32(2),
                        Quantity = r.GetInt32(3),
                        ExpiresAt = r.GetDateTime(4),

                        Destination = r.IsDBNull(5) ? "" : r.GetString(5),
                        Country = r.IsDBNull(6) ? "" : r.GetString(6),
                        StartDate = r.GetDateTime(7),
                        EndDate = r.GetDateTime(8),


                        Price = (r.GetInt32(2) / Math.Max(1, r.GetInt32(3))),
                        ImageUrl = r.IsDBNull(9) ? "/images/default.jpg" : r.GetString(9)
                    });

                }
            }

            RefreshCartCount(userId.Value);
            RefreshNotifCount(userId.Value);

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

    int packageId = 0; // נשמור כדי ליצור offers אחרי הקומיט
    int? offerId = null;
    bool releasedSeats = false; // האם באמת התפנה מקום
    
    try
    {
        using (var conn = new SqlConnection(_connectionString))
        {
            conn.Open();
            using var tx = conn.BeginTransaction();

            try
            {
                int numPersons;

                // 1) להביא PackageId + numPersons
                using (var sel = new SqlCommand(@"
                    SELECT PackageId, numPersons, OfferId
                    FROM shoppingcart
                   WHERE Id = @rid AND userId = @uid AND inactive = 0;", conn, tx))
                {
                    sel.Parameters.AddWithValue("@rid", rowId);
                    sel.Parameters.AddWithValue("@uid", userId.Value);

                    using var r = sel.ExecuteReader();
                    if (!r.Read())
                    {
                        tx.Rollback();
                        return RedirectToAction("Cart");
                    }

                    packageId = r.GetInt32(0);
                    numPersons = r.IsDBNull(1) ? 1 : r.GetInt32(1);
                    offerId = r.IsDBNull(2) ? (int?)null : r.GetInt32(2);
                }


                // 2) inactive = 1
                int rowsAffected;

                using (var upd = new SqlCommand(@"
                    UPDATE shoppingcart
                    SET inactive = 1
                    WHERE Id = @rid AND userId = @uid AND inactive = 0;", conn, tx))
                {
                    upd.Parameters.AddWithValue("@rid", rowId);
                    upd.Parameters.AddWithValue("@uid", userId.Value);
                    rowsAffected = upd.ExecuteNonQuery();
                }

                if (rowsAffected == 0)
                {
                    tx.Rollback();
                    return RedirectToAction("Cart");
                }


                
// נשתמש בזה כדי לדעת אם באמת התפנה מקום (ואז מותר לייצר offers)
                releasedSeats = false;
// 3) אם השורה הגיעה מ-Offer -> מסמנים את ה-Offer כ-Expired
// (גם אם IsUsed=1, כי הוא "שומש" לצורך הוספה לעגלה אבל לא מומש לתשלום)
// ורק אם הצלחנו לסמן עכשיו ExpiredAt בפועל -> נחזיר מקומות.
                if (offerId.HasValue)
                {
                    int okOffer;

                    using (var exp = new SqlCommand(@"
        UPDATE WaitlistOffers
        SET ExpiredAt = GETDATE()
        WHERE Id = @oid
          AND ExpiredAt IS NULL;
    ", conn, tx))
                    {
                        exp.Parameters.AddWithValue("@oid", offerId.Value);
                        okOffer = exp.ExecuteNonQuery();
                    }

                    // ✅ נחזיר מקומות רק אם באמת סימנו ExpiredAt עכשיו (כדי לא להחזיר פעמיים)
                    if (okOffer > 0)
                    {
                        using (var back = new SqlCommand(@"
            UPDATE Package
            SET numFreePlaces = numFreePlaces + @n
            WHERE Id = @pid;
        ", conn, tx))
                        {
                            back.Parameters.AddWithValue("@pid", packageId);
                            back.Parameters.AddWithValue("@n", numPersons);
                            int seatsRows = back.ExecuteNonQuery();
                            releasedSeats = seatsRows > 0;
                        }
                    }
                    else
                    {
                        releasedSeats = false;
                    }
                }
                else
                {
                    // Hold רגיל
                    using (var back = new SqlCommand(@"
        UPDATE Package
        SET numFreePlaces = numFreePlaces + @n
        WHERE Id = @pid;
    ", conn, tx))
                    {
                        back.Parameters.AddWithValue("@pid", packageId);
                        back.Parameters.AddWithValue("@n", numPersons);
                        int seatsRows = back.ExecuteNonQuery();
                        releasedSeats = seatsRows > 0;
                    }
                }


                tx.Commit();
            }
            catch
            {
                try { tx.Rollback(); } catch { }
                throw;
            }
        }

        // אחרי שהמחיקה בוצעה והטרנזקציה נסגרה — יוצרים offers רק אם באמת התפנה מקום אמיתי
// (כלומר: רק אם הצלחנו להחזיר מקומות בפועל)
        if (releasedSeats && packageId > 0)
        {
            try
            {
                using var c2 = new SqlConnection(_connectionString);
                c2.Open();
                CreateOffersFromWaitlist(c2, packageId, reason: "cart");
            }
            catch (Exception ex2)
            {
                Console.WriteLine("CreateOffersFromWaitlist after RemoveRow failed: " + ex2);
            }
        }



        RefreshCartCount(userId.Value);
        RefreshNotifCount(userId.Value);
        return RedirectToAction("Cart");

    }
    catch (Exception ex)
    {
        TempData["CartError"] = "Could not remove item from cart. Please try again.";
        Console.WriteLine("RemoveRow failed: " + ex);
        return RedirectToAction("Cart");
    }
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

            
            int total = GetCartTotal(userId.Value);
            if (total <= 0)
            {
                TempData["PaymentToastError"] = "Your cart is empty.";
                return RedirectToAction("Cart");

            }

            return View(new PaymentViewModel());
        }
        
        [HttpPost]
        public IActionResult Payment(PaymentViewModel model)
        {
            var userId = GetUserId();
            if (!userId.HasValue)
                return RedirectToAction("Login", "Auth");

            int total = GetCartTotal(userId.Value);
            if (total <= 0)
            {
                TempData["PaymentToastError"] = "Your cart is empty.";
                return RedirectToAction("Cart");
            }

 if (!ModelState.IsValid)
{
    // ✅ סיבה מסכמת (בנוסף ל-errors האדומים ליד השדות)
    var reasons = ModelState.Values
        .SelectMany(v => v.Errors)
        .Select(e => e.ErrorMessage)
        .Where(m => !string.IsNullOrWhiteSpace(m))
        .Distinct()
        .ToList();

    TempData["PaymentToastError"] = reasons.Count > 0
        ? "Payment failed: " + string.Join(" | ", reasons)
        : "Payment failed. Please check the highlighted fields.";

    return View(model); // ✅ נשארים בעמוד Payment
}

try
{
    var insertedReservationIds = new List<int>();
    
    using (var conn = new SqlConnection(_connectionString))
    {
        conn.Open();
        using var tx = conn.BeginTransaction();

        try
        {
            
            int insertedRows = 0;

// 1) להכניס להיסטוריה + לקבל את ה-IDs שנוצרו (כדי לבנות PDF להזמנה הזאת)
            using (var insHist = new SqlCommand(@"
    INSERT INTO HistoryReservation (UserId, PackageId, inactive, numPersons, sum)
    OUTPUT INSERTED.Id
    SELECT userId, PackageId, 0, numPersons, sum
    FROM shoppingcart
    WHERE userId = @uid AND inactive = 0 AND ExpiresAt > GETDATE();
", conn, tx))
            {
                insHist.Parameters.AddWithValue("@uid", userId.Value);

                using var rr = insHist.ExecuteReader();
                while (rr.Read())
                {
                    insertedReservationIds.Add(rr.GetInt32(0));
                    insertedRows++;
                }
            }


// ✅ אם לא הוכנס כלום -> אין מה לשלם באמת (עגלה ריקה / race)
            if (insertedRows <= 0)
            {
                tx.Rollback();
                TempData["PaymentToastError"] = "Your cart is empty.";
                return RedirectToAction("Cart");
            }

// 2) לכבות עגלה (רק אחרי שהכנסנו להיסטוריה)
            using (var updCart = new SqlCommand(@"
    UPDATE shoppingcart
    SET inactive = 1
     WHERE userId = @uid AND inactive = 0 AND ExpiresAt > GETDATE();
", conn, tx))
            {
                updCart.Parameters.AddWithValue("@uid", userId.Value);
                updCart.ExecuteNonQuery();
            }

            tx.Commit();

        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    // ✅ Calculate the REAL total charged from the reservations inserted in THIS payment
    int chargedTotal = GetReservationsTotal(userId.Value, insertedReservationIds);

    _notificationService.Create(
        userId.Value,
        title: "Payment Successful",
        message: $"Your payment was completed successfully. Total charged: ${chargedTotal}",
        type: "success",
        linkUrl: null
    );

    // ✅ Send email with PDF attachment (only on success, and only if PDF is NOT empty)
    try
    {
        var email = GetUserEmail(userId.Value);

        if (string.IsNullOrWhiteSpace(email))
        {
            // Debug-friendly message (you can remove later)
            TempData["PaymentToastError"] = "Email not sent: your account has no email address.";
        }
        else
        {
            var pdfBytes = BuildPaymentReceiptPdf(userId.Value, insertedReservationIds, chargedTotal);

            if (pdfBytes == null || pdfBytes.Length == 0)
            {
                TempData["PaymentToastError"] = "Email not sent: receipt PDF generation failed.";
            }
            else
            {
                // Use the first reservation id as "receipt number" (simple and valid)
                int receiptNo = insertedReservationIds != null && insertedReservationIds.Count > 0
                    ? insertedReservationIds[0]
                    : 0;

                var subject = $"TravelAgency - Payment Receipt #{receiptNo}";
                var body = "";

                var receiptFileName = $"Receipt_{receiptNo}_{DateTime.Now:yyyyMMdd_HHmm}.pdf";
                var detailsFileName = $"Trip_Details_{receiptNo}_{DateTime.Now:yyyyMMdd_HHmm}.pdf";

                var detailsPdfBytes = BuildTripDetailsPdf(userId.Value, insertedReservationIds);

                _emailService.SendWithAttachments(
                    email.Trim(),
                    subject,
                    body,
                    new (byte[] Bytes, string FileName, string ContentType)[]
                    {
                        (pdfBytes, receiptFileName, "application/pdf"),
                        (detailsPdfBytes, detailsFileName, "application/pdf")
                    }
                );
            }
        }
    }

    catch (Exception mailEx)
    {
        // ✅ Show exact error so you can fix SMTP quickly (remove later if you want)
        TempData["PaymentToastError"] = "Email send failed: " + mailEx.Message;
        Console.WriteLine("Email send failed: " + mailEx);
    }

    RefreshCartCount(userId.Value);
    RefreshNotifCount(userId.Value);

    TempData["PaymentToastSuccess"] = "Payment successful!";
    return RedirectToAction("Website", "Feedback");

}
catch
{
    TempData["PaymentToastError"] = "Payment failed. Please try again.";
    return View(model); // ✅ נשארים בעמוד Payment + מציגים הודעה
}


        }
        
        // ✅ Offers expired cleanup is handled by CartCleanupHostedService (single source of truth)
        private int ExpireOldOffers()
        {
            return 0;
        }




        private int ExpireOldCartItems(int userId)
        {
            // ✅ Cart expiration cleanup is handled only by CartCleanupHostedService
            return 0;
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

        // ✅ סגירת רשומת ה-WaitingList כדי שלא תקבל Offer שוב
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
                // מישהו אחר כבר טיפל בזה – ממשיכים בבטחה
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

        // insert offer
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

        string dest = "";
        string country = "";

        using (var cmdTrip = new SqlCommand(@"
SELECT TOP 1 destination, ISNULL(country,'')
FROM Package
WHERE Id = @pid;
", conn))
        {
            cmdTrip.Parameters.AddWithValue("@pid", packageId);
            using var rr = cmdTrip.ExecuteReader();
            if (rr.Read())
            {
                dest = rr.IsDBNull(0) ? "" : rr.GetString(0);
                country = rr.IsDBNull(1) ? "" : rr.GetString(1);
            }
        }

        string tripLabel = string.IsNullOrWhiteSpace(dest) ? "your trip"
            : (string.IsNullOrWhiteSpace(country) ? dest : $"{dest}, {country}");

        _notificationService.Create(userId,
            title: "Spot available!",
            message: $"A spot is available for {tripLabel} for {numPersons} passenger(s). You have {minutes} minutes to add it to your cart.",
            type: "success",
            linkUrl: $"/Package/PackageDetails?id={packageId}&adults={numPersons}&children=0"
        );

    }
}

// -------------------- EMAIL + PDF helpers --------------------

        private class TripDetailsPdfRow
        {
            public int ReservationId { get; set; }
            public int NumPersons { get; set; }

            public int TotalSum { get; set; }
            public int PricePerPerson { get; set; }

            public string Destination { get; set; } = "";
            public string Country { get; set; } = "";
            public DateTime StartDate { get; set; }
            public DateTime EndDate { get; set; }
            public int AgeLimit { get; set; }
            public int CancelDays { get; set; }
            public string Information { get; set; } = "";
            public string CategoryName { get; set; } = "";
            public string ImageLocation { get; set; } = "";
        }

        

private string? GetUserEmail(int userId)
{
    using var conn = new SqlConnection(_connectionString);
    conn.Open();

    using var cmd = new SqlCommand(@"
        SELECT TOP 1 email
        FROM Users
        WHERE Id = @uid AND inactive = 0;
    ", conn);

    cmd.Parameters.AddWithValue("@uid", userId);

    var obj = cmd.ExecuteScalar();
    var email = obj?.ToString();
    return string.IsNullOrWhiteSpace(email) ? null : email.Trim();
}


// ✅ Sum the exact total for THIS payment (only these reservation ids)
private int GetReservationsTotal(int userId, List<int> reservationIds)
{
    if (reservationIds == null || reservationIds.Count == 0)
        return 0;

    using var conn = new SqlConnection(_connectionString);
    conn.Open();

    var ridParams = reservationIds.Select((id, i) => $"@rid{i}").ToList();
    var inClause = string.Join(", ", ridParams);

    using var cmd = new SqlCommand($@"
        SELECT ISNULL(SUM(h.sum), 0)
        FROM HistoryReservation h
        WHERE h.UserId = @uid
          AND h.inactive = 0
          AND h.Id IN ({inClause});
    ", conn);

    cmd.Parameters.AddWithValue("@uid", userId);

    for (int i = 0; i < reservationIds.Count; i++)
        cmd.Parameters.AddWithValue($"@rid{i}", reservationIds[i]);

    return Convert.ToInt32(cmd.ExecuteScalar());
}

private byte[] BuildPaymentReceiptPdf(int userId, List<int> reservationIds, int totalCharged)
{
    // Receipt rows (NO itinerary/info)
    var rows = new List<TripDetailsPdfRow>();

    using (var conn = new SqlConnection(_connectionString))
    {
        conn.Open();

        if (reservationIds == null || reservationIds.Count == 0)
            return Array.Empty<byte>();

        var ridParams = reservationIds
            .Select((id, i) => $"@rid{i}")
            .ToList();

        var inClause = string.Join(", ", ridParams);

        using var cmd = new SqlCommand($@"
SELECT
    h.Id as ReservationId,
    h.numPersons,
    h.sum as TotalSum,
    p.destination,
    ISNULL(p.country,'') as country,
    p.startDate,
    p.endDate,
    p.sum as PricePerPerson
FROM HistoryReservation h
INNER JOIN Package p ON p.Id = h.PackageId
WHERE h.UserId = @uid
  AND h.inactive = 0
  AND h.Id IN ({inClause})
ORDER BY h.Id DESC;
", conn);

        cmd.Parameters.AddWithValue("@uid", userId);

        for (int i = 0; i < reservationIds.Count; i++)
            cmd.Parameters.AddWithValue($"@rid{i}", reservationIds[i]);

        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            rows.Add(new TripDetailsPdfRow
            {
                ReservationId = r.GetInt32(0),
                NumPersons = r.IsDBNull(1) ? 1 : r.GetInt32(1),
                TotalSum = r.IsDBNull(2) ? 0 : Convert.ToInt32(r["TotalSum"]),
                Destination = r["destination"]?.ToString() ?? "",
                Country = r["country"]?.ToString() ?? "",
                StartDate = Convert.ToDateTime(r["startDate"]),
                EndDate = Convert.ToDateTime(r["endDate"]),
                PricePerPerson = Convert.ToInt32(r["PricePerPerson"])
            });
        }
    }

    QuestPDF.Settings.License = LicenseType.Community;

    // ✅ get user display name
    string firstName = "", lastName = "", username = "";
    using (var connU = new SqlConnection(_connectionString))
    {
        connU.Open();
        using var cmdU = new SqlCommand(@"
        SELECT ISNULL(firstName,''), ISNULL(lastName,''), ISNULL(Username,'')
        FROM Users
        WHERE Id = @uid AND inactive = 0;", connU);

        cmdU.Parameters.AddWithValue("@uid", userId);

        using var ru = cmdU.ExecuteReader();
        if (ru.Read())
        {
            firstName = ru.GetString(0);
            lastName = ru.GetString(1);
            username = ru.GetString(2);
        }
    }

    string fullName = (firstName + " " + lastName).Trim();
    if (string.IsNullOrWhiteSpace(fullName)) fullName = username;

    // receipt number: first reservation id (simple)
    int receiptNo = reservationIds != null && reservationIds.Count > 0 ? reservationIds[0] : 0;

    byte[] pdfBytes = Document.Create(container =>
    {
        container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(32);
            page.DefaultTextStyle(x => x.FontSize(12));

            page.Header().Column(h =>
            {
                h.Item().Text("Payment Receipt").FontSize(22).SemiBold().FontColor("#5A189A");
                h.Item().Text($"Receipt #: {receiptNo}").FontSize(11).FontColor(Colors.Grey.Darken2);
                h.Item().Text($"Generated: {DateTime.Now:dd/MM/yyyy HH:mm}").FontSize(10).FontColor(Colors.Grey.Darken2);
                h.Item().PaddingTop(8).LineHorizontal(1).LineColor("#E6E6F0");
            });

            page.Content().PaddingTop(16).Column(col =>
            {
                col.Spacing(12);

                col.Item().Row(r =>
                {
                    r.RelativeItem().Text("Paid by:").SemiBold();
                    r.RelativeItem().AlignRight().Text(fullName);
                });

    col.Item().PaddingTop(6).LineHorizontal(1).LineColor("#E6E6F0");

// ✅ Cards layout (same info, no table)
foreach (var x in rows)
{
    string tripLabel = string.IsNullOrWhiteSpace(x.Country)
        ? x.Destination
        : $"{x.Destination}, {x.Country}";

    string dates = $"{x.StartDate:dd/MM/yyyy} – {x.EndDate:dd/MM/yyyy}";

    col.Item().Element(card =>
    {
        card
            .Border(1).BorderColor("#E6E6F0")
            .Background("#FAFAFF")
            .Padding(14)
            .Column(c =>
            {
                c.Spacing(8);

                // Title
                c.Item().Text(tripLabel).SemiBold().FontSize(14);

                // Dates line
                c.Item().Text(dates).FontSize(10).FontColor(Colors.Grey.Darken2);

                // Details grid
                c.Item().Column(colDetails =>
                {
                    colDetails.Spacing(6);

                    colDetails.Item().Row(rr =>
                    {
                        rr.RelativeItem().Text("Passengers:").SemiBold();
                        rr.ConstantItem(80).AlignRight().Text($"{x.NumPersons}");
                    });

                    colDetails.Item().Row(rr =>
                    {
                        rr.RelativeItem().Text("Price per person:").SemiBold();
                        rr.ConstantItem(80).AlignRight().Text($"${x.PricePerPerson}");
                    });

                    // ✅ Line total מתחת למחיר
                    colDetails.Item().Row(rr =>
                    {
                        rr.RelativeItem().Text("Line total:").SemiBold();
                        rr.ConstantItem(80).AlignRight().Text($"${x.TotalSum}").SemiBold();
                    });
                });

            });
    });
}

col.Item().PaddingTop(10).LineHorizontal(1).LineColor("#E6E6F0");

col.Item().Row(r =>
{
    r.RelativeItem().Text("Total charged:").FontSize(14).SemiBold();
    r.RelativeItem().AlignRight().Text($"${totalCharged}").FontSize(14).SemiBold().FontColor("#5A189A");
});

            });

            page.Footer().AlignCenter().Text(x =>
            {
                x.Span("TravelAgency • ").FontSize(9).FontColor(Colors.Grey.Darken2);
                x.Span("Payment receipt generated automatically").FontSize(9).FontColor(Colors.Grey.Darken2);
            });
        });
    }).GeneratePdf();

    return pdfBytes;
}

// -------------------------------------------------------------


        private string? ResolveWwwRootPathFromImageLocation(string? imageLocation)
        {

            if (string.IsNullOrWhiteSpace(imageLocation))
                return null;

            // ImageLocation in DB is typically like "/uploads/packages/abc.jpg" or "uploads/packages/abc.jpg"
            var rel = imageLocation.Trim();

            // ignore remote urls
            if (rel.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                rel.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                return null;

            if (rel.StartsWith("/"))
                rel = rel.Substring(1);

            // combine with wwwroot
            var full = System.IO.Path.Combine(_env.WebRootPath, rel.Replace("/", System.IO.Path.DirectorySeparatorChar.ToString()));

            if (!System.IO.File.Exists(full))
                return null;

            return full;
        }
private byte[] BuildTripDetailsPdf(int userId, List<int> reservationIds)
{
    // Pull details for the reservations we just inserted
    var rows = new List<TripDetailsPdfRow>();

    using (var conn = new SqlConnection(_connectionString))
    {
        conn.Open();

        if (reservationIds == null || reservationIds.Count == 0)
            return Array.Empty<byte>();

        var ridParams = reservationIds
            .Select((id, i) => $"@rid{i}")
            .ToList();

        var inClause = string.Join(", ", ridParams);

        using var cmd = new SqlCommand($@"
SELECT
    h.Id as ReservationId,
    h.numPersons,
    h.sum as TotalSum,
    p.destination,
    ISNULL(p.country,'') as country,
    p.startDate,
    p.endDate,
    p.sum as PricePerPerson,
    ISNULL(c.name,'') as CategoryName,
    ISNULL(p.ageLimit, 0) as AgeLimit,
    ISNULL(p.cancelationDays, 0) as cancelationDays,
    ISNULL(p.Information, '') as Information,
    ISNULL((
        SELECT TOP 1 ImageLocation
        FROM ImagesPackage
        WHERE PackageId = p.Id
        ORDER BY Id
    ), '') as ImageLocation
FROM HistoryReservation h
INNER JOIN Package p ON p.Id = h.PackageId
LEFT JOIN Category c ON c.Id = p.idCategory
WHERE h.UserId = @uid
  AND h.inactive = 0
  AND h.Id IN ({inClause})
ORDER BY h.Id DESC;
", conn);

        cmd.Parameters.AddWithValue("@uid", userId);

        for (int i = 0; i < reservationIds.Count; i++)
            cmd.Parameters.AddWithValue($"@rid{i}", reservationIds[i]);

        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            rows.Add(new TripDetailsPdfRow
            {
                ReservationId = r.GetInt32(0),
                NumPersons = r.IsDBNull(1) ? 1 : r.GetInt32(1),
                TotalSum = r.IsDBNull(2) ? 0 : Convert.ToInt32(r["TotalSum"]),
                Destination = r["destination"]?.ToString() ?? "",
                Country = r["country"]?.ToString() ?? "",
                StartDate = Convert.ToDateTime(r["startDate"]),
                EndDate = Convert.ToDateTime(r["endDate"]),
                PricePerPerson = Convert.ToInt32(r["PricePerPerson"]),
                CategoryName = r["CategoryName"]?.ToString() ?? "",
                AgeLimit = Convert.ToInt32(r["AgeLimit"]),
                CancelDays = Convert.ToInt32(r["cancelationDays"]),
                Information = r["Information"]?.ToString() ?? "",
                ImageLocation = r["ImageLocation"]?.ToString() ?? ""
            });
        }
    }

    QuestPDF.Settings.License = LicenseType.Community;

    // ✅ get user display name for footer ("Booked by")
    string firstName = "", lastName = "", username = "";
    using (var connU = new SqlConnection(_connectionString))
    {
        connU.Open();
        using var cmdU = new SqlCommand(@"
        SELECT ISNULL(firstName,''), ISNULL(lastName,''), ISNULL(Username,'')
        FROM Users
        WHERE Id = @uid AND inactive = 0;", connU);

        cmdU.Parameters.AddWithValue("@uid", userId);

        using var ru = cmdU.ExecuteReader();
        if (ru.Read())
        {
            firstName = ru.GetString(0);
            lastName = ru.GetString(1);
            username = ru.GetString(2);
        }
    }

    string fullName = (firstName + " " + lastName).Trim();
    if (string.IsNullOrWhiteSpace(fullName)) fullName = username;

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
                    row.RelativeItem().Column(col =>
                    {
                        col.Item().Text("Trip Summary").FontSize(22).SemiBold().FontColor("#5A189A");

                        col.Item().Text($"Generated: {DateTime.Now:dd/MM/yyyy HH:mm}")
                            .FontSize(10).FontColor(Colors.Grey.Darken2);
                    });
                });

                h.Item().PaddingTop(10).LineHorizontal(1).LineColor("#E6E6F0");
            });

            page.Content().PaddingTop(18).Column(col =>
            {
                col.Spacing(14);

                foreach (var x in rows)
                {
                    string cancelText = x.CancelDays <= 0
                        ? "Cancellation is not available for this trip."
                        : $"Cancellation is available up to {x.CancelDays} day(s) before departure.";

                    col.Item().Row(row =>
                    {
                        row.RelativeItem().Column(c =>
                        {
                            c.Item().Text($"{x.Destination} — {x.Country}")
                                .SemiBold().FontSize(16);

                            if (!string.IsNullOrWhiteSpace(x.CategoryName))
                                c.Item().Text($"Category: {x.CategoryName}")
                                    .FontSize(11).FontColor(Colors.Grey.Darken2);

                            c.Item().Text($"{x.StartDate:dd/MM/yyyy}  –  {x.EndDate:dd/MM/yyyy}")
                                .FontSize(11).FontColor(Colors.Grey.Darken2);
                        });

                        row.ConstantItem(150).AlignRight().Element(imgBox =>
                        {
                            try
                            {
                                var imgPath = ResolveWwwRootPathFromImageLocation(x.ImageLocation);
                                if (!string.IsNullOrWhiteSpace(imgPath))
                                {
                                    imgBox
                                        .Height(90)
                                        .Border(1).BorderColor("#E6E6F0")
                                        .Background("#FFFFFF")
                                        .Padding(3)
                                        .Image(imgPath, ImageScaling.FitArea);
                                    return;
                                }
                            }
                            catch { }

                            imgBox.Height(90).Border(1).BorderColor("#E6E6F0").Background("#FAFAFF");
                        });
                    });

                    col.Item().Element(box =>
                    {
                        box.Border(1).BorderColor("#E6E6F0").Background("#FAFAFF").Padding(14).Column(c =>
                        {
                            c.Spacing(6);

                            c.Item().Row(rr =>
                            {
                                rr.RelativeItem().Text("Passengers:").SemiBold();
                                rr.RelativeItem().AlignRight().Text($"{x.NumPersons}");
                            });

                            c.Item().Row(rr =>
                            {
                                rr.RelativeItem().Text("Age limit:").SemiBold();
                                rr.RelativeItem().AlignRight().Text($"{x.AgeLimit}+");
                            });

                            c.Item().Row(rr =>
                            {
                                rr.RelativeItem().Text("Cancellation:").SemiBold();
                                rr.RelativeItem().AlignRight().Text(cancelText);
                            });

                            c.Item().Row(rr =>
                            {
                                rr.RelativeItem().Text("Travel dates:").SemiBold();
                                rr.RelativeItem().AlignRight().Text($"{x.StartDate:dd/MM/yyyy} – {x.EndDate:dd/MM/yyyy}");
                            });
                        });
                    });

                    var info = (x.Information ?? "").ToString();
                    info = info.Replace("\r\n", "\n").Replace("\r", "\n");

                    col.Item().Text("Itinerary & Information").FontSize(15).SemiBold().FontColor("#5A189A");

                    col.Item().Element(e =>
                    {
                        e.Border(1).BorderColor("#EFE7F7").Padding(14).Column(c =>
                        {
                            c.Spacing(6);

                            foreach (var p in info.Split(new[] { "\n\n" }, StringSplitOptions.None))
                            {
                                var paragraph = (p ?? "").Trim();
                                if (string.IsNullOrWhiteSpace(paragraph)) continue;
                                c.Item().Text(paragraph);
                            }
                        });
                    });
                    

                    col.Item().PaddingTop(10).LineHorizontal(1).LineColor("#E6E6F0");
                }
                col.Item().Row(rr =>
                {
                    rr.RelativeItem().Text("Booked by: ").SemiBold();
                    rr.RelativeItem().Text(fullName);
                });

            });

            page.Footer().AlignCenter().Text(x =>
            {
                x.Span("TravelAgency • ").FontSize(9).FontColor(Colors.Grey.Darken2);
                x.Span("Trip details generated automatically").FontSize(9).FontColor(Colors.Grey.Darken2);
            });
        });
    }).GeneratePdf();

    return pdfBytes;
}


    } 
}     


    
 
  
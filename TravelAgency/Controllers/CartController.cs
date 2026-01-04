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

namespace TravelAgency.Controllers
{
    public class CartController : Controller
    {
        private readonly string _connectionString;
        private readonly NotificationService _notificationService;
        private readonly EmailService _emailService;


        public CartController(IConfiguration config, NotificationService notificationService, EmailService emailService)
        {
            _connectionString = config.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
            _notificationService = notificationService;
            _emailService = emailService;
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
            string returnUrl = Request.Headers["Referer"].ToString();

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
            TempData["CartError"] = "Trip not found.";
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
        return RedirectToAction("Gallery", "Package");
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
      AND sc.inactive = 0;
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
      AND inactive = 0;
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
                    TempData["CartError"] = "Could not add to cart. Please try again. (DEV: " + ex.Message + ")";

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

            int totalPersons = Math.Max(1, adults + children);

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
            TempData["CartError"] = "Trip not found.";
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
        return RedirectToAction("Gallery", "Package");
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
      AND sc.inactive = 0;
", conn, tx))
                    {
                        cntCmd.Parameters.AddWithValue("@uid", userId.Value);
                        int activeCount = (int)cntCmd.ExecuteScalar();

                        if (activeCount >= 3)
                        {
                            tx.Rollback();
                            TempData["CartError"] = "You can have up to 3 active trips in your cart.";
                            return RedirectToAction("Gallery", "Package"); // ב-BuyNow אצלך זה Cart
                        }
                    }

                    // ✅ Prevent duplicate active row for same package + same passengers (BuyNow)
                    using (var dupCmd = new SqlCommand(@"
    SELECT TOP (1) Id
    FROM shoppingcart
    WHERE userId = @uid
      AND PackageId = @pid
      AND numPersons = @n
      AND inactive = 0;
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
                    TempData["CartError"] = "Could not add to cart. Please try again. (DEV: " + ex.Message + ")";

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
    WHERE sc.userId = @uid AND sc.inactive = 0
    ORDER BY sc.Id DESC;", conn);

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
                using (var upd = new SqlCommand(@"
                    UPDATE shoppingcart
                    SET inactive = 1
                    WHERE Id = @rid AND userId = @uid;", conn, tx))
                {
                    upd.Parameters.AddWithValue("@rid", rowId);
                    upd.Parameters.AddWithValue("@uid", userId.Value);
                    upd.ExecuteNonQuery();
                }

                // 3) להחזיר מקומות רק אם זה Hold רגיל (כלומר לא הגיע מ-Offer)
// אם OfferId קיים -> המקומות כבר הוקצו/נלקחו בזמן יצירת Offer,
// וב-Add/BuyNow לא הורדנו numFreePlaces, אז אסור להחזיר פה כדי לא לנפח מקומות.
                using (var back = new SqlCommand(@"
                    UPDATE Package
                    SET numFreePlaces = numFreePlaces + @n
                    WHERE Id = @pid;", conn, tx))
                {
                    back.Parameters.AddWithValue("@pid", packageId);
                    back.Parameters.AddWithValue("@n", numPersons);
                    back.ExecuteNonQuery();
                }


                tx.Commit();
            }
            catch
            {
                try { tx.Rollback(); } catch { }
                throw;
            }
        }

// ✅ אחרי שהמחיקה בוצעה והטרנזקציה נסגרה — יוצרים offers רק אם התפנה מקום אמיתי (לא Offer)
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
    WHERE userId = @uid AND inactive = 0;
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
    WHERE userId = @uid AND inactive = 0;
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

    _notificationService.Create(
        userId.Value,
        title: "Payment Successful",
        message: $"Your payment was completed successfully. Total charged: ${total}",
        type: "success",
        linkUrl: null
    );

// ✅ Send email with PDF attachment (only on success)
    try
    {
        var email = GetUserEmail(userId.Value);
        if (!string.IsNullOrWhiteSpace(email))
        {
            // IMPORTANT: insertedReservationIds exists from the insert section above
            var pdfBytes = BuildPaymentReceiptPdf(userId.Value, insertedReservationIds, total);

            var subject = "TravelAgency - Payment Receipt (PDF attached)";
            var body = "Hi,\n\nYour payment was successful. Please find your receipt attached as a PDF.\n\nThank you,\nTravelAgency";

            var fileName = $"Receipt_{DateTime.Now:yyyyMMdd_HHmm}.pdf";

            _emailService.SendWithAttachment(email, subject, body, pdfBytes, fileName);
        }
    }
    catch (Exception mailEx)
    {
        // לא מפילים תשלום בגלל מייל
        Console.WriteLine("Email send failed: " + mailEx.Message);
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

      
        _notificationService.Create(userId,
            title: "Spot available!",
            message: $"A spot is available for a trip you are waiting for. You have {minutes} minutes to add it to your cart.",
            type: "success",
            linkUrl: "/Users/MyTrips"
        );
    }
}
// -------------------- EMAIL + PDF helpers --------------------
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

private byte[] BuildPaymentReceiptPdf(int userId, List<int> reservationIds, int totalCharged)
{
    // Pull details for the reservations we just inserted
    var rows = new List<dynamic>();

    using (var conn = new SqlConnection(_connectionString))
    {
        conn.Open();

        // ✅ safety: אם אין IDs — אין מה למשוך
        if (reservationIds == null || reservationIds.Count == 0)
            return Array.Empty<byte>();

        // ✅ build IN (@rid0,@rid1,...) with parameters
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
    ISNULL(c.name,'') as CategoryName
FROM HistoryReservation h
INNER JOIN Package p ON p.Id = h.PackageId
LEFT JOIN Category c ON c.Id = p.idCategory
WHERE h.UserId = @uid
  AND h.inactive = 0
  AND h.Id IN ({inClause})
ORDER BY h.Id DESC;
", conn);

        cmd.Parameters.AddWithValue("@uid", userId);

        // ✅ add reservation id parameters
        for (int i = 0; i < reservationIds.Count; i++)
            cmd.Parameters.AddWithValue($"@rid{i}", reservationIds[i]);

        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            rows.Add(new
            {
                ReservationId = r.GetInt32(0),
                NumPersons = r.IsDBNull(1) ? 1 : r.GetInt32(1),
                TotalSum = r.IsDBNull(2) ? 0 : Convert.ToInt32(r["TotalSum"]),
                Destination = r["destination"]?.ToString() ?? "",
                Country = r["country"]?.ToString() ?? "",
                StartDate = Convert.ToDateTime(r["startDate"]),
                EndDate = Convert.ToDateTime(r["endDate"]),
                PricePerPerson = Convert.ToInt32(r["PricePerPerson"]),
                CategoryName = r["CategoryName"]?.ToString() ?? ""
            });
        }
    }

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
                h.Item().Text("Payment Receipt").FontSize(22).SemiBold();
                h.Item().Text($"Date: {DateTime.Now:dd/MM/yyyy HH:mm}").FontSize(10).FontColor(Colors.Grey.Darken2);
                h.Item().PaddingTop(8).LineHorizontal(1).LineColor("#E6E6F0");
            });

            page.Content().PaddingTop(16).Column(col =>
            {
                col.Spacing(10);

                col.Item().Text($"Total charged: ${totalCharged}").FontSize(14).SemiBold();

                foreach (var x in rows)
                {
                    col.Item().Element(box =>
                    {
                        box.Border(1).BorderColor("#E6E6F0").Background("#FAFAFF").Padding(12).Column(c =>
                        {
                            c.Spacing(4);

                            c.Item().Text($"{x.Destination} — {x.Country}").SemiBold();
                            if (!string.IsNullOrWhiteSpace(x.CategoryName))
                                c.Item().Text($"Category: {x.CategoryName}").FontSize(10).FontColor(Colors.Grey.Darken2);

                            c.Item().Text($"Dates: {x.StartDate:dd/MM/yyyy} – {x.EndDate:dd/MM/yyyy}");
                            c.Item().Text($"Passengers: {x.NumPersons}");
                            c.Item().Text($"Price per person: ${x.PricePerPerson}");
                            c.Item().Text($"Line total: ${x.TotalSum}").SemiBold();
                        });
                    });
                }

                col.Item().PaddingTop(10).LineHorizontal(1).LineColor("#E6E6F0");
                col.Item().Text("Thank you for booking with TravelAgency!").FontSize(11).FontColor(Colors.Grey.Darken2);
            });

            page.Footer().AlignCenter().Text(x =>
            {
                x.Span("TravelAgency • ").FontSize(9).FontColor(Colors.Grey.Darken2);
                x.Span("This receipt was generated automatically").FontSize(9).FontColor(Colors.Grey.Darken2);
            });
        });
    }).GeneratePdf();

    return pdfBytes;
}
// -------------------------------------------------------------


    
 
    }
}

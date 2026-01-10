using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using TravelAgency.Models;

namespace TravelAgency.Controllers
{
    public class NotificationsController : Controller
    {
        private readonly string _connectionString;

        public NotificationsController(IConfiguration config)
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

        // ✅ מעדכן את הבאדג' ב-Layout דרך Session
        private void RefreshNotifCount(int userId)
        {
            using var conn = new SqlConnection(_connectionString);
            conn.Open();

            using var cmd = new SqlCommand(@"
                SELECT COUNT(*)
                FROM dbo.Notifications
                WHERE UserId = @uid
                  AND IsRead = 0
                  AND inactive = 0;", conn);

            cmd.Parameters.AddWithValue("@uid", userId);

            int count = (int)cmd.ExecuteScalar();
            HttpContext.Session.SetInt32("NotifCount", count);
        }

        // (אופציונלי) אם אי פעם תרצי להביא ספירה ב-AJAX
        [HttpGet]
        public IActionResult UnreadCount()
        {
            var userId = GetUserId();
            if (!userId.HasValue) return Json(new { count = 0 });

            using var conn = new SqlConnection(_connectionString);
            conn.Open();

            using var cmd = new SqlCommand(@"
                SELECT COUNT(*)
                FROM dbo.Notifications
                WHERE UserId = @uid
                  AND IsRead = 0
                  AND inactive = 0;", conn);

            cmd.Parameters.AddWithValue("@uid", userId.Value);

            int count = (int)cmd.ExecuteScalar();
            return Json(new { count });
        }

        // ✅ דף כל ההתראות
        [HttpGet]
        public IActionResult Index()
        {
            var userId = GetUserId();
            if (!userId.HasValue) return RedirectToAction("Login", "Auth");

            var list = new List<NotificationItem>();

            using var conn = new SqlConnection(_connectionString);
            conn.Open();

            using var cmd = new SqlCommand(@"
                SELECT TOP (100)
                    Id, Title, Message, Type, LinkUrl, IsRead, CreatedAt
                FROM dbo.Notifications
                WHERE UserId = @uid
                  AND inactive = 0
                ORDER BY CreatedAt DESC, Id DESC;", conn);

            cmd.Parameters.AddWithValue("@uid", userId.Value);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                // 1) להביא CreatedAt מה-DB (עמודה 6)
                DateTime createdUtc = r.IsDBNull(6)
                    ? DateTime.UtcNow
                    : r.GetDateTime(6);

                // 2) SQL מחזיר DateTime בלי Kind -> נסמן כ-UTC
                createdUtc = DateTime.SpecifyKind(createdUtc, DateTimeKind.Utc);

                // 3) להמיר לשעון ישראל
                var israelTz = TimeZoneInfo.FindSystemTimeZoneById("Israel Standard Time");
                var israelTime = TimeZoneInfo.ConvertTimeFromUtc(createdUtc, israelTz);

                // 4) להוסיף לרשימה
                list.Add(new NotificationItem
                {
                    Id = r.GetInt32(0),
                    Title = r.IsDBNull(1) ? "" : r.GetString(1),
                    Message = r.IsDBNull(2) ? "" : r.GetString(2),
                    Type = r.IsDBNull(3) ? "" : r.GetString(3),
                    LinkUrl = r.IsDBNull(4) ? null : r.GetString(4),
                    IsRead = !r.IsDBNull(5) && r.GetBoolean(5),
                    CreatedAt = israelTime
                });
            }


            RefreshNotifCount(userId.Value); // ✅ חשוב
            return View(list);
        }

        // ✅ "Mark all as read"
        [HttpPost]
        public IActionResult MarkAllRead()
        {
            var userId = GetUserId();
            if (!userId.HasValue) return RedirectToAction("Login", "Auth");

            using var conn = new SqlConnection(_connectionString);
            conn.Open();

            using var cmd = new SqlCommand(@"
                UPDATE dbo.Notifications
                SET IsRead = 1
                WHERE UserId = @uid AND inactive = 0;", conn);

            cmd.Parameters.AddWithValue("@uid", userId.Value);
            cmd.ExecuteNonQuery();

            RefreshNotifCount(userId.Value); // ✅ חשוב
            return RedirectToAction("Index");
        }

        // ✅ "Dismiss" = מחיקה לוגית (inactive=1)
        [HttpPost]
        public IActionResult Dismiss(int id)
        {
            var userId = GetUserId();
            if (!userId.HasValue) return RedirectToAction("Login", "Auth");

            using var conn = new SqlConnection(_connectionString);
            conn.Open();

            using var cmd = new SqlCommand(@"
                UPDATE dbo.Notifications
                SET inactive = 1
                WHERE Id = @id AND UserId = @uid AND inactive = 0;", conn);

            cmd.Parameters.AddWithValue("@id", id);
            cmd.Parameters.AddWithValue("@uid", userId.Value);
            cmd.ExecuteNonQuery();

            RefreshNotifCount(userId.Value); // ✅ חשוב
            return RedirectToAction("Index");
        }

        // ✅ סימון נקרא + מעבר ללינק (אם יש)
        [HttpPost]
        public IActionResult Open(int id)
        {
            var userId = GetUserId();
            if (!userId.HasValue) return RedirectToAction("Login", "Auth");

            string? linkUrl;

            using var conn = new SqlConnection(_connectionString);
            conn.Open();

            using (var cmd = new SqlCommand(@"
                UPDATE dbo.Notifications
                SET IsRead = 1
                OUTPUT INSERTED.LinkUrl
                WHERE Id = @id AND UserId = @uid AND inactive = 0;", conn))
            {
                cmd.Parameters.AddWithValue("@id", id);
                cmd.Parameters.AddWithValue("@uid", userId.Value);

                var result = cmd.ExecuteScalar();
                linkUrl = result == DBNull.Value ? null : result as string;
            }

            RefreshNotifCount(userId.Value); // ✅ חשוב

            if (!string.IsNullOrWhiteSpace(linkUrl))
                return Redirect(linkUrl);

            return RedirectToAction("Index");
        }
                // ✅ Decline offer (for "Spot available!" notification)
        [HttpPost]
        public IActionResult DeclineOffer(int id)
        {
            var userId = GetUserId();
            if (!userId.HasValue) return RedirectToAction("Login", "Auth");

            int uid = userId.Value;

            using var conn = new SqlConnection(_connectionString);
            conn.Open();

            // 1) Get LinkUrl for this notification (must belong to this user)
            string? linkUrl;
            using (var cmd = new SqlCommand(@"
                SELECT LinkUrl
                FROM dbo.Notifications
                WHERE Id=@id AND UserId=@uid AND inactive=0;", conn))
            {
                cmd.Parameters.AddWithValue("@id", id);
                cmd.Parameters.AddWithValue("@uid", uid);
                linkUrl = cmd.ExecuteScalar() as string;
            }

            // If no link - just dismiss
            if (string.IsNullOrWhiteSpace(linkUrl))
            {
                using var dismissCmd = new SqlCommand(@"
                    UPDATE dbo.Notifications
                    SET inactive=1, IsRead=1
                    WHERE Id=@id AND UserId=@uid AND inactive=0;", conn);
                dismissCmd.Parameters.AddWithValue("@id", id);
                dismissCmd.Parameters.AddWithValue("@uid", uid);
                dismissCmd.ExecuteNonQuery();

                RefreshNotifCount(uid);
                return RedirectToAction("Index");
            }

            // 2) Parse packageId + adults from LinkUrl
            //    Supported: ...PackageDetails?id=123&adults=2&children=0  OR packageId=123...
            int? packageId = TryGetIntFromQuery(linkUrl, "id") ?? TryGetIntFromQuery(linkUrl, "packageId");
            int adults = TryGetIntFromQuery(linkUrl, "adults") ?? 1;
            int children = TryGetIntFromQuery(linkUrl, "children") ?? 0;
            int numPersons = Math.Max(1, adults) + Math.Max(0, children);

            if (!packageId.HasValue)
            {
                // Can't identify package => just dismiss notification
                using var dismissCmd = new SqlCommand(@"
                    UPDATE dbo.Notifications
                    SET inactive=1, IsRead=1
                    WHERE Id=@id AND UserId=@uid AND inactive=0;", conn);
                dismissCmd.Parameters.AddWithValue("@id", id);
                dismissCmd.Parameters.AddWithValue("@uid", uid);
                dismissCmd.ExecuteNonQuery();

                RefreshNotifCount(uid);
                return RedirectToAction("Index");
            }

            string? reason = null;
            int? offerId = null;
            int offerNumPersons = 0;

using var tx = conn.BeginTransaction();

try
{
    // 3) Find active offer for this user+package (prefer exact passengers, but use DB NumPersons for seats)
    using (var findOffer = new SqlCommand(@"
        SELECT TOP(1) Id, Reason, NumPersons
        FROM dbo.WaitlistOffers
        WHERE UserId=@uid
          AND PackageId=@pid
          AND IsUsed=0
          AND ExpiredAt IS NULL
          AND OfferEnd > GETDATE()
          AND NumPersons=@n
        ORDER BY OfferStart DESC, Id DESC;", conn, tx))
    {
        findOffer.Parameters.AddWithValue("@uid", uid);
        findOffer.Parameters.AddWithValue("@pid", packageId.Value);
        findOffer.Parameters.AddWithValue("@n", numPersons);

        using var r = findOffer.ExecuteReader();
        if (r.Read())
        {
            offerId = r.GetInt32(0);
            reason = r.IsDBNull(1) ? null : r.GetString(1);
            offerNumPersons = r.IsDBNull(2) ? 0 : r.GetInt32(2);
        }
    }

    // 4) Expire offer + return seats (only once)
    if (offerId.HasValue && offerNumPersons > 0)
    {
        int expired;
        using (var expire = new SqlCommand(@"
            UPDATE dbo.WaitlistOffers
            SET ExpiredAt = GETDATE()
            WHERE Id=@oid
              AND UserId=@uid
              AND IsUsed=0
              AND ExpiredAt IS NULL;", conn, tx))
        {
            expire.Parameters.AddWithValue("@oid", offerId.Value);
            expire.Parameters.AddWithValue("@uid", uid);
            expired = expire.ExecuteNonQuery();
        }

        // Return seats only if we actually expired now
        if (expired == 1)
        {
            using var backSeats = new SqlCommand(@"
                UPDATE dbo.Package
                SET numFreePlaces = numFreePlaces + @n
                WHERE Id=@pid;", conn, tx);
            backSeats.Parameters.AddWithValue("@n", offerNumPersons);
            backSeats.Parameters.AddWithValue("@pid", packageId.Value);
            backSeats.ExecuteNonQuery();
        }
    }




                // 5) Dismiss the notification (logical delete)
                using (var dismiss = new SqlCommand(@"
                    UPDATE dbo.Notifications
                    SET inactive=1, IsRead=1
                    WHERE Id=@id AND UserId=@uid AND inactive=0;", conn, tx))
                {
                    dismiss.Parameters.AddWithValue("@id", id);
                    dismiss.Parameters.AddWithValue("@uid", uid);
                    dismiss.ExecuteNonQuery();
                }

                tx.Commit();
            }
            catch
            {
                tx.Rollback();
                throw;
            }

            // 6) Create next offers from waiting list (if seats are now free)
            //    Use the same reason if we have it, else fallback to "cancel"
            CreateOffersFromWaitlist(packageId.Value, string.IsNullOrWhiteSpace(reason) ? "cancel" : reason);

            RefreshNotifCount(uid);
            return RedirectToAction("Index");
        }

        // ---------- Helpers (keep inside this controller) ----------
        private static int? TryGetIntFromQuery(string url, string key)
        {
            // Works with absolute/relative URL
            string query = "";
            var qIndex = url.IndexOf("?", StringComparison.Ordinal);
            if (qIndex >= 0 && qIndex < url.Length - 1)
                query = url[(qIndex + 1)..];

            if (string.IsNullOrWhiteSpace(query)) return null;

            foreach (var part in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var kv = part.Split('=', 2);
                if (kv.Length != 2) continue;

                var k = Uri.UnescapeDataString(kv[0]);
                if (!string.Equals(k, key, StringComparison.OrdinalIgnoreCase)) continue;

                var v = Uri.UnescapeDataString(kv[1]);
                if (int.TryParse(v, out int num)) return num;
            }
            return null;
        }

        private void CreateOffersFromWaitlist(int packageId, string reason)
        {
            using var conn = new SqlConnection(_connectionString);
            conn.Open();

            // Create offers while there are free places and there is someone waiting that fits
            while (true)
            {
                int freePlaces;
                string destination = "";
                string country = "";

                using (var pkgCmd = new SqlCommand(@"
                    SELECT numFreePlaces, destination, country
                    FROM dbo.Package
                    WHERE Id=@pid;", conn))
                {
                    pkgCmd.Parameters.AddWithValue("@pid", packageId);
                    using var r = pkgCmd.ExecuteReader();
                    if (!r.Read()) return;

                    freePlaces = r.GetInt32(0);
                    destination = r.IsDBNull(1) ? "" : r.GetString(1);
                    country = r.IsDBNull(2) ? "" : r.GetString(2);
                }

                if (freePlaces <= 0) return;

                int? wlId = null;
                int? wlUserId = null;
                int wlNumPersons = 0;

                using (var pick = new SqlCommand(@"
                    SELECT TOP(1) Id, UserId, numPersons
                    FROM dbo.WaitingList
                    WHERE PackageId=@pid AND inactive=0 AND numPersons <= @free
                    ORDER BY JoinDate ASC, Id ASC;", conn))
                {
                    pick.Parameters.AddWithValue("@pid", packageId);
                    pick.Parameters.AddWithValue("@free", freePlaces);

                    using var r = pick.ExecuteReader();
                    if (!r.Read()) return;

                    wlId = r.GetInt32(0);
                    wlUserId = r.GetInt32(1);
                    wlNumPersons = r.IsDBNull(2) ? 1 : r.GetInt32(2);
                }

                // Offer window: 15min for "cart", 60min for "cancel"
                int minutes = string.Equals(reason, "cart", StringComparison.OrdinalIgnoreCase) ? 15 : 60;

                using var tx = conn.BeginTransaction();
                try
                {
                    // Inactivate waiting list row
                    using (var offWL = new SqlCommand(@"
                        UPDATE dbo.WaitingList
                        SET inactive=1, notificationDate=GETDATE()
                        WHERE Id=@id AND inactive=0;", conn, tx))
                    {
                        offWL.Parameters.AddWithValue("@id", wlId!.Value);
                        offWL.ExecuteNonQuery();
                    }

                    // Reserve seats (since offer is exclusive)
                    using (var takeSeats = new SqlCommand(@"
                        UPDATE dbo.Package
                        SET numFreePlaces = numFreePlaces - @n
                        WHERE Id=@pid AND numFreePlaces >= @n;", conn, tx))
                    {
                        takeSeats.Parameters.AddWithValue("@n", wlNumPersons);
                        takeSeats.Parameters.AddWithValue("@pid", packageId);

                        int ok = takeSeats.ExecuteNonQuery();
                        if (ok != 1)
                        {
                            tx.Rollback();
                            continue; // try again
                        }
                    }

                    // Insert offer
                    int newOfferId;
                    using (var insOffer = new SqlCommand(@"
                        INSERT INTO dbo.WaitlistOffers (PackageId, UserId, NumPersons, Reason, OfferStart, OfferEnd, IsUsed)
                        OUTPUT INSERTED.Id
                        VALUES (@pid, @uid, @n, @reason, GETDATE(), DATEADD(MINUTE, @mins, GETDATE()), 0);", conn, tx))
                    {
                        insOffer.Parameters.AddWithValue("@pid", packageId);
                        insOffer.Parameters.AddWithValue("@uid", wlUserId!.Value);
                        insOffer.Parameters.AddWithValue("@n", wlNumPersons);
                        insOffer.Parameters.AddWithValue("@reason", reason);
                        insOffer.Parameters.AddWithValue("@mins", minutes);

                        newOfferId = (int)insOffer.ExecuteScalar();
                    }

                    // Insert notification for that user
                    var msg = $"A spot is available for {destination}, {country} (for {wlNumPersons} traveler(s)).";
                    var link = $"/Package/PackageDetails?id={packageId}&adults={wlNumPersons}&children=0";

                    using (var insNotif = new SqlCommand(@"
                        INSERT INTO dbo.Notifications (UserId, Title, Message, Type, LinkUrl, IsRead, inactive)
                        VALUES (@uid, @title, @msg, @type, @link, 0, 0);", conn, tx))
                    {
                        insNotif.Parameters.AddWithValue("@uid", wlUserId.Value);
                        insNotif.Parameters.AddWithValue("@title", "Spot available!");
                        insNotif.Parameters.AddWithValue("@msg", msg);
                        insNotif.Parameters.AddWithValue("@type", "success");
                        insNotif.Parameters.AddWithValue("@link", link);
                        insNotif.ExecuteNonQuery();
                    }

                    tx.Commit();
                }
                catch
                {
                    tx.Rollback();
                    throw;
                }
            }
        }

    }
    
}
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
    }
    
}
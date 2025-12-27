using Microsoft.Data.SqlClient;

namespace TravelAgency.Middleware
{
    public class BadgeCountsMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly string _cs;

        public BadgeCountsMiddleware(RequestDelegate next, IConfiguration config)
        {
            _next = next;
            _cs = config.GetConnectionString("DefaultConnection")
                 ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
        }

        public async Task Invoke(HttpContext context)
        {
            // אם אין Session פעיל עדיין - ממשיכים
            if (!context.Session.IsAvailable)
            {
                await _next(context);
                return;
            }

            var userIdStr = context.Session.GetString("UserId");
            if (int.TryParse(userIdStr, out int userId))
            {
                using var conn = new SqlConnection(_cs);
                await conn.OpenAsync();
                
                using (var cmd = new SqlCommand(@"
    SELECT COUNT(*)
    FROM shoppingcart sc
    INNER JOIN Package p ON p.Id = sc.PackageId AND p.inactive = 0
    WHERE sc.userId = @uid
      AND sc.inactive = 0;", conn))
                {
                    cmd.Parameters.AddWithValue("@uid", userId);
                    int cartCount = (int)await cmd.ExecuteScalarAsync();
                    context.Session.SetInt32("CartCount", cartCount);
                }


                // NotifCount
                using (var cmd = new SqlCommand(@"
                    SELECT COUNT(*)
                    FROM dbo.Notifications
                    WHERE UserId = @uid AND IsRead = 0 AND inactive = 0;", conn))
                {
                    cmd.Parameters.AddWithValue("@uid", userId);
                    int notifCount = (int)await cmd.ExecuteScalarAsync();
                    context.Session.SetInt32("NotifCount", notifCount);
                }
            }

            await _next(context);
        }
    }
}

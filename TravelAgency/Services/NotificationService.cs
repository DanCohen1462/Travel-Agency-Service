using Microsoft.Data.SqlClient;

namespace TravelAgency.Services
{
    public class NotificationService
    {
        private readonly string _connectionString;

        public NotificationService(IConfiguration config)
        {
            _connectionString = config.GetConnectionString("DefaultConnection")
                                ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
        }

        public void Create(int userId, string title, string message, string type = "info", string? linkUrl = null)
        {
            using var conn = new SqlConnection(_connectionString);
            conn.Open();

            using var cmd = new SqlCommand(@"
                INSERT INTO dbo.Notifications (UserId, Title, Message, Type, LinkUrl, IsRead, CreatedAt, inactive)
                VALUES (@uid, @title, @msg, @type, @link, 0, GETUTCDATE(), 0);", conn);

            cmd.Parameters.AddWithValue("@uid", userId);
            cmd.Parameters.AddWithValue("@title", title);
            cmd.Parameters.AddWithValue("@msg", message);
            cmd.Parameters.AddWithValue("@type", type);
            cmd.Parameters.AddWithValue("@link", (object?)linkUrl ?? DBNull.Value);

            cmd.ExecuteNonQuery();
        }
        
    }
}
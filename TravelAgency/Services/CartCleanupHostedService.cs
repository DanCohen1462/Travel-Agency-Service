using Microsoft.Data.SqlClient;
using TravelAgency.Services;

namespace TravelAgency.Services
{
    public class CartCleanupHostedService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IConfiguration _config;

        public CartCleanupHostedService(IServiceScopeFactory scopeFactory, IConfiguration config)
        {
            _scopeFactory = scopeFactory;
            _config = config;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // רץ כל דקה (אפשר לשנות)
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                   

                    await CleanupOnce(stoppingToken);
                }
                catch
                {
                    // בכוונה שקט: לא להפיל את האפליקציה אם ניקוי נכשל
                }

                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            }
        }

        private async Task CleanupOnce(CancellationToken ct)
        {
            using var scope = _scopeFactory.CreateScope();
            var notificationService = scope.ServiceProvider.GetRequiredService<NotificationService>();

            var cs = _config.GetConnectionString("DefaultConnection")
                     ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

           using var conn = new SqlConnection(cs);
await conn.OpenAsync(ct);

// ✅ 0) ניקוי Offers שפגו (WaitlistOffers) => להחזיר מקומות ולתת Offer הבא
var expiredOffers = new List<(int OfferId, int UserId, int PackageId, int NumPersons, string Reason)>();

using (var selOff = new SqlCommand(@"
SELECT Id, UserId, PackageId, NumPersons, Reason
FROM WaitlistOffers
WHERE IsUsed = 0
  AND OfferEnd <= GETDATE()
  AND ExpiredAt IS NULL;
", conn))

{
    using var r = await selOff.ExecuteReaderAsync(ct);
    while (await r.ReadAsync(ct))
    {
        expiredOffers.Add((
            r.GetInt32(0),
            r.GetInt32(1),
            r.GetInt32(2),
            r.IsDBNull(3) ? 1 : r.GetInt32(3),
            r.IsDBNull(4) ? "cart" : r.GetString(4)
        ));
    }
}

foreach (var off in expiredOffers)
{
    using var tx = conn.BeginTransaction();

    try
    {
        // 0.1) לסמן את ההצעה כלא פעילה כדי שלא נטפל שוב
        using (var upd = new SqlCommand(@"
UPDATE WaitlistOffers
SET ExpiredAt = GETDATE()
WHERE Id = @oid
  AND IsUsed = 0
  AND OfferEnd <= GETDATE()
  AND ExpiredAt IS NULL;
", conn, tx))
        {
            upd.Parameters.AddWithValue("@oid", off.OfferId);
            int ok = await upd.ExecuteNonQueryAsync(ct);
            if (ok == 0)
            {
                tx.Rollback();
                continue;
            }
        }

        // 0.2) להחזיר מקומות לחבילה
        using (var back = new SqlCommand(@"
UPDATE Package
SET numFreePlaces = numFreePlaces + @n
WHERE Id = @pid;
", conn, tx))
        {
            back.Parameters.AddWithValue("@n", off.NumPersons);
            back.Parameters.AddWithValue("@pid", off.PackageId);
            await back.ExecuteNonQueryAsync(ct);
        }

        tx.Commit();
    }
    catch
    {
        try { tx.Rollback(); } catch { }
        continue;
    }

    // 0.3) לתת Offer הבא בתור (לפי אותו reason)
    try
    {
        CreateOffersFromWaitlist(conn, notificationService, off.PackageId, reason: off.Reason);
    }
    catch { }
}

// 1) מציאת שורות עגלה שפג תוקפן (מכל המשתמשים)
var expiredRows = new List<(int CartId, int UserId, int PackageId, int NumPersons)>();


            using (var sel = new SqlCommand(@"
SELECT Id, userId, PackageId, numPersons
FROM shoppingcart
WHERE inactive = 0 AND ExpiresAt <= GETDATE();
", conn))
            {
                using var r = await sel.ExecuteReaderAsync(ct);
                while (await r.ReadAsync(ct))
                {
                    expiredRows.Add((
                        r.GetInt32(0),
                        r.GetInt32(1),
                        r.GetInt32(2),
                        r.IsDBNull(3) ? 1 : r.GetInt32(3)
                    ));
                }
            }



            foreach (var row in expiredRows)
            {
                using var tx = conn.BeginTransaction();

                try
                {
                    // 1) לכבות רק את השורה הזו, ורק אם עדיין פעילה
                    using (var updOne = new SqlCommand(@"
UPDATE shoppingcart
SET inactive = 1
WHERE Id = @rid AND inactive = 0;
", conn, tx))
                    {
                        updOne.Parameters.AddWithValue("@rid", row.CartId);
                        int ok = await updOne.ExecuteNonQueryAsync(ct);

                        if (ok == 0)
                        {
                            tx.Rollback();
                            continue; // כבר טופל
                        }
                    }

                    // 2) להחזיר מקומות
                    using (var back = new SqlCommand(@"
UPDATE Package
SET numFreePlaces = numFreePlaces + @n
WHERE Id = @pid;
", conn, tx))
                    {
                        back.Parameters.AddWithValue("@n", row.NumPersons);
                        back.Parameters.AddWithValue("@pid", row.PackageId);
                        await back.ExecuteNonQueryAsync(ct);
                    }

                    tx.Commit();
                }
                catch
                {
                    try { tx.Rollback(); } catch { }
                    continue;
                }

                // 3) אחרי קומיט – ליצור offers + התראות (בלי tx)
                try
                {
                    CreateOffersFromWaitlist(conn, notificationService, row.PackageId, reason: "cart");
                }
                catch { }

                string dest = "";
                string country = "";

                using (var cmdTrip = new SqlCommand(@"
SELECT TOP 1 destination, ISNULL(country,'')
FROM Package
WHERE Id = @pid;
", conn))
                {
                    cmdTrip.Parameters.AddWithValue("@pid", row.PackageId);
                    using var rr = cmdTrip.ExecuteReader();
                    if (rr.Read())
                    {
                        dest = rr.IsDBNull(0) ? "" : rr.GetString(0);
                        country = rr.IsDBNull(1) ? "" : rr.GetString(1);
                    }
                }

                string tripLabel = string.IsNullOrWhiteSpace(dest) ? "your trip" :
                    (string.IsNullOrWhiteSpace(country) ? dest : $"{dest}, {country}");

                notificationService.Create(
                    row.UserId,
                    title: "Cart reservation expired",
                    message: $"Your 15-minute reservation expired for {tripLabel}.",
                    type: "warning",
                    linkUrl: "/Cart/Cart"
                );

            }

        }

        // העתקתי את אותה פונקציה שלך, רק עם NotificationService כפרמטר
        private void CreateOffersFromWaitlist(SqlConnection conn, NotificationService notificationService, int packageId, string reason)
        {
            int minutes = (reason == "cancel") ? 60 : 15;

            while (true)
            {
                int freePlaces;
                using (var cmdFree = new SqlCommand(@"
SELECT numFreePlaces FROM Package WHERE Id = @pid;
", conn))
                {
                    cmdFree.Parameters.AddWithValue("@pid", packageId);
                    freePlaces = (int)cmdFree.ExecuteScalar();
                }

                if (freePlaces <= 0) break;

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

                using (var cmdInact = new SqlCommand(@"
UPDATE WaitingList
SET inactive = 1, notificationDate = GETDATE()
WHERE Id = @wid AND inactive = 0;
", conn))
                {
                    cmdInact.Parameters.AddWithValue("@wid", waitId);
                    int okWl = cmdInact.ExecuteNonQuery();
                    if (okWl == 0) continue;
                }

                using (var cmdHold = new SqlCommand(@"
UPDATE Package
SET numFreePlaces = numFreePlaces - @n
WHERE Id = @pid AND numFreePlaces >= @n;
", conn))
                {
                    cmdHold.Parameters.AddWithValue("@n", numPersons);
                    cmdHold.Parameters.AddWithValue("@pid", packageId);
                    int ok = cmdHold.ExecuteNonQuery();
                    if (ok == 0) break;
                }

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

                notificationService.Create(
                    userId,
                    title: "Spot available!",
                    message: $"A spot is available for a trip you are waiting for. You have {minutes} minutes to add it to your cart.",
                    type: "success",
                    linkUrl: "/Users/MyTrips"
                );
            }
        }
    }
}

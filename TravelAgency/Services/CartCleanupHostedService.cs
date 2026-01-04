
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
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

var expiredRows = new List<(int CartId, int UserId, int PackageId, int NumPersons, int? OfferId, string OfferReason, string TripLabel)>();

using (var sel = new SqlCommand(@"
SELECT 
    sc.Id, 
    sc.userId, 
    sc.PackageId, 
    sc.numPersons,
    sc.OfferId,
    ISNULL(o.Reason,'cart') as OfferReason,
    ISNULL(p.destination,'') as destination,
    ISNULL(p.country,'') as country
FROM shoppingcart sc
INNER JOIN Package p ON p.Id = sc.PackageId
LEFT JOIN WaitlistOffers o ON o.Id = sc.OfferId
WHERE sc.inactive = 0 
  AND sc.ExpiresAt <= GETDATE();
", conn))
{
    using var r = await sel.ExecuteReaderAsync(ct);
    while (await r.ReadAsync(ct))
    {
        int cartId = r.GetInt32(0);
        int userId = r.GetInt32(1);
        int packageId = r.GetInt32(2);
        int numPersons = r.IsDBNull(3) ? 1 : r.GetInt32(3);
        int? offerId = r.IsDBNull(4) ? (int?)null : r.GetInt32(4);
        string offerReason = r.IsDBNull(5) ? "cart" : r.GetString(5);

        string dest = r.IsDBNull(6) ? "" : r.GetString(6);
        string country = r.IsDBNull(7) ? "" : r.GetString(7);

        string tripLabel = string.IsNullOrWhiteSpace(dest) ? "your trip"
            : (string.IsNullOrWhiteSpace(country) ? dest : $"{dest}, {country}");

        expiredRows.Add((cartId, userId, packageId, numPersons, offerId, offerReason, tripLabel));
    }
}



var expiredByUser = new Dictionary<int, List<string>>();

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

// 2) אם זה הגיע מ-Offer -> נסמן את ה-Offer כ-Expired ונחזיר מקומות רק אם באמת סימנו עכשיו
// אם זה Hold רגיל (OfferId==null) -> מחזירים מקומות כרגיל
                    if (row.OfferId != null)
                    {
                        int okOffer;
                        using (var updOffer = new SqlCommand(@"
UPDATE WaitlistOffers
SET ExpiredAt = GETDATE()
WHERE Id = @oid
  AND ExpiredAt IS NULL;
", conn, tx))
                        {
                            updOffer.Parameters.AddWithValue("@oid", row.OfferId.Value);
                            okOffer = await updOffer.ExecuteNonQueryAsync(ct);
                        }

                        if (okOffer > 0)
                        {
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
                        }
                    }
                    else
                    {
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
                    }


                    tx.Commit();

                }
                catch
                {
                    try { tx.Rollback(); } catch { }
                    continue;
                }

// 3) אחרי קומיט – ליצור offers (בלי tx)
                try
                {
                    CreateOffersFromWaitlist(conn, notificationService, row.PackageId, reason: row.OfferReason);

                }
                catch { }

// ✅ במקום Notification לכל שורה — אוספים לפי משתמש (TripLabel כבר כולל יעד+מדינה)
                if (!expiredByUser.ContainsKey(row.UserId))
                    expiredByUser[row.UserId] = new List<string>();

                if (!string.IsNullOrWhiteSpace(row.TripLabel))
                    expiredByUser[row.UserId].Add(row.TripLabel);
}

// ✅ B2: אחרי שסיימנו לאסוף — שולחים התראה אחת מאוחדת לכל משתמש,
// ובנוסף “מכבים” התראות קודמות מאותו סוג כדי שלא יישארו כפולות.
foreach (var kv in expiredByUser)
{
    int uid = kv.Key;

    // unique labels + nice formatting
    var labels = kv.Value
        .Where(s => !string.IsNullOrWhiteSpace(s))
        .Select(s => s.Trim())
        .Distinct()
        .ToList();

    if (labels.Count == 0) 
        continue;

    // ✅ 1) לפני שמכבים — קוראים את ההודעות הקודמות הפעילות ומחלצים מהן יעדים
    using (var readPrev = new SqlCommand(@"
SELECT Message
FROM Notifications
WHERE UserId = @uid
  AND inactive = 0
  AND Title = 'Cart reservation expired'
  AND Type = 'warning';
", conn))
    {
        readPrev.Parameters.AddWithValue("@uid", uid);
        using var rr = await readPrev.ExecuteReaderAsync(ct);
        while (await rr.ReadAsync(ct))
        {
            string prevMsg = rr.IsDBNull(0) ? "" : rr.GetString(0);
            foreach (var t in ExtractTripLabelsFromExpiredMessage(prevMsg))
                labels.Add(t);
        }
    }

    // ✅ unique again אחרי שהוספנו מהעבר
    labels = labels
        .Where(s => !string.IsNullOrWhiteSpace(s))
        .Select(s => s.Trim())
        .Distinct()
        .ToList();

    if (labels.Count == 0)
        continue;

    string message = labels.Count == 1
        ? $"Your 15-minute reservation expired for {labels[0]}."
        : $"Your 15-minute reservations expired for:\n- {string.Join("\n- ", labels)}";

    // ✅ 2) עכשיו מכבים את הקודמות כדי שלא יישארו כפולות
    using (var deact = new SqlCommand(@"
UPDATE Notifications
SET inactive = 1
WHERE UserId = @uid
  AND inactive = 0
  AND Title = 'Cart reservation expired'
  AND Type = 'warning';
", conn))
    {

        deact.Parameters.AddWithValue("@uid", uid);
        await deact.ExecuteNonQueryAsync(ct);
    }



    notificationService.Create(
        uid,
        title: "Cart reservation expired",
        message: message,
        type: "warning",
        linkUrl: "/Cart/Cart"
    );



        }

    }


        
        private static List<string> ExtractTripLabelsFromExpiredMessage(string msg)
        {
            var res = new List<string>();
            if (string.IsNullOrWhiteSpace(msg))
                return res;

            msg = msg.Replace("\r\n", "\n").Trim();

            // פורמט מרובה:
            // "Your 15-minute reservations expired for:\n- A\n- B"
            var lines = msg.Split('\n');
            foreach (var line in lines)
            {
                var t = line.Trim();
                if (t.StartsWith("- "))
                {
                    var label = t.Substring(2).Trim();
                    if (!string.IsNullOrWhiteSpace(label))
                        res.Add(label);
                }
            }

            if (res.Count > 0)
                return res;

            // פורמט יחיד:
            // "Your 15-minute reservation expired for X."
            const string prefix = "Your 15-minute reservation expired for ";
            if (msg.StartsWith(prefix))
            {
                var label = msg.Substring(prefix.Length).Trim();
                if (label.EndsWith("."))
                    label = label.Substring(0, label.Length - 1).Trim();

                if (!string.IsNullOrWhiteSpace(label))
                    res.Add(label);
            }

            return res;
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

                notificationService.Create(
                    userId,
                    title: "Spot available!",
                    message: $"A spot is available for {tripLabel} for {numPersons} passenger(s). You have {minutes} minutes to add it to your cart.",
                    type: "success",
                    linkUrl: $"/Package/PackageDetails?packageId={packageId}&adults={numPersons}&children=0"
                );


            }
        }
    }
}


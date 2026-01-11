using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System;
using TravelAgency.Models;

namespace TravelAgency.Controllers
{
    public class WaitlistController : Controller
    {
        private readonly string _connectionString;

        public WaitlistController(IConfiguration config)
        {
            _connectionString = config.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
        }

        // ✅ NEW: info for modal (users ahead + estimated days up to 20)
[HttpGet]
public IActionResult Info(int packageId)
{
    var userIdStr = HttpContext.Session.GetString("UserId");
    if (string.IsNullOrEmpty(userIdStr))
        return Unauthorized();

    int userId = int.Parse(userIdStr);

    using (var conn = new SqlConnection(_connectionString))
    {
        conn.Open();

        // ✅ Check if user already joined (active)
        bool alreadyJoined = false;
        int joinedNumPersons = 0;

        using (var cmdJoined = new SqlCommand(@"
            SELECT TOP 1 numPersons
            FROM WaitingList
            WHERE PackageId = @pid
              AND UserId = @uid
              AND inactive = 0
            ORDER BY JoinDate ASC, Id ASC;
        ", conn))
        {
            cmdJoined.Parameters.AddWithValue("@pid", packageId);
            cmdJoined.Parameters.AddWithValue("@uid", userId);

            var obj = cmdJoined.ExecuteScalar();
            if (obj != null && obj != DBNull.Value)
            {
                alreadyJoined = true;
                joinedNumPersons = Convert.ToInt32(obj);
            }
        }


        // 1) find the user's JoinDate for this package (if already in waitlist)
        string myJoinDateSql = @"
            SELECT TOP 1 JoinDate
            FROM WaitingList
            WHERE UserId=@u AND PackageId=@p AND inactive=0
            ORDER BY JoinDate ASC;
        ";

        DateTime? myJoinDate = null;
        using (var cmd = new SqlCommand(myJoinDateSql, conn))
        {
            cmd.Parameters.AddWithValue("@u", userId);
            cmd.Parameters.AddWithValue("@p", packageId);

            var obj = cmd.ExecuteScalar();
            if (obj != null && obj != DBNull.Value)
                myJoinDate = (DateTime)obj;
        }

        int usersAhead;

        if (myJoinDate.HasValue)
        {
            // already in list -> count users before him
            string aheadSql = @"
                SELECT COUNT(*)
                FROM WaitingList
                WHERE PackageId=@p AND inactive=0
                  AND (JoinDate < @myDate OR (JoinDate = @myDate AND Id < (
                        SELECT TOP 1 Id FROM WaitingList
                        WHERE UserId=@u AND PackageId=@p AND inactive=0
                        ORDER BY JoinDate ASC, Id ASC
                  )));
            ";

            using (var cmd = new SqlCommand(aheadSql, conn))
            {
                cmd.Parameters.AddWithValue("@p", packageId);
                cmd.Parameters.AddWithValue("@u", userId);
                cmd.Parameters.AddWithValue("@myDate", myJoinDate.Value);
                usersAhead = (int)cmd.ExecuteScalar();
            }
        }
        else
        {
            // not in list yet -> count all current waiters (he will be after them)
            string countSql = @"
                SELECT COUNT(*)
                FROM WaitingList
                WHERE PackageId=@p AND inactive=0;
            ";

            using (var cmd = new SqlCommand(countSql, conn))
            {
                cmd.Parameters.AddWithValue("@p", packageId);
                usersAhead = (int)cmd.ExecuteScalar();
            }
        }


        // ✅ Determine ETA unit (15 vs 60) based on the REAL temporary block reason:
        // - Active cart hold => 15
        // - Active offer reason='cart' => 15
        // - Active offer reason='cancel' => 60
        // - No temp blocks => 60
        int minutesPerUser;

        using (var cmd = new SqlCommand(@"
            SELECT CASE
                WHEN EXISTS (
                    SELECT 1
                    FROM shoppingcart
                    WHERE PackageId = @pid
                      AND inactive = 0
                      AND ExpiresAt > GETDATE()
                ) THEN 15

                WHEN EXISTS (
                    SELECT 1
                    FROM WaitlistOffers
                    WHERE PackageId = @pid
                      AND IsUsed = 0
                      AND OfferEnd > GETDATE()
                      AND ExpiredAt IS NULL
                      AND Reason = 'cart'
                ) THEN 15

                WHEN EXISTS (
                    SELECT 1
                    FROM WaitlistOffers
                    WHERE PackageId = @pid
                      AND IsUsed = 0
                      AND OfferEnd > GETDATE()
                      AND ExpiredAt IS NULL
                      AND Reason = 'cancel'
                ) THEN 60

                ELSE 60
            END;
        ", conn))
        {
            cmd.Parameters.AddWithValue("@pid", packageId);
            minutesPerUser = Convert.ToInt32(cmd.ExecuteScalar());
        }

        // ✅ Option A: usersAhead לפי WaitingList, זמן לפי הסיבה (15/60), ותמיד מכפילים ב-1 מינימום
        int estimatedMinutes = Math.Max(1, usersAhead) * minutesPerUser;

        return Json(new
        {
            usersAhead = usersAhead,
            estimatedMinutes = estimatedMinutes,
            alreadyJoined = alreadyJoined,
            joinedNumPersons = joinedNumPersons
        });




    }
}

        
        [HttpPost]
        public IActionResult Join(int packageId, int numPersons = 1)
        {
            // אם יש לך Session של UserId כמו אצלך בעגלה:
            var userIdStr = HttpContext.Session.GetString("UserId");
            if (string.IsNullOrEmpty(userIdStr))
                return RedirectToAction("Login", "Auth");

            int userId = int.Parse(userIdStr);
            if (numPersons < 1) numPersons = 1;
            var referer = Request.Headers["Referer"].ToString();


            using (var conn = new SqlConnection(_connectionString))
            {
                conn.Open();

                // לא להוסיף כפול לאותו משתמש/אותה חבילה אם כבר פעיל
                string existsSql = @"
    SELECT TOP 1 numPersons
    FROM WaitingList
    WHERE UserId=@u AND PackageId=@p AND inactive=0
    ORDER BY JoinDate ASC, Id ASC;
";

                using (var existsCmd = new SqlCommand(existsSql, conn))
                {
                    existsCmd.Parameters.AddWithValue("@u", userId);
                    existsCmd.Parameters.AddWithValue("@p", packageId);

                    var obj = existsCmd.ExecuteScalar();
                    if (obj != null && obj != DBNull.Value)
                    {
                        int existingNumPersons = Convert.ToInt32(obj);

                        TempData["WaitlistToast"] = $"You are already on the waiting list for {existingNumPersons} passenger(s).";

                        if (!string.IsNullOrEmpty(referer))
                            return Redirect(referer);

                        return RedirectToAction("Gallery", "Package");
                    }
                }


                bool hasActiveTempBlocks;
                using (var cmd = new SqlCommand(@"
    SELECT CASE 
        WHEN EXISTS (
            SELECT 1
            FROM shoppingcart
            WHERE PackageId = @pid
              AND inactive = 0
              AND ExpiresAt > GETDATE()
        )
        OR EXISTS (
            SELECT 1
            FROM WaitlistOffers
            WHERE PackageId = @pid
              AND IsUsed = 0
              AND OfferEnd > GETDATE()
              AND ExpiredAt IS NULL
        )
        THEN 1 ELSE 0
    END;
", conn))
                {
                    cmd.Parameters.AddWithValue("@pid", packageId);
                    hasActiveTempBlocks = ((int)cmd.ExecuteScalar()) == 1;
                }

                string finalReason = hasActiveTempBlocks ? "cart" : "full";
                
                string insertSql = @"
                    INSERT INTO WaitingList (UserId, PackageId, JoinDate, inactive, numPersons, Reason)
                    VALUES (@u, @p, GETDATE(), 0, @n, @reason);
                ";

                using (var cmd = new SqlCommand(insertSql, conn))
                {
                    cmd.Parameters.AddWithValue("@u", userId);
                    cmd.Parameters.AddWithValue("@p", packageId);
                    cmd.Parameters.AddWithValue("@n", numPersons);
                    cmd.Parameters.AddWithValue("@reason", finalReason);

                    cmd.ExecuteNonQuery();
                }
            }

            TempData["WaitlistToast"] = "You joined the waiting list.";
            if (!string.IsNullOrEmpty(referer))
                return Redirect(referer);

            return RedirectToAction("Gallery", "Package");
            
        }

        [HttpPost]
        public IActionResult Update(int packageId, int numPersons = 1)
        {
            var userIdStr = HttpContext.Session.GetString("UserId");
            if (string.IsNullOrEmpty(userIdStr))
                return RedirectToAction("Login", "Auth");

            int userId = int.Parse(userIdStr);
            if (numPersons < 1) numPersons = 1;

            var referer = Request.Headers["Referer"].ToString();

            using (var conn = new SqlConnection(_connectionString))
            {
                conn.Open();

                // Update only if user already has an active row (keep JoinDate for fairness)
                using (var cmd = new SqlCommand(@"
                    UPDATE WaitingList
                    SET numPersons = @n
                    WHERE UserId = @u
                      AND PackageId = @p
                      AND inactive = 0;
                ", conn))
                {
                    cmd.Parameters.AddWithValue("@u", userId);
                    cmd.Parameters.AddWithValue("@p", packageId);
                    cmd.Parameters.AddWithValue("@n", numPersons);

                    int rows = cmd.ExecuteNonQuery();
                    if (rows <= 0)
                    {
                        TempData["WaitlistToast"] = "You are not on the waiting list for this trip.";
                        if (!string.IsNullOrEmpty(referer)) return Redirect(referer);
                        return RedirectToAction("Gallery", "Package");
                    }
                }
            }

            TempData["WaitlistToast"] = $"Your waiting list request was updated to {numPersons} passenger(s).";
            if (!string.IsNullOrEmpty(referer))
                return Redirect(referer);

            return RedirectToAction("Gallery", "Package");
        }
    }
    
}


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


        bool hasActiveCartHolds;
        using (var cmd = new SqlCommand(@"
    SELECT CASE 
        WHEN EXISTS (
            SELECT 1
            FROM shoppingcart
            WHERE PackageId = @pid
              AND inactive = 0
              AND ExpiresAt > GETDATE()
        )
        THEN 1 ELSE 0
    END;
", conn))
        {
            cmd.Parameters.AddWithValue("@pid", packageId);
            hasActiveCartHolds = ((int)cmd.ExecuteScalar()) == 1;
        }
        int minutesPerUser;
        
        if (hasActiveCartHolds)
        {
            // מקומות תפוסים זמנית בעגלה
            minutesPerUser = 15;
        }
        else
        {
            // מקומות תפוסים ע"י הזמנות (רק ביטול משחרר)
            minutesPerUser = 60;
        }


        int perUserMinutes = hasActiveCartHolds ? 15 : 60;
        int estimatedMinutes = usersAhead * perUserMinutes;

        return Json(new
        {
            alreadyJoined = myJoinDate.HasValue,
            usersAhead,
            estimatedMinutes
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
                    SELECT COUNT(*)
                    FROM WaitingList
                    WHERE UserId=@u AND PackageId=@p AND inactive=0;
                ";

                using (var existsCmd = new SqlCommand(existsSql, conn))
                {
                    existsCmd.Parameters.AddWithValue("@u", userId);
                    existsCmd.Parameters.AddWithValue("@p", packageId);

                    int exists = (int)existsCmd.ExecuteScalar();
                    if (exists > 0)
                    {
                        // ✅ Toast (לא חלונית)
                        TempData["WaitlistToast"] = "You are already on the waiting list for this trip.";

                        if (!string.IsNullOrEmpty(referer))
                            return Redirect(referer);

                        return RedirectToAction("Gallery", "Package");
                    }


                    
                }

                string insertSql = @"
                    INSERT INTO WaitingList (UserId, PackageId, JoinDate, inactive, numPersons)
                    VALUES (@u, @p, GETDATE(), 0, @n);
                ";

                using (var cmd = new SqlCommand(insertSql, conn))
                {
                    cmd.Parameters.AddWithValue("@u", userId);
                    cmd.Parameters.AddWithValue("@p", packageId);
                    cmd.Parameters.AddWithValue("@n", numPersons);
                    cmd.ExecuteNonQuery();
                }
            }

            TempData["WaitlistToast"] = "You joined the waiting list.";
            if (!string.IsNullOrEmpty(referer))
                return Redirect(referer);

            return RedirectToAction("Gallery", "Package");
            
        }
    }
    
}

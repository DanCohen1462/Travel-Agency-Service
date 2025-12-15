using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System;

namespace TravelAgency.Controllers
{
    public class BookingController : Controller
    {
        private readonly string _connectionString;

        public BookingController(IConfiguration config)
        {
            _connectionString = config.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
        }

        // POST: Book Trip (for group size)
        [HttpPost]
        public IActionResult Book(int packageId, int numPersons)
        {
            if (numPersons < 1) numPersons = 1;

            // ⚠️ כאן את אמורה לקחת את ה-UserId מה-Session/Claims.
            // זמנית: אם אין לך לוגין מחובר, תעשי בדיקה/ערך זמני
            int userId = 1;

            using (var conn = new SqlConnection(_connectionString))
            {
                conn.Open();

                // חשוב: Transaction כדי שלא תהיה בעיית "שני אנשים הזמינו את המקום האחרון"
                using (var tx = conn.BeginTransaction())
                {
                    try
                    {
                        // 1) בדיקה + שמירת מקום בצורה אטומית
                        // אם אין מספיק מקום, UPDATE לא יעשה כלום (0 rows affected)
                        var updateCmd = new SqlCommand(@"
                            UPDATE Package
                            SET numFreePlaces = numFreePlaces - @n
                            WHERE Id = @pid
                              AND inactive = 0
                              AND numFreePlaces >= @n;
                        ", conn, tx);

                        updateCmd.Parameters.AddWithValue("@n", numPersons);
                        updateCmd.Parameters.AddWithValue("@pid", packageId);

                        int affected = updateCmd.ExecuteNonQuery();

                        if (affected == 0)
                        {
                            tx.Rollback();
                            TempData["BookError"] = "Not enough available places for this group. You can join the waiting list.";
                            return RedirectToAction("PackageDetails", "Package", new { id = packageId });
                        }

                        // 2) הכנסת הזמנה להיסטוריה
                        var insertCmd = new SqlCommand(@"
                            INSERT INTO HistoryReservation(UserId, PackageId, inactive, numPersons, sum)
                            VALUES(@uid, @pid, 0, @n, 0);
                        ", conn, tx);

                        insertCmd.Parameters.AddWithValue("@uid", userId);
                        insertCmd.Parameters.AddWithValue("@pid", packageId);
                        insertCmd.Parameters.AddWithValue("@n", numPersons);

                        insertCmd.ExecuteNonQuery();

                        tx.Commit();

                        TempData["BookSuccess"] = "Trip booked successfully!";
                        return RedirectToAction("PackageDetails", "Package", new { id = packageId });
                    }
                    catch
                    {
                        tx.Rollback();
                        TempData["BookError"] = "Unexpected error. Please try again.";
                        return RedirectToAction("PackageDetails", "Package", new { id = packageId });
                    }
                }
            }
        }
    }
}

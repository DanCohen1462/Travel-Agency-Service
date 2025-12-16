using System;

namespace TravelAgency.Models
{
    public class UserTripViewModel
    {
        public int ReservationId { get; set; }  // מספר הזמנה
        public string Destination { get; set; } // יעד (מטבלת Package)
        public DateTime StartDate { get; set; } // תאריך התחלה
        public DateTime EndDate { get; set; }   // תאריך סיום
        public int NumPersons { get; set; }     // כמות אנשים (מטבלת HistoryReservation)
        public int TotalPrice { get; set; }     // מחיר ששולם (מטבלת HistoryReservation)
        public string ImageUrl { get; set; }    // תמונה

        // בודק אם הטיול עתידי או עבר
        public bool IsUpcoming => StartDate > DateTime.Now;
    }
}
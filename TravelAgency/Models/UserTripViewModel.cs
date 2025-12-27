using System;

namespace TravelAgency.Models
{
    public class UserTripViewModel
    {
        public int ReservationId { get; set; }  
        public string Destination { get; set; } 
        
        public string Country { get; set; }
        
        public DateTime StartDate { get; set; } // תאריך התחלה
        public DateTime EndDate { get; set; }   // תאריך סיום
        public int NumPersons { get; set; }    
        public int TotalPrice { get; set; }     // מחיר ששולם (מטבלת HistoryReservation)
        public string ImageUrl { get; set; }    // תמונה
        
        public bool IsUpcoming { get; set; }
        
        public int PackageId { get; set; }  
        
        public int CancelationDays { get; set; } 
        
        public int CategoryId { get; set; }
        
        public string CategoryName { get; set; }
        
        public bool HasRated { get; set; }

    }
}
using System;

namespace TravelAgency.Models
{
    public class Feedback
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public string Description { get; set; }
        public int Rate { get; set; }
        public string feedbackType { get; set; }
        public bool inactive { get; set; }
        public string? UserFullName { get; set; } // נשלף מה-JOIN
    }
}
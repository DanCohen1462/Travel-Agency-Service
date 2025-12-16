namespace TravelAgency.Models
{
    public class TravelerViewModel
    {
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public string PhoneNumber { get; set; }
        public int PartySize { get; set; } // כמה אנשים בהזמנה (numPersons)
        public int OrderId { get; set; }   // מספר הזמנה
        
        // שם מלא לנוחות
        public string FullName => $"{FirstName} {LastName}";
    }
}
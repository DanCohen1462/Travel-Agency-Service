using System.ComponentModel.DataAnnotations;

namespace TravelAgency.Models
{
    public class ContactFormViewModel
    {
        public int? UserId { get; set; }  // למקרה של משתמש מחובר

        [Required]
        [Display(Name = "Full Name")]
        public string FullName { get; set; }

        [Required]
        [EmailAddress]
        [Display(Name = "Email")]
        public string Email { get; set; }

        [Display(Name = "Phone")]
        public string Phone { get; set; }

        [Display(Name = "Preferred Destination")]
        public string Destination { get; set; }

        [Required]
        [Display(Name = "Message")]
        public string Message { get; set; }
    }
}
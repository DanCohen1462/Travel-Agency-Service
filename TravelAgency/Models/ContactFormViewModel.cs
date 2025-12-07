using System.ComponentModel.DataAnnotations;

namespace TravelAgency.Models
{
    public class ContactFormViewModel
    {
        public int? UserId { get; set; }  // למקרה של משתמש מחובר

        [Required(ErrorMessage = "Full name is required")]
        [StringLength(40, MinimumLength = 3, ErrorMessage = "Full name must be 3–40 characters")]
        [Display(Name = "Full Name")]
        public string FullName { get; set; }

        [Required(ErrorMessage = "Email is required")]
        [EmailAddress(ErrorMessage = "Invalid email format")]
        [Display(Name = "Email")]
        public string Email { get; set; }

        [Required(ErrorMessage = "Phone number is required")]
        [RegularExpression("^[0-9]{9,10}$", ErrorMessage = "Phone number must be 9–10 digits")]
        [Display(Name = "Phone")]
        public string Phone { get; set; }

        [Display(Name = "Preferred Destination")]
        public string Destination { get; set; }

        [Required(ErrorMessage = "Message is required")]
        [Display(Name = "Message")]
        public string Message { get; set; }
    }
}
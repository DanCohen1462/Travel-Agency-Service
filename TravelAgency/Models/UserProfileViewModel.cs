using System;
using System.ComponentModel.DataAnnotations;

namespace TravelAgency.Models
{
    public class UserProfileViewModel
    {
        public int Id { get; set; }

        [Display(Name = "Username")]
        public string Username { get; set; }   // יוצג לקריאה בלבד

        [Required]
        [Display(Name = "First Name")]
        public string FirstName { get; set; }

        [Required]
        [Display(Name = "Last Name")]
        public string LastName { get; set; }

        [Display(Name = "Birth Date")]
        [DataType(DataType.Date)]
        public DateTime? BirthDate { get; set; }

        [Display(Name = "Gender")]
        public string Gender { get; set; }

        [Display(Name = "Phone Number")]
        public string PhoneNumber { get; set; }

        [Required]
        [EmailAddress]
        [Display(Name = "Email")]
        public string Email { get; set; }

        // 1 = Admin, 2 = Worker, 3 = Customer
        public int Type { get; set; }
    }
}
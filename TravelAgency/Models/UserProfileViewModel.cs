using System;
using System.ComponentModel.DataAnnotations;

namespace TravelAgency.Models
{
    public class UserProfileViewModel
    {
        public int Id { get; set; }

        // USERNAME – ניתן לעריכה, עם אותם חוקים כמו בהרשמה
        [Required(ErrorMessage = "Username is required")]
        [MinLength(3, ErrorMessage = "Username must be at least 3 characters")]
        [RegularExpression("^[A-Za-z0-9]*$", ErrorMessage = "Username can only contain letters and numbers")]
        [Display(Name = "Username")]
        public string Username { get; set; }

        // FIRST NAME (2–10 letters)
        [Required(ErrorMessage = "First name is required")]
        [StringLength(10, MinimumLength = 2, ErrorMessage = "First name must be 2–10 letters")]
        [RegularExpression("^[A-Za-z]+$", ErrorMessage = "First name must contain only letters")]
        [Display(Name = "First Name")]
        public string FirstName { get; set; }

        // LAST NAME (2–20 letters)
        [Required(ErrorMessage = "Last name is required")]
        [StringLength(20, MinimumLength = 2, ErrorMessage = "Last name must be 2–20 letters")]
        [RegularExpression("^[A-Za-z]+$", ErrorMessage = "Last name must contain only letters")]
        [Display(Name = "Last Name")]
        public string LastName { get; set; }

        // BIRTH DATE → Age ≥ 18
        [Required(ErrorMessage = "Birth date is required")]
        [DataType(DataType.Date)]
        [MinAge(18, ErrorMessage = "You must be at least 18 years old")]
        [Display(Name = "Birth Date")]
        public DateTime? BirthDate { get; set; }

        // GENDER
        [Required(ErrorMessage = "Gender is required")]
        [Display(Name = "Gender")]
        public string Gender { get; set; }

        // PHONE NUMBER – בדיוק 10 ספרות
        [Required(ErrorMessage = "Phone number is required")]
        [RegularExpression("^[0-9]{10}$", ErrorMessage = "Phone number must be exactly 10 digits")]
        [Display(Name = "Phone Number")]
        public string PhoneNumber { get; set; }

        // EMAIL
        [Required(ErrorMessage = "Email is required")]
        [EmailAddress(ErrorMessage = "Invalid email format")]
        [Display(Name = "Email")]
        public string Email { get; set; }

        // 1 = Admin, 2 = Worker, 3 = Customer
        public int Type { get; set; }
    }
}

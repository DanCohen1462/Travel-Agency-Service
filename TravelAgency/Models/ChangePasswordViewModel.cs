using System.ComponentModel.DataAnnotations;

namespace TravelAgency.Models
{
    public class ChangePasswordViewModel
    {
        [Required(ErrorMessage = "Current password is required")]
        [DataType(DataType.Password)]
        [Display(Name = "Current Password")]
        public string CurrentPassword { get; set; }

        [Required(ErrorMessage = "New password is required")]
        [StringLength(12, MinimumLength = 8, ErrorMessage = "Password must be 8–12 characters")]
        [RegularExpression("^(?=.*[A-Z])(?=.*[0-9]).+$",
            ErrorMessage = "Password must contain at least one uppercase letter and one digit")]
        [DataType(DataType.Password)]
        [Display(Name = "New Password")]
        public string NewPassword { get; set; }

        [Required(ErrorMessage = "Please confirm the new password")]
        [DataType(DataType.Password)]
        [Compare("NewPassword", ErrorMessage = "Passwords do not match")]
        [Display(Name = "Confirm New Password")]
        public string ConfirmPassword { get; set; }
    }
}
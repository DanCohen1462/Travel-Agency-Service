using System.ComponentModel.DataAnnotations;

namespace TravelAgency.Models
{
    public class PaymentViewModel
    {
        [Required, StringLength(50, MinimumLength = 3)]
        public string FullName { get; set; }

        // 16 ספרות בלבד (בלי רווחים)
        [Required, RegularExpression(@"^\d{16}$", ErrorMessage = "Card number must be 16 digits.")]
        public string CardNumber { get; set; }

        // MM/YY
        [Required, RegularExpression(@"^(0[1-9]|1[0-2])\/\d{2}$", ErrorMessage = "Expiration must be MM/YY.")]
        public string Expiration { get; set; }

        [Required, RegularExpression(@"^\d{3}$", ErrorMessage = "CVV must be 3 digits.")]
        public string CVV { get; set; }
    }
}
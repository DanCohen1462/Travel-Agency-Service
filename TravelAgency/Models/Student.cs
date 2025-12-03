namespace TravelAgency.Models;
using System.ComponentModel.DataAnnotations;

public class Student
{
    public int Id { get; set; }
    [Required ]
    [StringLength(50, MinimumLength = 2, ErrorMessage = "fName must contain only letters and be 2–50 characters long.")]
    
    
    
    public string FirstName { get; set; }
    [Required ]
    [StringLength(50, MinimumLength = 2, ErrorMessage = "LName must contain only letters and be 2–50 characters long.")]
    public string LastName { get; set; }
    
    [Required]
    [RegularExpression(@"^^[^\s@]+@[^\s@]+\.[^\s@]+$", ErrorMessage = "Please enter a valid email address.")]
    public string Email { get; set; }
    [Required]
    [RegularExpression(@"^(?:\+972-?|0)(?:5[0-9])[ -]?[0-9]{7}$", ErrorMessage = "Please enter a valid Israeli phone number.")]
    public string PhoneNumber { get; set; }
    [Required]
    [RegularExpression(@"^[A-Za-zא-ת]+$", ErrorMessage = "Major must contain letters only.\n")]

    public string Major { get; set; }
    [Required]
    [RegularExpression(@"^\d{4}$", ErrorMessage = "Year must contain exactly 4 digits.\n")]

    public int Year { get; set; }

}
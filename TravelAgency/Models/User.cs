using System;
using System.ComponentModel.DataAnnotations;

public class User
{
    public int Id { get; set; }

    // USERNAME
    [Required(ErrorMessage = "Username is required")]
    [MinLength(3, ErrorMessage = "Username must be at least 3 characters")]
    [RegularExpression("^[A-Za-z0-9]*$", ErrorMessage = "Username can only contain letters and numbers")]
    public string Username { get; set; }

    // FIRST NAME (2–10 letters)
    [Required(ErrorMessage = "First name is required")]
    [StringLength(10, MinimumLength = 2, ErrorMessage = "First name must be 2–10 letters")]
    [RegularExpression("^[A-Za-z]+$", ErrorMessage = "First name must contain only letters")]
    public string firstName { get; set; }

    // LAST NAME (2–20 letters)
    [Required(ErrorMessage = "Last name is required")]
    [StringLength(20, MinimumLength = 2, ErrorMessage = "Last name must be 2–20 letters")]
    [RegularExpression("^[A-Za-z]+$", ErrorMessage = "Last name must contain only letters")]
    public string lastName { get; set; }

    // BIRTH DATE → Age ≥ 18
    [Required(ErrorMessage = "Birth date is required")]
    [DataType(DataType.Date)]
    [MinAge(18, ErrorMessage = "You must be at least 18 years old")]
    public DateTime birthDate { get; set; }

    // GENDER
    [Required(ErrorMessage = "Gender is required")]
    public string gender { get; set; }

    // PHONE NUMBER
    [Required(ErrorMessage = "Phone number is required")]
    [RegularExpression("^[0-9]{9,10}$", ErrorMessage = "Phone number must be 9–10 digits")]
    public string phoneNumber { get; set; }

    // EMAIL
    [Required(ErrorMessage = "Email is required")]
    [EmailAddress(ErrorMessage = "Invalid email format")]
    public string email { get; set; }

    // PASSWORD (8–12 chars, ≥1 uppercase, ≥1 digit)
    [Required(ErrorMessage = "Password is required")]
    [StringLength(12, MinimumLength = 8, ErrorMessage = "Password must be 8–12 characters")]
    [RegularExpression("^(?=.*[A-Z])(?=.*[0-9]).+$",
        ErrorMessage = "Password must contain at least one uppercase letter and one digit")]
    public string Password { get; set; }
    
    
    
    public bool IsAvailable { get; set; }
}





// ⬇⬇⬇ כאן בתוך אותו קובץ — מחלקת הולידציה לגיל 18 ⬇⬇⬇

public class MinAge : ValidationAttribute
{
    private readonly int _minAge;

    public MinAge(int minAge)
    {
        _minAge = minAge;
    }

    public override bool IsValid(object value)
    {
        if (value == null)
            return false;

        DateTime birthDate = (DateTime)value;

        int age = DateTime.Today.Year - birthDate.Year;

        // אם יום ההולדת טרם עבר השנה
        if (birthDate.Date > DateTime.Today.AddYears(-age).Date)
            age--;

        return age >= _minAge;
    }
}

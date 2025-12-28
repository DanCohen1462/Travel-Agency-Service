namespace TravelAgency.Models;
using System;
using System.ComponentModel.DataAnnotations;

public class Package
{
    public int Id { get; set; }
    [Required(ErrorMessage = "Destination is required")]
    [StringLength(50, ErrorMessage = "Destination must be up to 50 characters")]
    public string destination { get; set; }
    [Required(ErrorMessage = "Start date is required")]
    public DateTime StartDate { get; set; }
    [Required(ErrorMessage = "End date is required")]
    [DateGreaterThan("StartDate", ErrorMessage = "End date must be after start date")]
    public DateTime EndDate { get; set; }
    [Required(ErrorMessage = "Price is required")]
    [Range(1, int.MaxValue, ErrorMessage = "Price must be greater than 0")]
    public int sum { get; set; }
    [Required(ErrorMessage = "Age limit is required")]
    [Range(1, 120, ErrorMessage = "Age limit must be between 1–120")]
    public int ageLimit { get; set; }
    public string? image { get; set; }
    [Required(ErrorMessage = "Available spots is required")]
    [Range(1, 500, ErrorMessage = "Available spots must be at least 1")]
    public int numFreePlaces { get; set; }
    [Required(ErrorMessage = "Category is required")]
    public int idCategory { get; set; }
    
    public int UserId { get; set; }
    
    [Required(ErrorMessage = "Description is required")]
    [StringLength(500, ErrorMessage = "Description must be up to 500 characters")]
    public string? information { get; set; }
    
    public bool inactive {get; set; }
    public int ActiveDiscount{get; set; }
    [Required(ErrorMessage = "Country is required")]
    [StringLength(50, ErrorMessage = "Country must be up to 50 characters")]
    public string? country { get; set; }
    
    public string? RandomImage { get; set; }
    
    public string? CategoryName { get; set; }
    public int TotalBookings { get; set; }
    public int? DiscountPercent { get; set; }
    public string? ImageUrl { get; set; }
    [Range(0, 365, ErrorMessage = "Cancellation days must be between 0 and 365")]

    public int? cancelationDays { get; set; } 
   
}
public class DateGreaterThanAttribute : ValidationAttribute
{
    private readonly string _comparisonProperty;

    public DateGreaterThanAttribute(string comparisonProperty)
    {
        _comparisonProperty = comparisonProperty;
    }

    protected override ValidationResult IsValid(object value, ValidationContext validationContext)
    {
        var currentValue = (DateTime?)value;

        if (currentValue == null)
            return ValidationResult.Success;

        // מחפש את תכונת ההשוואה (StartDate או EndDate)
        var property = validationContext.ObjectType.GetProperty(_comparisonProperty);

        if (property == null)
            return new ValidationResult($"Unknown property: {_comparisonProperty}");

        var comparisonValue = (DateTime?)property.GetValue(validationContext.ObjectInstance);

        if (comparisonValue == null)
            return ValidationResult.Success;

        // ⚠ דרישה חדשה: StartDate חייב להיות לפחות יום אחד אחרי היום
        if (property.Name == "StartDate")
        {
            if (comparisonValue.Value.Date <= DateTime.Today)
            {
                return new ValidationResult("Start date must be at least 1 day in the future.");
            }
        }

        // בדיקה רגילה: EndDate > StartDate
        if (currentValue <= comparisonValue)
        {
            return new ValidationResult(ErrorMessage ?? $"{validationContext.DisplayName} must be after {property.Name}.");
        }

        return ValidationResult.Success;
    }
}

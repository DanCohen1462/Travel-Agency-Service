namespace TravelAgency.Models;

public class Package
{
    public int Id { get; set; }
    public string destination { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public int sum { get; set; }
    public int ageLimit { get; set; }
    public string image { get; set; }
    public int numFreePlaces { get; set; }
    public int idCategory { get; set; }
    
    public int UserId { get; set; }
    public string information { get; set; }
    
    public bool inactive {get; set; }
    public int ActiveDiscount{get; set; }
    
}
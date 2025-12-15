namespace TravelAgency.Models
{
    public class SearchSuggestion
    {
        public string DisplayText { get; set; } = "";
        public string Destination { get; set; } = "";
        public string Country { get; set; } = "";
        public int? CategoryId { get; set; }
        public string? CategoryName { get; set; }
    }
}
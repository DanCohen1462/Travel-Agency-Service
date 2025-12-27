namespace TravelAgency.Models
{
    public class HomeIndexViewModel
    {
        public List<WebsiteFeedbackVM> WebsiteFeedbacks { get; set; } = new();
        public double AvgWebsiteRate { get; set; }
        public int WebsiteReviewsCount { get; set; }

        public List<PopularDestinationVM> PopularDestinations { get; set; } = new();
    }

    public class WebsiteFeedbackVM
    {
        public int Id { get; set; }
        public string UserFullName { get; set; } = "Anonymous";
        public string Description { get; set; } = "";
        public int Rate { get; set; } // 1-5
    }

    public class PopularDestinationVM
    {
        public int PackageId { get; set; }
        public string Destination { get; set; } = "";
        public string Country { get; set; } = "";
        public string ImageUrl { get; set; } = "/images/default.jpg";
        public int PopularityScore { get; set; } // למשל SUM(numPersons)
    }
}
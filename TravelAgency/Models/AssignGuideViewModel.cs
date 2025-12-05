namespace TravelAgency.Models
{
    public class AssignGuideViewModel
    {
        public List<User> Guides { get; set; }
        public List<Package> UnassignedPackages { get; set; }
    }
}
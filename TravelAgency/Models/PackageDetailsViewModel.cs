using System;

namespace TravelAgency.Models
{
    public class PackageDetailsViewModel
    {
        public Package Package { get; set; }
        public string CategoryName { get; set; }

        public bool IsFull => Package?.numFreePlaces <= 0;
        public bool IsAlmostFull =>
            Package != null &&
            Package.numFreePlaces > 0 &&
            Package.numFreePlaces <= 5;

    }
}
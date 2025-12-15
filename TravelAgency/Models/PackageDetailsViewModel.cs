using System;
using System.Collections.Generic;
using System.Linq;

namespace TravelAgency.Models
{
    public class PackageDetailsViewModel
    {
        public Package Package { get; set; }
        public string CategoryName { get; set; } = "";
        public List<Feedback> Reviews { get; set; } = new List<Feedback>();

        // optional: for "per family" / passengers multiplier
        public int TotalPriceMultiplier { get; set; } = 1;

        // ✅ NEW helper (passengers count)
        public int PassengersCount => (TotalPriceMultiplier < 1 ? 1 : TotalPriceMultiplier);

        // ===== Price helpers =====
        public int FinalPrice => Package?.sum ?? 0;

        public int DiscountedPrice
        {
            get
            {
                if (Package == null) return 0;

                int percent = Package.DiscountPercent ?? 0;
                if (percent <= 0) return Package.sum;

                double price = Package.sum * (1 - (percent / 100.0));
                return (int)Math.Round(price);
            }
        }

        public int TotalDiscountedSum =>
            (DiscountedPrice * (TotalPriceMultiplier < 1 ? 1 : TotalPriceMultiplier));

        // ===== Reviews helpers =====
        public int TotalReviews => Reviews?.Count ?? 0;

        public double AverageRating
        {
            get
            {
                if (Reviews == null || Reviews.Count == 0) return 0;
                return Math.Round(Reviews.Average(r => r.Rate), 1);
            }
        }

        // ===== Availability =====
        // ✅ FIX: full is also when seats < passengers
        public bool IsFull =>
            Package == null ||
            Package.numFreePlaces <= 0 ||
            Package.numFreePlaces < PassengersCount;

        // ✅ FIX: almost full only if not full AND enough seats for passengers
        public bool IsAlmostFull =>
            Package != null &&
            Package.numFreePlaces > 0 &&
            Package.numFreePlaces <= 5 &&
            Package.numFreePlaces >= PassengersCount;
    }
}

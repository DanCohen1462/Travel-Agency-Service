using System;

namespace TravelAgency.Models
{
    public class CartItem
    {
        public int PackageId { get; set; }
        public string Destination { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public int Price { get; set; }
        public int Quantity { get; set; }
    }
}
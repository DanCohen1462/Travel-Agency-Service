using System.Collections.Generic;
using System.Linq;

namespace TravelAgency.Models
{
    public class CartViewModel
    {
        public List<CartItem> Items { get; set; } = new List<CartItem>();

        public int TotalItems => Items.Sum(i => i.Quantity);
        public int TotalPrice => Items.Sum(i => i.Price * i.Quantity);
    }
}
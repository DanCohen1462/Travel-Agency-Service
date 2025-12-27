public class CartItem
{
    public int PackageId { get; set; }
    public string Destination { get; set; } = "";
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public int Price { get; set; }          // price per person
    public int Quantity { get; set; }       // numPersons
    public string ImageUrl { get; set; } = "/images/default.jpg";

    public string Country { get; set; }

    public DateTime ExpiresAt { get; set; }

    public int ShoppingCartRowId { get; set; } // row id in shoppingcart
    public int TotalSum { get; set; }          // total price for that row
}
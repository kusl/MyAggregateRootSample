namespace MyClassLibrary;

// Value Object for OrderItem (immutable, no identity of its own, part of Order)
public class MyOrderItem
{
    public string Product { get; private set; }
    public int Quantity { get; private set; }
    public decimal Price { get; private set; }

    public MyOrderItem(string product, int quantity, decimal price)
    {
        if (quantity <= 0) throw new ArgumentException("Quantity must be positive.");
        if (price <= 0) throw new ArgumentException("Price must be positive.");

        Product = product;
        Quantity = quantity;
        Price = price;
    }

    public decimal LineTotal => Quantity * Price;
}

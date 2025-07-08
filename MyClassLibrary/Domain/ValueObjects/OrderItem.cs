namespace MyClassLibrary.Domain.ValueObjects;

public record OrderItem
{
    public string Product { get; }
    public int Quantity { get; }
    public decimal Price { get; }
    public decimal LineTotal => Quantity * Price;

    public OrderItem(string product, int quantity, decimal price)
    {
        if (string.IsNullOrWhiteSpace(product))
            throw new ArgumentException("Product cannot be null or empty.", nameof(product));
        if (quantity <= 0)
            throw new ArgumentException("Quantity must be positive.", nameof(quantity));
        if (price <= 0)
            throw new ArgumentException("Price must be positive.", nameof(price));

        Product = product.Trim();
        Quantity = quantity;
        Price = price;
    }
}
using MyClassLibrary.Domain.ValueObjects;
using MyClassLibrary.Domain.Events;

namespace MyClassLibrary.Domain.Entities;

public class Order
{
    public Guid Id { get; private set; }
    public DateTime OrderDate { get; private set; }
    private readonly List<OrderItem> _items = [];
    public IReadOnlyList<OrderItem> Items => _items.AsReadOnly();

    // For ORM reconstruction
    private Order() { }

    public Order(Guid id, DateTime orderDate)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("Order ID cannot be empty.", nameof(id));
        if (orderDate > DateTime.UtcNow)
            throw new ArgumentException("Order date cannot be in the future.", nameof(orderDate));

        Id = id;
        OrderDate = orderDate;
    }

    public void AddItem(OrderItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        // Business rule: Prevent duplicate items by combining quantities
        var existingItem = _items.FirstOrDefault(i => i.Product == item.Product && i.Price == item.Price);
        if (existingItem != null)
        {
            _items.Remove(existingItem);
            _items.Add(new OrderItem(item.Product, existingItem.Quantity + item.Quantity, item.Price));
        }
        else
        {
            _items.Add(item);
        }
    }

    public decimal TotalAmount => _items.Sum(item => item.LineTotal);

    public bool IsOutstanding(int outstandingDays) =>
        OrderDate.AddDays(outstandingDays) > DateTime.UtcNow;
}
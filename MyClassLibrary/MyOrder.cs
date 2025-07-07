namespace MyClassLibrary;
// Order Entity (part of the Customer aggregate, not an aggregate root itself)
// It doesn't need its own repository because it's managed by the Customer aggregate.
public class Order
{
    public Guid Id { get; private set; }
    public DateTime OrderDate { get; private set; }
    private readonly List<MyOrderItem> _items = [];
    public IReadOnlyCollection<MyOrderItem> Items => _items.AsReadOnly();

    private Order() { } // Private constructor for ORM or internal use

    public Order(Guid id, DateTime orderDate)
    {
        Id = id;
        OrderDate = orderDate;
    }

    public void AddItem(MyOrderItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        _items.Add(item);
        // Could add business rules here, e.g., max items per order, etc.
    }

    public decimal TotalAmount => _items.Sum(item => item.LineTotal);
}

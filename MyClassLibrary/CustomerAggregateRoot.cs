using Microsoft.Extensions.Logging;
namespace MyClassLibrary;

// Customer Aggregate Root
// This is the only entity that should have a repository.
public class CustomerAggregateRoot
{
    public Guid Id { get; private set; }
    public string Name { get; private set; }
    private readonly ILogger<CustomerAggregateRoot> _logger;
    private readonly CustomerBusinessRules _businessRules;
    private readonly List<Order> _orders = [];
    public IReadOnlyCollection<Order> Orders => _orders.AsReadOnly();

    private CustomerAggregateRoot()
    {
        Name = "";  // Private constructor for ORM or internal use
        _businessRules = new();
        _logger = null!; // Will be set during hydration/reconstruction
    }

    public CustomerAggregateRoot(Guid id, string name, ILogger<CustomerAggregateRoot> logger, CustomerBusinessRules businessRules)
    {
        Id = id;
        Name = name;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _businessRules = businessRules ?? throw new ArgumentNullException(nameof(businessRules));
    }

    public Order PlaceNewOrder()
    {
        int outstandingOrders = _orders.Count(o => o.OrderDate.AddDays(_businessRules.OutstandingOrderDays) > DateTime.UtcNow);
        if (outstandingOrders >= _businessRules.MaxOutstandingOrders)
        {
            throw new InvalidOperationException($"Customer has too many outstanding orders. Maximum allowed: {_businessRules.MaxOutstandingOrders}");
        }

        Order newOrder = new(Guid.NewGuid(), DateTime.UtcNow);
        _orders.Add(newOrder);
        _logger?.LogInformation("Customer '{Name}' placed a new order with Id {OrderId}.", Name, newOrder.Id);
        return newOrder;
    }

    // Method to find an order within the aggregate
    public Order? GetOrder(Guid orderId)
    {
        return _orders.FirstOrDefault(o => o.Id == orderId);
    }
}
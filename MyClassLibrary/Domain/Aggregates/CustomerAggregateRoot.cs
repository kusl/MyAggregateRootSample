using Microsoft.Extensions.Logging;
using MyClassLibrary.Domain.Entities;
using MyClassLibrary.Domain.ValueObjects;
using MyClassLibrary.Domain.Events;
using MyClassLibrary.Domain.Configuration;

namespace MyClassLibrary.Domain.Aggregates;

public class CustomerAggregateRoot
{
    public Guid Id { get; private set; }
    public string Name { get; private set; }
    private readonly List<Order> _orders = [];
    private readonly List<DomainEvent> _domainEvents = [];
    private readonly ILogger<CustomerAggregateRoot>? _logger;
    private readonly CustomerBusinessRules _businessRules;

    public IReadOnlyList<Order> Orders => _orders.AsReadOnly();
    public IReadOnlyList<DomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    // For ORM reconstruction
    private CustomerAggregateRoot()
    {
        Name = "";
        _businessRules = new CustomerBusinessRules();
    }

    public CustomerAggregateRoot(Guid id, string name, CustomerBusinessRules businessRules, ILogger<CustomerAggregateRoot>? logger = null)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("Customer ID cannot be empty.", nameof(id));
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Customer name cannot be null or empty.", nameof(name));

        Id = id;
        Name = name.Trim();
        _businessRules = businessRules ?? throw new ArgumentNullException(nameof(businessRules));
        _logger = logger;

        AddDomainEvent(new CustomerCreatedEvent(Guid.NewGuid(), DateTime.UtcNow, Id, Name));
    }

    public Order PlaceNewOrder()
    {
        int outstandingOrders = _orders.Count(o => o.IsOutstanding(_businessRules.OutstandingOrderDays));

        if (outstandingOrders >= _businessRules.MaxOutstandingOrders)
        {
            InvalidOperationException exception = new(
                $"Customer '{Name}' has reached the maximum of {_businessRules.MaxOutstandingOrders} outstanding orders.");
            _logger?.LogWarning(exception, "Order placement failed for customer {CustomerId}", Id);
            throw exception;
        }

        Order newOrder = new(Guid.NewGuid(), DateTime.UtcNow);
        _orders.Add(newOrder);

        AddDomainEvent(new OrderPlacedEvent(Guid.NewGuid(), DateTime.UtcNow, Id, newOrder.Id, newOrder.OrderDate));
        _logger?.LogInformation("Customer '{CustomerName}' placed order {OrderId}", Name, newOrder.Id);

        return newOrder;
    }

    public Order? GetOrder(Guid orderId)
    {
        return _orders.FirstOrDefault(o => o.Id == orderId);
    }

    public void AddItemToOrder(Guid orderId, OrderItem item)
    {
        Order? order = GetOrder(orderId);
        if (order == null)
        {
            InvalidOperationException exception = new($"Order {orderId} not found for customer {Id}");
            _logger?.LogWarning(exception, "Failed to add item to order {OrderId} for customer {CustomerId}", orderId, Id);
            throw exception;
        }

        order.AddItem(item);
        AddDomainEvent(new OrderItemAddedEvent(Guid.NewGuid(), DateTime.UtcNow, Id, orderId, item));
        _logger?.LogInformation("Added item {Product} to order {OrderId} for customer {CustomerId}",
            item.Product, orderId, Id);
    }

    public void ClearDomainEvents()
    {
        _domainEvents.Clear();
    }

    private void AddDomainEvent(DomainEvent domainEvent)
    {
        _domainEvents.Add(domainEvent);
    }
}

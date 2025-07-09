// MyClassLibrary.cs - All library code in one file
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MyClassLibrary;

// ========== VALUE OBJECTS ==========

public record Address
{
    public string Street { get; }
    public string City { get; }
    public string State { get; }
    public string PostalCode { get; }
    public string Country { get; }

    public Address(string street, string city, string state, string postalCode, string country)
    {
        if (string.IsNullOrWhiteSpace(street))
            throw new ArgumentException("Street cannot be null or empty.", nameof(street));
        if (string.IsNullOrWhiteSpace(city))
            throw new ArgumentException("City cannot be null or empty.", nameof(city));
        if (string.IsNullOrWhiteSpace(state))
            throw new ArgumentException("State cannot be null or empty.", nameof(state));
        if (string.IsNullOrWhiteSpace(postalCode))
            throw new ArgumentException("Postal code cannot be null or empty.", nameof(postalCode));
        if (string.IsNullOrWhiteSpace(country))
            throw new ArgumentException("Country cannot be null or empty.", nameof(country));

        Street = street.Trim();
        City = city.Trim();
        State = state.Trim();
        PostalCode = postalCode.Trim();
        Country = country.Trim();
    }

    public override string ToString() => $"{Street}, {City}, {State} {PostalCode}, {Country}";
}

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

// ========== CONFIGURATION ==========

public class CustomerBusinessRules
{
    public int MaxOutstandingOrders { get; set; } = 10;
    public int OutstandingOrderDays { get; set; } = 30;
}

// ========== DOMAIN EVENTS ==========

public abstract record DomainEvent(Guid Id, DateTime OccurredOn);

public record CustomerCreatedEvent(Guid Id, DateTime OccurredOn, Guid CustomerId, string CustomerName)
    : DomainEvent(Id, OccurredOn);

public record CustomerAddressUpdatedEvent(Guid Id, DateTime OccurredOn, Guid CustomerId, Address OldAddress, Address NewAddress)
    : DomainEvent(Id, OccurredOn);

public record OrderPlacedEvent(Guid Id, DateTime OccurredOn, Guid CustomerId, Guid OrderId, DateTime OrderDate, Address ShippingAddress, Address BillingAddress)
    : DomainEvent(Id, OccurredOn);

public record OrderItemAddedEvent(Guid Id, DateTime OccurredOn, Guid CustomerId, Guid OrderId, OrderItem Item)
    : DomainEvent(Id, OccurredOn);

// ========== ENTITIES ==========

public class Order
{
    public Guid Id { get; private set; }
    public DateTime OrderDate { get; private set; }
    public Address ShippingAddress { get; private set; }
    public Address BillingAddress { get; private set; }
    private readonly List<OrderItem> _items = [];
    public IReadOnlyList<OrderItem> Items => _items.AsReadOnly();

    // For ORM reconstruction
    private Order() 
    { 
        ShippingAddress = null!;
        BillingAddress = null!;
    }

    public Order(Guid id, DateTime orderDate, Address shippingAddress, Address billingAddress)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("Order ID cannot be empty.", nameof(id));
        if (orderDate > DateTime.UtcNow)
            throw new ArgumentException("Order date cannot be in the future.", nameof(orderDate));
        
        Id = id;
        OrderDate = orderDate;
        ShippingAddress = shippingAddress ?? throw new ArgumentNullException(nameof(shippingAddress));
        BillingAddress = billingAddress ?? throw new ArgumentNullException(nameof(billingAddress));
    }

    public void AddItem(OrderItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        // Business rule: Prevent duplicate items by combining quantities
        OrderItem? existingItem = _items.FirstOrDefault(i => i.Product == item.Product && i.Price == item.Price);
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

// ========== AGGREGATES ==========

public class CustomerAggregateRoot
{
    public Guid Id { get; private set; }
    public string Name { get; private set; }
    public Address? DefaultShippingAddress { get; private set; }
    public Address? DefaultBillingAddress { get; private set; }
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

    public void UpdateDefaultAddresses(Address? shippingAddress, Address? billingAddress)
    {
        var oldShipping = DefaultShippingAddress;
        var oldBilling = DefaultBillingAddress;

        DefaultShippingAddress = shippingAddress;
        DefaultBillingAddress = billingAddress;

        if (oldShipping != shippingAddress && shippingAddress != null)
        {
            AddDomainEvent(new CustomerAddressUpdatedEvent(
                Guid.NewGuid(), 
                DateTime.UtcNow, 
                Id, 
                oldShipping ?? new Address("", "", "", "", ""), 
                shippingAddress));
            _logger?.LogInformation("Updated shipping address for customer {CustomerId}", Id);
        }

        if (oldBilling != billingAddress && billingAddress != null)
        {
            AddDomainEvent(new CustomerAddressUpdatedEvent(
                Guid.NewGuid(), 
                DateTime.UtcNow, 
                Id, 
                oldBilling ?? new Address("", "", "", "", ""), 
                billingAddress));
            _logger?.LogInformation("Updated billing address for customer {CustomerId}", Id);
        }
    }

    public Order PlaceNewOrder(Address? shippingAddress = null, Address? billingAddress = null)
    {
        // Use provided addresses or fall back to defaults
        var orderShippingAddress = shippingAddress ?? DefaultShippingAddress;
        var orderBillingAddress = billingAddress ?? DefaultBillingAddress;

        if (orderShippingAddress == null)
            throw new InvalidOperationException("Shipping address is required to place an order.");
        if (orderBillingAddress == null)
            throw new InvalidOperationException("Billing address is required to place an order.");

        int outstandingOrders = _orders.Count(o => o.IsOutstanding(_businessRules.OutstandingOrderDays));

        if (outstandingOrders >= _businessRules.MaxOutstandingOrders)
        {
            InvalidOperationException exception = new(
                $"Customer '{Name}' has reached the maximum of {_businessRules.MaxOutstandingOrders} outstanding orders.");
            _logger?.LogWarning(exception, "Order placement failed for customer {CustomerId}", Id);
            throw exception;
        }

        Order newOrder = new(Guid.NewGuid(), DateTime.UtcNow, orderShippingAddress, orderBillingAddress);
        _orders.Add(newOrder);

        AddDomainEvent(new OrderPlacedEvent(
            Guid.NewGuid(), 
            DateTime.UtcNow, 
            Id, 
            newOrder.Id, 
            newOrder.OrderDate,
            orderShippingAddress,
            orderBillingAddress));
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

// ========== APPLICATION INTERFACES ==========

public interface ICustomerAggregateRepository
{
    Task<CustomerAggregateRoot?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task SaveAsync(CustomerAggregateRoot customer, CancellationToken cancellationToken = default);
}

public interface IDomainEventDispatcher
{
    Task DispatchAsync(IEnumerable<DomainEvent> events, CancellationToken cancellationToken = default);
}

// ========== APPLICATION SERVICES ==========

public class CustomerApplicationService(
    ICustomerAggregateRepository customerRepository,
    IDomainEventDispatcher eventDispatcher,
    CustomerBusinessRules businessRules,
    ILogger<CustomerApplicationService> logger,
    ILogger<CustomerAggregateRoot> customerLogger)
{
    private readonly ICustomerAggregateRepository _customerRepository = customerRepository ?? throw new ArgumentNullException(nameof(customerRepository));
    private readonly IDomainEventDispatcher _eventDispatcher = eventDispatcher ?? throw new ArgumentNullException(nameof(eventDispatcher));
    private readonly CustomerBusinessRules _businessRules = businessRules ?? throw new ArgumentNullException(nameof(businessRules));
    private readonly ILogger<CustomerApplicationService> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly ILogger<CustomerAggregateRoot> _customerLogger = customerLogger ?? throw new ArgumentNullException(nameof(customerLogger));

    public async Task<Guid> CreateCustomerAndPlaceOrderAsync(
        string customerName,
        Address shippingAddress,
        Address billingAddress,
        List<(string product, int quantity, decimal price)> orderItemsData,
        CancellationToken cancellationToken = default)
    {
        try
        {
            Guid customerId = Guid.NewGuid();
            CustomerAggregateRoot customer = new(customerId, customerName, _businessRules, _customerLogger);
            
            // Set default addresses
            customer.UpdateDefaultAddresses(shippingAddress, billingAddress);
            
            // Place order with the same addresses
            Order order = customer.PlaceNewOrder(shippingAddress, billingAddress);

            foreach ((string product, int quantity, decimal price) in orderItemsData)
            {
                OrderItem item = new(product, quantity, price);
                customer.AddItemToOrder(order.Id, item);
            }

            await _customerRepository.SaveAsync(customer, cancellationToken);
            await _eventDispatcher.DispatchAsync(customer.DomainEvents, cancellationToken);
            customer.ClearDomainEvents();

            _logger.LogInformation("Successfully created customer {CustomerName} with order {OrderId}",
                customerName, order.Id);

            return customerId;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create customer and place order for {CustomerName}", customerName);
            throw;
        }
    }

    public async Task UpdateCustomerAddressesAsync(
        Guid customerId,
        Address? shippingAddress,
        Address? billingAddress,
        CancellationToken cancellationToken = default)
    {
        try
        {
            CustomerAggregateRoot? customer = await _customerRepository.GetByIdAsync(customerId, cancellationToken);
            if (customer == null)
            {
                InvalidOperationException exception = new($"Customer {customerId} not found");
                _logger.LogWarning(exception, "Customer {CustomerId} not found", customerId);
                throw exception;
            }

            customer.UpdateDefaultAddresses(shippingAddress, billingAddress);

            await _customerRepository.SaveAsync(customer, cancellationToken);
            await _eventDispatcher.DispatchAsync(customer.DomainEvents, cancellationToken);
            customer.ClearDomainEvents();

            _logger.LogInformation("Successfully updated addresses for customer {CustomerId}", customerId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update addresses for customer {CustomerId}", customerId);
            throw;
        }
    }

    public async Task<Guid> PlaceOrderForExistingCustomerAsync(
        Guid customerId,
        Address? shippingAddress,
        Address? billingAddress,
        List<(string product, int quantity, decimal price)> orderItemsData,
        CancellationToken cancellationToken = default)
    {
        try
        {
            CustomerAggregateRoot? customer = await _customerRepository.GetByIdAsync(customerId, cancellationToken);
            if (customer == null)
            {
                InvalidOperationException exception = new($"Customer {customerId} not found");
                _logger.LogWarning(exception, "Customer {CustomerId} not found", customerId);
                throw exception;
            }

            // Place order with provided addresses or use customer defaults
            Order order = customer.PlaceNewOrder(shippingAddress, billingAddress);

            foreach ((string product, int quantity, decimal price) in orderItemsData)
            {
                OrderItem item = new(product, quantity, price);
                customer.AddItemToOrder(order.Id, item);
            }

            await _customerRepository.SaveAsync(customer, cancellationToken);
            await _eventDispatcher.DispatchAsync(customer.DomainEvents, cancellationToken);
            customer.ClearDomainEvents();

            _logger.LogInformation("Successfully placed order {OrderId} for customer {CustomerId}",
                order.Id, customerId);

            return order.Id;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to place order for customer {CustomerId}", customerId);
            throw;
        }
    }

    public async Task AddOrderItemsToExistingOrderAsync(
        Guid customerId,
        Guid orderId,
        List<(string product, int quantity, decimal price)> newItemsData,
        CancellationToken cancellationToken = default)
    {
        try
        {
            CustomerAggregateRoot? customer = await _customerRepository.GetByIdAsync(customerId, cancellationToken);
            if (customer == null)
            {
                InvalidOperationException exception = new($"Customer {customerId} not found");
                _logger.LogWarning(exception, "Customer {CustomerId} not found", customerId);
                throw exception;
            }

            foreach ((string product, int quantity, decimal price) in newItemsData)
            {
                OrderItem item = new(product, quantity, price);
                customer.AddItemToOrder(orderId, item);
            }

            await _customerRepository.SaveAsync(customer, cancellationToken);
            await _eventDispatcher.DispatchAsync(customer.DomainEvents, cancellationToken);
            customer.ClearDomainEvents();

            _logger.LogInformation("Successfully added items to order {OrderId} for customer {CustomerId}",
                orderId, customerId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to add items to order {OrderId} for customer {CustomerId}",
                orderId, customerId);
            throw;
        }
    }
}

// ========== INFRASTRUCTURE ==========

public class LoggingDomainEventDispatcher(ILogger<LoggingDomainEventDispatcher> logger) : IDomainEventDispatcher
{
    private readonly ILogger<LoggingDomainEventDispatcher> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public Task DispatchAsync(IEnumerable<DomainEvent> events, CancellationToken cancellationToken = default)
    {
        foreach (DomainEvent domainEvent in events)
        {
            _logger.LogInformation("Domain event dispatched: {EventType} - {EventId} at {OccurredOn}",
                domainEvent.GetType().Name, domainEvent.Id, domainEvent.OccurredOn);
        }

        return Task.CompletedTask;
    }
}

public class InMemoryCustomerAggregateRepository(ILogger<InMemoryCustomerAggregateRepository> logger) : ICustomerAggregateRepository
{
    private static readonly Dictionary<Guid, CustomerAggregateRoot> _customers = [];
    private readonly ILogger<InMemoryCustomerAggregateRepository> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public Task<CustomerAggregateRoot?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Retrieving customer aggregate with ID {CustomerId}", id);

        bool found = _customers.TryGetValue(id, out CustomerAggregateRoot? customer);

        if (!found)
        {
            _logger.LogWarning("Customer aggregate with ID {CustomerId} not found", id);
        }
        else
        {
            _logger.LogDebug("Found customer aggregate {CustomerName} with ID {CustomerId}", customer!.Name, id);
        }

        return Task.FromResult(customer);
    }

    public Task SaveAsync(CustomerAggregateRoot customer, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(customer);

        bool isUpdate = _customers.ContainsKey(customer.Id);
        string action = isUpdate ? "Updated" : "Created";

        _customers[customer.Id] = customer;

        _logger.LogInformation("{Action} customer aggregate {CustomerName} with ID {CustomerId}",
            action, customer.Name, customer.Id);

        return Task.CompletedTask;
    }

    public static void ClearRepository()
    {
        _customers.Clear();
    }
}

// ========== EXTENSIONS ==========

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddCustomerDomain(this IServiceCollection services, IConfiguration configuration)
    {
        // Configure business rules
        services.Configure<CustomerBusinessRules>(configuration.GetSection("CustomerBusinessRules"));
        services.AddSingleton(provider =>
        {
            IOptions<CustomerBusinessRules> options = provider.GetRequiredService<IOptions<CustomerBusinessRules>>();
            return options.Value;
        });

        // Register repositories
        services.AddSingleton<ICustomerAggregateRepository, InMemoryCustomerAggregateRepository>();

        // Register domain event dispatcher
        services.AddSingleton<IDomainEventDispatcher, LoggingDomainEventDispatcher>();

        // Register application services
        services.AddTransient<CustomerApplicationService>();

        return services;
    }
}
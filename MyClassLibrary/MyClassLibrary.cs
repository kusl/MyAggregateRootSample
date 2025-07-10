// MyClassLibrary.cs - All library code in one file
using System.Reflection;
using System.Text.Json;
using Npgsql;
using Dapper;
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
        {
            throw new ArgumentException("Street cannot be null or empty.", nameof(street));
        }

        if (string.IsNullOrWhiteSpace(city))
        {
            throw new ArgumentException("City cannot be null or empty.", nameof(city));
        }

        if (string.IsNullOrWhiteSpace(state))
        {
            throw new ArgumentException("State cannot be null or empty.", nameof(state));
        }

        if (string.IsNullOrWhiteSpace(postalCode))
        {
            throw new ArgumentException("Postal code cannot be null or empty.", nameof(postalCode));
        }

        if (string.IsNullOrWhiteSpace(country))
        {
            throw new ArgumentException("Country cannot be null or empty.", nameof(country));
        }

        Street = street.Trim();
        City = city.Trim();
        State = state.Trim();
        PostalCode = postalCode.Trim();
        Country = country.Trim();
    }

    public override string ToString()
    {
        return $"{Street}, {City}, {State} {PostalCode}, {Country}";
    }
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
        {
            throw new ArgumentException("Product cannot be null or empty.", nameof(product));
        }

        if (quantity <= 0)
        {
            throw new ArgumentException("Quantity must be positive.", nameof(quantity));
        }

        if (price <= 0)
        {
            throw new ArgumentException("Price must be positive.", nameof(price));
        }

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
        {
            throw new ArgumentException("Order ID cannot be empty.", nameof(id));
        }

        if (orderDate > DateTime.UtcNow)
        {
            throw new ArgumentException("Order date cannot be in the future.", nameof(orderDate));
        }

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
            _ = _items.Remove(existingItem);
            _items.Add(new OrderItem(item.Product, existingItem.Quantity + item.Quantity, item.Price));
        }
        else
        {
            _items.Add(item);
        }
    }

    public decimal TotalAmount => _items.Sum(item => item.LineTotal);

    public bool IsOutstanding(int outstandingDays)
    {
        return OrderDate.AddDays(outstandingDays) > DateTime.UtcNow;
    }
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
        {
            throw new ArgumentException("Customer ID cannot be empty.", nameof(id));
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Customer name cannot be null or empty.", nameof(name));
        }

        Id = id;
        Name = name.Trim();
        _businessRules = businessRules ?? throw new ArgumentNullException(nameof(businessRules));
        _logger = logger;

        AddDomainEvent(new CustomerCreatedEvent(Guid.NewGuid(), DateTime.UtcNow, Id, Name));
    }

    // Replace the UpdateDefaultAddresses method in CustomerAggregateRoot with this fixed version:

    public void UpdateDefaultAddresses(Address? shippingAddress, Address? billingAddress)
    {
        Address? oldShipping = DefaultShippingAddress;
        Address? oldBilling = DefaultBillingAddress;

        DefaultShippingAddress = shippingAddress;
        DefaultBillingAddress = billingAddress;

        if (oldShipping != shippingAddress && shippingAddress != null)
        {
            AddDomainEvent(new CustomerAddressUpdatedEvent(
                Guid.NewGuid(),
                DateTime.UtcNow,
                Id,
                oldShipping!, // Use null-forgiving operator since we only create event when new address is not null
                shippingAddress));
            _logger?.LogInformation("Updated shipping address for customer {CustomerId}", Id);
        }

        if (oldBilling != billingAddress && billingAddress != null)
        {
            AddDomainEvent(new CustomerAddressUpdatedEvent(
                Guid.NewGuid(),
                DateTime.UtcNow,
                Id,
                oldBilling!, // Use null-forgiving operator since we only create event when new address is not null
                billingAddress));
            _logger?.LogInformation("Updated billing address for customer {CustomerId}", Id);
        }
    }

    public Order PlaceNewOrder(Address? shippingAddress = null, Address? billingAddress = null)
    {
        // Use provided addresses or fall back to defaults
        Address? orderShippingAddress = shippingAddress ?? DefaultShippingAddress;
        Address? orderBillingAddress = billingAddress ?? DefaultBillingAddress;

        if (orderShippingAddress == null)
        {
            throw new InvalidOperationException("Shipping address is required to place an order.");
        }

        if (orderBillingAddress == null)
        {
            throw new InvalidOperationException("Billing address is required to place an order.");
        }

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

// Replace the InMemoryCustomerAggregateRepository class in MyClassLibrary.cs with this thread-safe version:

public class InMemoryCustomerAggregateRepository(ILogger<InMemoryCustomerAggregateRepository> logger) : ICustomerAggregateRepository
{
    private static readonly Dictionary<Guid, CustomerAggregateRoot> _customers = [];
    private static readonly Lock _lock = new(); // Add lock for thread safety
    private readonly ILogger<InMemoryCustomerAggregateRepository> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public Task<CustomerAggregateRoot?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Retrieving customer aggregate with ID {CustomerId}", id);

        CustomerAggregateRoot? customer;
        lock (_lock)
        {
            _ = _customers.TryGetValue(id, out customer);
        }

        if (customer == null)
        {
            _logger.LogWarning("Customer aggregate with ID {CustomerId} not found", id);
        }
        else
        {
            _logger.LogDebug("Found customer aggregate {CustomerName} with ID {CustomerId}", customer.Name, id);
        }

        return Task.FromResult(customer);
    }

    public Task SaveAsync(CustomerAggregateRoot customer, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(customer);

        bool isUpdate;
        lock (_lock)
        {
            isUpdate = _customers.ContainsKey(customer.Id);
            _customers[customer.Id] = customer;
        }

        string action = isUpdate ? "Updated" : "Created";
        _logger.LogInformation("{Action} customer aggregate {CustomerName} with ID {CustomerId}",
            action, customer.Name, customer.Id);

        return Task.CompletedTask;
    }

    public static void ClearRepository()
    {
        lock (_lock)
        {
            _customers.Clear();
        }
    }
}

// ========== EXTENSIONS ==========

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddCustomerDomain(this IServiceCollection services, IConfiguration configuration)
    {
        // Configure business rules
        _ = services.Configure<CustomerBusinessRules>(configuration.GetSection("CustomerBusinessRules"));
        _ = services.AddSingleton(provider =>
        {
            IOptions<CustomerBusinessRules> options = provider.GetRequiredService<IOptions<CustomerBusinessRules>>();
            return options.Value;
        });

        // Register repositories
        _ = services.AddSingleton<ICustomerAggregateRepository, InMemoryCustomerAggregateRepository>();

        // Register domain event dispatcher
        _ = services.AddSingleton<IDomainEventDispatcher, LoggingDomainEventDispatcher>();

        // Register application services
        _ = services.AddTransient<CustomerApplicationService>();

        return services;
    }
}

// Add these sections to your MyClassLibrary.cs file

// ========== POSTGRESQL MODELS (Add after ENTITIES section) ==========

public class CustomerEntity
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? DefaultShippingAddressJson { get; set; }
    public string? DefaultBillingAddressJson { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class OrderEntity
{
    public Guid Id { get; set; }
    public Guid CustomerId { get; set; }
    public DateTime OrderDate { get; set; }
    public string ShippingAddressJson { get; set; } = string.Empty;
    public string BillingAddressJson { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class OrderItemEntity
{
    public Guid Id { get; set; }
    public Guid OrderId { get; set; }
    public string Product { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal Price { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class DomainEventEntity
{
    public Guid Id { get; set; }
    public Guid AggregateId { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string EventData { get; set; } = string.Empty;
    public DateTime OccurredOn { get; set; }
    public bool Processed { get; set; }
}

// ========== POSTGRESQL REPOSITORY (Add after INFRASTRUCTURE section) ==========

public class PostgreSqlCustomerAggregateRepository : ICustomerAggregateRepository
{
    private readonly string _connectionString;
    private readonly ILogger<PostgreSqlCustomerAggregateRepository> _logger;

    public PostgreSqlCustomerAggregateRepository(string connectionString, ILogger<PostgreSqlCustomerAggregateRepository> logger)
    {
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<CustomerAggregateRoot?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Retrieving customer aggregate with ID {CustomerId}", id);

        using NpgsqlConnection connection = new(_connectionString);
        await connection.OpenAsync(cancellationToken);
        using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            // Get customer
            const string customerSql = @"
                SELECT id, name, default_shipping_address_json, default_billing_address_json, created_at, updated_at
                FROM customers 
                WHERE id = @CustomerId";

            CustomerEntity? customerEntity = await connection.QueryFirstOrDefaultAsync<CustomerEntity>(
                customerSql, new { CustomerId = id }, transaction);

            if (customerEntity == null)
            {
                _logger.LogWarning("Customer aggregate with ID {CustomerId} not found", id);
                return null;
            }

            // Get orders
            const string ordersSql = @"
                SELECT id, customer_id, order_date, shipping_address_json, billing_address_json, created_at, updated_at
                FROM orders 
                WHERE customer_id = @CustomerId
                ORDER BY order_date";

            IEnumerable<OrderEntity> orderEntities = await connection.QueryAsync<OrderEntity>(
                ordersSql, new { CustomerId = id }, transaction);

            // Get order items
            const string orderItemsSql = @"
                SELECT oi.id, oi.order_id, oi.product, oi.quantity, oi.price, oi.created_at
                FROM order_items oi
                INNER JOIN orders o ON oi.order_id = o.id
                WHERE o.customer_id = @CustomerId
                ORDER BY oi.created_at";

            IEnumerable<OrderItemEntity> orderItemEntities = await connection.QueryAsync<OrderItemEntity>(
                orderItemsSql, new { CustomerId = id }, transaction);

            await transaction.CommitAsync(cancellationToken);

            // Reconstruct aggregate
            CustomerAggregateRoot customer = ReconstructCustomerAggregate(customerEntity, orderEntities, orderItemEntities);

            _logger.LogDebug("Found customer aggregate {CustomerName} with ID {CustomerId}", customer.Name, id);
            return customer;
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(cancellationToken);
            _logger.LogError(ex, "Error retrieving customer aggregate with ID {CustomerId}", id);
            throw;
        }
    }

    public async Task SaveAsync(CustomerAggregateRoot customer, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(customer);

        using NpgsqlConnection connection = new(_connectionString);
        await connection.OpenAsync(cancellationToken);
        using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            bool isUpdate = await CustomerExistsAsync(customer.Id, connection, transaction, cancellationToken);

            await SaveCustomerEntityAsync(customer, connection, transaction, cancellationToken);
            await SaveOrdersAsync(customer, connection, transaction, cancellationToken);
            await SaveDomainEventsAsync(customer, connection, transaction, cancellationToken);

            await transaction.CommitAsync(cancellationToken);

            string action = isUpdate ? "Updated" : "Created";
            _logger.LogInformation("{Action} customer aggregate {CustomerName} with ID {CustomerId}",
                action, customer.Name, customer.Id);
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(cancellationToken);
            _logger.LogError(ex, "Error saving customer aggregate {CustomerName} with ID {CustomerId}",
                customer.Name, customer.Id);
            throw;
        }
    }

    private async Task<bool> CustomerExistsAsync(Guid customerId, NpgsqlConnection connection,
        NpgsqlTransaction transaction, CancellationToken cancellationToken)
    {
        const string sql = "SELECT COUNT(1) FROM customers WHERE id = @CustomerId";
        int count = await connection.ExecuteScalarAsync<int>(sql, new { CustomerId = customerId }, transaction);
        return count > 0;
    }

    private async Task SaveCustomerEntityAsync(CustomerAggregateRoot customer, NpgsqlConnection connection,
        NpgsqlTransaction transaction, CancellationToken cancellationToken)
    {
        const string sql = @"
            INSERT INTO customers (id, name, default_shipping_address_json, default_billing_address_json, created_at, updated_at)
            VALUES (@Id, @Name, @DefaultShippingAddressJson, @DefaultBillingAddressJson, @CreatedAt, @UpdatedAt)
            ON CONFLICT (id) DO UPDATE SET
                name = EXCLUDED.name,
                default_shipping_address_json = EXCLUDED.default_shipping_address_json,
                default_billing_address_json = EXCLUDED.default_billing_address_json,
                updated_at = EXCLUDED.updated_at";

        await connection.ExecuteAsync(sql, new
        {
            Id = customer.Id,
            Name = customer.Name,
            DefaultShippingAddressJson = customer.DefaultShippingAddress != null ?
                JsonSerializer.Serialize(customer.DefaultShippingAddress) : null,
            DefaultBillingAddressJson = customer.DefaultBillingAddress != null ?
                JsonSerializer.Serialize(customer.DefaultBillingAddress) : null,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        }, transaction);
    }

    private async Task SaveOrdersAsync(CustomerAggregateRoot customer, NpgsqlConnection connection,
        NpgsqlTransaction transaction, CancellationToken cancellationToken)
    {
        // Get existing order IDs
        const string existingOrdersSql = "SELECT id FROM orders WHERE customer_id = @CustomerId";
        IEnumerable<Guid> existingOrderIds = await connection.QueryAsync<Guid>(
            existingOrdersSql, new { CustomerId = customer.Id }, transaction);
        HashSet<Guid> existingOrderSet = [.. existingOrderIds];

        foreach (Order order in customer.Orders)
        {
            bool isNewOrder = !existingOrderSet.Contains(order.Id);

            if (isNewOrder)
            {
                // Insert new order
                const string orderSql = @"
                    INSERT INTO orders (id, customer_id, order_date, shipping_address_json, billing_address_json, created_at, updated_at)
                    VALUES (@Id, @CustomerId, @OrderDate, @ShippingAddressJson, @BillingAddressJson, @CreatedAt, @UpdatedAt)";

                await connection.ExecuteAsync(orderSql, new
                {
                    Id = order.Id,
                    CustomerId = customer.Id,
                    OrderDate = order.OrderDate,
                    ShippingAddressJson = JsonSerializer.Serialize(order.ShippingAddress),
                    BillingAddressJson = JsonSerializer.Serialize(order.BillingAddress),
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                }, transaction);
            }

            // Handle order items - delete existing and re-insert (simple approach)
            const string deleteItemsSql = "DELETE FROM order_items WHERE order_id = @OrderId";
            await connection.ExecuteAsync(deleteItemsSql, new { OrderId = order.Id }, transaction);

            foreach (OrderItem item in order.Items)
            {
                const string itemSql = @"
                    INSERT INTO order_items (id, order_id, product, quantity, price, created_at)
                    VALUES (@Id, @OrderId, @Product, @Quantity, @Price, @CreatedAt)";

                await connection.ExecuteAsync(itemSql, new
                {
                    Id = Guid.NewGuid(),
                    OrderId = order.Id,
                    Product = item.Product,
                    Quantity = item.Quantity,
                    Price = item.Price,
                    CreatedAt = DateTime.UtcNow
                }, transaction);
            }
        }
    }

    private async Task SaveDomainEventsAsync(CustomerAggregateRoot customer, NpgsqlConnection connection,
        NpgsqlTransaction transaction, CancellationToken cancellationToken)
    {
        foreach (DomainEvent domainEvent in customer.DomainEvents)
        {
            const string sql = @"
                INSERT INTO domain_events (id, aggregate_id, event_type, event_data, occurred_on, processed)
                VALUES (@Id, @AggregateId, @EventType, @EventData, @OccurredOn, @Processed)
                ON CONFLICT (id) DO NOTHING";

            await connection.ExecuteAsync(sql, new
            {
                Id = domainEvent.Id,
                AggregateId = customer.Id,
                EventType = domainEvent.GetType().Name,
                EventData = JsonSerializer.Serialize(domainEvent),
                OccurredOn = domainEvent.OccurredOn,
                Processed = false
            }, transaction);
        }
    }

    private CustomerAggregateRoot ReconstructCustomerAggregate(CustomerEntity customerEntity,
        IEnumerable<OrderEntity> orderEntities, IEnumerable<OrderItemEntity> orderItemEntities)
    {
        // Create customer using reflection to bypass constructor validation
        CustomerAggregateRoot customer = CreateCustomerWithReflection(customerEntity);

        // Group order items by order
        Dictionary<Guid, List<OrderItemEntity>> orderItemsGrouped = orderItemEntities
            .GroupBy(oi => oi.OrderId)
            .ToDictionary(g => g.Key, g => g.ToList());

        // Reconstruct orders
        foreach (OrderEntity orderEntity in orderEntities)
        {
            Address shippingAddress = JsonSerializer.Deserialize<Address>(orderEntity.ShippingAddressJson)!;
            Address billingAddress = JsonSerializer.Deserialize<Address>(orderEntity.BillingAddressJson)!;

            Order order = new(orderEntity.Id, orderEntity.OrderDate, shippingAddress, billingAddress);

            // Add items to order
            if (orderItemsGrouped.TryGetValue(order.Id, out List<OrderItemEntity>? items))
            {
                foreach (OrderItemEntity itemEntity in items)
                {
                    OrderItem orderItem = new(itemEntity.Product, itemEntity.Quantity, itemEntity.Price);
                    order.AddItem(orderItem);
                }
            }

            // Add order to customer using reflection
            AddOrderToCustomerWithReflection(customer, order);
        }

        return customer;
    }

    private CustomerAggregateRoot CreateCustomerWithReflection(CustomerEntity customerEntity)
    {
        // Create empty customer using private constructor
        CustomerAggregateRoot customer = (CustomerAggregateRoot)Activator.CreateInstance(
            typeof(CustomerAggregateRoot), true)!;

        // Set properties using reflection
        SetPrivateProperty(customer, "Id", customerEntity.Id);
        SetPrivateProperty(customer, "Name", customerEntity.Name);

        if (!string.IsNullOrEmpty(customerEntity.DefaultShippingAddressJson))
        {
            Address shippingAddress = JsonSerializer.Deserialize<Address>(customerEntity.DefaultShippingAddressJson)!;
            SetPrivateProperty(customer, "DefaultShippingAddress", shippingAddress);
        }

        if (!string.IsNullOrEmpty(customerEntity.DefaultBillingAddressJson))
        {
            Address billingAddress = JsonSerializer.Deserialize<Address>(customerEntity.DefaultBillingAddressJson)!;
            SetPrivateProperty(customer, "DefaultBillingAddress", billingAddress);
        }

        return customer;
    }

    private void AddOrderToCustomerWithReflection(CustomerAggregateRoot customer, Order order)
    {
        FieldInfo? ordersField = typeof(CustomerAggregateRoot).GetField("_orders",
            BindingFlags.NonPublic | BindingFlags.Instance);

        if (ordersField?.GetValue(customer) is List<Order> orders)
        {
            orders.Add(order);
        }
    }

    private void SetPrivateProperty(object obj, string propertyName, object value)
    {
        PropertyInfo? property = obj.GetType().GetProperty(propertyName,
            BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);

        if (property != null && property.CanWrite)
        {
            property.SetValue(obj, value);
        }
        else
        {
            // Try to set backing field if property is read-only
            FieldInfo? field = obj.GetType().GetField($"<{propertyName}>k__BackingField",
                BindingFlags.NonPublic | BindingFlags.Instance);
            field?.SetValue(obj, value);
        }
    }
}

// ========== POSTGRESQL OUTBOX PATTERN (Add after PostgreSQL Repository) ==========

public class PostgreSqlOutboxDomainEventDispatcher : IDomainEventDispatcher
{
    private readonly string _connectionString;
    private readonly ILogger<PostgreSqlOutboxDomainEventDispatcher> _logger;

    public PostgreSqlOutboxDomainEventDispatcher(string connectionString, ILogger<PostgreSqlOutboxDomainEventDispatcher> logger)
    {
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task DispatchAsync(IEnumerable<DomainEvent> events, CancellationToken cancellationToken = default)
    {
        // In outbox pattern, events are already saved by the repository
        // This method could be used to mark events as processed or publish them
        foreach (DomainEvent domainEvent in events)
        {
            _logger.LogInformation("Domain event queued for processing: {EventType} - {EventId} at {OccurredOn}",
                domainEvent.GetType().Name, domainEvent.Id, domainEvent.OccurredOn);
        }
    }

    public async Task ProcessOutboxEventsAsync(CancellationToken cancellationToken = default)
    {
        using NpgsqlConnection connection = new(_connectionString);
        await connection.OpenAsync(cancellationToken);

        const string sql = @"
            SELECT id, aggregate_id, event_type, event_data, occurred_on
            FROM domain_events 
            WHERE processed = false
            ORDER BY occurred_on
            LIMIT 100";

        IEnumerable<DomainEventEntity> eventEntities = await connection.QueryAsync<DomainEventEntity>(sql);

        foreach (DomainEventEntity eventEntity in eventEntities)
        {
            try
            {
                // Here you would publish to message bus, call webhook, etc.
                _logger.LogInformation("Processing domain event: {EventType} - {EventId}",
                    eventEntity.EventType, eventEntity.Id);

                // Mark as processed
                const string updateSql = "UPDATE domain_events SET processed = true WHERE id = @Id";
                await connection.ExecuteAsync(updateSql, new { Id = eventEntity.Id });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process domain event {EventId}", eventEntity.Id);
            }
        }
    }
}

// ========== POSTGRESQL EXTENSIONS (Add to ServiceCollectionExtensions) ==========

public static class PostgreSqlServiceCollectionExtensions
{
    public static IServiceCollection AddCustomerDomainWithPostgreSql(this IServiceCollection services,
        IConfiguration configuration, string connectionString)
    {
        // Configure business rules
        _ = services.Configure<CustomerBusinessRules>(configuration.GetSection("CustomerBusinessRules"));
        _ = services.AddSingleton(provider =>
        {
            IOptions<CustomerBusinessRules> options = provider.GetRequiredService<IOptions<CustomerBusinessRules>>();
            return options.Value;
        });

        // Register PostgreSQL repositories
        _ = services.AddSingleton<ICustomerAggregateRepository>(provider =>
        {
            ILogger<PostgreSqlCustomerAggregateRepository> logger =
                provider.GetRequiredService<ILogger<PostgreSqlCustomerAggregateRepository>>();
            return new PostgreSqlCustomerAggregateRepository(connectionString, logger);
        });

        // Register PostgreSQL domain event dispatcher
        _ = services.AddSingleton<IDomainEventDispatcher>(provider =>
        {
            ILogger<PostgreSqlOutboxDomainEventDispatcher> logger =
                provider.GetRequiredService<ILogger<PostgreSqlOutboxDomainEventDispatcher>>();
            return new PostgreSqlOutboxDomainEventDispatcher(connectionString, logger);
        });

        // Register application services
        _ = services.AddTransient<CustomerApplicationService>();

        return services;
    }
}

// ========== DATABASE SCHEMA (Add as constants) ==========

public static class PostgreSqlSchema
{
    public const string CreateTablesScript = @"
        -- Create customers table
        CREATE TABLE IF NOT EXISTS customers (
            id UUID PRIMARY KEY,
            name VARCHAR(255) NOT NULL,
            default_shipping_address_json JSONB,
            default_billing_address_json JSONB,
            created_at TIMESTAMP WITH TIME ZONE NOT NULL,
            updated_at TIMESTAMP WITH TIME ZONE NOT NULL
        );

        -- Create orders table
        CREATE TABLE IF NOT EXISTS orders (
            id UUID PRIMARY KEY,
            customer_id UUID NOT NULL REFERENCES customers(id),
            order_date TIMESTAMP WITH TIME ZONE NOT NULL,
            shipping_address_json JSONB NOT NULL,
            billing_address_json JSONB NOT NULL,
            created_at TIMESTAMP WITH TIME ZONE NOT NULL,
            updated_at TIMESTAMP WITH TIME ZONE NOT NULL
        );

        -- Create order_items table
        CREATE TABLE IF NOT EXISTS order_items (
            id UUID PRIMARY KEY,
            order_id UUID NOT NULL REFERENCES orders(id) ON DELETE CASCADE,
            product VARCHAR(255) NOT NULL,
            quantity INTEGER NOT NULL CHECK (quantity > 0),
            price DECIMAL(10,2) NOT NULL CHECK (price > 0),
            created_at TIMESTAMP WITH TIME ZONE NOT NULL
        );

        -- Create domain_events table (outbox pattern)
        CREATE TABLE IF NOT EXISTS domain_events (
            id UUID PRIMARY KEY,
            aggregate_id UUID NOT NULL,
            event_type VARCHAR(255) NOT NULL,
            event_data JSONB NOT NULL,
            occurred_on TIMESTAMP WITH TIME ZONE NOT NULL,
            processed BOOLEAN NOT NULL DEFAULT FALSE
        );

        -- Create indexes
        CREATE INDEX IF NOT EXISTS idx_orders_customer_id ON orders(customer_id);
        CREATE INDEX IF NOT EXISTS idx_orders_order_date ON orders(order_date);
        CREATE INDEX IF NOT EXISTS idx_order_items_order_id ON order_items(order_id);
        CREATE INDEX IF NOT EXISTS idx_domain_events_processed ON domain_events(processed);
        CREATE INDEX IF NOT EXISTS idx_domain_events_occurred_on ON domain_events(occurred_on);
        CREATE INDEX IF NOT EXISTS idx_domain_events_aggregate_id ON domain_events(aggregate_id);
    ";

    public static async Task InitializeDatabaseAsync(string connectionString)
    {
        using NpgsqlConnection connection = new(connectionString);
        await connection.OpenAsync();
        await connection.ExecuteAsync(CreateTablesScript);
    }
}

// ========== ADDITIONAL USING STATEMENTS (Add to top of file) ==========
/*
You'll need to add these using statements at the top of your file:

using System.Reflection;
using System.Text.Json;
using Npgsql;
using Dapper;
*/

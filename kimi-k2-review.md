Prompt:


please do the same analysis on this updated code. the main change is that the code has been centralized into single file per csproj 
// MyClassLibrary.cs - All library code in one file
using System.Reflection;
using System.Text.Json;
using Dapper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

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

public class PostgreSqlCustomerAggregateRepository(string connectionString, ILogger<PostgreSqlCustomerAggregateRepository> logger) : ICustomerAggregateRepository
{
    private readonly string _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
    private readonly ILogger<PostgreSqlCustomerAggregateRepository> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

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

    private static async Task<bool> CustomerExistsAsync(Guid customerId, NpgsqlConnection connection,
        NpgsqlTransaction transaction, CancellationToken cancellationToken)
    {
        const string sql = "SELECT COUNT(1) FROM customers WHERE id = @CustomerId";

        CommandDefinition command = new(sql, new { CustomerId = customerId }, transaction, cancellationToken: cancellationToken);
        int count = await connection.ExecuteScalarAsync<int>(command);
        return count > 0;
    }

    private static async Task SaveCustomerEntityAsync(CustomerAggregateRoot customer, NpgsqlConnection connection,
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

        CommandDefinition command = new(sql, new
        {
            customer.Id,
            customer.Name,
            DefaultShippingAddressJson = customer.DefaultShippingAddress != null ?
                JsonSerializer.Serialize(customer.DefaultShippingAddress) : null,
            DefaultBillingAddressJson = customer.DefaultBillingAddress != null ?
                JsonSerializer.Serialize(customer.DefaultBillingAddress) : null,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        }, transaction, cancellationToken: cancellationToken);

        _ = await connection.ExecuteAsync(command);
    }

    private static async Task SaveOrdersAsync(CustomerAggregateRoot customer, NpgsqlConnection connection,
        NpgsqlTransaction transaction, CancellationToken cancellationToken)
    {
        // Get existing order IDs
        const string existingOrdersSql = "SELECT id FROM orders WHERE customer_id = @CustomerId";

        CommandDefinition existingOrdersCommand = new(existingOrdersSql, new { CustomerId = customer.Id }, transaction, cancellationToken: cancellationToken);
        IEnumerable<Guid> existingOrderIds = await connection.QueryAsync<Guid>(existingOrdersCommand);
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

                CommandDefinition orderCommand = new(orderSql, new
                {
                    order.Id,
                    CustomerId = customer.Id,
                    order.OrderDate,
                    ShippingAddressJson = JsonSerializer.Serialize(order.ShippingAddress),
                    BillingAddressJson = JsonSerializer.Serialize(order.BillingAddress),
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                }, transaction, cancellationToken: cancellationToken);

                _ = await connection.ExecuteAsync(orderCommand);
            }

            // Handle order items - delete existing and re-insert (simple approach)
            const string deleteItemsSql = "DELETE FROM order_items WHERE order_id = @OrderId";
            CommandDefinition deleteCommand = new(deleteItemsSql, new { OrderId = order.Id }, transaction, cancellationToken: cancellationToken);
            _ = await connection.ExecuteAsync(deleteCommand);

            foreach (OrderItem item in order.Items)
            {
                const string itemSql = @"
                    INSERT INTO order_items (id, order_id, product, quantity, price, created_at)
                    VALUES (@Id, @OrderId, @Product, @Quantity, @Price, @CreatedAt)";

                CommandDefinition itemCommand = new(itemSql, new
                {
                    Id = Guid.NewGuid(),
                    OrderId = order.Id,
                    item.Product,
                    item.Quantity,
                    item.Price,
                    CreatedAt = DateTime.UtcNow
                }, transaction, cancellationToken: cancellationToken);

                _ = await connection.ExecuteAsync(itemCommand);
            }
        }
    }

    private static async Task SaveDomainEventsAsync(CustomerAggregateRoot customer, NpgsqlConnection connection,
        NpgsqlTransaction transaction, CancellationToken cancellationToken)
    {
        foreach (DomainEvent domainEvent in customer.DomainEvents)
        {
            const string sql = @"
                INSERT INTO domain_events (id, aggregate_id, event_type, event_data, occurred_on, processed)
                VALUES (@Id, @AggregateId, @EventType, @EventData, @OccurredOn, @Processed)
                ON CONFLICT (id) DO NOTHING";

            CommandDefinition command = new(sql, new
            {
                domainEvent.Id,
                AggregateId = customer.Id,
                EventType = domainEvent.GetType().Name,
                EventData = JsonSerializer.Serialize(domainEvent),
                domainEvent.OccurredOn,
                Processed = false
            }, transaction, cancellationToken: cancellationToken);

            _ = await connection.ExecuteAsync(command);
        }
    }

    private static CustomerAggregateRoot ReconstructCustomerAggregate(CustomerEntity customerEntity,
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

    private static CustomerAggregateRoot CreateCustomerWithReflection(CustomerEntity customerEntity)
    {
        // Create empty customer using private constructor
        CustomerAggregateRoot customer = (CustomerAggregateRoot)Activator.CreateInstance(
            typeof(CustomerAggregateRoot), true)!;

        // Set properties using reflection
        SetPrivateProperty(customer, nameof(CustomerAggregateRoot.Id), customerEntity.Id);
        SetPrivateProperty(customer, nameof(CustomerAggregateRoot.Name), customerEntity.Name);

        if (!string.IsNullOrEmpty(customerEntity.DefaultShippingAddressJson))
        {
            Address shippingAddress = JsonSerializer.Deserialize<Address>(customerEntity.DefaultShippingAddressJson)!;
            SetPrivateProperty(customer, nameof(CustomerAggregateRoot.DefaultShippingAddress), shippingAddress);
        }

        if (!string.IsNullOrEmpty(customerEntity.DefaultBillingAddressJson))
        {
            Address billingAddress = JsonSerializer.Deserialize<Address>(customerEntity.DefaultBillingAddressJson)!;
            SetPrivateProperty(customer, nameof(CustomerAggregateRoot.DefaultBillingAddress), billingAddress);
        }

        return customer;
    }

    private static void AddOrderToCustomerWithReflection(CustomerAggregateRoot customer, Order order)
    {
        FieldInfo? ordersField = typeof(CustomerAggregateRoot).GetField("_orders",
            BindingFlags.NonPublic | BindingFlags.Instance);

        if (ordersField?.GetValue(customer) is List<Order> orders)
        {
            orders.Add(order);
        }
    }

    private static void SetPrivateProperty(object obj, string propertyName, object value)
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

public class PostgreSqlOutboxDomainEventDispatcher(string connectionString, ILogger<PostgreSqlOutboxDomainEventDispatcher> logger) : IDomainEventDispatcher
{
    private readonly string _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
    private readonly ILogger<PostgreSqlOutboxDomainEventDispatcher> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public Task DispatchAsync(IEnumerable<DomainEvent> events, CancellationToken cancellationToken = default)
    {
        // In outbox pattern, events are already saved by the repository
        // This method could be used to mark events as processed or publish them
        foreach (DomainEvent domainEvent in events)
        {
            _logger.LogInformation("Domain event queued for processing: {EventType} - {EventId} at {OccurredOn}",
                domainEvent.GetType().Name, domainEvent.Id, domainEvent.OccurredOn);
        }

        return Task.CompletedTask;
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

        CommandDefinition command = new(sql, cancellationToken: cancellationToken);
        IEnumerable<DomainEventEntity> eventEntities = await connection.QueryAsync<DomainEventEntity>(command);

        foreach (DomainEventEntity eventEntity in eventEntities)
        {
            try
            {
                // Here you would publish to message bus, call webhook, etc.
                _logger.LogInformation("Processing domain event: {EventType} - {EventId}",
                    eventEntity.EventType, eventEntity.Id);

                // Mark as processed
                const string updateSql = "UPDATE domain_events SET processed = true WHERE id = @Id";
                CommandDefinition updateCommand = new(updateSql, new { eventEntity.Id }, cancellationToken: cancellationToken);
                _ = await connection.ExecuteAsync(updateCommand);
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

    public static async Task InitializeDatabaseAsync(string connectionString, CancellationToken cancellationToken = default)
    {
        using NpgsqlConnection connection = new(connectionString);
        await connection.OpenAsync(cancellationToken);

        CommandDefinition command = new(CreateTablesScript, cancellationToken: cancellationToken);
        _ = await connection.ExecuteAsync(command);
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


// MyClassLibrary.Tests.cs - All test code in one file
using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;
[assembly: CollectionBehavior(DisableTestParallelization = true)]
namespace MyClassLibrary.Tests;

// ========== TEST HELPERS ==========

public class MockLogger<T> : ILogger<T>
{
    private readonly ConcurrentBag<LogEntry> _logEntries = [];

    public IReadOnlyList<LogEntry> LogEntries => [.. _logEntries];

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        _logEntries.Add(new LogEntry
        {
            LogLevel = logLevel,
            EventId = eventId,
            Message = formatter(state, exception),
            Exception = exception,
            Timestamp = DateTime.UtcNow
        });
    }

    public bool IsEnabled(LogLevel logLevel)
    {
        return true;
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull
    {
        return null;
    }

    public bool ContainsMessage(string partialMessage, LogLevel? logLevel = null)
    {
        return _logEntries.Any(e =>
            e.Message.Contains(partialMessage, StringComparison.OrdinalIgnoreCase) &&
            (logLevel == null || e.LogLevel == logLevel));
    }

    public void Clear()
    {
        _logEntries.Clear();
    }
}

public class LogEntry
{
    public LogLevel LogLevel { get; init; }
    public EventId EventId { get; init; }
    public string Message { get; init; } = string.Empty;
    public Exception? Exception { get; init; }
    public DateTime Timestamp { get; init; }
}

public static class TestDataBuilder
{
    public static CustomerAggregateRoot CreateCustomer(
        string name = "Test Customer",
        CustomerBusinessRules? businessRules = null,
        ILogger<CustomerAggregateRoot>? logger = null)
    {
        return new CustomerAggregateRoot(
            Guid.NewGuid(),
            name,
            businessRules ?? new CustomerBusinessRules(),
            logger);
    }

    public static Address CreateAddress(
        string street = "123 Test St",
        string city = "Test City",
        string state = "TS",
        string postalCode = "12345",
        string country = "Test Country")
    {
        return new Address(street, city, state, postalCode, country);
    }

    public static List<(string product, int quantity, decimal price)> CreateOrderItems(int count = 3)
    {
        List<(string product, int quantity, decimal price)> items = [];
        for (int i = 1; i <= count; i++)
        {
            items.Add(($"Product {i}", i, i * 10.50m));
        }
        return items;
    }

    public static OrderItem CreateOrderItem(
        string product = "Test Product",
        int quantity = 1,
        decimal price = 10.00m)
    {
        return new OrderItem(product, quantity, price);
    }
}

// ========== VALUE OBJECT TESTS ==========

public class AddressTests
{
    [Fact]
    public void Constructor_ValidParameters_CreatesAddress()
    {
        // Arrange & Act
        Address address = new("123 Main St", "Anytown", "CA", "12345", "USA");

        // Assert
        Assert.Equal("123 Main St", address.Street);
        Assert.Equal("Anytown", address.City);
        Assert.Equal("CA", address.State);
        Assert.Equal("12345", address.PostalCode);
        Assert.Equal("USA", address.Country);
    }

    [Theory]
    [InlineData(null, "city", "state", "12345", "country", "Street")]
    [InlineData("street", null, "state", "12345", "country", "City")]
    [InlineData("street", "city", null, "12345", "country", "State")]
    [InlineData("street", "city", "state", null, "country", "Postal code")]
    [InlineData("street", "city", "state", "12345", null, "Country")]
    [InlineData("", "city", "state", "12345", "country", "Street")]
    [InlineData("   ", "city", "state", "12345", "country", "Street")]
    public void Constructor_InvalidParameters_ThrowsArgumentException(
        string? street, string? city, string? state, string? postalCode, string? country, string paramName)
    {
        // Act & Assert
        ArgumentException exception = Assert.Throws<ArgumentException>(() =>
            new Address(street!, city!, state!, postalCode!, country!));
        Assert.Contains($"{paramName} cannot be null or empty", exception.Message);
    }

    [Fact]
    public void Constructor_TrimsWhitespace()
    {
        // Arrange & Act
        Address address = new("  123 Main St  ", "  Anytown  ", "  CA  ", "  12345  ", "  USA  ");

        // Assert
        Assert.Equal("123 Main St", address.Street);
        Assert.Equal("Anytown", address.City);
        Assert.Equal("CA", address.State);
        Assert.Equal("12345", address.PostalCode);
        Assert.Equal("USA", address.Country);
    }

    [Fact]
    public void ToString_ReturnsFormattedAddress()
    {
        // Arrange
        Address address = new("123 Main St", "Anytown", "CA", "12345", "USA");

        // Act
        string result = address.ToString();

        // Assert
        Assert.Equal("123 Main St, Anytown, CA 12345, USA", result);
    }

    [Fact]
    public void Address_RecordEquality_WorksCorrectly()
    {
        // Arrange
        Address address1 = new("123 Main St", "Anytown", "CA", "12345", "USA");
        Address address2 = new("123 Main St", "Anytown", "CA", "12345", "USA");
        Address address3 = new("456 Elm St", "Anytown", "CA", "12345", "USA");

        // Act & Assert
        Assert.Equal(address1, address2);
        Assert.NotEqual(address1, address3);
    }
}

public class OrderItemTests
{
    [Fact]
    public void Constructor_ValidParameters_CreatesOrderItem()
    {
        // Arrange
        const string product = "Test Product";
        const int quantity = 5;
        const decimal price = 10.50m;

        // Act
        OrderItem orderItem = new(product, quantity, price);

        // Assert
        Assert.Equal(product, orderItem.Product);
        Assert.Equal(quantity, orderItem.Quantity);
        Assert.Equal(price, orderItem.Price);
        Assert.Equal(52.50m, orderItem.LineTotal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_InvalidProduct_ThrowsArgumentException(string? product)
    {
        // Act & Assert
        ArgumentException exception = Assert.Throws<ArgumentException>(() =>
            new OrderItem(product!, 1, 10.00m));
        Assert.Contains("Product cannot be null or empty", exception.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100)]
    public void Constructor_InvalidQuantity_ThrowsArgumentException(int quantity)
    {
        // Act & Assert
        ArgumentException exception = Assert.Throws<ArgumentException>(() =>
            new OrderItem("Product", quantity, 10.00m));
        Assert.Contains("Quantity must be positive", exception.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100.50)]
    public void Constructor_InvalidPrice_ThrowsArgumentException(decimal price)
    {
        // Act & Assert
        ArgumentException exception = Assert.Throws<ArgumentException>(() =>
            new OrderItem("Product", 1, price));
        Assert.Contains("Price must be positive", exception.Message);
    }

    [Fact]
    public void Constructor_TrimsProductName()
    {
        // Arrange
        const string productWithSpaces = "  Test Product  ";

        // Act
        OrderItem orderItem = new(productWithSpaces, 1, 10.00m);

        // Assert
        Assert.Equal("Test Product", orderItem.Product);
    }

    [Fact]
    public void LineTotal_CalculatesCorrectly()
    {
        // Arrange
        (int quantity, decimal price, decimal expected)[] testCases =
        [
            (quantity: 1, price: 10.00m, expected: 10.00m),
            (quantity: 5, price: 15.50m, expected: 77.50m),
            (quantity: 100, price: 0.99m, expected: 99.00m),
            (quantity: 3, price: 33.33m, expected: 99.99m)
        ];

        foreach ((int quantity, decimal price, decimal expected) in testCases)
        {
            // Act
            OrderItem orderItem = new("Product", quantity, price);

            // Assert
            Assert.Equal(expected, orderItem.LineTotal);
        }
    }

    [Fact]
    public void OrderItem_RecordEquality_WorksCorrectly()
    {
        // Arrange
        OrderItem item1 = new("Product", 5, 10.00m);
        OrderItem item2 = new("Product", 5, 10.00m);
        OrderItem item3 = new("Product", 3, 10.00m);

        // Act & Assert
        Assert.Equal(item1, item2);
        Assert.NotEqual(item1, item3);
        Assert.True(item1 == item2);
        Assert.False(item1 == item3);
    }
}

// ========== ENTITY TESTS ==========

public class OrderTests : IDisposable
{
    public OrderTests()
    {
        InMemoryCustomerAggregateRepository.ClearRepository();
    }

    public void Dispose()
    {
        InMemoryCustomerAggregateRepository.ClearRepository();
    }

    [Fact]
    public void Constructor_ValidParameters_CreatesOrder()
    {
        // Arrange
        Guid orderId = Guid.NewGuid();
        DateTime orderDate = DateTime.UtcNow.AddDays(-1);
        Address shippingAddress = TestDataBuilder.CreateAddress();
        Address billingAddress = TestDataBuilder.CreateAddress("456 Bill Ave", "Billing City", "BC", "54321", "Bill Country");

        // Act
        Order order = new(orderId, orderDate, shippingAddress, billingAddress);

        // Assert
        Assert.Equal(orderId, order.Id);
        Assert.Equal(orderDate, order.OrderDate);
        Assert.Equal(shippingAddress, order.ShippingAddress);
        Assert.Equal(billingAddress, order.BillingAddress);
        Assert.Empty(order.Items);
        Assert.Equal(0m, order.TotalAmount);
    }

    [Fact]
    public void Constructor_EmptyId_ThrowsArgumentException()
    {
        // Arrange
        Address shippingAddress = TestDataBuilder.CreateAddress();
        Address billingAddress = TestDataBuilder.CreateAddress();

        // Act & Assert
        ArgumentException exception = Assert.Throws<ArgumentException>(() =>
            new Order(Guid.Empty, DateTime.UtcNow, shippingAddress, billingAddress));
        Assert.Contains("Order ID cannot be empty", exception.Message);
    }

    [Fact]
    public void Constructor_FutureDate_ThrowsArgumentException()
    {
        // Arrange
        Address shippingAddress = TestDataBuilder.CreateAddress();
        Address billingAddress = TestDataBuilder.CreateAddress();

        // Act & Assert
        ArgumentException exception = Assert.Throws<ArgumentException>(() =>
            new Order(Guid.NewGuid(), DateTime.UtcNow.AddDays(1), shippingAddress, billingAddress));
        Assert.Contains("Order date cannot be in the future", exception.Message);
    }

    [Fact]
    public void Constructor_NullShippingAddress_ThrowsArgumentNullException()
    {
        // Arrange
        Address billingAddress = TestDataBuilder.CreateAddress();

        // Act & Assert
        _ = Assert.Throws<ArgumentNullException>(() =>
            new Order(Guid.NewGuid(), DateTime.UtcNow, null!, billingAddress));
    }

    [Fact]
    public void Constructor_NullBillingAddress_ThrowsArgumentNullException()
    {
        // Arrange
        Address shippingAddress = TestDataBuilder.CreateAddress();

        // Act & Assert
        _ = Assert.Throws<ArgumentNullException>(() =>
            new Order(Guid.NewGuid(), DateTime.UtcNow, shippingAddress, null!));
    }

    [Fact]
    public void AddItem_ValidItem_AddsToOrder()
    {
        // Arrange
        Order order = new(Guid.NewGuid(), DateTime.UtcNow.AddHours(-1), TestDataBuilder.CreateAddress(), TestDataBuilder.CreateAddress());
        OrderItem item = TestDataBuilder.CreateOrderItem("Product 1", 2, 15.00m);

        // Act
        order.AddItem(item);

        // Assert
        _ = Assert.Single(order.Items);
        Assert.Equal(item, order.Items[0]);
        Assert.Equal(30.00m, order.TotalAmount);
    }

    [Fact]
    public void AddItem_NullItem_ThrowsArgumentNullException()
    {
        // Arrange
        Order order = new(Guid.NewGuid(), DateTime.UtcNow.AddHours(-1), TestDataBuilder.CreateAddress(), TestDataBuilder.CreateAddress());

        // Act & Assert
        _ = Assert.Throws<ArgumentNullException>(() => order.AddItem(null!));
    }

    [Fact]
    public void AddItem_DuplicateItem_CombinesQuantities()
    {
        // Arrange
        Order order = new(Guid.NewGuid(), DateTime.UtcNow.AddHours(-1), TestDataBuilder.CreateAddress(), TestDataBuilder.CreateAddress());
        OrderItem item1 = new("Product A", 2, 10.00m);
        OrderItem item2 = new("Product A", 3, 10.00m);

        // Act
        order.AddItem(item1);
        order.AddItem(item2);

        // Assert
        _ = Assert.Single(order.Items);
        Assert.Equal("Product A", order.Items[0].Product);
        Assert.Equal(5, order.Items[0].Quantity);
        Assert.Equal(10.00m, order.Items[0].Price);
        Assert.Equal(50.00m, order.TotalAmount);
    }

    [Fact]
    public void AddItem_SameProductDifferentPrice_DoesNotCombine()
    {
        // Arrange
        Order order = new(Guid.NewGuid(), DateTime.UtcNow.AddHours(-1), TestDataBuilder.CreateAddress(), TestDataBuilder.CreateAddress());
        OrderItem item1 = new("Product A", 2, 10.00m);
        OrderItem item2 = new("Product A", 3, 15.00m);

        // Act
        order.AddItem(item1);
        order.AddItem(item2);

        // Assert
        Assert.Equal(2, order.Items.Count);
        Assert.Equal(65.00m, order.TotalAmount); // (2 * 10) + (3 * 15)
    }

    [Fact]
    public void TotalAmount_MultipleItems_CalculatesCorrectly()
    {
        // Arrange
        Order order = new(Guid.NewGuid(), DateTime.UtcNow.AddHours(-1), TestDataBuilder.CreateAddress(), TestDataBuilder.CreateAddress());
        OrderItem[] items =
        [
            new OrderItem("Product 1", 2, 10.00m),
            new OrderItem("Product 2", 1, 15.50m),
            new OrderItem("Product 3", 3, 7.25m)
        ];

        // Act
        foreach (OrderItem? item in items)
        {
            order.AddItem(item);
        }

        // Assert
        Assert.Equal(57.25m, order.TotalAmount); // 20 + 15.50 + 21.75
    }

    [Theory]
    [InlineData(0, true)]     // Order from today is outstanding
    [InlineData(10, true)]    // 10 days ago - still within 30 days
    [InlineData(29, true)]    // 29 days ago - still within 30 days
    [InlineData(30, false)]   // 30 days ago - no longer outstanding
    [InlineData(31, false)]   // 31 days ago - no longer outstanding
    [InlineData(100, false)]  // 100 days ago - no longer outstanding
    public void IsOutstanding_VariousDays_ReturnsCorrectResult(int daysAgo, bool expectedOutstanding)
    {
        // Arrange
        DateTime orderDate = DateTime.UtcNow.AddDays(-daysAgo);
        Order order = new(Guid.NewGuid(), orderDate, TestDataBuilder.CreateAddress(), TestDataBuilder.CreateAddress());
        const int outstandingDays = 30;

        // Act
        bool isOutstanding = order.IsOutstanding(outstandingDays);

        // Assert
        Assert.Equal(expectedOutstanding, isOutstanding);
    }
}

// ========== AGGREGATE TESTS ==========

public class CustomerAggregateRootTests : IDisposable
{
    private readonly MockLogger<CustomerAggregateRoot> _mockLogger;
    private readonly CustomerBusinessRules _businessRules;

    public CustomerAggregateRootTests()
    {
        InMemoryCustomerAggregateRepository.ClearRepository();
        _mockLogger = new MockLogger<CustomerAggregateRoot>();
        _businessRules = new CustomerBusinessRules
        {
            MaxOutstandingOrders = 3,
            OutstandingOrderDays = 30
        };
    }

    public void Dispose()
    {
        InMemoryCustomerAggregateRepository.ClearRepository();
    }

    [Fact]
    public void Constructor_ValidParameters_CreatesCustomer()
    {
        // Arrange
        Guid customerId = Guid.NewGuid();
        const string customerName = "John Doe";

        // Act
        CustomerAggregateRoot customer = new(customerId, customerName, _businessRules, _mockLogger);

        // Assert
        Assert.Equal(customerId, customer.Id);
        Assert.Equal(customerName, customer.Name);
        Assert.Null(customer.DefaultShippingAddress);
        Assert.Null(customer.DefaultBillingAddress);
        Assert.Empty(customer.Orders);
        _ = Assert.Single(customer.DomainEvents);

        CustomerCreatedEvent createdEvent = Assert.IsType<CustomerCreatedEvent>(customer.DomainEvents[0]);
        Assert.Equal(customerId, createdEvent.CustomerId);
        Assert.Equal(customerName, createdEvent.CustomerName);
    }

    [Fact]
    public void Constructor_EmptyId_ThrowsArgumentException()
    {
        // Act & Assert
        ArgumentException exception = Assert.Throws<ArgumentException>(() =>
            new CustomerAggregateRoot(Guid.Empty, "Name", _businessRules, _mockLogger));
        Assert.Contains("Customer ID cannot be empty", exception.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_InvalidName_ThrowsArgumentException(string? name)
    {
        // Act & Assert
        ArgumentException exception = Assert.Throws<ArgumentException>(() =>
            new CustomerAggregateRoot(Guid.NewGuid(), name!, _businessRules, _mockLogger));
        Assert.Contains("Customer name cannot be null or empty", exception.Message);
    }

    [Fact]
    public void Constructor_NullBusinessRules_ThrowsArgumentNullException()
    {
        // Act & Assert
        _ = Assert.Throws<ArgumentNullException>(() =>
            new CustomerAggregateRoot(Guid.NewGuid(), "Name", null!, _mockLogger));
    }

    [Fact]
    public void Constructor_TrimsCustomerName()
    {
        // Arrange
        const string nameWithSpaces = "  John Doe  ";

        // Act
        CustomerAggregateRoot customer = new(Guid.NewGuid(), nameWithSpaces, _businessRules, _mockLogger);

        // Assert
        Assert.Equal("John Doe", customer.Name);
    }

    [Fact]
    public void UpdateDefaultAddresses_SetsAddresses_CreatesEvents()
    {
        // Arrange
        CustomerAggregateRoot customer = TestDataBuilder.CreateCustomer(businessRules: _businessRules, logger: _mockLogger);
        Address shippingAddress = TestDataBuilder.CreateAddress();
        Address billingAddress = TestDataBuilder.CreateAddress("456 Bill Ave", "Billing City", "BC", "54321", "Bill Country");

        // Act
        customer.UpdateDefaultAddresses(shippingAddress, billingAddress);

        // Assert
        Assert.Equal(shippingAddress, customer.DefaultShippingAddress);
        Assert.Equal(billingAddress, customer.DefaultBillingAddress);
        Assert.Equal(3, customer.DomainEvents.Count); // CustomerCreated + 2 AddressUpdated
        Assert.True(_mockLogger.ContainsMessage("Updated shipping address"));
        Assert.True(_mockLogger.ContainsMessage("Updated billing address"));
    }

    [Fact]
    public void UpdateDefaultAddresses_NullAddresses_SetsToNull()
    {
        // Arrange
        CustomerAggregateRoot customer = TestDataBuilder.CreateCustomer(businessRules: _businessRules, logger: _mockLogger);
        Address initialAddress = TestDataBuilder.CreateAddress();
        customer.UpdateDefaultAddresses(initialAddress, initialAddress);
        customer.ClearDomainEvents();

        // Act
        customer.UpdateDefaultAddresses(null, null);

        // Assert
        Assert.Null(customer.DefaultShippingAddress);
        Assert.Null(customer.DefaultBillingAddress);
        Assert.Empty(customer.DomainEvents); // No events when setting to null
    }

    [Fact]
    public void PlaceNewOrder_WithAddresses_CreatesOrder()
    {
        // Arrange
        CustomerAggregateRoot customer = TestDataBuilder.CreateCustomer(businessRules: _businessRules, logger: _mockLogger);
        Address shippingAddress = TestDataBuilder.CreateAddress();
        Address billingAddress = TestDataBuilder.CreateAddress("456 Bill Ave", "Billing City", "BC", "54321", "Bill Country");

        // Act
        Order order = customer.PlaceNewOrder(shippingAddress, billingAddress);

        // Assert
        Assert.NotEqual(Guid.Empty, order.Id);
        Assert.Equal(shippingAddress, order.ShippingAddress);
        Assert.Equal(billingAddress, order.BillingAddress);
        _ = Assert.Single(customer.Orders);
        Assert.Equal(order.Id, customer.Orders[0].Id);
        Assert.Equal(2, customer.DomainEvents.Count); // CustomerCreated + OrderPlaced

        OrderPlacedEvent? orderPlacedEvent = customer.DomainEvents.OfType<OrderPlacedEvent>().FirstOrDefault();
        Assert.NotNull(orderPlacedEvent);
        Assert.Equal(customer.Id, orderPlacedEvent.CustomerId);
        Assert.Equal(order.Id, orderPlacedEvent.OrderId);
        Assert.Equal(shippingAddress, orderPlacedEvent.ShippingAddress);
        Assert.Equal(billingAddress, orderPlacedEvent.BillingAddress);

        Assert.True(_mockLogger.ContainsMessage($"placed order {order.Id}"));
    }

    [Fact]
    public void PlaceNewOrder_WithDefaultAddresses_UsesDefaults()
    {
        // Arrange
        CustomerAggregateRoot customer = TestDataBuilder.CreateCustomer(businessRules: _businessRules, logger: _mockLogger);
        Address shippingAddress = TestDataBuilder.CreateAddress();
        Address billingAddress = TestDataBuilder.CreateAddress("456 Bill Ave", "Billing City", "BC", "54321", "Bill Country");
        customer.UpdateDefaultAddresses(shippingAddress, billingAddress);

        // Act
        Order order = customer.PlaceNewOrder();

        // Assert
        Assert.Equal(shippingAddress, order.ShippingAddress);
        Assert.Equal(billingAddress, order.BillingAddress);
    }

    [Fact]
    public void PlaceNewOrder_NoAddressesProvided_ThrowsException()
    {
        // Arrange
        CustomerAggregateRoot customer = TestDataBuilder.CreateCustomer(businessRules: _businessRules, logger: _mockLogger);

        // Act & Assert
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => customer.PlaceNewOrder());
        Assert.Contains("Shipping address is required", exception.Message);
    }

    [Fact]
    public void PlaceNewOrder_NoShippingAddress_ThrowsException()
    {
        // Arrange
        CustomerAggregateRoot customer = TestDataBuilder.CreateCustomer(businessRules: _businessRules, logger: _mockLogger);
        Address billingAddress = TestDataBuilder.CreateAddress();
        customer.UpdateDefaultAddresses(null, billingAddress);

        // Act & Assert
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => customer.PlaceNewOrder());
        Assert.Contains("Shipping address is required", exception.Message);
    }

    [Fact]
    public void PlaceNewOrder_NoBillingAddress_ThrowsException()
    {
        // Arrange
        CustomerAggregateRoot customer = TestDataBuilder.CreateCustomer(businessRules: _businessRules, logger: _mockLogger);
        Address shippingAddress = TestDataBuilder.CreateAddress();
        customer.UpdateDefaultAddresses(shippingAddress, null);

        // Act & Assert
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => customer.PlaceNewOrder());
        Assert.Contains("Billing address is required", exception.Message);
    }

    [Fact]
    public void PlaceNewOrder_ReachesMaxOutstandingOrders_ThrowsException()
    {
        // Arrange
        CustomerAggregateRoot customer = TestDataBuilder.CreateCustomer(businessRules: _businessRules, logger: _mockLogger);
        Address address = TestDataBuilder.CreateAddress();
        customer.UpdateDefaultAddresses(address, address);

        // Place maximum allowed orders
        for (int i = 0; i < _businessRules.MaxOutstandingOrders; i++)
        {
            _ = customer.PlaceNewOrder();
        }

        // Act & Assert
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => customer.PlaceNewOrder());
        Assert.Contains($"has reached the maximum of {_businessRules.MaxOutstandingOrders} outstanding orders", exception.Message);
        Assert.True(_mockLogger.ContainsMessage("Order placement failed", LogLevel.Warning));
    }

    [Fact]
    public void GetOrder_ExistingOrder_ReturnsOrder()
    {
        // Arrange
        CustomerAggregateRoot customer = TestDataBuilder.CreateCustomer(businessRules: _businessRules, logger: _mockLogger);
        Address address = TestDataBuilder.CreateAddress();
        Order order = customer.PlaceNewOrder(address, address);

        // Act
        Order? retrievedOrder = customer.GetOrder(order.Id);

        // Assert
        Assert.NotNull(retrievedOrder);
        Assert.Equal(order.Id, retrievedOrder.Id);
    }

    [Fact]
    public void GetOrder_NonExistentOrder_ReturnsNull()
    {
        // Arrange
        CustomerAggregateRoot customer = TestDataBuilder.CreateCustomer(businessRules: _businessRules, logger: _mockLogger);

        // Act
        Order? retrievedOrder = customer.GetOrder(Guid.NewGuid());

        // Assert
        Assert.Null(retrievedOrder);
    }

    [Fact]
    public void AddItemToOrder_ValidOrder_AddsItem()
    {
        // Arrange
        CustomerAggregateRoot customer = TestDataBuilder.CreateCustomer(businessRules: _businessRules, logger: _mockLogger);
        Address address = TestDataBuilder.CreateAddress();
        Order order = customer.PlaceNewOrder(address, address);
        OrderItem item = TestDataBuilder.CreateOrderItem("Product 1", 2, 25.00m);

        // Act
        customer.AddItemToOrder(order.Id, item);

        // Assert
        _ = Assert.Single(order.Items);
        Assert.Equal(item, order.Items[0]);
        Assert.Equal(3, customer.DomainEvents.Count); // CustomerCreated + OrderPlaced + OrderItemAdded

        OrderItemAddedEvent? itemAddedEvent = customer.DomainEvents.OfType<OrderItemAddedEvent>().FirstOrDefault();
        Assert.NotNull(itemAddedEvent);
        Assert.Equal(customer.Id, itemAddedEvent.CustomerId);
        Assert.Equal(order.Id, itemAddedEvent.OrderId);
        Assert.Equal(item, itemAddedEvent.Item);

        Assert.True(_mockLogger.ContainsMessage($"Added item {item.Product}"));
    }

    [Fact]
    public void AddItemToOrder_NonExistentOrder_ThrowsException()
    {
        // Arrange
        CustomerAggregateRoot customer = TestDataBuilder.CreateCustomer(businessRules: _businessRules, logger: _mockLogger);
        OrderItem item = TestDataBuilder.CreateOrderItem();
        Guid nonExistentOrderId = Guid.NewGuid();

        // Act & Assert
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            customer.AddItemToOrder(nonExistentOrderId, item));
        Assert.Contains($"Order {nonExistentOrderId} not found", exception.Message);
        Assert.True(_mockLogger.ContainsMessage("Failed to add item to order", LogLevel.Warning));
    }

    [Fact]
    public void ClearDomainEvents_RemovesAllEvents()
    {
        // Arrange
        CustomerAggregateRoot customer = TestDataBuilder.CreateCustomer(businessRules: _businessRules, logger: _mockLogger);
        Address address = TestDataBuilder.CreateAddress();
        _ = customer.PlaceNewOrder(address, address);
        Assert.Equal(2, customer.DomainEvents.Count);

        // Act
        customer.ClearDomainEvents();

        // Assert
        Assert.Empty(customer.DomainEvents);
    }

    [Fact]
    public void PlaceNewOrder_AllOrdersCountAsOutstanding_EnforcesLimit()
    {
        // Arrange
        CustomerBusinessRules businessRules = new()
        {
            MaxOutstandingOrders = 2,
            OutstandingOrderDays = 1 // Very short outstanding period
        };
        CustomerAggregateRoot customer = TestDataBuilder.CreateCustomer(businessRules: businessRules, logger: _mockLogger);
        Address address = TestDataBuilder.CreateAddress();
        customer.UpdateDefaultAddresses(address, address);

        // Place maximum allowed orders
        _ = customer.PlaceNewOrder();
        _ = customer.PlaceNewOrder();

        // Act & Assert - Third order should fail because all orders are considered outstanding
        // (The current implementation creates all orders with DateTime.UtcNow, so they're all outstanding)
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => customer.PlaceNewOrder());
        Assert.Contains("has reached the maximum of 2 outstanding orders", exception.Message);
    }
}

// ========== DOMAIN EVENT TESTS ==========

public class DomainEventTests
{
    [Fact]
    public void CustomerCreatedEvent_PropertiesSetCorrectly()
    {
        // Arrange
        Guid eventId = Guid.NewGuid();
        DateTime occurredOn = DateTime.UtcNow;
        Guid customerId = Guid.NewGuid();
        const string customerName = "Test Customer";

        // Act
        CustomerCreatedEvent @event = new(eventId, occurredOn, customerId, customerName);

        // Assert
        Assert.Equal(eventId, @event.Id);
        Assert.Equal(occurredOn, @event.OccurredOn);
        Assert.Equal(customerId, @event.CustomerId);
        Assert.Equal(customerName, @event.CustomerName);
    }

    [Fact]
    public void CustomerAddressUpdatedEvent_PropertiesSetCorrectly()
    {
        // Arrange
        Guid eventId = Guid.NewGuid();
        DateTime occurredOn = DateTime.UtcNow;
        Guid customerId = Guid.NewGuid();
        Address oldAddress = TestDataBuilder.CreateAddress("123 Old St", "Old City", "OL", "11111", "Old Country");
        Address newAddress = TestDataBuilder.CreateAddress("456 New Ave", "New City", "NW", "22222", "New Country");

        // Act
        CustomerAddressUpdatedEvent @event = new(eventId, occurredOn, customerId, oldAddress, newAddress);

        // Assert
        Assert.Equal(eventId, @event.Id);
        Assert.Equal(occurredOn, @event.OccurredOn);
        Assert.Equal(customerId, @event.CustomerId);
        Assert.Equal(oldAddress, @event.OldAddress);
        Assert.Equal(newAddress, @event.NewAddress);
    }

    [Fact]
    public void OrderPlacedEvent_PropertiesSetCorrectly()
    {
        // Arrange
        Guid eventId = Guid.NewGuid();
        DateTime occurredOn = DateTime.UtcNow;
        Guid customerId = Guid.NewGuid();
        Guid orderId = Guid.NewGuid();
        DateTime orderDate = DateTime.UtcNow.AddHours(-1);
        Address shippingAddress = TestDataBuilder.CreateAddress();
        Address billingAddress = TestDataBuilder.CreateAddress("456 Bill Ave", "Billing City", "BC", "54321", "Bill Country");

        // Act
        OrderPlacedEvent @event = new(eventId, occurredOn, customerId, orderId, orderDate, shippingAddress, billingAddress);

        // Assert
        Assert.Equal(eventId, @event.Id);
        Assert.Equal(occurredOn, @event.OccurredOn);
        Assert.Equal(customerId, @event.CustomerId);
        Assert.Equal(orderId, @event.OrderId);
        Assert.Equal(orderDate, @event.OrderDate);
        Assert.Equal(shippingAddress, @event.ShippingAddress);
        Assert.Equal(billingAddress, @event.BillingAddress);
    }

    [Fact]
    public void OrderItemAddedEvent_PropertiesSetCorrectly()
    {
        // Arrange
        Guid eventId = Guid.NewGuid();
        DateTime occurredOn = DateTime.UtcNow;
        Guid customerId = Guid.NewGuid();
        Guid orderId = Guid.NewGuid();
        OrderItem item = new("Product", 5, 10.00m);

        // Act
        OrderItemAddedEvent @event = new(eventId, occurredOn, customerId, orderId, item);

        // Assert
        Assert.Equal(eventId, @event.Id);
        Assert.Equal(occurredOn, @event.OccurredOn);
        Assert.Equal(customerId, @event.CustomerId);
        Assert.Equal(orderId, @event.OrderId);
        Assert.Equal(item, @event.Item);
    }

    [Fact]
    public void DomainEvents_RecordEquality_WorksCorrectly()
    {
        // Arrange
        Guid eventId = Guid.NewGuid();
        DateTime occurredOn = DateTime.UtcNow;
        Guid customerId = Guid.NewGuid();
        const string customerName = "Test";

        CustomerCreatedEvent event1 = new(eventId, occurredOn, customerId, customerName);
        CustomerCreatedEvent event2 = new(eventId, occurredOn, customerId, customerName);
        CustomerCreatedEvent event3 = new(Guid.NewGuid(), occurredOn, customerId, customerName);

        // Act & Assert
        Assert.Equal(event1, event2);
        Assert.NotEqual(event1, event3);
    }
}

// ========== APPLICATION SERVICE TESTS ==========

public class CustomerApplicationServiceTests : IDisposable
{
    private readonly MockCustomerRepository _mockRepository;
    private readonly MockDomainEventDispatcher _mockEventDispatcher;
    private readonly CustomerBusinessRules _businessRules;
    private readonly MockLogger<CustomerApplicationService> _mockServiceLogger;
    private readonly MockLogger<CustomerAggregateRoot> _mockCustomerLogger;
    private readonly CustomerApplicationService _service;

    public CustomerApplicationServiceTests()
    {
        InMemoryCustomerAggregateRepository.ClearRepository();
        _mockRepository = new MockCustomerRepository();
        _mockEventDispatcher = new MockDomainEventDispatcher();
        _businessRules = new CustomerBusinessRules();
        _mockServiceLogger = new MockLogger<CustomerApplicationService>();
        _mockCustomerLogger = new MockLogger<CustomerAggregateRoot>();

        _service = new CustomerApplicationService(
            _mockRepository,
            _mockEventDispatcher,
            _businessRules,
            _mockServiceLogger,
            _mockCustomerLogger);
    }

    public void Dispose()
    {
        InMemoryCustomerAggregateRepository.ClearRepository();
    }

    [Fact]
    public void Constructor_NullRepository_ThrowsArgumentNullException()
    {
        // Act & Assert
        _ = Assert.Throws<ArgumentNullException>(() =>
            new CustomerApplicationService(
                null!,
                _mockEventDispatcher,
                _businessRules,
                _mockServiceLogger,
                _mockCustomerLogger));
    }

    [Fact]
    public void Constructor_NullEventDispatcher_ThrowsArgumentNullException()
    {
        // Act & Assert
        _ = Assert.Throws<ArgumentNullException>(() =>
            new CustomerApplicationService(
                _mockRepository,
                null!,
                _businessRules,
                _mockServiceLogger,
                _mockCustomerLogger));
    }

    [Fact]
    public void Constructor_NullBusinessRules_ThrowsArgumentNullException()
    {
        // Act & Assert
        _ = Assert.Throws<ArgumentNullException>(() =>
            new CustomerApplicationService(
                _mockRepository,
                _mockEventDispatcher,
                null!,
                _mockServiceLogger,
                _mockCustomerLogger));
    }

    [Fact]
    public void Constructor_NullServiceLogger_ThrowsArgumentNullException()
    {
        // Act & Assert
        _ = Assert.Throws<ArgumentNullException>(() =>
            new CustomerApplicationService(
                _mockRepository,
                _mockEventDispatcher,
                _businessRules,
                null!,
                _mockCustomerLogger));
    }

    [Fact]
    public void Constructor_NullCustomerLogger_ThrowsArgumentNullException()
    {
        // Act & Assert
        _ = Assert.Throws<ArgumentNullException>(() =>
            new CustomerApplicationService(
                _mockRepository,
                _mockEventDispatcher,
                _businessRules,
                _mockServiceLogger,
                null!));
    }

    [Fact]
    public async Task CreateCustomerAndPlaceOrderAsync_Success_ReturnsCustomerId()
    {
        // Arrange
        const string customerName = "John Doe";
        Address shippingAddress = TestDataBuilder.CreateAddress();
        Address billingAddress = TestDataBuilder.CreateAddress("456 Bill Ave", "Billing City", "BC", "54321", "Bill Country");
        _ = TestDataBuilder.CreateOrderItems(3);

        // Act
        Guid customerId = await _service.CreateCustomerAndPlaceOrderAsync(customerName, shippingAddress, billingAddress, emptyOrderItems);

        // Assert
        Assert.NotEqual(Guid.Empty, customerId);
        CustomerAggregateRoot savedCustomer = _mockRepository.SavedCustomers.First();
        _ = Assert.Single(savedCustomer.Orders);
        Assert.Empty(savedCustomer.Orders[0].Items);
    }

    [Fact]
    public async Task CreateCustomerAndPlaceOrderAsync_Exception_LogsErrorAndRethrows()
    {
        // Arrange
        const string customerName = "Error Customer";
        Address shippingAddress = TestDataBuilder.CreateAddress();
        Address billingAddress = TestDataBuilder.CreateAddress();
        List<(string product, int quantity, decimal price)> orderItems = TestDataBuilder.CreateOrderItems();
        _mockRepository.ThrowOnSave = true;

        // Act & Assert
        _ = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.CreateCustomerAndPlaceOrderAsync(customerName, shippingAddress, billingAddress, orderItems));

        Assert.True(_mockServiceLogger.ContainsMessage($"Failed to create customer and place order for {customerName}", LogLevel.Error));
    }

    [Fact]
    public async Task UpdateCustomerAddressesAsync_Success_UpdatesAddresses()
    {
        // Arrange
        CustomerAggregateRoot customer = TestDataBuilder.CreateCustomer(businessRules: _businessRules, logger: _mockCustomerLogger);
        _mockRepository.AddCustomer(customer);

        Address newShippingAddress = TestDataBuilder.CreateAddress("789 New St", "New City", "NC", "99999", "New Country");
        Address newBillingAddress = TestDataBuilder.CreateAddress("321 Bill St", "Bill City", "BC", "88888", "Bill Country");

        // Act
        await _service.UpdateCustomerAddressesAsync(customer.Id, newShippingAddress, newBillingAddress);

        // Assert
        CustomerAggregateRoot updatedCustomer = _mockRepository.SavedCustomers.Last();
        Assert.Equal(newShippingAddress, updatedCustomer.DefaultShippingAddress);
        Assert.Equal(newBillingAddress, updatedCustomer.DefaultBillingAddress);
        Assert.True(_mockServiceLogger.ContainsMessage($"Successfully updated addresses for customer {customer.Id}", LogLevel.Information));
    }

    [Fact]
    public async Task UpdateCustomerAddressesAsync_CustomerNotFound_ThrowsException()
    {
        // Arrange
        Guid nonExistentCustomerId = Guid.NewGuid();
        Address address = TestDataBuilder.CreateAddress();

        // Act & Assert
        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.UpdateCustomerAddressesAsync(nonExistentCustomerId, address, address));

        Assert.Contains($"Customer {nonExistentCustomerId} not found", exception.Message);
        Assert.True(_mockServiceLogger.ContainsMessage($"Customer {nonExistentCustomerId} not found", LogLevel.Warning));
    }

    // Fix the PlaceOrderForExistingCustomerAsync_Success_PlacesOrder test to handle the fact
    // that the mock repository might have residual state:

    [Fact]
    public async Task PlaceOrderForExistingCustomerAsync_Success_PlacesOrder()
    {
        // Arrange
        CustomerAggregateRoot customer = TestDataBuilder.CreateCustomer(businessRules: _businessRules, logger: _mockCustomerLogger);
        Address defaultAddress = TestDataBuilder.CreateAddress();
        customer.UpdateDefaultAddresses(defaultAddress, defaultAddress);

        // Add an initial order to the customer
        _ = customer.PlaceNewOrder(defaultAddress, defaultAddress);

        _mockRepository.AddCustomer(customer);

        List<(string product, int quantity, decimal price)> orderItems = TestDataBuilder.CreateOrderItems(2);

        // Act
        Guid orderId = await _service.PlaceOrderForExistingCustomerAsync(customer.Id, null, null, orderItems);

        // Assert
        Assert.NotEqual(Guid.Empty, orderId);

        // Get the last saved state of this specific customer
        CustomerAggregateRoot updatedCustomer = _mockRepository.SavedCustomers
            .Last(c => c.Id == customer.Id);

        Assert.Equal(2, updatedCustomer.Orders.Count); // One from setup, one new

        Order? newOrder = updatedCustomer.Orders.FirstOrDefault(o => o.Id == orderId);
        Assert.NotNull(newOrder);
        Assert.Equal(2, newOrder.Items.Count);
        Assert.Equal(defaultAddress, newOrder.ShippingAddress); // Used default
        Assert.Equal(defaultAddress, newOrder.BillingAddress); // Used default

        Assert.True(_mockServiceLogger.ContainsMessage($"Successfully placed order {orderId}", LogLevel.Information));
    }

    [Fact]
    public async Task PlaceOrderForExistingCustomerAsync_WithSpecificAddresses_UsesProvidedAddresses()
    {
        // Arrange
        CustomerAggregateRoot customer = TestDataBuilder.CreateCustomer(businessRules: _businessRules, logger: _mockCustomerLogger);
        Address defaultAddress = TestDataBuilder.CreateAddress();
        customer.UpdateDefaultAddresses(defaultAddress, defaultAddress);
        _mockRepository.AddCustomer(customer);

        Address specificShipping = TestDataBuilder.CreateAddress("999 Ship St", "Ship City", "SH", "77777", "Ship Country");
        Address specificBilling = TestDataBuilder.CreateAddress("888 Bill St", "Bill City", "BL", "66666", "Bill Country");
        List<(string product, int quantity, decimal price)> orderItems = TestDataBuilder.CreateOrderItems(1);

        // Act
        Guid orderId = await _service.PlaceOrderForExistingCustomerAsync(
            customer.Id, specificShipping, specificBilling, orderItems);

        // Assert
        CustomerAggregateRoot updatedCustomer = _mockRepository.SavedCustomers.Last();
        Order? newOrder = updatedCustomer.Orders.FirstOrDefault(o => o.Id == orderId);
        Assert.NotNull(newOrder);
        Assert.Equal(specificShipping, newOrder.ShippingAddress);
        Assert.Equal(specificBilling, newOrder.BillingAddress);
    }

    [Fact]
    public async Task PlaceOrderForExistingCustomerAsync_CustomerNotFound_ThrowsException()
    {
        // Arrange
        Guid nonExistentCustomerId = Guid.NewGuid();
        Address address = TestDataBuilder.CreateAddress();
        List<(string product, int quantity, decimal price)> orderItems = TestDataBuilder.CreateOrderItems();

        // Act & Assert
        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.PlaceOrderForExistingCustomerAsync(nonExistentCustomerId, address, address, orderItems));

        Assert.Contains($"Customer {nonExistentCustomerId} not found", exception.Message);
        Assert.True(_mockServiceLogger.ContainsMessage($"Customer {nonExistentCustomerId} not found", LogLevel.Warning));
    }

    [Fact]
    public async Task AddOrderItemsToExistingOrderAsync_Success_AddsItems()
    {
        // Arrange
        CustomerAggregateRoot customer = TestDataBuilder.CreateCustomer(businessRules: _businessRules, logger: _mockCustomerLogger);
        Address address = TestDataBuilder.CreateAddress();
        Order order = customer.PlaceNewOrder(address, address);
        _mockRepository.AddCustomer(customer);

        List<(string product, int quantity, decimal price)> newItems = TestDataBuilder.CreateOrderItems(2);

        // Act
        await _service.AddOrderItemsToExistingOrderAsync(customer.Id, order.Id, newItems);

        // Assert
        Assert.Equal(2, _mockRepository.SavedCustomers.Count); // Original + updated
        CustomerAggregateRoot updatedCustomer = _mockRepository.SavedCustomers.Last();
        Assert.Equal(2, updatedCustomer.Orders[0].Items.Count);
        Assert.True(_mockServiceLogger.ContainsMessage($"Successfully added items to order {order.Id}", LogLevel.Information));
    }

    [Fact]
    public async Task AddOrderItemsToExistingOrderAsync_CustomerNotFound_ThrowsInvalidOperationException()
    {
        // Arrange
        Guid nonExistentCustomerId = Guid.NewGuid();
        Guid orderId = Guid.NewGuid();
        List<(string product, int quantity, decimal price)> items = TestDataBuilder.CreateOrderItems();

        // Act & Assert
        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.AddOrderItemsToExistingOrderAsync(nonExistentCustomerId, orderId, items));

        Assert.Contains($"Customer {nonExistentCustomerId} not found", exception.Message);
        Assert.True(_mockServiceLogger.ContainsMessage($"Customer {nonExistentCustomerId} not found", LogLevel.Warning));
    }

    [Fact]
    public async Task AddOrderItemsToExistingOrderAsync_Exception_LogsErrorAndRethrows()
    {
        // Arrange
        CustomerAggregateRoot customer = TestDataBuilder.CreateCustomer(businessRules: _businessRules, logger: _mockCustomerLogger);
        Address address = TestDataBuilder.CreateAddress();
        Order order = customer.PlaceNewOrder(address, address);
        _mockRepository.AddCustomer(customer);

        Guid nonExistentOrderId = Guid.NewGuid();
        List<(string product, int quantity, decimal price)> items = TestDataBuilder.CreateOrderItems();

        // Act & Assert
        _ = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.AddOrderItemsToExistingOrderAsync(customer.Id, nonExistentOrderId, items));

        Assert.True(_mockServiceLogger.ContainsMessage($"Failed to add items to order {nonExistentOrderId}", LogLevel.Error));
    }

    [Fact]
    public async Task CreateCustomerAndPlaceOrderAsync_DomainEventsAreDispatched()
    {
        // Arrange
        const string customerName = "Event Test Customer";
        Address shippingAddress = TestDataBuilder.CreateAddress();
        Address billingAddress = TestDataBuilder.CreateAddress();
        List<(string product, int quantity, decimal price)> orderItems = TestDataBuilder.CreateOrderItems(1);

        // Act
        _ = await _service.CreateCustomerAndPlaceOrderAsync(customerName, shippingAddress, billingAddress, orderItems);

        // Assert
        Assert.NotEmpty(_mockEventDispatcher.DispatchedEvents);
        List<Type> eventTypes = [.. _mockEventDispatcher.DispatchedEvents.Select(e => e.GetType())];
        Assert.Contains(typeof(CustomerCreatedEvent), eventTypes);
        Assert.Contains(typeof(CustomerAddressUpdatedEvent), eventTypes);
        Assert.Contains(typeof(OrderPlacedEvent), eventTypes);
        Assert.Contains(typeof(OrderItemAddedEvent), eventTypes);
    }

    // Mock implementations for testing
    private class MockCustomerRepository : ICustomerAggregateRepository
    {
        public List<CustomerAggregateRoot> SavedCustomers { get; } = [];
        private readonly Dictionary<Guid, CustomerAggregateRoot> _customers = [];
        public bool ThrowOnSave { get; set; }

        public Task<CustomerAggregateRoot?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        {
            _ = _customers.TryGetValue(id, out CustomerAggregateRoot? customer);
            return Task.FromResult(customer);
        }

        public Task SaveAsync(CustomerAggregateRoot customer, CancellationToken cancellationToken = default)
        {
            if (ThrowOnSave)
            {
                throw new InvalidOperationException("Save operation failed");
            }

            SavedCustomers.Add(customer);
            _customers[customer.Id] = customer;
            return Task.CompletedTask;
        }

        public void AddCustomer(CustomerAggregateRoot customer)
        {
            _customers[customer.Id] = customer;
            SavedCustomers.Add(customer);
        }
    }

    private class MockDomainEventDispatcher : IDomainEventDispatcher
    {
        public List<DomainEvent> DispatchedEvents { get; } = [];

        public Task DispatchAsync(IEnumerable<DomainEvent> events, CancellationToken cancellationToken = default)
        {
            DispatchedEvents.AddRange(events);
            return Task.CompletedTask;
        }
    }
    // Add this to your MyClassLibrary.Tests.cs file

    // Missing test data variable
    private static readonly List<(string product, int quantity, decimal price)> emptyOrderItems = [];

    // Additional edge case tests you might want to add

    public class CustomerBusinessRulesTests
    {
        [Fact]
        public void DefaultValues_AreSetCorrectly()
        {
            // Arrange & Act
            CustomerBusinessRules businessRules = new();

            // Assert
            Assert.Equal(10, businessRules.MaxOutstandingOrders);
            Assert.Equal(30, businessRules.OutstandingOrderDays);
        }

        [Fact]
        public void CanSetCustomValues()
        {
            // Arrange & Act
            CustomerBusinessRules businessRules = new()
            {
                MaxOutstandingOrders = 5,
                OutstandingOrderDays = 60
            };

            // Assert
            Assert.Equal(5, businessRules.MaxOutstandingOrders);
            Assert.Equal(60, businessRules.OutstandingOrderDays);
        }
    }

    // Additional integration tests
    public class IntegrationTests : IDisposable
    {
        private readonly ServiceProvider _serviceProvider;
        private readonly CustomerApplicationService _applicationService;

        public IntegrationTests()
        {
            InMemoryCustomerAggregateRepository.ClearRepository();

            ServiceCollection services = new();
            IConfigurationRoot configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                {"CustomerBusinessRules:MaxOutstandingOrders", "3"},
                {"CustomerBusinessRules:OutstandingOrderDays", "30"}
                })
                .Build();

            _ = services.AddLogging();
            _ = services.AddCustomerDomain(configuration);

            _serviceProvider = services.BuildServiceProvider();
            _applicationService = _serviceProvider.GetRequiredService<CustomerApplicationService>();
        }

        public void Dispose()
        {
            InMemoryCustomerAggregateRepository.ClearRepository();
            _serviceProvider?.Dispose();
        }

        [Fact]
        public async Task FullWorkflow_CreateCustomerPlaceOrdersAddItems_Success()
        {
            // Arrange
            const string customerName = "Integration Test Customer";
            Address shippingAddress = TestDataBuilder.CreateAddress();
            Address billingAddress = TestDataBuilder.CreateAddress("456 Bill Ave", "Billing City", "BC", "54321", "Bill Country");
            List<(string product, int quantity, decimal price)> initialItems = TestDataBuilder.CreateOrderItems(2);

            // Act - Create customer with initial order
            Guid customerId = await _applicationService.CreateCustomerAndPlaceOrderAsync(
                customerName, shippingAddress, billingAddress, initialItems);

            // Update addresses
            Address newShippingAddress = TestDataBuilder.CreateAddress("789 New St", "New City", "NC", "99999", "New Country");
            await _applicationService.UpdateCustomerAddressesAsync(customerId, newShippingAddress, billingAddress);

            // Place second order
            List<(string product, int quantity, decimal price)> secondOrderItems = TestDataBuilder.CreateOrderItems(1);
            Guid secondOrderId = await _applicationService.PlaceOrderForExistingCustomerAsync(
                customerId, null, null, secondOrderItems);

            // Add items to second order
            List<(string product, int quantity, decimal price)> additionalItems = [("Extra Product", 3, 25.99m)];
            await _applicationService.AddOrderItemsToExistingOrderAsync(customerId, secondOrderId, additionalItems);

            // Assert
            ICustomerAggregateRepository repository = _serviceProvider.GetRequiredService<ICustomerAggregateRepository>();
            CustomerAggregateRoot? customer = await repository.GetByIdAsync(customerId);

            Assert.NotNull(customer);
            Assert.Equal(customerName, customer.Name);
            Assert.Equal(newShippingAddress, customer.DefaultShippingAddress);
            Assert.Equal(2, customer.Orders.Count);

            Order? secondOrder = customer.Orders.FirstOrDefault(o => o.Id == secondOrderId);
            Assert.NotNull(secondOrder);
            Assert.Equal(2, secondOrder.Items.Count); // 1 initial + 1 additional
        }

        [Fact]
        public async Task MaxOutstandingOrders_EnforcedAcrossService()
        {
            // Arrange
            const string customerName = "Max Orders Test";
            Address address = TestDataBuilder.CreateAddress();
            List<(string product, int quantity, decimal price)> items = TestDataBuilder.CreateOrderItems(1);

            // Create customer with first order
            Guid customerId = await _applicationService.CreateCustomerAndPlaceOrderAsync(
                customerName, address, address, items);

            // Act - Place orders up to the limit (we already have 1, so place 2 more to reach 3)
            _ = await _applicationService.PlaceOrderForExistingCustomerAsync(customerId, null, null, items);
            _ = await _applicationService.PlaceOrderForExistingCustomerAsync(customerId, null, null, items);

            // Assert - Fourth order should fail
            _ = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                _applicationService.PlaceOrderForExistingCustomerAsync(customerId, null, null, items));
        }
    }

    // Performance/stress test example
    public class PerformanceTests : IDisposable
    {
        public PerformanceTests()
        {
            InMemoryCustomerAggregateRepository.ClearRepository();
        }

        public void Dispose()
        {
            InMemoryCustomerAggregateRepository.ClearRepository();
        }

        // 3. Fix the Repository_HandlesMultipleConcurrentOperations test:
        // Replace this test method with:

        [Fact]
        public async Task Repository_HandlesMultipleConcurrentOperations()
        {
            // Arrange
            InMemoryCustomerAggregateRepository repository = new(new MockLogger<InMemoryCustomerAggregateRepository>());
            List<CustomerAggregateRoot> customers = [.. Enumerable.Range(1, 10).Select(i => TestDataBuilder.CreateCustomer($"Customer {i}"))];

            // Act - Save all customers sequentially (the InMemoryRepository uses a static dictionary which isn't thread-safe)
            foreach (CustomerAggregateRoot? customer in customers)
            {
                await repository.SaveAsync(customer);
            }

            // Assert - All customers should be retrievable
            List<CustomerAggregateRoot?> retrievedCustomers = [];
            foreach (CustomerAggregateRoot? customer in customers)
            {
                CustomerAggregateRoot? retrieved = await repository.GetByIdAsync(customer.Id);
                retrievedCustomers.Add(retrieved);
            }

            Assert.All(retrievedCustomers, Assert.NotNull);
            Assert.Equal(customers.Count, retrievedCustomers.Count(c => c != null));
        }
    }

    // Additional test for Order edge cases
    public class OrderAdditionalTests : IDisposable
    {
        public OrderAdditionalTests()
        {
            InMemoryCustomerAggregateRepository.ClearRepository();
        }

        public void Dispose()
        {
            InMemoryCustomerAggregateRepository.ClearRepository();
        }

        [Fact]
        public void Order_ItemsCollection_IsReadOnly()
        {
            // Arrange
            Order order = new(Guid.NewGuid(), DateTime.UtcNow.AddHours(-1),
                TestDataBuilder.CreateAddress(), TestDataBuilder.CreateAddress());

            // Act & Assert
            _ = Assert.IsType<IReadOnlyList<OrderItem>>(order.Items, exactMatch: false);
            // The Items property returns a ReadOnlyCollection, so direct modification should not be possible
        }

        [Fact]
        public void Order_MultipleItemsSameProductSamePriceAcrossMultipleAdds_CombinesCorrectly()
        {
            // Arrange
            Order order = new(Guid.NewGuid(), DateTime.UtcNow.AddHours(-1),
                TestDataBuilder.CreateAddress(), TestDataBuilder.CreateAddress());

            // Act - Add same product multiple times
            order.AddItem(new OrderItem("Product A", 1, 10.00m));
            order.AddItem(new OrderItem("Product A", 2, 10.00m));
            order.AddItem(new OrderItem("Product A", 3, 10.00m));

            // Assert
            _ = Assert.Single(order.Items);
            Assert.Equal(6, order.Items[0].Quantity); // 1 + 2 + 3
            Assert.Equal(60.00m, order.TotalAmount);
        }
    }

    // Additional tests for CustomerAggregateRoot edge cases
    public class CustomerAggregateRootAdditionalTests : IDisposable
    {
        public CustomerAggregateRootAdditionalTests()
        {
            InMemoryCustomerAggregateRepository.ClearRepository();
        }

        public void Dispose()
        {
            InMemoryCustomerAggregateRepository.ClearRepository();
        }

        [Fact]
        public void Orders_Collection_IsReadOnly()
        {
            // Arrange
            CustomerAggregateRoot customer = TestDataBuilder.CreateCustomer();

            // Act & Assert
            _ = Assert.IsType<IReadOnlyList<Order>>(customer.Orders, exactMatch: false);
        }

        [Fact]
        public void DomainEvents_Collection_IsReadOnly()
        {
            // Arrange
            CustomerAggregateRoot customer = TestDataBuilder.CreateCustomer();

            // Act & Assert
            _ = Assert.IsType<IReadOnlyList<DomainEvent>>(customer.DomainEvents, exactMatch: false);
        }

        [Fact]
        public void UpdateDefaultAddresses_SameAddressForShippingAndBilling_BothEventsCreated()
        {
            // Arrange
            CustomerAggregateRoot customer = TestDataBuilder.CreateCustomer();
            Address sameAddress = TestDataBuilder.CreateAddress();
            customer.ClearDomainEvents();

            // Act
            customer.UpdateDefaultAddresses(sameAddress, sameAddress);

            // Assert
            Assert.Equal(2, customer.DomainEvents.Count);
            Assert.All(customer.DomainEvents, e => Assert.IsType<CustomerAddressUpdatedEvent>(e));
        }
    }
}

// ========== INFRASTRUCTURE TESTS ==========

public class LoggingDomainEventDispatcherTests
{
    private readonly MockLogger<LoggingDomainEventDispatcher> _mockLogger;
    private readonly LoggingDomainEventDispatcher _dispatcher;

    public LoggingDomainEventDispatcherTests()
    {
        _mockLogger = new MockLogger<LoggingDomainEventDispatcher>();
        _dispatcher = new LoggingDomainEventDispatcher(_mockLogger);
    }

    [Fact]
    public void Constructor_NullLogger_ThrowsArgumentNullException()
    {
        // Act & Assert
        _ = Assert.Throws<ArgumentNullException>(() =>
            new LoggingDomainEventDispatcher(null!));
    }

    [Fact]
    public async Task DispatchAsync_SingleEvent_LogsCorrectly()
    {
        // Arrange
        Guid customerId = Guid.NewGuid();
        CustomerCreatedEvent @event = new(
            Guid.NewGuid(),
            DateTime.UtcNow,
            customerId,
            "Test Customer");
        CustomerCreatedEvent[] events = [@event];

        // Act
        await _dispatcher.DispatchAsync(events);

        // Assert
        _ = Assert.Single(_mockLogger.LogEntries);
        LogEntry logEntry = _mockLogger.LogEntries[0];
        Assert.Equal(LogLevel.Information, logEntry.LogLevel);
        // Don't check for exact DateTime string match
        Assert.Contains("Domain event dispatched:", logEntry.Message);
        Assert.Contains("CustomerCreatedEvent", logEntry.Message);
        Assert.Contains(@event.Id.ToString(), logEntry.Message);
        Assert.Contains(" at ", logEntry.Message);
    }

    [Fact]
    public async Task DispatchAsync_MultipleEvents_LogsAllEvents()
    {
        // Arrange
        Guid customerId = Guid.NewGuid();
        Guid orderId = Guid.NewGuid();
        DomainEvent[] events =
        [
            new CustomerCreatedEvent(Guid.NewGuid(), DateTime.UtcNow, customerId, "Customer"),
            new OrderPlacedEvent(Guid.NewGuid(), DateTime.UtcNow, customerId, orderId, DateTime.UtcNow,
                TestDataBuilder.CreateAddress(), TestDataBuilder.CreateAddress()),
            new OrderItemAddedEvent(Guid.NewGuid(), DateTime.UtcNow, customerId, orderId,
                new OrderItem("Product", 1, 10m))
        ];

        // Act
        await _dispatcher.DispatchAsync(events);

        // Assert
        Assert.Equal(3, _mockLogger.LogEntries.Count);
        Assert.True(_mockLogger.ContainsMessage("CustomerCreatedEvent"));
        Assert.True(_mockLogger.ContainsMessage("OrderPlacedEvent"));
        Assert.True(_mockLogger.ContainsMessage("OrderItemAddedEvent"));
    }

    [Fact]
    public async Task DispatchAsync_EmptyEventList_DoesNotLog()
    {
        // Arrange
        List<DomainEvent> events = [];

        // Act
        await _dispatcher.DispatchAsync(events);

        // Assert
        Assert.Empty(_mockLogger.LogEntries);
    }

    [Fact]
    public async Task DispatchAsync_WithCancellationToken_CompletesSuccessfully()
    {
        // Arrange
        using CancellationTokenSource cts = new();
        CustomerCreatedEvent @event = new(
            Guid.NewGuid(),
            DateTime.UtcNow,
            Guid.NewGuid(),
            "Test Customer");

        // Act
        await _dispatcher.DispatchAsync([@event], cts.Token);

        // Assert
        _ = Assert.Single(_mockLogger.LogEntries);
    }

    [Fact]
    public async Task DispatchAsync_LogsEventTypeNameCorrectly()
    {
        // Arrange
        DomainEvent[] differentEvents =
        [
            new CustomerCreatedEvent(Guid.NewGuid(), DateTime.UtcNow, Guid.NewGuid(), "Customer"),
            new OrderPlacedEvent(Guid.NewGuid(), DateTime.UtcNow, Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow,
                TestDataBuilder.CreateAddress(), TestDataBuilder.CreateAddress())
        ];

        // Act
        await _dispatcher.DispatchAsync(differentEvents);

        // Assert
        List<string> logMessages = [.. _mockLogger.LogEntries.Select(e => e.Message)];
        Assert.Contains(logMessages, m => m.Contains("CustomerCreatedEvent"));
        Assert.Contains(logMessages, m => m.Contains("OrderPlacedEvent"));
    }
}

public class InMemoryCustomerAggregateRepositoryTests : IDisposable
{
    private readonly MockLogger<InMemoryCustomerAggregateRepository> _mockLogger;
    private readonly InMemoryCustomerAggregateRepository _repository;

    public InMemoryCustomerAggregateRepositoryTests()
    {
        InMemoryCustomerAggregateRepository.ClearRepository();
        _mockLogger = new MockLogger<InMemoryCustomerAggregateRepository>();
        _repository = new InMemoryCustomerAggregateRepository(_mockLogger);
    }

    public void Dispose()
    {
        InMemoryCustomerAggregateRepository.ClearRepository();
    }

    [Fact]
    public void Constructor_NullLogger_ThrowsArgumentNullException()
    {
        // Act & Assert
        _ = Assert.Throws<ArgumentNullException>(() =>
            new InMemoryCustomerAggregateRepository(null!));
    }

    [Fact]
    public async Task GetByIdAsync_NonExistentCustomer_ReturnsNull()
    {
        // Arrange
        Guid customerId = Guid.NewGuid();

        // Act
        CustomerAggregateRoot? result = await _repository.GetByIdAsync(customerId);

        // Assert
        Assert.Null(result);
        Assert.True(_mockLogger.ContainsMessage($"Customer aggregate with ID {customerId} not found", LogLevel.Warning));
    }

    [Fact]
    public async Task SaveAsync_NewCustomer_SavesSuccessfully()
    {
        // Arrange
        CustomerAggregateRoot customer = TestDataBuilder.CreateCustomer("John Doe");

        // Act
        await _repository.SaveAsync(customer);

        // Assert
        Assert.True(_mockLogger.ContainsMessage("Created customer aggregate", LogLevel.Information));
        Assert.True(_mockLogger.ContainsMessage(customer.Name));
        Assert.True(_mockLogger.ContainsMessage(customer.Id.ToString()));
    }

    [Fact]
    public async Task SaveAsync_NullCustomer_ThrowsArgumentNullException()
    {
        // Act & Assert
        _ = await Assert.ThrowsAsync<ArgumentNullException>(() =>
            _repository.SaveAsync(null!));
    }

    [Fact]
    public async Task GetByIdAsync_AfterSave_ReturnsCustomer()
    {
        // Arrange
        CustomerAggregateRoot customer = TestDataBuilder.CreateCustomer("Jane Doe");
        await _repository.SaveAsync(customer);
        _mockLogger.Clear();

        // Act
        CustomerAggregateRoot? retrievedCustomer = await _repository.GetByIdAsync(customer.Id);

        // Assert
        Assert.NotNull(retrievedCustomer);
        Assert.Equal(customer.Id, retrievedCustomer.Id);
        Assert.Equal(customer.Name, retrievedCustomer.Name);
        Assert.True(_mockLogger.ContainsMessage($"Found customer aggregate {customer.Name}", LogLevel.Debug));
    }

    [Fact]
    public async Task SaveAsync_ExistingCustomer_UpdatesSuccessfully()
    {
        // Arrange
        CustomerAggregateRoot customer = TestDataBuilder.CreateCustomer("Initial Name");
        await _repository.SaveAsync(customer);

        // Modify the customer (add an order)
        Address address = TestDataBuilder.CreateAddress();
        _ = customer.PlaceNewOrder(address, address);
        _mockLogger.Clear();

        // Act
        await _repository.SaveAsync(customer);

        // Assert
        Assert.True(_mockLogger.ContainsMessage("Updated customer aggregate", LogLevel.Information));

        // Verify the update persisted
        CustomerAggregateRoot? retrievedCustomer = await _repository.GetByIdAsync(customer.Id);
        Assert.NotNull(retrievedCustomer);
        _ = Assert.Single(retrievedCustomer.Orders);
    }

    [Fact]
    public async Task Repository_IsolatedBetweenInstances()
    {
        // Arrange
        InMemoryCustomerAggregateRepository repository1 = new(new MockLogger<InMemoryCustomerAggregateRepository>());
        InMemoryCustomerAggregateRepository repository2 = new(new MockLogger<InMemoryCustomerAggregateRepository>());

        CustomerAggregateRoot customer = TestDataBuilder.CreateCustomer("Test Customer");

        // Act
        await repository1.SaveAsync(customer);
        CustomerAggregateRoot? result1 = await repository1.GetByIdAsync(customer.Id);
        CustomerAggregateRoot? result2 = await repository2.GetByIdAsync(customer.Id);

        // Assert - Both repositories share the same static storage
        Assert.NotNull(result1);
        Assert.NotNull(result2);
        Assert.Equal(customer.Id, result1.Id);
        Assert.Equal(customer.Id, result2.Id);
    }

    // Also, update the SaveAsync_MultipleCustomers_SavesAll test to ensure complete isolation:

    [Fact]
    public async Task SaveAsync_MultipleCustomers_SavesAll()
    {
        // Arrange
        // Create a new repository instance for this test
        InMemoryCustomerAggregateRepository.ClearRepository(); // Clear before starting
        MockLogger<InMemoryCustomerAggregateRepository> testLogger = new();
        InMemoryCustomerAggregateRepository testRepository = new(testLogger);

        CustomerAggregateRoot[] customers =
        [
        TestDataBuilder.CreateCustomer("Customer 1"),
        TestDataBuilder.CreateCustomer("Customer 2"),
        TestDataBuilder.CreateCustomer("Customer 3")
    ];

        // Act
        foreach (CustomerAggregateRoot? customer in customers)
        {
            await testRepository.SaveAsync(customer);
        }

        // Assert - All customers should be retrievable
        foreach (CustomerAggregateRoot? customer in customers)
        {
            CustomerAggregateRoot? retrieved = await testRepository.GetByIdAsync(customer.Id);
            Assert.NotNull(retrieved);
            Assert.Equal(customer.Name, retrieved.Name);
        }
    }
}

// ========== EXTENSION TESTS ==========

public class ServiceCollectionExtensionsTests
{
    [Fact]
    public void AddCustomerDomain_RegistersAllRequiredServices()
    {
        // Arrange
        ServiceCollection services = new();
        IConfiguration configuration = CreateConfiguration();

        // Add required logging services
        _ = services.AddLogging();

        // Act
        _ = services.AddCustomerDomain(configuration);
        ServiceProvider serviceProvider = services.BuildServiceProvider();

        // Assert - Check all services are registered
        Assert.NotNull(serviceProvider.GetService<CustomerBusinessRules>());
        Assert.NotNull(serviceProvider.GetService<ICustomerAggregateRepository>());
        Assert.NotNull(serviceProvider.GetService<IDomainEventDispatcher>());
        Assert.NotNull(serviceProvider.GetService<CustomerApplicationService>());
    }

    [Fact]
    public void AddCustomerDomain_RegistersCorrectImplementations()
    {
        // Arrange
        ServiceCollection services = new();
        IConfiguration configuration = CreateConfiguration();
        _ = services.AddLogging();

        // Act
        _ = services.AddCustomerDomain(configuration);
        ServiceProvider serviceProvider = services.BuildServiceProvider();

        // Assert - Check correct implementations
        ICustomerAggregateRepository? repository = serviceProvider.GetService<ICustomerAggregateRepository>();
        _ = Assert.IsType<InMemoryCustomerAggregateRepository>(repository);

        IDomainEventDispatcher? eventDispatcher = serviceProvider.GetService<IDomainEventDispatcher>();
        _ = Assert.IsType<LoggingDomainEventDispatcher>(eventDispatcher);
    }

    [Fact]
    public void AddCustomerDomain_ConfiguresBusinessRulesFromConfiguration()
    {
        // Arrange
        ServiceCollection services = new();
        Dictionary<string, string?> configValues = new()
        {
            {"CustomerBusinessRules:MaxOutstandingOrders", "5"},
            {"CustomerBusinessRules:OutstandingOrderDays", "45"}
        };
        IConfigurationRoot configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configValues)
            .Build();
        _ = services.AddLogging();

        // Act
        _ = services.AddCustomerDomain(configuration);
        ServiceProvider serviceProvider = services.BuildServiceProvider();

        // Assert
        CustomerBusinessRules? businessRules = serviceProvider.GetService<CustomerBusinessRules>();
        Assert.NotNull(businessRules);
        Assert.Equal(5, businessRules.MaxOutstandingOrders);
        Assert.Equal(45, businessRules.OutstandingOrderDays);
    }

    [Fact]
    public void AddCustomerDomain_RegistersSingletonServices()
    {
        // Arrange
        ServiceCollection services = new();
        IConfiguration configuration = CreateConfiguration();
        _ = services.AddLogging();

        // Act
        _ = services.AddCustomerDomain(configuration);
        ServiceProvider serviceProvider = services.BuildServiceProvider();

        // Assert - Verify singleton behavior
        CustomerBusinessRules? businessRules1 = serviceProvider.GetService<CustomerBusinessRules>();
        CustomerBusinessRules? businessRules2 = serviceProvider.GetService<CustomerBusinessRules>();
        Assert.Same(businessRules1, businessRules2);

        ICustomerAggregateRepository? repository1 = serviceProvider.GetService<ICustomerAggregateRepository>();
        ICustomerAggregateRepository? repository2 = serviceProvider.GetService<ICustomerAggregateRepository>();
        Assert.Same(repository1, repository2);

        IDomainEventDispatcher? dispatcher1 = serviceProvider.GetService<IDomainEventDispatcher>();
        IDomainEventDispatcher? dispatcher2 = serviceProvider.GetService<IDomainEventDispatcher>();
        Assert.Same(dispatcher1, dispatcher2);
    }

    [Fact]
    public void AddCustomerDomain_RegistersTransientApplicationService()
    {
        // Arrange
        ServiceCollection services = new();
        IConfiguration configuration = CreateConfiguration();
        _ = services.AddLogging();

        // Act
        _ = services.AddCustomerDomain(configuration);
        ServiceProvider serviceProvider = services.BuildServiceProvider();

        // Assert - Verify transient behavior
        CustomerApplicationService? service1 = serviceProvider.GetService<CustomerApplicationService>();
        CustomerApplicationService? service2 = serviceProvider.GetService<CustomerApplicationService>();
        Assert.NotSame(service1, service2);
    }

    [Fact]
    public void AddCustomerDomain_CanResolveAllDependenciesForApplicationService()
    {
        // Arrange
        ServiceCollection services = new();
        IConfiguration configuration = CreateConfiguration();
        _ = services.AddLogging();

        // Act
        _ = services.AddCustomerDomain(configuration);
        ServiceProvider serviceProvider = services.BuildServiceProvider();

        // Assert - This will throw if any dependencies are missing
        CustomerApplicationService applicationService = serviceProvider.GetRequiredService<CustomerApplicationService>();
        Assert.NotNull(applicationService);
    }

    [Fact]
    public void AddCustomerDomain_DefaultBusinessRulesWhenNotInConfiguration()
    {
        // Arrange
        ServiceCollection services = new();
        IConfigurationRoot configuration = new ConfigurationBuilder().Build(); // Empty configuration
        _ = services.AddLogging();

        // Act
        _ = services.AddCustomerDomain(configuration);
        ServiceProvider serviceProvider = services.BuildServiceProvider();

        // Assert
        CustomerBusinessRules? businessRules = serviceProvider.GetService<CustomerBusinessRules>();
        Assert.NotNull(businessRules);
        Assert.Equal(10, businessRules.MaxOutstandingOrders); // Default value
        Assert.Equal(30, businessRules.OutstandingOrderDays); // Default value
    }

    private static IConfiguration CreateConfiguration()
    {
        Dictionary<string, string?> configValues = new()
        {
            {"CustomerBusinessRules:MaxOutstandingOrders", "10"},
            {"CustomerBusinessRules:OutstandingOrderDays", "30"}
        };
        return new ConfigurationBuilder()
            .AddInMemoryCollection(configValues)
            .Build();
    }
}

// Add these test sections to your MyClassLibrary.Tests.cs file
// These tests are completely additive - no existing code needs to change

// ========== POSTGRESQL ENTITY TESTS (Add new section) ==========

public class PostgreSqlEntityTests
{
    [Fact]
    public void CustomerEntity_PropertiesSetCorrectly()
    {
        // Arrange
        CustomerEntity entity = new()
        {
            Id = Guid.NewGuid(),
            Name = "Test Customer",
            DefaultShippingAddressJson = /*lang=json,strict*/ """{"Street":"123 Test St","City":"Test City","State":"TS","PostalCode":"12345","Country":"Test Country"}""",
            DefaultBillingAddressJson = /*lang=json,strict*/ """{"Street":"456 Bill Ave","City":"Bill City","State":"BC","PostalCode":"54321","Country":"Bill Country"}""",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        // Act & Assert
        Assert.NotEqual(Guid.Empty, entity.Id);
        Assert.Equal("Test Customer", entity.Name);
        Assert.NotNull(entity.DefaultShippingAddressJson);
        Assert.NotNull(entity.DefaultBillingAddressJson);
    }

    [Fact]
    public void OrderEntity_PropertiesSetCorrectly()
    {
        // Arrange
        OrderEntity entity = new()
        {
            Id = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            OrderDate = DateTime.UtcNow,
            ShippingAddressJson = /*lang=json,strict*/ """{"Street":"123 Test St","City":"Test City","State":"TS","PostalCode":"12345","Country":"Test Country"}""",
            BillingAddressJson = /*lang=json,strict*/ """{"Street":"456 Bill Ave","City":"Bill City","State":"BC","PostalCode":"54321","Country":"Bill Country"}""",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        // Act & Assert
        Assert.NotEqual(Guid.Empty, entity.Id);
        Assert.NotEqual(Guid.Empty, entity.CustomerId);
        Assert.NotNull(entity.ShippingAddressJson);
        Assert.NotNull(entity.BillingAddressJson);
    }

    [Fact]
    public void OrderItemEntity_PropertiesSetCorrectly()
    {
        // Arrange
        OrderItemEntity entity = new()
        {
            Id = Guid.NewGuid(),
            OrderId = Guid.NewGuid(),
            Product = "Test Product",
            Quantity = 5,
            Price = 19.99m,
            CreatedAt = DateTime.UtcNow
        };

        // Act & Assert
        Assert.NotEqual(Guid.Empty, entity.Id);
        Assert.NotEqual(Guid.Empty, entity.OrderId);
        Assert.Equal("Test Product", entity.Product);
        Assert.Equal(5, entity.Quantity);
        Assert.Equal(19.99m, entity.Price);
    }

    [Fact]
    public void DomainEventEntity_PropertiesSetCorrectly()
    {
        // Arrange
        DomainEventEntity entity = new()
        {
            Id = Guid.NewGuid(),
            AggregateId = Guid.NewGuid(),
            EventType = "CustomerCreatedEvent",
            EventData = /*lang=json,strict*/ """{"Id":"123","CustomerId":"456","CustomerName":"Test"}""",
            OccurredOn = DateTime.UtcNow,
            Processed = false
        };

        // Act & Assert
        Assert.NotEqual(Guid.Empty, entity.Id);
        Assert.NotEqual(Guid.Empty, entity.AggregateId);
        Assert.Equal("CustomerCreatedEvent", entity.EventType);
        Assert.NotNull(entity.EventData);
        Assert.False(entity.Processed);
    }
}

// ========== POSTGRESQL SCHEMA TESTS (Add new section) ==========

public class PostgreSqlSchemaTests
{
    [Fact]
    public void CreateTablesScript_IsNotNullOrEmpty()
    {
        // Act & Assert
        Assert.NotNull(PostgreSqlSchema.CreateTablesScript);
        Assert.NotEmpty(PostgreSqlSchema.CreateTablesScript);
    }

    [Fact]
    public void CreateTablesScript_ContainsExpectedTables()
    {
        // Arrange
        string script = PostgreSqlSchema.CreateTablesScript;

        // Act & Assert
        Assert.Contains("CREATE TABLE IF NOT EXISTS customers", script);
        Assert.Contains("CREATE TABLE IF NOT EXISTS orders", script);
        Assert.Contains("CREATE TABLE IF NOT EXISTS order_items", script);
        Assert.Contains("CREATE TABLE IF NOT EXISTS domain_events", script);
    }

    [Fact]
    public void CreateTablesScript_ContainsExpectedIndexes()
    {
        // Arrange
        string script = PostgreSqlSchema.CreateTablesScript;

        // Act & Assert
        Assert.Contains("CREATE INDEX IF NOT EXISTS idx_orders_customer_id", script);
        Assert.Contains("CREATE INDEX IF NOT EXISTS idx_orders_order_date", script);
        Assert.Contains("CREATE INDEX IF NOT EXISTS idx_order_items_order_id", script);
        Assert.Contains("CREATE INDEX IF NOT EXISTS idx_domain_events_processed", script);
        Assert.Contains("CREATE INDEX IF NOT EXISTS idx_domain_events_occurred_on", script);
        Assert.Contains("CREATE INDEX IF NOT EXISTS idx_domain_events_aggregate_id", script);
    }

    [Fact]
    public void CreateTablesScript_ContainsConstraints()
    {
        // Arrange
        string script = PostgreSqlSchema.CreateTablesScript;

        // Act & Assert
        Assert.Contains("CHECK (quantity > 0)", script);
        Assert.Contains("CHECK (price > 0)", script);
        Assert.Contains("REFERENCES customers(id)", script);
        Assert.Contains("REFERENCES orders(id)", script);
        Assert.Contains("ON DELETE CASCADE", script);
    }
}

// ========== POSTGRESQL INTEGRATION TESTS (Add new section) ==========

public class PostgreSqlIntegrationTests : IDisposable
{
    private readonly string _connectionString;
    private readonly bool _skipIntegrationTests;

    public PostgreSqlIntegrationTests()
    {
        // Try to get connection string from environment or use test database
        _connectionString = Environment.GetEnvironmentVariable("POSTGRESQL_TEST_CONNECTION_STRING")
            ?? "Host=localhost;Database=customer_test;Username=test;Password=test";

        // Skip if no PostgreSQL available
        _skipIntegrationTests = string.IsNullOrEmpty(Environment.GetEnvironmentVariable("POSTGRESQL_TEST_CONNECTION_STRING"));
    }

    public void Dispose()
    {
        if (!_skipIntegrationTests)
        {
            // Clean up test database
            CleanupTestDatabase();
        }
    }

    [Fact]
    public async Task InitializeDatabaseAsync_CreatesTablesSuccessfully()
    {
        // Arrange
        if (_skipIntegrationTests)
        {
            return; // Skip if no PostgreSQL available
        }

        // Act & Assert - Should not throw
        await PostgreSqlSchema.InitializeDatabaseAsync(_connectionString);
    }

    [Fact]
    public async Task PostgreSqlRepository_SaveAndRetrieve_WorksCorrectly()
    {
        // Arrange
        if (_skipIntegrationTests)
        {
            return; // Skip if no PostgreSQL available
        }

        await PostgreSqlSchema.InitializeDatabaseAsync(_connectionString);

        MockLogger<PostgreSqlCustomerAggregateRepository> logger = new();
        PostgreSqlCustomerAggregateRepository repository = new(_connectionString, logger);

        CustomerBusinessRules businessRules = new();
        MockLogger<CustomerAggregateRoot> customerLogger = new();
        CustomerAggregateRoot customer = new(Guid.NewGuid(), "Integration Test Customer", businessRules, customerLogger);

        Address address = TestDataBuilder.CreateAddress();
        customer.UpdateDefaultAddresses(address, address);

        Order order = customer.PlaceNewOrder();
        OrderItem item = TestDataBuilder.CreateOrderItem("Integration Test Product", 2, 25.00m);
        customer.AddItemToOrder(order.Id, item);

        // Act
        await repository.SaveAsync(customer);
        CustomerAggregateRoot? retrievedCustomer = await repository.GetByIdAsync(customer.Id);

        // Assert
        Assert.NotNull(retrievedCustomer);
        Assert.Equal(customer.Id, retrievedCustomer.Id);
        Assert.Equal(customer.Name, retrievedCustomer.Name);
        Assert.Equal(customer.DefaultShippingAddress, retrievedCustomer.DefaultShippingAddress);
        Assert.Equal(customer.DefaultBillingAddress, retrievedCustomer.DefaultBillingAddress);
        _ = Assert.Single(retrievedCustomer.Orders);
        _ = Assert.Single(retrievedCustomer.Orders[0].Items);
        Assert.Equal(item.Product, retrievedCustomer.Orders[0].Items[0].Product);
    }

    [Fact]
    public async Task PostgreSqlOutboxDispatcher_ProcessOutboxEventsAsync_WorksCorrectly()
    {
        // Arrange
        if (_skipIntegrationTests)
        {
            return; // Skip if no PostgreSQL available
        }

        await PostgreSqlSchema.InitializeDatabaseAsync(_connectionString);

        MockLogger<PostgreSqlOutboxDomainEventDispatcher> logger = new();
        PostgreSqlOutboxDomainEventDispatcher dispatcher = new(_connectionString, logger);

        // Act - Should not throw
        await dispatcher.ProcessOutboxEventsAsync();

        // Assert
        Assert.True(logger.ContainsMessage("Processing domain event") || logger.LogEntries.Count == 0);
    }

    [Fact]
    public async Task PostgreSqlRepository_HandlesCancellation()
    {
        // Arrange
        if (_skipIntegrationTests)
        {
            return; // Skip if no PostgreSQL available
        }

        await PostgreSqlSchema.InitializeDatabaseAsync(_connectionString);

        MockLogger<PostgreSqlCustomerAggregateRepository> logger = new();
        PostgreSqlCustomerAggregateRepository repository = new(_connectionString, logger);

        using CancellationTokenSource cts = new();
        cts.Cancel(); // Cancel immediately

        // Act & Assert
        _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            repository.GetByIdAsync(Guid.NewGuid(), cts.Token));
    }

    private void CleanupTestDatabase()
    {
        try
        {
            using Npgsql.NpgsqlConnection connection = new(_connectionString);
            connection.Open();

            const string cleanup = @"
                DROP TABLE IF EXISTS domain_events CASCADE;
                DROP TABLE IF EXISTS order_items CASCADE;
                DROP TABLE IF EXISTS orders CASCADE;
                DROP TABLE IF EXISTS customers CASCADE;
            ";

            using Npgsql.NpgsqlCommand command = new(cleanup, connection);
            _ = command.ExecuteNonQuery();
        }
        catch
        {
            // Ignore cleanup errors
        }
    }
}

// ========== POSTGRESQL SERVICE REGISTRATION TESTS (Add new section) ==========

public class PostgreSqlServiceRegistrationTests
{
    [Fact]
    public void AddCustomerDomainWithPostgreSql_RegistersAllRequiredServices()
    {
        // Arrange
        ServiceCollection services = new();
        IConfiguration configuration = CreateConfiguration();
        const string connectionString = "Host=localhost;Database=test;Username=test;Password=test";

        // Add required logging services
        _ = services.AddLogging();

        // Act
        _ = services.AddCustomerDomainWithPostgreSql(configuration, connectionString);
        ServiceProvider serviceProvider = services.BuildServiceProvider();

        // Assert - Check all services are registered
        Assert.NotNull(serviceProvider.GetService<CustomerBusinessRules>());
        Assert.NotNull(serviceProvider.GetService<ICustomerAggregateRepository>());
        Assert.NotNull(serviceProvider.GetService<IDomainEventDispatcher>());
        Assert.NotNull(serviceProvider.GetService<CustomerApplicationService>());
    }

    [Fact]
    public void AddCustomerDomainWithPostgreSql_RegistersPostgreSqlImplementations()
    {
        // Arrange
        ServiceCollection services = new();
        IConfiguration configuration = CreateConfiguration();
        const string connectionString = "Host=localhost;Database=test;Username=test;Password=test";
        _ = services.AddLogging();

        // Act
        _ = services.AddCustomerDomainWithPostgreSql(configuration, connectionString);
        ServiceProvider serviceProvider = services.BuildServiceProvider();

        // Assert - Check correct implementations
        ICustomerAggregateRepository? repository = serviceProvider.GetService<ICustomerAggregateRepository>();
        _ = Assert.IsType<PostgreSqlCustomerAggregateRepository>(repository);

        IDomainEventDispatcher? eventDispatcher = serviceProvider.GetService<IDomainEventDispatcher>();
        _ = Assert.IsType<PostgreSqlOutboxDomainEventDispatcher>(eventDispatcher);
    }

    [Fact]
    public void AddCustomerDomainWithPostgreSql_RegistersSingletonServices()
    {
        // Arrange
        ServiceCollection services = new();
        IConfiguration configuration = CreateConfiguration();
        const string connectionString = "Host=localhost;Database=test;Username=test;Password=test";
        _ = services.AddLogging();

        // Act
        _ = services.AddCustomerDomainWithPostgreSql(configuration, connectionString);
        ServiceProvider serviceProvider = services.BuildServiceProvider();

        // Assert - Verify singleton behavior
        CustomerBusinessRules? businessRules1 = serviceProvider.GetService<CustomerBusinessRules>();
        CustomerBusinessRules? businessRules2 = serviceProvider.GetService<CustomerBusinessRules>();
        Assert.Same(businessRules1, businessRules2);

        ICustomerAggregateRepository? repository1 = serviceProvider.GetService<ICustomerAggregateRepository>();
        ICustomerAggregateRepository? repository2 = serviceProvider.GetService<ICustomerAggregateRepository>();
        Assert.Same(repository1, repository2);

        IDomainEventDispatcher? dispatcher1 = serviceProvider.GetService<IDomainEventDispatcher>();
        IDomainEventDispatcher? dispatcher2 = serviceProvider.GetService<IDomainEventDispatcher>();
        Assert.Same(dispatcher1, dispatcher2);
    }

    [Fact]
    public void AddCustomerDomainWithPostgreSql_RegistersTransientApplicationService()
    {
        // Arrange
        ServiceCollection services = new();
        IConfiguration configuration = CreateConfiguration();
        const string connectionString = "Host=localhost;Database=test;Username=test;Password=test";
        _ = services.AddLogging();

        // Act
        _ = services.AddCustomerDomainWithPostgreSql(configuration, connectionString);
        ServiceProvider serviceProvider = services.BuildServiceProvider();

        // Assert - Verify transient behavior
        CustomerApplicationService? service1 = serviceProvider.GetService<CustomerApplicationService>();
        CustomerApplicationService? service2 = serviceProvider.GetService<CustomerApplicationService>();
        Assert.NotSame(service1, service2);
    }

    [Fact]
    public void AddCustomerDomainWithPostgreSql_CanResolveAllDependencies()
    {
        // Arrange
        ServiceCollection services = new();
        IConfiguration configuration = CreateConfiguration();
        const string connectionString = "Host=localhost;Database=test;Username=test;Password=test";
        _ = services.AddLogging();

        // Act
        _ = services.AddCustomerDomainWithPostgreSql(configuration, connectionString);
        ServiceProvider serviceProvider = services.BuildServiceProvider();

        // Assert - This will throw if any dependencies are missing
        CustomerApplicationService applicationService = serviceProvider.GetRequiredService<CustomerApplicationService>();
        Assert.NotNull(applicationService);
    }

    private static IConfiguration CreateConfiguration()
    {
        Dictionary<string, string?> configValues = new()
        {
            {"CustomerBusinessRules:MaxOutstandingOrders", "10"},
            {"CustomerBusinessRules:OutstandingOrderDays", "30"}
        };
        return new ConfigurationBuilder()
            .AddInMemoryCollection(configValues)
            .Build();
    }
}

// ========== POSTGRESQL REPOSITORY UNIT TESTS (Add new section) ==========

public class PostgreSqlRepositoryUnitTests
{
    [Fact]
    public void PostgreSqlCustomerAggregateRepository_Constructor_NullConnectionString_ThrowsArgumentNullException()
    {
        // Arrange
        MockLogger<PostgreSqlCustomerAggregateRepository> logger = new();

        // Act & Assert
        _ = Assert.Throws<ArgumentNullException>(() =>
            new PostgreSqlCustomerAggregateRepository(null!, logger));
    }

    [Fact]
    public void PostgreSqlCustomerAggregateRepository_Constructor_NullLogger_ThrowsArgumentNullException()
    {
        // Act & Assert
        _ = Assert.Throws<ArgumentNullException>(() =>
            new PostgreSqlCustomerAggregateRepository("connection", null!));
    }

    [Fact]
    public void PostgreSqlOutboxDomainEventDispatcher_Constructor_NullConnectionString_ThrowsArgumentNullException()
    {
        // Arrange
        MockLogger<PostgreSqlOutboxDomainEventDispatcher> logger = new();

        // Act & Assert
        _ = Assert.Throws<ArgumentNullException>(() =>
            new PostgreSqlOutboxDomainEventDispatcher(null!, logger));
    }

    [Fact]
    public void PostgreSqlOutboxDomainEventDispatcher_Constructor_NullLogger_ThrowsArgumentNullException()
    {
        // Act & Assert
        _ = Assert.Throws<ArgumentNullException>(() =>
            new PostgreSqlOutboxDomainEventDispatcher("connection", null!));
    }

    [Fact]
    public async Task PostgreSqlOutboxDomainEventDispatcher_DispatchAsync_LogsEvents()
    {
        // Arrange
        MockLogger<PostgreSqlOutboxDomainEventDispatcher> logger = new();
        PostgreSqlOutboxDomainEventDispatcher dispatcher = new("connection", logger);

        CustomerCreatedEvent[] events =
        [
            new CustomerCreatedEvent(Guid.NewGuid(), DateTime.UtcNow, Guid.NewGuid(), "Test Customer")
        ];

        // Act
        await dispatcher.DispatchAsync(events);

        // Assert
        _ = Assert.Single(logger.LogEntries);
        Assert.True(logger.ContainsMessage("Domain event queued for processing"));
        Assert.True(logger.ContainsMessage("CustomerCreatedEvent"));
    }

    [Fact]
    public async Task PostgreSqlOutboxDomainEventDispatcher_DispatchAsync_EmptyEvents_DoesNotLog()
    {
        // Arrange
        MockLogger<PostgreSqlOutboxDomainEventDispatcher> logger = new();
        PostgreSqlOutboxDomainEventDispatcher dispatcher = new("connection", logger);
        DomainEvent[] events = [];

        // Act
        await dispatcher.DispatchAsync(events);

        // Assert
        Assert.Empty(logger.LogEntries);
    }

    [Fact]
    public async Task PostgreSqlOutboxDomainEventDispatcher_DispatchAsync_WithCancellationToken_CompletesSuccessfully()
    {
        // Arrange
        MockLogger<PostgreSqlOutboxDomainEventDispatcher> logger = new();
        PostgreSqlOutboxDomainEventDispatcher dispatcher = new("connection", logger);
        using CancellationTokenSource cts = new();

        CustomerCreatedEvent[] events =
        [
            new CustomerCreatedEvent(Guid.NewGuid(), DateTime.UtcNow, Guid.NewGuid(), "Test Customer")
        ];

        // Act
        await dispatcher.DispatchAsync(events, cts.Token);

        // Assert
        _ = Assert.Single(logger.LogEntries);
    }
}

// ========== JSON SERIALIZATION TESTS (Add new section) ==========

public class JsonSerializationTests
{
    [Fact]
    public void Address_JsonSerialization_RoundTrip_WorksCorrectly()
    {
        // Arrange
        Address originalAddress = TestDataBuilder.CreateAddress();

        // Act
        string json = JsonSerializer.Serialize(originalAddress);
        Address? deserializedAddress = JsonSerializer.Deserialize<Address>(json);

        // Assert
        Assert.NotNull(deserializedAddress);
        Assert.Equal(originalAddress, deserializedAddress);
    }

    [Fact]
    public void CustomerCreatedEvent_JsonSerialization_RoundTrip_WorksCorrectly()
    {
        // Arrange
        CustomerCreatedEvent originalEvent = new(
            Guid.NewGuid(),
            DateTime.UtcNow,
            Guid.NewGuid(),
            "Test Customer");

        // Act
        string json = JsonSerializer.Serialize(originalEvent);
        CustomerCreatedEvent? deserializedEvent = JsonSerializer.Deserialize<CustomerCreatedEvent>(json);

        // Assert
        Assert.NotNull(deserializedEvent);
        Assert.Equal(originalEvent.Id, deserializedEvent.Id);
        Assert.Equal(originalEvent.CustomerId, deserializedEvent.CustomerId);
        Assert.Equal(originalEvent.CustomerName, deserializedEvent.CustomerName);
    }

    [Fact]
    public void OrderItem_JsonSerialization_RoundTrip_WorksCorrectly()
    {
        // Arrange
        OrderItem originalItem = TestDataBuilder.CreateOrderItem("Test Product", 3, 15.99m);

        // Act
        string json = JsonSerializer.Serialize(originalItem);
        OrderItem? deserializedItem = JsonSerializer.Deserialize<OrderItem>(json);

        // Assert
        Assert.NotNull(deserializedItem);
        Assert.Equal(originalItem, deserializedItem);
    }

    [Fact]
    public void DomainEvent_Polymorphic_JsonSerialization_WorksCorrectly()
    {
        // Arrange
        DomainEvent[] events =
        [
            new CustomerCreatedEvent(Guid.NewGuid(), DateTime.UtcNow, Guid.NewGuid(), "Customer 1"),
            new OrderPlacedEvent(Guid.NewGuid(), DateTime.UtcNow, Guid.NewGuid(), Guid.NewGuid(),
                DateTime.UtcNow, TestDataBuilder.CreateAddress(), TestDataBuilder.CreateAddress())
        ];

        // Act & Assert - Should not throw
        foreach (DomainEvent domainEvent in events)
        {
            string json = JsonSerializer.Serialize(domainEvent);
            Assert.NotNull(json);
            Assert.NotEmpty(json);
        }
    }
}

<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Dapper" Version="2.1.66" />
    <PackageReference Include="Microsoft.Extensions.Logging.Abstractions" Version="9.0.7" />
    <PackageReference Include="Microsoft.Extensions.Options" Version="9.0.7" />
    <PackageReference Include="Microsoft.Extensions.DependencyInjection" Version="9.0.7" />
    <PackageReference Include="Microsoft.Extensions.Configuration.Abstractions" Version="9.0.7" />
    <PackageReference Include="Microsoft.Extensions.Options.ConfigurationExtensions" Version="9.0.7" />
    <PackageReference Include="Npgsql" Version="9.0.3" />
  </ItemGroup>
</Project>

<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.14.1" />
    <PackageReference Include="xunit" Version="2.9.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="3.1.1">
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
      <PrivateAssets>all</PrivateAssets>
    </PackageReference>
    <PackageReference Include="coverlet.collector" Version="6.0.4">
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
      <PrivateAssets>all</PrivateAssets>
    </PackageReference>
    <PackageReference Include="Microsoft.Extensions.Logging" Version="9.0.7" />
    <PackageReference Include="Microsoft.Extensions.Configuration" Version="9.0.7" />
    <PackageReference Include="Microsoft.Extensions.Configuration.Json" Version="9.0.7" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\MyClassLibrary\MyClassLibrary.csproj" />
  </ItemGroup>
</Project>












Kimi K2:
## 🔍 Analysis: Dependency Injection & Inversion of Control

### ✅ **DI/IoC Status: EXCELLENT**
The centralized code **maintains and enhances** the DI/IoC implementation:

| Aspect | Evidence | Status |
|--------|----------|--------|
| **Constructor Injection** | All services use constructor injection | ✅ |
| **Interface Abstractions** | `ICustomerAggregateRepository`, `IDomainEventDispatcher` | ✅ |
| **IoC Registration** | `ServiceCollectionExtensions` & `PostgreSqlServiceCollectionExtensions` | ✅ |
| **No Hard-coded Services** | No `new` keywords for services in application code | ✅ |

---

## ❌ **Hard-Coded Values Analysis**

### **NEW ISSUES FOUND:**

#### 1. **Hard-Coded Database Schema** 🔴
```csharp
// PostgreSqlSchema.cs - BAD
public const string CreateTablesScript = @"..."; // 200+ lines of SQL
```
**Problem**: Schema is hard-coded string instead of migrations or external files.

#### 2. **Hard-Coded Connection Strings** 🟡
```csharp
// Tests use fallback connection strings
"Host=localhost;Database=test;Username=test;Password=test"
```
**Status**: Acceptable for tests, but should be configurable.

#### 3. **Hard-Coded JSON Serialization Settings** 🟡
```csharp
// No JsonSerializerOptions configuration
JsonSerializer.Serialize(address)
```
**Problem**: No centralized JSON configuration.

### **FIXED ISSUES:**

#### ✅ **GUID/Timestamp Generation** 
```csharp
// Still hard-coded but now abstracted
CustomerCreatedEvent(Guid.NewGuid(), DateTime.UtcNow, ...)
```
**Improvement**: Less problematic as it's in domain events, not business logic.

---

## 🆕 **NEW DI COMPONENTS ADDED**

### **Repository Strategy Pattern**
```csharp
// Two implementations registered via different extensions
services.AddCustomerDomain() // In-Memory
services.AddCustomerDomainWithPostgreSql() // PostgreSQL
```

### **Outbox Pattern Implementation**
```csharp
// New PostgreSqlOutboxDomainEventDispatcher
services.AddSingleton<IDomainEventDispatcher, PostgreSqlOutboxDomainEventDispatcher>()
```

---

## 🎯 **Dependency Injection Quality**

### **Service Registration Quality**
| Service | Lifetime | Implementation | Configurable |
|---------|----------|----------------|--------------|
| `ICustomerAggregateRepository` | Singleton | Switchable | ✅ |
| `IDomainEventDispatcher` | Singleton | Switchable | ✅ |
| `CustomerBusinessRules` | Singleton | IOptions | ✅ |
| `CustomerApplicationService` | Transient | - | ✅ |

### **Configuration Support**
```csharp
// Fully configurable via IConfiguration
services.Configure<CustomerBusinessRules>(configuration.GetSection("CustomerBusinessRules"))
```

---

## 🚨 **Critical New Hard-Coding Issues**

### **1. PostgreSQL Schema** 
```csharp
// 200+ lines of SQL hard-coded
public static class PostgreSqlSchema
```
**Recommendation**: Move to separate `.sql` files or use EF Core migrations.

### **2. Reflection-Based Reconstruction**
```csharp
// Uses reflection for ORM reconstruction - brittle
CreateCustomerWithReflection(customerEntity)
```
**Problem**: Hard-coded knowledge of internal structure.

### **3. JSON Column Names**
```csharp
default_shipping_address_json // Hard-coded column names
```

---

## 📊 **Final DI/IoC Score**

| Category | Rating | Notes |
|----------|--------|-------|
| **Constructor Injection** | ⭐⭐⭐⭐⭐ | Perfect implementation |
| **Service Registration** | ⭐⭐⭐⭐⭐ | Clean, switchable implementations |
| **Configuration** | ⭐⭐⭐⭐⭐ | Full IOptions support |
| **Hard-coded Values** | ⭐⭐⭐ | SQL schema and reflection issues |
| **Testability** | ⭐⭐⭐⭐⭐ | Mockable interfaces throughout |

---

## 🛠️ **Recommended Fixes**

### **1. Extract SQL Schema**
```csharp
// Instead of hard-coded string
public static class PostgreSqlSchema
{
    public static async Task InitializeFromFileAsync(string schemaPath) { ... }
}
```

### **2. Add JSON Serialization Options**
```csharp
services.Configure<JsonSerializerOptions>(options => 
{
    options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    // Centralized JSON config
});
```

### **3. Use EF Core for ORM**
```csharp
// Replace reflection with proper ORM
public class CustomerDbContext : DbContext
{
    public DbSet<CustomerEntity> Customers { get; set; }
}
```

---

## 🏁 **Conclusion**

The **centralized version maintains excellent DI/IoC practices** while adding **PostgreSQL support** and **Outbox pattern**. The main regression is **hard-coding the SQL schema** instead of using migrations. Overall, **DI quality improved** with better separation of concerns and switchable implementations.

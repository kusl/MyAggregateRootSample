using MyClassLibrary.Application.Services;
using MyClassLibrary.Application.Interfaces;
using MyClassLibrary.Domain.Aggregates;
using MyClassLibrary.Domain.Configuration;
using MyClassLibrary.Domain.Events;
using MyClassLibrary.Tests.TestHelpers;
using Microsoft.Extensions.Logging;
using Xunit;

namespace MyClassLibrary.Tests.Application.Services;

public class CustomerApplicationServiceTests
{
    private readonly MockCustomerRepository _mockRepository;
    private readonly MockDomainEventDispatcher _mockEventDispatcher;
    private readonly CustomerBusinessRules _businessRules;
    private readonly MockLogger<CustomerApplicationService> _mockServiceLogger;
    private readonly MockLogger<CustomerAggregateRoot> _mockCustomerLogger;
    private readonly CustomerApplicationService _service;

    public CustomerApplicationServiceTests()
    {
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

    [Fact]
    public void Constructor_NullRepository_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
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
        Assert.Throws<ArgumentNullException>(() =>
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
        Assert.Throws<ArgumentNullException>(() =>
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
        Assert.Throws<ArgumentNullException>(() =>
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
        Assert.Throws<ArgumentNullException>(() =>
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
        List<(string product, int quantity, decimal price)> orderItems = TestDataBuilder.CreateOrderItems(3);

        // Act
        Guid customerId = await _service.CreateCustomerAndPlaceOrderAsync(customerName, orderItems);

        // Assert
        Assert.NotEqual(Guid.Empty, customerId);
        Assert.Single(_mockRepository.SavedCustomers);

        CustomerAggregateRoot savedCustomer = _mockRepository.SavedCustomers.First();
        Assert.Equal(customerId, savedCustomer.Id);
        Assert.Equal(customerName, savedCustomer.Name);
        Assert.Single(savedCustomer.Orders);
        Assert.Equal(3, savedCustomer.Orders[0].Items.Count);

        Assert.NotEmpty(_mockEventDispatcher.DispatchedEvents);
        Assert.True(_mockServiceLogger.ContainsMessage($"Successfully created customer {customerName}", LogLevel.Information));
    }

    [Fact]
    public async Task CreateCustomerAndPlaceOrderAsync_EmptyOrderItems_CreatesOrderWithNoItems()
    {
        // Arrange
        const string customerName = "Jane Doe";
        List<(string product, int quantity, decimal price)> emptyOrderItems = [];

        // Act
        Guid customerId = await _service.CreateCustomerAndPlaceOrderAsync(customerName, emptyOrderItems);

        // Assert
        Assert.NotEqual(Guid.Empty, customerId);
        CustomerAggregateRoot savedCustomer = _mockRepository.SavedCustomers.First();
        Assert.Single(savedCustomer.Orders);
        Assert.Empty(savedCustomer.Orders[0].Items);
    }

    [Fact]
    public async Task CreateCustomerAndPlaceOrderAsync_Exception_LogsErrorAndRethrows()
    {
        // Arrange
        const string customerName = "Error Customer";
        List<(string product, int quantity, decimal price)> orderItems = TestDataBuilder.CreateOrderItems();
        _mockRepository.ThrowOnSave = true;

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.CreateCustomerAndPlaceOrderAsync(customerName, orderItems));

        Assert.True(_mockServiceLogger.ContainsMessage($"Failed to create customer and place order for {customerName}", LogLevel.Error));
    }

    [Fact]
    public async Task AddOrderItemsToExistingOrderAsync_Success_AddsItems()
    {
        // Arrange
        CustomerAggregateRoot customer = TestDataBuilder.CreateCustomer(businessRules: _businessRules, logger: _mockCustomerLogger);
        MyClassLibrary.Domain.Entities.Order order = customer.PlaceNewOrder();
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
        MyClassLibrary.Domain.Entities.Order order = customer.PlaceNewOrder();
        _mockRepository.AddCustomer(customer);

        Guid nonExistentOrderId = Guid.NewGuid();
        List<(string product, int quantity, decimal price)> items = TestDataBuilder.CreateOrderItems();

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.AddOrderItemsToExistingOrderAsync(customer.Id, nonExistentOrderId, items));

        Assert.True(_mockServiceLogger.ContainsMessage($"Failed to add items to order {nonExistentOrderId}", LogLevel.Error));
    }

    [Fact]
    public async Task CreateCustomerAndPlaceOrderAsync_DomainEventsAreDispatched()
    {
        // Arrange
        const string customerName = "Event Test Customer";
        List<(string product, int quantity, decimal price)> orderItems = TestDataBuilder.CreateOrderItems(1);

        // Act
        await _service.CreateCustomerAndPlaceOrderAsync(customerName, orderItems);

        // Assert
        Assert.NotEmpty(_mockEventDispatcher.DispatchedEvents);
        List<Type> eventTypes = [.. _mockEventDispatcher.DispatchedEvents.Select(e => e.GetType())];
        Assert.Contains(typeof(CustomerCreatedEvent), eventTypes);
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
            _customers.TryGetValue(id, out CustomerAggregateRoot? customer);
            return Task.FromResult(customer);
        }

        public Task SaveAsync(CustomerAggregateRoot customer, CancellationToken cancellationToken = default)
        {
            if (ThrowOnSave)
                throw new InvalidOperationException("Save operation failed");

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
}


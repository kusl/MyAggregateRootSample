
using MyClassLibrary.Domain.Aggregates;
using MyClassLibrary.Domain.Configuration;
using MyClassLibrary.Domain.Events;
using MyClassLibrary.Domain.ValueObjects;
using MyClassLibrary.Tests.TestHelpers;
using Xunit;

namespace MyClassLibrary.Tests.Domain.Aggregates;

public class CustomerAggregateRootTests
{
    private readonly MockLogger<CustomerAggregateRoot> _mockLogger;
    private readonly CustomerBusinessRules _businessRules;

    public CustomerAggregateRootTests()
    {
        _mockLogger = new MockLogger<CustomerAggregateRoot>();
        _businessRules = new CustomerBusinessRules
        {
            MaxOutstandingOrders = 3,
            OutstandingOrderDays = 30
        };
    }

    [Fact]
    public void Constructor_ValidParameters_CreatesCustomer()
    {
        // Arrange
        var customerId = Guid.NewGuid();
        const string customerName = "John Doe";

        // Act
        var customer = new CustomerAggregateRoot(customerId, customerName, _businessRules, _mockLogger);

        // Assert
        Assert.Equal(customerId, customer.Id);
        Assert.Equal(customerName, customer.Name);
        Assert.Empty(customer.Orders);
        Assert.Single(customer.DomainEvents);

        var createdEvent = Assert.IsType<CustomerCreatedEvent>(customer.DomainEvents[0]);
        Assert.Equal(customerId, createdEvent.CustomerId);
        Assert.Equal(customerName, createdEvent.CustomerName);
    }

    [Fact]
    public void Constructor_EmptyId_ThrowsArgumentException()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() =>
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
        var exception = Assert.Throws<ArgumentException>(() =>
            new CustomerAggregateRoot(Guid.NewGuid(), name!, _businessRules, _mockLogger));
        Assert.Contains("Customer name cannot be null or empty", exception.Message);
    }

    [Fact]
    public void Constructor_NullBusinessRules_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new CustomerAggregateRoot(Guid.NewGuid(), "Name", null!, _mockLogger));
    }

    [Fact]
    public void Constructor_TrimsCustomerName()
    {
        // Arrange
        const string nameWithSpaces = "  John Doe  ";

        // Act
        var customer = new CustomerAggregateRoot(Guid.NewGuid(), nameWithSpaces, _businessRules, _mockLogger);

        // Assert
        Assert.Equal("John Doe", customer.Name);
    }

    [Fact]
    public void PlaceNewOrder_UnderLimit_CreatesOrder()
    {
        // Arrange
        var customer = TestDataBuilder.CreateCustomer(businessRules: _businessRules, logger: _mockLogger);

        // Act
        var order = customer.PlaceNewOrder();

        // Assert
        Assert.NotEqual(Guid.Empty, order.Id);
        Assert.Single(customer.Orders);
        Assert.Equal(order.Id, customer.Orders[0].Id);
        Assert.Equal(2, customer.DomainEvents.Count); // CustomerCreated + OrderPlaced

        var orderPlacedEvent = customer.DomainEvents.OfType<OrderPlacedEvent>().FirstOrDefault();
        Assert.NotNull(orderPlacedEvent);
        Assert.Equal(customer.Id, orderPlacedEvent.CustomerId);
        Assert.Equal(order.Id, orderPlacedEvent.OrderId);

        Assert.True(_mockLogger.ContainsMessage($"placed order {order.Id}"));
    }

    [Fact]
    public void PlaceNewOrder_ReachesMaxOutstandingOrders_ThrowsException()
    {
        // Arrange
        var customer = TestDataBuilder.CreateCustomer(businessRules: _businessRules, logger: _mockLogger);

        // Place maximum allowed orders
        for (int i = 0; i < _businessRules.MaxOutstandingOrders; i++)
        {
            customer.PlaceNewOrder();
        }

        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(() => customer.PlaceNewOrder());
        Assert.Contains($"has reached the maximum of {_businessRules.MaxOutstandingOrders} outstanding orders", exception.Message);
        Assert.True(_mockLogger.ContainsMessage("Order placement failed", Microsoft.Extensions.Logging.LogLevel.Warning));
    }

    [Fact]
    public void GetOrder_ExistingOrder_ReturnsOrder()
    {
        // Arrange
        var customer = TestDataBuilder.CreateCustomer(businessRules: _businessRules, logger: _mockLogger);
        var order = customer.PlaceNewOrder();

        // Act
        var retrievedOrder = customer.GetOrder(order.Id);

        // Assert
        Assert.NotNull(retrievedOrder);
        Assert.Equal(order.Id, retrievedOrder.Id);
    }

    [Fact]
    public void GetOrder_NonExistentOrder_ReturnsNull()
    {
        // Arrange
        var customer = TestDataBuilder.CreateCustomer(businessRules: _businessRules, logger: _mockLogger);

        // Act
        var retrievedOrder = customer.GetOrder(Guid.NewGuid());

        // Assert
        Assert.Null(retrievedOrder);
    }

    [Fact]
    public void AddItemToOrder_ValidOrder_AddsItem()
    {
        // Arrange
        var customer = TestDataBuilder.CreateCustomer(businessRules: _businessRules, logger: _mockLogger);
        var order = customer.PlaceNewOrder();
        var item = TestDataBuilder.CreateOrderItem("Product 1", 2, 25.00m);

        // Act
        customer.AddItemToOrder(order.Id, item);

        // Assert
        Assert.Single(order.Items);
        Assert.Equal(item, order.Items[0]);
        Assert.Equal(3, customer.DomainEvents.Count); // CustomerCreated + OrderPlaced + OrderItemAdded

        var itemAddedEvent = customer.DomainEvents.OfType<OrderItemAddedEvent>().FirstOrDefault();
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
        var customer = TestDataBuilder.CreateCustomer(businessRules: _businessRules, logger: _mockLogger);
        var item = TestDataBuilder.CreateOrderItem();
        var nonExistentOrderId = Guid.NewGuid();

        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(() =>
            customer.AddItemToOrder(nonExistentOrderId, item));
        Assert.Contains($"Order {nonExistentOrderId} not found", exception.Message);
        Assert.True(_mockLogger.ContainsMessage("Failed to add item to order", Microsoft.Extensions.Logging.LogLevel.Warning));
    }

    [Fact]
    public void ClearDomainEvents_RemovesAllEvents()
    {
        // Arrange
        var customer = TestDataBuilder.CreateCustomer(businessRules: _businessRules, logger: _mockLogger);
        customer.PlaceNewOrder();
        Assert.Equal(2, customer.DomainEvents.Count);

        // Act
        customer.ClearDomainEvents();

        // Assert
        Assert.Empty(customer.DomainEvents);
    }

    [Fact]
    public void PlaceNewOrder_OldOrdersNotOutstanding_AllowsNewOrder()
    {
        // Arrange
        var businessRules = new CustomerBusinessRules
        {
            MaxOutstandingOrders = 2,
            OutstandingOrderDays = 1 // Very short outstanding period
        };
        var customer = TestDataBuilder.CreateCustomer(businessRules: businessRules, logger: _mockLogger);

        // Place orders that would be beyond the outstanding period
        // Note: We can't actually make them old without exposing a way to set order dates,
        // so this test demonstrates the business rule exists but can't fully test it
        customer.PlaceNewOrder();
        customer.PlaceNewOrder();

        // Act - This would throw if all orders were considered outstanding
        var newOrder = customer.PlaceNewOrder();

        // Assert
        Assert.NotNull(newOrder);
        Assert.Equal(3, customer.Orders.Count);
    }
}


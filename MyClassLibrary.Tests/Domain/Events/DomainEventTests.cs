using MyClassLibrary.Domain.Events;
using MyClassLibrary.Domain.ValueObjects;
using Xunit;

namespace MyClassLibrary.Tests.Domain.Events;

public class DomainEventTests
{
    [Fact]
    public void CustomerCreatedEvent_PropertiesSetCorrectly()
    {
        // Arrange
        var eventId = Guid.NewGuid();
        var occurredOn = DateTime.UtcNow;
        var customerId = Guid.NewGuid();
        const string customerName = "Test Customer";

        // Act
        var @event = new CustomerCreatedEvent(eventId, occurredOn, customerId, customerName);

        // Assert
        Assert.Equal(eventId, @event.Id);
        Assert.Equal(occurredOn, @event.OccurredOn);
        Assert.Equal(customerId, @event.CustomerId);
        Assert.Equal(customerName, @event.CustomerName);
    }

    [Fact]
    public void OrderPlacedEvent_PropertiesSetCorrectly()
    {
        // Arrange
        var eventId = Guid.NewGuid();
        var occurredOn = DateTime.UtcNow;
        var customerId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var orderDate = DateTime.UtcNow.AddHours(-1);

        // Act
        var @event = new OrderPlacedEvent(eventId, occurredOn, customerId, orderId, orderDate);

        // Assert
        Assert.Equal(eventId, @event.Id);
        Assert.Equal(occurredOn, @event.OccurredOn);
        Assert.Equal(customerId, @event.CustomerId);
        Assert.Equal(orderId, @event.OrderId);
        Assert.Equal(orderDate, @event.OrderDate);
    }

    [Fact]
    public void OrderItemAddedEvent_PropertiesSetCorrectly()
    {
        // Arrange
        var eventId = Guid.NewGuid();
        var occurredOn = DateTime.UtcNow;
        var customerId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var item = new OrderItem("Product", 5, 10.00m);

        // Act
        var @event = new OrderItemAddedEvent(eventId, occurredOn, customerId, orderId, item);

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
        var eventId = Guid.NewGuid();
        var occurredOn = DateTime.UtcNow;
        var customerId = Guid.NewGuid();
        const string customerName = "Test";

        var event1 = new CustomerCreatedEvent(eventId, occurredOn, customerId, customerName);
        var event2 = new CustomerCreatedEvent(eventId, occurredOn, customerId, customerName);
        var event3 = new CustomerCreatedEvent(Guid.NewGuid(), occurredOn, customerId, customerName);

        // Act & Assert
        Assert.Equal(event1, event2);
        Assert.NotEqual(event1, event3);
    }
}

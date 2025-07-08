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
    public void OrderPlacedEvent_PropertiesSetCorrectly()
    {
        // Arrange
        Guid eventId = Guid.NewGuid();
        DateTime occurredOn = DateTime.UtcNow;
        Guid customerId = Guid.NewGuid();
        Guid orderId = Guid.NewGuid();
        DateTime orderDate = DateTime.UtcNow.AddHours(-1);

        // Act
        OrderPlacedEvent @event = new(eventId, occurredOn, customerId, orderId, orderDate);

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

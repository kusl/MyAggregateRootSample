using MyClassLibrary.Infrastructure.Events;
using MyClassLibrary.Domain.Events;
using MyClassLibrary.Tests.TestHelpers;
using Microsoft.Extensions.Logging;
using Xunit;

namespace MyClassLibrary.Tests.Infrastructure.Events;

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
        Assert.Throws<ArgumentNullException>(() =>
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
        Assert.Single(_mockLogger.LogEntries);
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
            new OrderPlacedEvent(Guid.NewGuid(), DateTime.UtcNow, customerId, orderId, DateTime.UtcNow),
            new OrderItemAddedEvent(Guid.NewGuid(), DateTime.UtcNow, customerId, orderId,
                new MyClassLibrary.Domain.ValueObjects.OrderItem("Product", 1, 10m))
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
        Assert.Single(_mockLogger.LogEntries);
    }

    [Fact]
    public async Task DispatchAsync_LogsEventTypeNameCorrectly()
    {
        // Arrange
        DomainEvent[] differentEvents =
        [
            new CustomerCreatedEvent(Guid.NewGuid(), DateTime.UtcNow, Guid.NewGuid(), "Customer"),
            new OrderPlacedEvent(Guid.NewGuid(), DateTime.UtcNow, Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow)
        ];

        // Act
        await _dispatcher.DispatchAsync(differentEvents);

        // Assert
        List<string> logMessages = [.. _mockLogger.LogEntries.Select(e => e.Message)];
        Assert.Contains(logMessages, m => m.Contains("CustomerCreatedEvent"));
        Assert.Contains(logMessages, m => m.Contains("OrderPlacedEvent"));
    }
}

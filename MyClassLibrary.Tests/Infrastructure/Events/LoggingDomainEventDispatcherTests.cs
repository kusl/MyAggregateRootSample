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
        var customerId = Guid.NewGuid();
        var @event = new CustomerCreatedEvent(
            Guid.NewGuid(),
            DateTime.UtcNow,
            customerId,
            "Test Customer");
        var events = new[] { @event };

        // Act
        await _dispatcher.DispatchAsync(events);

        // Assert
        Assert.Single(_mockLogger.LogEntries);
        var logEntry = _mockLogger.LogEntries.First();
        Assert.Equal(LogLevel.Information, logEntry.LogLevel);
        Assert.Contains("CustomerCreatedEvent", logEntry.Message);
        Assert.Contains(@event.Id.ToString(), logEntry.Message);
        Assert.Contains(@event.OccurredOn.ToString(), logEntry.Message);
    }

    [Fact]
    public async Task DispatchAsync_MultipleEvents_LogsAllEvents()
    {
        // Arrange
        var customerId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var events = new DomainEvent[]
        {
            new CustomerCreatedEvent(Guid.NewGuid(), DateTime.UtcNow, customerId, "Customer"),
            new OrderPlacedEvent(Guid.NewGuid(), DateTime.UtcNow, customerId, orderId, DateTime.UtcNow),
            new OrderItemAddedEvent(Guid.NewGuid(), DateTime.UtcNow, customerId, orderId,
                new MyClassLibrary.Domain.ValueObjects.OrderItem("Product", 1, 10m))
        };

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
        var events = new List<DomainEvent>();

        // Act
        await _dispatcher.DispatchAsync(events);

        // Assert
        Assert.Empty(_mockLogger.LogEntries);
    }

    [Fact]
    public async Task DispatchAsync_WithCancellationToken_CompletesSuccessfully()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        var @event = new CustomerCreatedEvent(
            Guid.NewGuid(),
            DateTime.UtcNow,
            Guid.NewGuid(),
            "Test Customer");

        // Act
        await _dispatcher.DispatchAsync(new[] { @event }, cts.Token);

        // Assert
        Assert.Single(_mockLogger.LogEntries);
    }

    [Fact]
    public async Task DispatchAsync_LogsEventTypeNameCorrectly()
    {
        // Arrange
        var differentEvents = new DomainEvent[]
        {
            new CustomerCreatedEvent(Guid.NewGuid(), DateTime.UtcNow, Guid.NewGuid(), "Customer"),
            new OrderPlacedEvent(Guid.NewGuid(), DateTime.UtcNow, Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow)
        };

        // Act
        await _dispatcher.DispatchAsync(differentEvents);

        // Assert
        var logMessages = _mockLogger.LogEntries.Select(e => e.Message).ToList();
        Assert.Contains(logMessages, m => m.Contains("CustomerCreatedEvent"));
        Assert.Contains(logMessages, m => m.Contains("OrderPlacedEvent"));
    }
}


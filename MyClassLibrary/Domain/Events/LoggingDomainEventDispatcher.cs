using Microsoft.Extensions.Logging;
using MyClassLibrary.Application.Interfaces;
using MyClassLibrary.Domain.Events;

namespace MyClassLibrary.Infrastructure.Events;

public class LoggingDomainEventDispatcher : IDomainEventDispatcher
{
    private readonly ILogger<LoggingDomainEventDispatcher> _logger;

    public LoggingDomainEventDispatcher(ILogger<LoggingDomainEventDispatcher> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task DispatchAsync(IEnumerable<DomainEvent> events, CancellationToken cancellationToken = default)
    {
        foreach (var domainEvent in events)
        {
            _logger.LogInformation("Domain event dispatched: {EventType} - {EventId} at {OccurredOn}",
                domainEvent.GetType().Name, domainEvent.Id, domainEvent.OccurredOn);
        }

        return Task.CompletedTask;
    }
}
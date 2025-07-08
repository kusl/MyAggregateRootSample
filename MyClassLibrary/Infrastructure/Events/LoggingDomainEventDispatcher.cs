using Microsoft.Extensions.Logging;
using MyClassLibrary.Application.Interfaces;
using MyClassLibrary.Domain.Events;

namespace MyClassLibrary.Infrastructure.Events;

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
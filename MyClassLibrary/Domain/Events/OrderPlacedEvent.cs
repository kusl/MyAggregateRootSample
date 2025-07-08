namespace MyClassLibrary.Domain.Events;

public record OrderPlacedEvent(Guid Id, DateTime OccurredOn, Guid CustomerId, Guid OrderId, DateTime OrderDate)
    : DomainEvent(Id, OccurredOn);

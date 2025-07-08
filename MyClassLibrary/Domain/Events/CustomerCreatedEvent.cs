namespace MyClassLibrary.Domain.Events;

public record CustomerCreatedEvent(Guid Id, DateTime OccurredOn, Guid CustomerId, string CustomerName)
    : DomainEvent(Id, OccurredOn);

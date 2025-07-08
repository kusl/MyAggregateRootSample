using MyClassLibrary.Domain.ValueObjects;

namespace MyClassLibrary.Domain.Events;

public record OrderItemAddedEvent(Guid Id, DateTime OccurredOn, Guid CustomerId, Guid OrderId, OrderItem Item)
    : DomainEvent(Id, OccurredOn);
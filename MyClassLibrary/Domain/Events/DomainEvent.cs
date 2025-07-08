namespace MyClassLibrary.Domain.Events;

public abstract record DomainEvent(Guid Id, DateTime OccurredOn);

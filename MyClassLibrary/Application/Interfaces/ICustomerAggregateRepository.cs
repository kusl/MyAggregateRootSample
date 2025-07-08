using MyClassLibrary.Domain.Aggregates;

namespace MyClassLibrary.Application.Interfaces;

public interface ICustomerAggregateRepository
{
    Task<CustomerAggregateRoot?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task SaveAsync(CustomerAggregateRoot customer, CancellationToken cancellationToken = default);
}

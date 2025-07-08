using Microsoft.Extensions.Logging;
using MyClassLibrary.Application.Interfaces;
using MyClassLibrary.Domain.Aggregates;

namespace MyClassLibrary.Infrastructure.Repositories;

public class InMemoryCustomerAggregateRepository : ICustomerAggregateRepository
{
    private static readonly Dictionary<Guid, CustomerAggregateRoot> _customers = [];
    private readonly ILogger<InMemoryCustomerAggregateRepository> _logger;

    public InMemoryCustomerAggregateRepository(ILogger<InMemoryCustomerAggregateRepository> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task<CustomerAggregateRoot?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Retrieving customer aggregate with ID {CustomerId}", id);

        var found = _customers.TryGetValue(id, out var customer);

        if (!found)
        {
            _logger.LogWarning("Customer aggregate with ID {CustomerId} not found", id);
        }
        else
        {
            _logger.LogDebug("Found customer aggregate {CustomerName} with ID {CustomerId}", customer!.Name, id);
        }

        return Task.FromResult(customer);
    }

    public Task SaveAsync(CustomerAggregateRoot customer, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(customer);

        var isUpdate = _customers.ContainsKey(customer.Id);
        var action = isUpdate ? "Updated" : "Created";

        _customers[customer.Id] = customer;

        _logger.LogInformation("{Action} customer aggregate {CustomerName} with ID {CustomerId}",
            action, customer.Name, customer.Id);

        return Task.CompletedTask;
    }
}
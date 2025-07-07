using Microsoft.Extensions.Logging;

namespace MyClassLibrary;

public class CustomerAggregateRepository(ILogger<CustomerAggregateRepository> logger) : ICustomerAggregateRepository
{
    // In a real application, this would interact with a DbContext.
    // We'll simulate in-memory storage for simplicity.
    private static readonly Dictionary<Guid, CustomerAggregateRoot> _customers = [];

    public CustomerAggregateRoot? GetById(Guid id)
    {
        logger.LogDebug("Getting CustomerAggregateRoot with Id {CustomerId}", id);
        _customers.TryGetValue(id, out CustomerAggregateRoot? customer);

        if (customer == null)
        {
            logger.LogWarning("CustomerAggregateRoot with Id {CustomerId} not found", id);
        }
        else
        {
            logger.LogDebug("Found CustomerAggregateRoot {CustomerName} with Id {CustomerId}", customer.Name, id);
        }

        return customer;
    }

    public void Save(CustomerAggregateRoot customer)
    {
        ArgumentNullException.ThrowIfNull(customer);

        bool isUpdate = _customers.ContainsKey(customer.Id);
        logger.LogInformation("{Action} CustomerAggregateRoot {CustomerName} with Id {CustomerId}",
            isUpdate ? "Updating" : "Creating", customer.Name, customer.Id);

        _customers[customer.Id] = customer; // Adds or updates
    }
}
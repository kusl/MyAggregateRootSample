namespace MyClassLibrary;


// Repository only for the Aggregate Root
public interface ICustomerAggregateRepository
{
    CustomerAggregateRoot? GetById(Guid id);
    void Save(CustomerAggregateRoot customer); // Save implies add or update
}

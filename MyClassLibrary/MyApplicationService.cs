using Microsoft.Extensions.Logging;
namespace MyClassLibrary;

// Application Service interacts only with the Aggregate Root Repository
public class MyApplicationService(
    ICustomerAggregateRepository customerRepository,
    CustomerBusinessRules rules,
    ILogger<MyApplicationService> logger,
    ILogger<CustomerAggregateRoot> customerLogger)
{
    public void CreateCustomerAndPlaceOrder(string customerName, List<(string product, int quantity, decimal price)> orderItemsData)
    {
        // Pass the specific CustomerAggregateRoot logger
        CustomerAggregateRoot customer = new(Guid.NewGuid(), customerName, customerLogger, rules);
        Order order = customer.PlaceNewOrder(); // Order creation goes through the aggregate

        foreach ((string product, int quantity, decimal price) in orderItemsData)
        {
            order.AddItem(new MyOrderItem(product, quantity, price));
        }

        customerRepository.Save(customer); // Only the aggregate root is saved
        logger.LogInformation("Customer '{CustomerName}' and Order '{OrderId}' created and saved through Customer Aggregate.", customer.Name, order.Id);
    }

    public void AddOrderItemsToExistingOrder(Guid customerId, Guid orderId, List<(string product, int quantity, decimal price)> newItemsData)
    {
        CustomerAggregateRoot? customer = customerRepository.GetById(customerId);
        if (customer == null)
        {
            logger.LogWarning("Customer {CustomerId} not found.", customerId);
            return;
        }

        Order? order = customer.GetOrder(orderId);
        if (order == null)
        {
            logger.LogWarning("Order {OrderId} not found for Customer {CustomerId}.", orderId, customerId);
            return;
        }

        foreach ((string product, int quantity, decimal price) in newItemsData)
        {
            order.AddItem(new MyOrderItem(product, quantity, price));
        }

        customerRepository.Save(customer); // Save the whole aggregate
        logger.LogInformation("Items added to order {OrderId} for Customer {CustomerId} and saved.", orderId, customerId);
    }
}
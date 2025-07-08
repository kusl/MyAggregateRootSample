using Microsoft.Extensions.Logging;
using MyClassLibrary.Application.Interfaces;
using MyClassLibrary.Domain.Aggregates;
using MyClassLibrary.Domain.ValueObjects;
using MyClassLibrary.Domain.Configuration;

namespace MyClassLibrary.Application.Services;

public class CustomerApplicationService(
    ICustomerAggregateRepository customerRepository,
    IDomainEventDispatcher eventDispatcher,
    CustomerBusinessRules businessRules,
    ILogger<CustomerApplicationService> logger,
    ILogger<CustomerAggregateRoot> customerLogger)
{
    private readonly ICustomerAggregateRepository _customerRepository = customerRepository ?? throw new ArgumentNullException(nameof(customerRepository));
    private readonly IDomainEventDispatcher _eventDispatcher = eventDispatcher ?? throw new ArgumentNullException(nameof(eventDispatcher));
    private readonly CustomerBusinessRules _businessRules = businessRules ?? throw new ArgumentNullException(nameof(businessRules));
    private readonly ILogger<CustomerApplicationService> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly ILogger<CustomerAggregateRoot> _customerLogger = customerLogger ?? throw new ArgumentNullException(nameof(customerLogger));

    public async Task<Guid> CreateCustomerAndPlaceOrderAsync(
        string customerName,
        List<(string product, int quantity, decimal price)> orderItemsData,
        CancellationToken cancellationToken = default)
    {
        try
        {
            Guid customerId = Guid.NewGuid();
            CustomerAggregateRoot customer = new(customerId, customerName, _businessRules, _customerLogger);
            Domain.Entities.Order order = customer.PlaceNewOrder();

            foreach ((string product, int quantity, decimal price) in orderItemsData)
            {
                OrderItem item = new(product, quantity, price);
                customer.AddItemToOrder(order.Id, item);
            }

            await _customerRepository.SaveAsync(customer, cancellationToken);
            await _eventDispatcher.DispatchAsync(customer.DomainEvents, cancellationToken);
            customer.ClearDomainEvents();

            _logger.LogInformation("Successfully created customer {CustomerName} with order {OrderId}",
                customerName, order.Id);

            return customerId;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create customer and place order for {CustomerName}", customerName);
            throw;
        }
    }

    public async Task AddOrderItemsToExistingOrderAsync(
        Guid customerId,
        Guid orderId,
        List<(string product, int quantity, decimal price)> newItemsData,
        CancellationToken cancellationToken = default)
    {
        try
        {
            CustomerAggregateRoot? customer = await _customerRepository.GetByIdAsync(customerId, cancellationToken);
            if (customer == null)
            {
                InvalidOperationException exception = new($"Customer {customerId} not found");
                _logger.LogWarning(exception, "Customer {CustomerId} not found", customerId);
                throw exception;
            }

            foreach ((string product, int quantity, decimal price) in newItemsData)
            {
                OrderItem item = new(product, quantity, price);
                customer.AddItemToOrder(orderId, item);
            }

            await _customerRepository.SaveAsync(customer, cancellationToken);
            await _eventDispatcher.DispatchAsync(customer.DomainEvents, cancellationToken);
            customer.ClearDomainEvents();

            _logger.LogInformation("Successfully added items to order {OrderId} for customer {CustomerId}",
                orderId, customerId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to add items to order {OrderId} for customer {CustomerId}",
                orderId, customerId);
            throw;
        }
    }
}
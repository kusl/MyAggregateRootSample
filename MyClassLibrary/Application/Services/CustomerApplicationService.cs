using Microsoft.Extensions.Logging;
using MyClassLibrary.Application.Interfaces;
using MyClassLibrary.Domain.Aggregates;
using MyClassLibrary.Domain.ValueObjects;
using MyClassLibrary.Domain.Configuration;

namespace MyClassLibrary.Application.Services;

public class CustomerApplicationService
{
    private readonly ICustomerAggregateRepository _customerRepository;
    private readonly IDomainEventDispatcher _eventDispatcher;
    private readonly CustomerBusinessRules _businessRules;
    private readonly ILogger<CustomerApplicationService> _logger;
    private readonly ILogger<CustomerAggregateRoot> _customerLogger;

    public CustomerApplicationService(
        ICustomerAggregateRepository customerRepository,
        IDomainEventDispatcher eventDispatcher,
        CustomerBusinessRules businessRules,
        ILogger<CustomerApplicationService> logger,
        ILogger<CustomerAggregateRoot> customerLogger)
    {
        _customerRepository = customerRepository ?? throw new ArgumentNullException(nameof(customerRepository));
        _eventDispatcher = eventDispatcher ?? throw new ArgumentNullException(nameof(eventDispatcher));
        _businessRules = businessRules ?? throw new ArgumentNullException(nameof(businessRules));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _customerLogger = customerLogger ?? throw new ArgumentNullException(nameof(customerLogger));
    }

    public async Task<Guid> CreateCustomerAndPlaceOrderAsync(
        string customerName,
        List<(string product, int quantity, decimal price)> orderItemsData,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var customerId = Guid.NewGuid();
            var customer = new CustomerAggregateRoot(customerId, customerName, _businessRules, _customerLogger);
            var order = customer.PlaceNewOrder();

            foreach (var (product, quantity, price) in orderItemsData)
            {
                var item = new OrderItem(product, quantity, price);
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
            var customer = await _customerRepository.GetByIdAsync(customerId, cancellationToken);
            if (customer == null)
            {
                var exception = new InvalidOperationException($"Customer {customerId} not found");
                _logger.LogWarning(exception, "Customer {CustomerId} not found", customerId);
                throw exception;
            }

            foreach (var (product, quantity, price) in newItemsData)
            {
                var item = new OrderItem(product, quantity, price);
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
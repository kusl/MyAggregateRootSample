using MyClassLibrary.Domain.Aggregates;
using MyClassLibrary.Domain.Configuration;
using MyClassLibrary.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace MyClassLibrary.Tests.TestHelpers;

public static class TestDataBuilder
{
    public static CustomerAggregateRoot CreateCustomer(
        string name = "Test Customer",
        CustomerBusinessRules? businessRules = null,
        ILogger<CustomerAggregateRoot>? logger = null)
    {
        return new CustomerAggregateRoot(
            Guid.NewGuid(),
            name,
            businessRules ?? new CustomerBusinessRules(),
            logger);
    }

    public static List<(string product, int quantity, decimal price)> CreateOrderItems(int count = 3)
    {
        var items = new List<(string product, int quantity, decimal price)>();
        for (int i = 1; i <= count; i++)
        {
            items.Add(($"Product {i}", i, i * 10.50m));
        }
        return items;
    }

    public static OrderItem CreateOrderItem(
        string product = "Test Product",
        int quantity = 1,
        decimal price = 10.00m)
    {
        return new OrderItem(product, quantity, price);
    }
}

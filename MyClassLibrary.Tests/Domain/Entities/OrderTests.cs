﻿using MyClassLibrary.Domain.Entities;
using MyClassLibrary.Domain.ValueObjects;
using MyClassLibrary.Tests.TestHelpers;
using Xunit;

namespace MyClassLibrary.Tests.Domain.Entities;

public class OrderTests
{
    [Fact]
    public void Constructor_ValidParameters_CreatesOrder()
    {
        // Arrange
        Guid orderId = Guid.NewGuid();
        DateTime orderDate = DateTime.UtcNow.AddDays(-1);

        // Act
        Order order = new(orderId, orderDate);

        // Assert
        Assert.Equal(orderId, order.Id);
        Assert.Equal(orderDate, order.OrderDate);
        Assert.Empty(order.Items);
        Assert.Equal(0m, order.TotalAmount);
    }

    [Fact]
    public void Constructor_EmptyId_ThrowsArgumentException()
    {
        // Act & Assert
        ArgumentException exception = Assert.Throws<ArgumentException>(() =>
            new Order(Guid.Empty, DateTime.UtcNow));
        Assert.Contains("Order ID cannot be empty", exception.Message);
    }

    [Fact]
    public void Constructor_FutureDate_ThrowsArgumentException()
    {
        // Act & Assert
        ArgumentException exception = Assert.Throws<ArgumentException>(() =>
            new Order(Guid.NewGuid(), DateTime.UtcNow.AddDays(1)));
        Assert.Contains("Order date cannot be in the future", exception.Message);
    }

    [Fact]
    public void AddItem_ValidItem_AddsToOrder()
    {
        // Arrange
        Order order = new(Guid.NewGuid(), DateTime.UtcNow.AddHours(-1));
        OrderItem item = TestDataBuilder.CreateOrderItem("Product 1", 2, 15.00m);

        // Act
        order.AddItem(item);

        // Assert
        Assert.Single(order.Items);
        Assert.Equal(item, order.Items[0]);
        Assert.Equal(30.00m, order.TotalAmount);
    }

    [Fact]
    public void AddItem_NullItem_ThrowsArgumentNullException()
    {
        // Arrange
        Order order = new(Guid.NewGuid(), DateTime.UtcNow.AddHours(-1));

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => order.AddItem(null!));
    }

    [Fact]
    public void AddItem_DuplicateItem_CombinesQuantities()
    {
        // Arrange
        Order order = new(Guid.NewGuid(), DateTime.UtcNow.AddHours(-1));
        OrderItem item1 = new("Product A", 2, 10.00m);
        OrderItem item2 = new("Product A", 3, 10.00m);

        // Act
        order.AddItem(item1);
        order.AddItem(item2);

        // Assert
        Assert.Single(order.Items);
        Assert.Equal("Product A", order.Items[0].Product);
        Assert.Equal(5, order.Items[0].Quantity);
        Assert.Equal(10.00m, order.Items[0].Price);
        Assert.Equal(50.00m, order.TotalAmount);
    }

    [Fact]
    public void AddItem_SameProductDifferentPrice_DoesNotCombine()
    {
        // Arrange
        Order order = new(Guid.NewGuid(), DateTime.UtcNow.AddHours(-1));
        OrderItem item1 = new("Product A", 2, 10.00m);
        OrderItem item2 = new("Product A", 3, 15.00m);

        // Act
        order.AddItem(item1);
        order.AddItem(item2);

        // Assert
        Assert.Equal(2, order.Items.Count);
        Assert.Equal(65.00m, order.TotalAmount); // (2 * 10) + (3 * 15)
    }

    [Fact]
    public void TotalAmount_MultipleItems_CalculatesCorrectly()
    {
        // Arrange
        Order order = new(Guid.NewGuid(), DateTime.UtcNow.AddHours(-1));
        OrderItem[] items =
        [
            new OrderItem("Product 1", 2, 10.00m),
            new OrderItem("Product 2", 1, 15.50m),
            new OrderItem("Product 3", 3, 7.25m)
        ];

        // Act
        foreach (OrderItem? item in items)
        {
            order.AddItem(item);
        }

        // Assert
        Assert.Equal(57.25m, order.TotalAmount); // 20 + 15.50 + 21.75
    }

    [Theory]
    [InlineData(0, true)]     // Order from today is outstanding
    [InlineData(10, true)]    // 10 days ago - still within 30 days
    [InlineData(29, true)]    // 29 days ago - still within 30 days
    [InlineData(30, false)]   // 30 days ago - no longer outstanding
    [InlineData(31, false)]   // 31 days ago - no longer outstanding
    [InlineData(100, false)]  // 100 days ago - no longer outstanding
    public void IsOutstanding_VariousDays_ReturnsCorrectResult(int daysAgo, bool expectedOutstanding)
    {
        // Arrange
        DateTime orderDate = DateTime.UtcNow.AddDays(-daysAgo);
        Order order = new(Guid.NewGuid(), orderDate);
        const int outstandingDays = 30;

        // Act
        bool isOutstanding = order.IsOutstanding(outstandingDays);

        // Assert
        Assert.Equal(expectedOutstanding, isOutstanding);
    }
}
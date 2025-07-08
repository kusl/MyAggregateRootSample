using MyClassLibrary.Domain.Entities;
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
        var orderId = Guid.NewGuid();
        var orderDate = DateTime.UtcNow.AddDays(-1);

        // Act
        var order = new Order(orderId, orderDate);

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
        var exception = Assert.Throws<ArgumentException>(() =>
            new Order(Guid.Empty, DateTime.UtcNow));
        Assert.Contains("Order ID cannot be empty", exception.Message);
    }

    [Fact]
    public void Constructor_FutureDate_ThrowsArgumentException()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() =>
            new Order(Guid.NewGuid(), DateTime.UtcNow.AddDays(1)));
        Assert.Contains("Order date cannot be in the future", exception.Message);
    }

    [Fact]
    public void AddItem_ValidItem_AddsToOrder()
    {
        // Arrange
        var order = new Order(Guid.NewGuid(), DateTime.UtcNow.AddHours(-1));
        var item = TestDataBuilder.CreateOrderItem("Product 1", 2, 15.00m);

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
        var order = new Order(Guid.NewGuid(), DateTime.UtcNow.AddHours(-1));

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => order.AddItem(null!));
    }

    [Fact]
    public void AddItem_DuplicateItem_CombinesQuantities()
    {
        // Arrange
        var order = new Order(Guid.NewGuid(), DateTime.UtcNow.AddHours(-1));
        var item1 = new OrderItem("Product A", 2, 10.00m);
        var item2 = new OrderItem("Product A", 3, 10.00m);

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
        var order = new Order(Guid.NewGuid(), DateTime.UtcNow.AddHours(-1));
        var item1 = new OrderItem("Product A", 2, 10.00m);
        var item2 = new OrderItem("Product A", 3, 15.00m);

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
        var order = new Order(Guid.NewGuid(), DateTime.UtcNow.AddHours(-1));
        var items = new[]
        {
            new OrderItem("Product 1", 2, 10.00m),
            new OrderItem("Product 2", 1, 15.50m),
            new OrderItem("Product 3", 3, 7.25m)
        };

        // Act
        foreach (var item in items)
        {
            order.AddItem(item);
        }

        // Assert
        Assert.Equal(57.25m, order.TotalAmount); // 20 + 15.50 + 21.75
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(10, false)]
    [InlineData(29, false)]
    [InlineData(30, true)]
    [InlineData(31, true)]
    [InlineData(100, true)]
    public void IsOutstanding_VariousDays_ReturnsCorrectResult(int daysAgo, bool expectedOutstanding)
    {
        // Arrange
        var orderDate = DateTime.UtcNow.AddDays(-daysAgo);
        var order = new Order(Guid.NewGuid(), orderDate);
        const int outstandingDays = 30;

        // Act
        var isOutstanding = order.IsOutstanding(outstandingDays);

        // Assert
        Assert.Equal(expectedOutstanding, isOutstanding);
    }
}


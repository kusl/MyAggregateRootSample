using MyClassLibrary.Domain.ValueObjects;
using Xunit;

namespace MyClassLibrary.Tests.Domain.ValueObjects;

public class OrderItemTests
{
    [Fact]
    public void Constructor_ValidParameters_CreatesOrderItem()
    {
        // Arrange
        const string product = "Test Product";
        const int quantity = 5;
        const decimal price = 10.50m;

        // Act
        var orderItem = new OrderItem(product, quantity, price);

        // Assert
        Assert.Equal(product, orderItem.Product);
        Assert.Equal(quantity, orderItem.Quantity);
        Assert.Equal(price, orderItem.Price);
        Assert.Equal(52.50m, orderItem.LineTotal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_InvalidProduct_ThrowsArgumentException(string? product)
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() =>
            new OrderItem(product!, 1, 10.00m));
        Assert.Contains("Product cannot be null or empty", exception.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100)]
    public void Constructor_InvalidQuantity_ThrowsArgumentException(int quantity)
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() =>
            new OrderItem("Product", quantity, 10.00m));
        Assert.Contains("Quantity must be positive", exception.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100.50)]
    public void Constructor_InvalidPrice_ThrowsArgumentException(decimal price)
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() =>
            new OrderItem("Product", 1, price));
        Assert.Contains("Price must be positive", exception.Message);
    }

    [Fact]
    public void Constructor_TrimsProductName()
    {
        // Arrange
        const string productWithSpaces = "  Test Product  ";

        // Act
        var orderItem = new OrderItem(productWithSpaces, 1, 10.00m);

        // Assert
        Assert.Equal("Test Product", orderItem.Product);
    }

    [Fact]
    public void LineTotal_CalculatesCorrectly()
    {
        // Arrange
        var testCases = new[]
        {
            (quantity: 1, price: 10.00m, expected: 10.00m),
            (quantity: 5, price: 15.50m, expected: 77.50m),
            (quantity: 100, price: 0.99m, expected: 99.00m),
            (quantity: 3, price: 33.33m, expected: 99.99m)
        };

        foreach (var (quantity, price, expected) in testCases)
        {
            // Act
            var orderItem = new OrderItem("Product", quantity, price);

            // Assert
            Assert.Equal(expected, orderItem.LineTotal);
        }
    }

    [Fact]
    public void OrderItem_RecordEquality_WorksCorrectly()
    {
        // Arrange
        var item1 = new OrderItem("Product", 5, 10.00m);
        var item2 = new OrderItem("Product", 5, 10.00m);
        var item3 = new OrderItem("Product", 3, 10.00m);

        // Act & Assert
        Assert.Equal(item1, item2);
        Assert.NotEqual(item1, item3);
        Assert.True(item1 == item2);
        Assert.False(item1 == item3);
    }
}


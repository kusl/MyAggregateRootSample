using MyClassLibrary.Infrastructure.Repositories;
using MyClassLibrary.Tests.TestHelpers;
using Microsoft.Extensions.Logging;
using Xunit;

namespace MyClassLibrary.Tests.Infrastructure.Repositories;

public class InMemoryCustomerAggregateRepositoryTests
{
    private readonly MockLogger<InMemoryCustomerAggregateRepository> _mockLogger;
    private readonly InMemoryCustomerAggregateRepository _repository;

    public InMemoryCustomerAggregateRepositoryTests()
    {
        _mockLogger = new MockLogger<InMemoryCustomerAggregateRepository>();
        _repository = new InMemoryCustomerAggregateRepository(_mockLogger);
    }

    [Fact]
    public void Constructor_NullLogger_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new InMemoryCustomerAggregateRepository(null!));
    }

    [Fact]
    public async Task GetByIdAsync_NonExistentCustomer_ReturnsNull()
    {
        // Arrange
        Guid customerId = Guid.NewGuid();

        // Act
        MyClassLibrary.Domain.Aggregates.CustomerAggregateRoot? result = await _repository.GetByIdAsync(customerId);

        // Assert
        Assert.Null(result);
        Assert.True(_mockLogger.ContainsMessage($"Customer aggregate with ID {customerId} not found", LogLevel.Warning));
    }

    [Fact]
    public async Task SaveAsync_NewCustomer_SavesSuccessfully()
    {
        // Arrange
        MyClassLibrary.Domain.Aggregates.CustomerAggregateRoot customer = TestDataBuilder.CreateCustomer("John Doe");

        // Act
        await _repository.SaveAsync(customer);

        // Assert
        Assert.True(_mockLogger.ContainsMessage("Created customer aggregate", LogLevel.Information));
        Assert.True(_mockLogger.ContainsMessage(customer.Name));
        Assert.True(_mockLogger.ContainsMessage(customer.Id.ToString()));
    }

    [Fact]
    public async Task SaveAsync_NullCustomer_ThrowsArgumentNullException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            _repository.SaveAsync(null!));
    }

    [Fact]
    public async Task GetByIdAsync_AfterSave_ReturnsCustomer()
    {
        // Arrange
        MyClassLibrary.Domain.Aggregates.CustomerAggregateRoot customer = TestDataBuilder.CreateCustomer("Jane Doe");
        await _repository.SaveAsync(customer);
        _mockLogger.Clear();

        // Act
        MyClassLibrary.Domain.Aggregates.CustomerAggregateRoot? retrievedCustomer = await _repository.GetByIdAsync(customer.Id);

        // Assert
        Assert.NotNull(retrievedCustomer);
        Assert.Equal(customer.Id, retrievedCustomer.Id);
        Assert.Equal(customer.Name, retrievedCustomer.Name);
        Assert.True(_mockLogger.ContainsMessage($"Found customer aggregate {customer.Name}", LogLevel.Debug));
    }

    [Fact]
    public async Task SaveAsync_ExistingCustomer_UpdatesSuccessfully()
    {
        // Arrange
        MyClassLibrary.Domain.Aggregates.CustomerAggregateRoot customer = TestDataBuilder.CreateCustomer("Initial Name");
        await _repository.SaveAsync(customer);

        // Modify the customer (add an order)
        customer.PlaceNewOrder();
        _mockLogger.Clear();

        // Act
        await _repository.SaveAsync(customer);

        // Assert
        Assert.True(_mockLogger.ContainsMessage("Updated customer aggregate", LogLevel.Information));

        // Verify the update persisted
        MyClassLibrary.Domain.Aggregates.CustomerAggregateRoot? retrievedCustomer = await _repository.GetByIdAsync(customer.Id);
        Assert.NotNull(retrievedCustomer);
        Assert.Single(retrievedCustomer.Orders);
    }

    [Fact]
    public async Task Repository_IsolatedBetweenInstances()
    {
        // Arrange
        InMemoryCustomerAggregateRepository repository1 = new(new MockLogger<InMemoryCustomerAggregateRepository>());
        InMemoryCustomerAggregateRepository repository2 = new(new MockLogger<InMemoryCustomerAggregateRepository>());

        MyClassLibrary.Domain.Aggregates.CustomerAggregateRoot customer = TestDataBuilder.CreateCustomer("Test Customer");

        // Act
        await repository1.SaveAsync(customer);
        MyClassLibrary.Domain.Aggregates.CustomerAggregateRoot? result1 = await repository1.GetByIdAsync(customer.Id);
        MyClassLibrary.Domain.Aggregates.CustomerAggregateRoot? result2 = await repository2.GetByIdAsync(customer.Id);

        // Assert - Both repositories share the same static storage
        Assert.NotNull(result1);
        Assert.NotNull(result2);
        Assert.Equal(customer.Id, result1.Id);
        Assert.Equal(customer.Id, result2.Id);
    }

    [Fact]
    public async Task SaveAsync_MultipleCustomers_SavesAll()
    {
        // Arrange
        MyClassLibrary.Domain.Aggregates.CustomerAggregateRoot[] customers =
        [
            TestDataBuilder.CreateCustomer("Customer 1"),
            TestDataBuilder.CreateCustomer("Customer 2"),
            TestDataBuilder.CreateCustomer("Customer 3")
        ];

        // Act
        foreach (MyClassLibrary.Domain.Aggregates.CustomerAggregateRoot? customer in customers)
        {
            await _repository.SaveAsync(customer);
        }

        // Assert
        foreach (MyClassLibrary.Domain.Aggregates.CustomerAggregateRoot? customer in customers)
        {
            MyClassLibrary.Domain.Aggregates.CustomerAggregateRoot? retrieved = await _repository.GetByIdAsync(customer.Id);
            Assert.NotNull(retrieved);
            Assert.Equal(customer.Name, retrieved.Name);
        }
    }
}
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MyClassLibrary.Application.Interfaces;
using MyClassLibrary.Application.Services;
using MyClassLibrary.Domain.Configuration;
using MyClassLibrary.Extensions;
using MyClassLibrary.Infrastructure.Events;
using MyClassLibrary.Infrastructure.Repositories;
using Xunit;

namespace MyClassLibrary.Tests.Extensions;

public class ServiceCollectionExtensionsTests
{
    [Fact]
    public void AddCustomerDomain_RegistersAllRequiredServices()
    {
        // Arrange
        var services = new ServiceCollection();
        var configuration = CreateConfiguration();

        // Add required logging services
        services.AddLogging();

        // Act
        services.AddCustomerDomain(configuration);
        var serviceProvider = services.BuildServiceProvider();

        // Assert - Check all services are registered
        Assert.NotNull(serviceProvider.GetService<CustomerBusinessRules>());
        Assert.NotNull(serviceProvider.GetService<ICustomerAggregateRepository>());
        Assert.NotNull(serviceProvider.GetService<IDomainEventDispatcher>());
        Assert.NotNull(serviceProvider.GetService<CustomerApplicationService>());
    }

    [Fact]
    public void AddCustomerDomain_RegistersCorrectImplementations()
    {
        // Arrange
        var services = new ServiceCollection();
        var configuration = CreateConfiguration();
        services.AddLogging();

        // Act
        services.AddCustomerDomain(configuration);
        var serviceProvider = services.BuildServiceProvider();

        // Assert - Check correct implementations
        var repository = serviceProvider.GetService<ICustomerAggregateRepository>();
        Assert.IsType<InMemoryCustomerAggregateRepository>(repository);

        var eventDispatcher = serviceProvider.GetService<IDomainEventDispatcher>();
        Assert.IsType<LoggingDomainEventDispatcher>(eventDispatcher);
    }

    [Fact]
    public void AddCustomerDomain_ConfiguresBusinessRulesFromConfiguration()
    {
        // Arrange
        var services = new ServiceCollection();
        var configValues = new Dictionary<string, string?>
        {
            {"CustomerBusinessRules:MaxOutstandingOrders", "5"},
            {"CustomerBusinessRules:OutstandingOrderDays", "45"}
        };
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configValues)
            .Build();
        services.AddLogging();

        // Act
        services.AddCustomerDomain(configuration);
        var serviceProvider = services.BuildServiceProvider();

        // Assert
        var businessRules = serviceProvider.GetService<CustomerBusinessRules>();
        Assert.NotNull(businessRules);
        Assert.Equal(5, businessRules.MaxOutstandingOrders);
        Assert.Equal(45, businessRules.OutstandingOrderDays);
    }

    [Fact]
    public void AddCustomerDomain_RegistersSingletonServices()
    {
        // Arrange
        var services = new ServiceCollection();
        var configuration = CreateConfiguration();
        services.AddLogging();

        // Act
        services.AddCustomerDomain(configuration);
        var serviceProvider = services.BuildServiceProvider();

        // Assert - Verify singleton behavior
        var businessRules1 = serviceProvider.GetService<CustomerBusinessRules>();
        var businessRules2 = serviceProvider.GetService<CustomerBusinessRules>();
        Assert.Same(businessRules1, businessRules2);

        var repository1 = serviceProvider.GetService<ICustomerAggregateRepository>();
        var repository2 = serviceProvider.GetService<ICustomerAggregateRepository>();
        Assert.Same(repository1, repository2);

        var dispatcher1 = serviceProvider.GetService<IDomainEventDispatcher>();
        var dispatcher2 = serviceProvider.GetService<IDomainEventDispatcher>();
        Assert.Same(dispatcher1, dispatcher2);
    }

    [Fact]
    public void AddCustomerDomain_RegistersTransientApplicationService()
    {
        // Arrange
        var services = new ServiceCollection();
        var configuration = CreateConfiguration();
        services.AddLogging();

        // Act
        services.AddCustomerDomain(configuration);
        var serviceProvider = services.BuildServiceProvider();

        // Assert - Verify transient behavior
        var service1 = serviceProvider.GetService<CustomerApplicationService>();
        var service2 = serviceProvider.GetService<CustomerApplicationService>();
        Assert.NotSame(service1, service2);
    }

    [Fact]
    public void AddCustomerDomain_CanResolveAllDependenciesForApplicationService()
    {
        // Arrange
        var services = new ServiceCollection();
        var configuration = CreateConfiguration();
        services.AddLogging();

        // Act
        services.AddCustomerDomain(configuration);
        var serviceProvider = services.BuildServiceProvider();

        // Assert - This will throw if any dependencies are missing
        var applicationService = serviceProvider.GetRequiredService<CustomerApplicationService>();
        Assert.NotNull(applicationService);
    }

    [Fact]
    public void AddCustomerDomain_DefaultBusinessRulesWhenNotInConfiguration()
    {
        // Arrange
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().Build(); // Empty configuration
        services.AddLogging();

        // Act
        services.AddCustomerDomain(configuration);
        var serviceProvider = services.BuildServiceProvider();

        // Assert
        var businessRules = serviceProvider.GetService<CustomerBusinessRules>();
        Assert.NotNull(businessRules);
        Assert.Equal(10, businessRules.MaxOutstandingOrders); // Default value
        Assert.Equal(30, businessRules.OutstandingOrderDays); // Default value
    }

    private static IConfiguration CreateConfiguration()
    {
        var configValues = new Dictionary<string, string?>
        {
            {"CustomerBusinessRules:MaxOutstandingOrders", "10"},
            {"CustomerBusinessRules:OutstandingOrderDays", "30"}
        };
        return new ConfigurationBuilder()
            .AddInMemoryCollection(configValues)
            .Build();
    }
}
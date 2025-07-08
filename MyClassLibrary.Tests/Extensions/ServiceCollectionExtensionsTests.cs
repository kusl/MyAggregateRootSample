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
        ServiceCollection services = new();
        IConfiguration configuration = CreateConfiguration();

        // Add required logging services
        services.AddLogging();

        // Act
        services.AddCustomerDomain(configuration);
        ServiceProvider serviceProvider = services.BuildServiceProvider();

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
        ServiceCollection services = new();
        IConfiguration configuration = CreateConfiguration();
        services.AddLogging();

        // Act
        services.AddCustomerDomain(configuration);
        ServiceProvider serviceProvider = services.BuildServiceProvider();

        // Assert - Check correct implementations
        ICustomerAggregateRepository? repository = serviceProvider.GetService<ICustomerAggregateRepository>();
        Assert.IsType<InMemoryCustomerAggregateRepository>(repository);

        IDomainEventDispatcher? eventDispatcher = serviceProvider.GetService<IDomainEventDispatcher>();
        Assert.IsType<LoggingDomainEventDispatcher>(eventDispatcher);
    }

    [Fact]
    public void AddCustomerDomain_ConfiguresBusinessRulesFromConfiguration()
    {
        // Arrange
        ServiceCollection services = new();
        Dictionary<string, string?> configValues = new()
        {
            {"CustomerBusinessRules:MaxOutstandingOrders", "5"},
            {"CustomerBusinessRules:OutstandingOrderDays", "45"}
        };
        IConfigurationRoot configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configValues)
            .Build();
        services.AddLogging();

        // Act
        services.AddCustomerDomain(configuration);
        ServiceProvider serviceProvider = services.BuildServiceProvider();

        // Assert
        CustomerBusinessRules? businessRules = serviceProvider.GetService<CustomerBusinessRules>();
        Assert.NotNull(businessRules);
        Assert.Equal(5, businessRules.MaxOutstandingOrders);
        Assert.Equal(45, businessRules.OutstandingOrderDays);
    }

    [Fact]
    public void AddCustomerDomain_RegistersSingletonServices()
    {
        // Arrange
        ServiceCollection services = new();
        IConfiguration configuration = CreateConfiguration();
        services.AddLogging();

        // Act
        services.AddCustomerDomain(configuration);
        ServiceProvider serviceProvider = services.BuildServiceProvider();

        // Assert - Verify singleton behavior
        CustomerBusinessRules? businessRules1 = serviceProvider.GetService<CustomerBusinessRules>();
        CustomerBusinessRules? businessRules2 = serviceProvider.GetService<CustomerBusinessRules>();
        Assert.Same(businessRules1, businessRules2);

        ICustomerAggregateRepository? repository1 = serviceProvider.GetService<ICustomerAggregateRepository>();
        ICustomerAggregateRepository? repository2 = serviceProvider.GetService<ICustomerAggregateRepository>();
        Assert.Same(repository1, repository2);

        IDomainEventDispatcher? dispatcher1 = serviceProvider.GetService<IDomainEventDispatcher>();
        IDomainEventDispatcher? dispatcher2 = serviceProvider.GetService<IDomainEventDispatcher>();
        Assert.Same(dispatcher1, dispatcher2);
    }

    [Fact]
    public void AddCustomerDomain_RegistersTransientApplicationService()
    {
        // Arrange
        ServiceCollection services = new();
        IConfiguration configuration = CreateConfiguration();
        services.AddLogging();

        // Act
        services.AddCustomerDomain(configuration);
        ServiceProvider serviceProvider = services.BuildServiceProvider();

        // Assert - Verify transient behavior
        CustomerApplicationService? service1 = serviceProvider.GetService<CustomerApplicationService>();
        CustomerApplicationService? service2 = serviceProvider.GetService<CustomerApplicationService>();
        Assert.NotSame(service1, service2);
    }

    [Fact]
    public void AddCustomerDomain_CanResolveAllDependenciesForApplicationService()
    {
        // Arrange
        ServiceCollection services = new();
        IConfiguration configuration = CreateConfiguration();
        services.AddLogging();

        // Act
        services.AddCustomerDomain(configuration);
        ServiceProvider serviceProvider = services.BuildServiceProvider();

        // Assert - This will throw if any dependencies are missing
        CustomerApplicationService applicationService = serviceProvider.GetRequiredService<CustomerApplicationService>();
        Assert.NotNull(applicationService);
    }

    [Fact]
    public void AddCustomerDomain_DefaultBusinessRulesWhenNotInConfiguration()
    {
        // Arrange
        ServiceCollection services = new();
        IConfigurationRoot configuration = new ConfigurationBuilder().Build(); // Empty configuration
        services.AddLogging();

        // Act
        services.AddCustomerDomain(configuration);
        ServiceProvider serviceProvider = services.BuildServiceProvider();

        // Assert
        CustomerBusinessRules? businessRules = serviceProvider.GetService<CustomerBusinessRules>();
        Assert.NotNull(businessRules);
        Assert.Equal(10, businessRules.MaxOutstandingOrders); // Default value
        Assert.Equal(30, businessRules.OutstandingOrderDays); // Default value
    }

    private static IConfiguration CreateConfiguration()
    {
        Dictionary<string, string?> configValues = new()
        {
            {"CustomerBusinessRules:MaxOutstandingOrders", "10"},
            {"CustomerBusinessRules:OutstandingOrderDays", "30"}
        };
        return new ConfigurationBuilder()
            .AddInMemoryCollection(configValues)
            .Build();
    }
}
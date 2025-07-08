using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MyClassLibrary.Application.Interfaces;
using MyClassLibrary.Application.Services;
using MyClassLibrary.Domain.Configuration;
using MyClassLibrary.Infrastructure.Events;
using MyClassLibrary.Infrastructure.Repositories;
namespace MyClassLibrary.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddCustomerDomain(this IServiceCollection services, IConfiguration configuration)
    {
        // Configure business rules
        services.Configure<CustomerBusinessRules>(configuration.GetSection("CustomerBusinessRules"));
        services.AddSingleton(provider =>
        {
            Microsoft.Extensions.Options.IOptions<CustomerBusinessRules> options = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<CustomerBusinessRules>>();
            return options.Value;
        });

        // Register repositories
        services.AddSingleton<ICustomerAggregateRepository, InMemoryCustomerAggregateRepository>();

        // Register domain event dispatcher
        services.AddSingleton<IDomainEventDispatcher, LoggingDomainEventDispatcher>();

        // Register application services
        services.AddTransient<CustomerApplicationService>();

        return services;
    }
}
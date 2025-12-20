using Microsoft.Extensions.DependencyInjection;

namespace Application;

/// <summary>
/// Provides extension methods for registering application layer services into the dependency injection container.
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// Registers the application layer services, including validators, mappers, and handlers.
    /// </summary>
    /// <param name="services">The service collection to which the services will be added.</param>
    /// <returns>The updated service collection to allow for chaining.</returns>
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        return services;
    }
}
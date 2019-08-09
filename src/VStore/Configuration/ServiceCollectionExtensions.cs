using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace NuClear.VStore.Configuration
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddPgContext<TContext>(this IServiceCollection services, IConfiguration configuration, string name)
            where TContext : DbContext
        {
            var connectionString = configuration.GetConnectionString(name);
            return services.AddDbContext<TContext>(
                builder => builder.UseNpgsql(connectionString)
                                  .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking),
                ServiceLifetime.Transient);
        }
    }
}
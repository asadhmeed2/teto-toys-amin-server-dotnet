using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using StackExchange.Redis;
using AdmineTetoToys.Domain.Interfaces;
using AdmineTetoToys.Infrastructure.Caching;
using AdmineTetoToys.Infrastructure.Data;
using AdmineTetoToys.Infrastructure.Email;
using AdmineTetoToys.Infrastructure.Security;

namespace AdmineTetoToys.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        // Redis
        var redisHost = configuration["Redis:Host"] ?? "127.0.0.1";
        var redisPort = configuration["Redis:Port"] ?? "6379";
        var redisPassword = configuration["Redis:Password"];

        var redisConfig = new ConfigurationOptions
        {
            EndPoints = { $"{redisHost}:{redisPort}" },
            Password = string.IsNullOrEmpty(redisPassword) ? null : redisPassword,
            ConnectTimeout = 5000,
            SyncTimeout = 3000,
            AbortOnConnectFail = false,
        };

        var multiplexer = ConnectionMultiplexer.Connect(redisConfig);
        services.AddSingleton<IConnectionMultiplexer>(multiplexer);
        services.AddScoped<IRedisCacheService, RedisCacheService>();

        // MySQL
        var rawConnectionString = configuration["MySQL:ConnectionString"]
            ?? configuration.GetConnectionString("DefaultConnection");

        var connBuilder = new MySql.Data.MySqlClient.MySqlConnectionStringBuilder(rawConnectionString);

        if (!string.IsNullOrEmpty(configuration["MySQL:Server"]))
            connBuilder.Server = configuration["MySQL:Server"];
        if (!string.IsNullOrEmpty(configuration["MySQL:Port"]) && uint.TryParse(configuration["MySQL:Port"], out var port))
            connBuilder.Port = port;
        if (!string.IsNullOrEmpty(configuration["MySQL:Database"]))
            connBuilder.Database = configuration["MySQL:Database"];
        if (!string.IsNullOrEmpty(configuration["MySQL:User"]))
            connBuilder.UserID = configuration["MySQL:User"];
        if (!string.IsNullOrEmpty(configuration["MySQL:Password"]))
            connBuilder.Password = configuration["MySQL:Password"];

        var mysqlConnectionString = connBuilder.ConnectionString;
        services.AddScoped<IUserRepository>(_ => new UserRepository(mysqlConnectionString));
        services.AddScoped<IAdminUserRepository>(_ => new AdminUserRepository(mysqlConnectionString));
        services.AddScoped<IProductRepository>(_ => new ProductRepository(mysqlConnectionString));

        // Security
        services.AddSingleton<IPasswordHasher, PasswordHasher>();
        services.AddSingleton<ITokenService, JwtTokenService>();

        // Email (Resend)
        var resendApiKey = configuration["Resend:ApiKey"] ?? string.Empty;
        var resendFromEmail = configuration["Resend:FromEmail"] ?? "noreply@tatotoys.com";
        services.AddHttpClient();
        services.AddScoped<IEmailService>(sp =>
            new ResendEmailService(
                sp.GetRequiredService<IHttpClientFactory>().CreateClient(),
                resendApiKey,
                resendFromEmail));

        return services;
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using EFCore.QueryAnalyzer.Core;
using EFCore.QueryAnalyzer.Services;
using EFCore.QueryAnalyzer.Core.Models;

namespace EFCore.QueryAnalyzer.Extensions
{
    /// <summary>
    /// Extension methods for configuring EF Core Query Analyzer
    /// </summary>
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// Adds EF Core Query Analyzer services using configuration
        /// </summary>
        /// <param name="services">The service collection</param>
        /// <param name="configuration">The configuration instance</param>
        /// <param name="sectionName">The configuration section name (default: "QueryAnalyzer")</param>
        /// <returns>The service collection for chaining</returns>
        public static IServiceCollection AddEFCoreQueryAnalyzer(
            this IServiceCollection services,
            IConfiguration configuration,
            string sectionName = "QueryAnalyzer")
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(configuration);

            // Configure options from configuration
            services.Configure<QueryAnalyzerOptions>(configuration.GetSection(sectionName));

            return AddEFCoreQueryAnalyzerCore(services);
        }

        /// <summary>
        /// Adds EF Core Query Analyzer services using an options delegate
        /// </summary>
        /// <param name="services">The service collection</param>
        /// <param name="configureOptions">The options configuration delegate</param>
        /// <returns>The service collection for chaining</returns>
        public static IServiceCollection AddEFCoreQueryAnalyzer(
            this IServiceCollection services,
            Action<QueryAnalyzerOptions> configureOptions)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(configureOptions);

            services.Configure(configureOptions);
            return AddEFCoreQueryAnalyzerCore(services);
        }

        /// <summary>
        /// Adds EF Core Query Analyzer services with default options
        /// </summary>
        /// <param name="services">The service collection</param>
        /// <returns>The service collection for chaining</returns>
        public static IServiceCollection AddEFCoreQueryAnalyzer(this IServiceCollection services)
        {
            ArgumentNullException.ThrowIfNull(services);

            services.Configure<QueryAnalyzerOptions>(_ => { });
            return AddEFCoreQueryAnalyzerCore(services);
        }

        /// <summary>
        /// Adds EF Core Query Analyzer with HTTP reporting service
        /// </summary>
        /// <param name="services">The service collection</param>
        /// <param name="configureOptions">The options configuration delegate</param>
        /// <param name="configureHttpClient">Optional HTTP client configuration</param>
        /// <returns>The service collection for chaining</returns>
        public static IServiceCollection AddEFCoreQueryAnalyzerWithHttp(
            this IServiceCollection services,
            Action<QueryAnalyzerOptions> configureOptions,
            Action<HttpClient>? configureHttpClient = null)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(configureOptions);

            services.Configure(configureOptions);

            // Add HTTP client for reporting service
            var httpClientBuilder = services.AddHttpClient<HttpQueryReportingService>();
            if (configureHttpClient != null)
            {
                httpClientBuilder.ConfigureHttpClient(configureHttpClient);
            }

            // Replace default reporting service with HTTP implementation
            services.Replace(ServiceDescriptor.Transient<IQueryReportingService, HttpQueryReportingService>());

            return AddEFCoreQueryAnalyzerCore(services, registerDefaultReportingService: false);
        }

        /// <summary>
        /// Adds EF Core Query Analyzer with in-memory reporting service (useful for testing)
        /// </summary>
        /// <param name="services">The service collection</param>
        /// <param name="configureOptions">The options configuration delegate</param>
        /// <returns>The service collection for chaining</returns>
        public static IServiceCollection AddEFCoreQueryAnalyzerWithInMemory(
            this IServiceCollection services,
            Action<QueryAnalyzerOptions>? configureOptions = null)
        {
            ArgumentNullException.ThrowIfNull(services);

            if (configureOptions != null)
            {
                services.Configure(configureOptions);
            }
            else
            {
                services.Configure<QueryAnalyzerOptions>(_ => { });
            }

            // Replace default reporting service with in-memory implementation
            services.Replace(ServiceDescriptor.Singleton<IQueryReportingService, InMemoryQueryReportingService>());

            return AddEFCoreQueryAnalyzerCore(services, registerDefaultReportingService: false);
        }

        /// <summary>
        /// Adds a custom reporting service implementation
        /// </summary>
        /// <typeparam name="TReportingService">The reporting service implementation type</typeparam>
        /// <param name="services">The service collection</param>
        /// <param name="configureOptions">The options configuration delegate</param>
        /// <param name="serviceLifetime">The service lifetime (default: Transient)</param>
        /// <returns>The service collection for chaining</returns>
        public static IServiceCollection AddEFCoreQueryAnalyzerWithCustomReporting<TReportingService>(
            this IServiceCollection services,
            Action<QueryAnalyzerOptions> configureOptions,
            ServiceLifetime serviceLifetime = ServiceLifetime.Transient)
            where TReportingService : class, IQueryReportingService
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(configureOptions);

            services.Configure(configureOptions);

            // Register custom reporting service
            services.Add(ServiceDescriptor.Describe(typeof(IQueryReportingService), typeof(TReportingService), serviceLifetime));

            return AddEFCoreQueryAnalyzerCore(services, registerDefaultReportingService: false);
        }

        private static IServiceCollection AddEFCoreQueryAnalyzerCore(IServiceCollection services, bool registerDefaultReportingService = true)
        {
            // Register options resolver
            services.TryAddTransient(provider =>
                provider.GetRequiredService<IOptions<QueryAnalyzerOptions>>().Value);

            // Register SQL Server statistics parser
            // services.TryAddTransient<SqlServerStatisticsParser>();

            // Register default HTTP reporting service if not already registered
            if (registerDefaultReportingService)
            {
                services.AddHttpClient<HttpQueryReportingService>();
                services.TryAddTransient<IQueryReportingService, HttpQueryReportingService>();
            }

            // Register the interceptor with service provider injection
            services.TryAddTransient(provider =>
                new QueryPerformanceInterceptor(
                    provider.GetRequiredService<ILogger<QueryPerformanceInterceptor>>(),
                    provider.GetRequiredService<IQueryReportingService>(),
                    provider.GetRequiredService<QueryAnalyzerOptions>()
                )
            );

            return services;
        }
    }

    /// <summary>
    /// Extension methods for DbContextOptionsBuilder
    /// </summary>
    public static class DbContextOptionsBuilderExtensions
    {
        /// <summary>
        /// Adds the query performance interceptor to the DbContext
        /// </summary>
        /// <param name="optionsBuilder">The DbContext options builder</param>
        /// <param name="serviceProvider">The service provider to resolve the interceptor</param>
        /// <returns>The options builder for chaining</returns>
        public static DbContextOptionsBuilder AddQueryAnalyzer(
            this DbContextOptionsBuilder optionsBuilder,
            IServiceProvider serviceProvider)
        {
            ArgumentNullException.ThrowIfNull(optionsBuilder);
            ArgumentNullException.ThrowIfNull(serviceProvider);

            var interceptor = serviceProvider.GetRequiredService<QueryPerformanceInterceptor>();
            return optionsBuilder.AddInterceptors(interceptor);
        }

        /// <summary>
        /// Adds the query performance interceptor to the DbContext
        /// </summary>
        /// <param name="optionsBuilder">The DbContext options builder</param>
        /// <param name="interceptor">The interceptor instance</param>
        /// <returns>The options builder for chaining</returns>
        public static DbContextOptionsBuilder AddQueryAnalyzer(
            this DbContextOptionsBuilder optionsBuilder,
            QueryPerformanceInterceptor interceptor)
        {
            ArgumentNullException.ThrowIfNull(optionsBuilder);
            ArgumentNullException.ThrowIfNull(interceptor);

            return optionsBuilder.AddInterceptors(interceptor);
        }

        /// <summary>
        /// Adds the query performance interceptor with inline configuration
        /// </summary>
        /// <param name="optionsBuilder">The DbContext options builder</param>
        /// <param name="configureOptions">Options configuration delegate</param>
        /// <param name="reportingService">Optional custom reporting service</param>
        /// <returns>The options builder for chaining</returns>
        public static DbContextOptionsBuilder AddQueryAnalyzer(
            this DbContextOptionsBuilder optionsBuilder,
            Action<QueryAnalyzerOptions> configureOptions,
            IQueryReportingService? reportingService = null)
        {
            ArgumentNullException.ThrowIfNull(optionsBuilder);
            ArgumentNullException.ThrowIfNull(configureOptions);

            var options = new QueryAnalyzerOptions();
            configureOptions(options);

            // Create a minimal service provider for the interceptor
            var services = new ServiceCollection();
            services.AddLogging();
            // services.AddTransient<SqlServerStatisticsParser>();

            if (reportingService != null)
            {
                services.AddSingleton(reportingService);
            }
            else
            {
                services.AddHttpClient<HttpQueryReportingService>();
                services.AddTransient<IQueryReportingService, HttpQueryReportingService>();
            }

            services.AddSingleton(options);
            services.AddTransient(provider =>
                new QueryPerformanceInterceptor(
                    provider.GetRequiredService<ILogger<QueryPerformanceInterceptor>>(),
                    provider.GetRequiredService<IQueryReportingService>(),
                    provider.GetRequiredService<QueryAnalyzerOptions>()
                )
            );

            var serviceProvider = services.BuildServiceProvider();
            var interceptor = serviceProvider.GetRequiredService<QueryPerformanceInterceptor>();

            return optionsBuilder.AddInterceptors(interceptor);
        }
    }

    /// <summary>
    /// Extension methods for typed DbContextOptionsBuilder
    /// </summary>
    public static class DbContextOptionsBuilderGenericExtensions
    {
        /// <summary>
        /// Adds the query performance interceptor to the typed DbContext
        /// </summary>
        /// <typeparam name="TContext">The DbContext type</typeparam>
        /// <param name="optionsBuilder">The DbContext options builder</param>
        /// <param name="serviceProvider">The service provider to resolve the interceptor</param>
        /// <returns>The options builder for chaining</returns>
        public static DbContextOptionsBuilder<TContext> AddQueryAnalyzer<TContext>(
            this DbContextOptionsBuilder<TContext> optionsBuilder,
            IServiceProvider serviceProvider)
            where TContext : DbContext
        {
            ArgumentNullException.ThrowIfNull(optionsBuilder);
            ArgumentNullException.ThrowIfNull(serviceProvider);

            var interceptor = serviceProvider.GetRequiredService<QueryPerformanceInterceptor>();
            return optionsBuilder.AddInterceptors(interceptor);
        }

        /// <summary>
        /// Adds the query performance interceptor to the typed DbContext
        /// </summary>
        /// <typeparam name="TContext">The DbContext type</typeparam>
        /// <param name="optionsBuilder">The DbContext options builder</param>
        /// <param name="interceptor">The interceptor instance</param>
        /// <returns>The options builder for chaining</returns>
        public static DbContextOptionsBuilder<TContext> AddQueryAnalyzer<TContext>(
            this DbContextOptionsBuilder<TContext> optionsBuilder,
            QueryPerformanceInterceptor interceptor)
            where TContext : DbContext
        {
            ArgumentNullException.ThrowIfNull(optionsBuilder);
            ArgumentNullException.ThrowIfNull(interceptor);

            return optionsBuilder.AddInterceptors(interceptor);
        }

        /// <summary>
        /// Adds the query performance interceptor with inline configuration to the typed DbContext
        /// </summary>
        /// <typeparam name="TContext">The DbContext type</typeparam>
        /// <param name="optionsBuilder">The DbContext options builder</param>
        /// <param name="configureOptions">Options configuration delegate</param>
        /// <param name="reportingService">Optional custom reporting service</param>
        /// <returns>The options builder for chaining</returns>
        public static DbContextOptionsBuilder<TContext> AddQueryAnalyzer<TContext>(
            this DbContextOptionsBuilder<TContext> optionsBuilder,
            Action<QueryAnalyzerOptions> configureOptions,
            IQueryReportingService? reportingService = null)
            where TContext : DbContext
        {
            ArgumentNullException.ThrowIfNull(optionsBuilder);
            ArgumentNullException.ThrowIfNull(configureOptions);

            var options = new QueryAnalyzerOptions();
            configureOptions(options);

            // Create a minimal service provider for the interceptor
            var services = new ServiceCollection();
            services.AddLogging();
            // services.AddTransient<SqlServerStatisticsParser>();

            if (reportingService != null)
            {
                services.AddSingleton(reportingService);
            }
            else
            {
                services.AddHttpClient<HttpQueryReportingService>();
                services.AddTransient<IQueryReportingService, HttpQueryReportingService>();
            }

            services.AddSingleton(options);
            services.AddTransient(provider =>
                new QueryPerformanceInterceptor(
                    provider.GetRequiredService<ILogger<QueryPerformanceInterceptor>>(),
                    provider.GetRequiredService<IQueryReportingService>(),
                    provider.GetRequiredService<QueryAnalyzerOptions>()
                )
            );

            var serviceProvider = services.BuildServiceProvider();
            var interceptor = serviceProvider.GetRequiredService<QueryPerformanceInterceptor>();

            return optionsBuilder.AddInterceptors(interceptor);
        }
    }
}
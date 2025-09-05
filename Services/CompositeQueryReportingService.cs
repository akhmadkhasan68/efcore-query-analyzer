using Microsoft.Extensions.Logging;
using EFCore.QueryAnalyzer.Core;
using EFCore.QueryAnalyzer.Core.Models;

namespace EFCore.QueryAnalyzer.Services
{
    /// <summary>
    /// Composite service that can use multiple reporting strategies
    /// </summary>
    public sealed class CompositeQueryReportingService(
        IEnumerable<IQueryReportingService> services,
        ILogger<CompositeQueryReportingService> logger) : IQueryReportingService
    {
        private readonly IEnumerable<IQueryReportingService> _services = services ?? throw new ArgumentNullException(nameof(services));
        private readonly ILogger<CompositeQueryReportingService> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        public async Task ReportSlowQueryAsync(QueryTrackingContext context, CancellationToken cancellationToken = default)
        {
            var tasks = _services.Select(async service =>
            {
                try
                {
                    await service.ReportSlowQueryAsync(context, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in reporting service {ServiceType} for query {QueryId}",
                        service.GetType().Name, context.QueryId);
                }
            });

            await Task.WhenAll(tasks);
        }
    }
}
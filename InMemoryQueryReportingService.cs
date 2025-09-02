using Microsoft.Extensions.Logging;

namespace EFCore.QueryAnalyzer
{
    /// <summary>
    /// In-memory implementation for testing or local development
    /// </summary>
    public sealed class InMemoryQueryReportingService(ILogger<InMemoryQueryReportingService> logger) : IQueryReportingService
    {
        private readonly ILogger<InMemoryQueryReportingService> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        private readonly List<SlowQueryReport> _reports = [];
        private readonly object _lock = new();

        public Task ReportSlowQueryAsync(QueryTrackingContext context, CancellationToken cancellationToken = default)
        {
            var report = new SlowQueryReport
            {
                QueryId = context.QueryId,
                RawQuery = context.CommandText,
                Parameters = context.Parameters,
                ExecutionTimeMs = context.ExecutionTime.TotalMilliseconds,
                StackTrace = context.StackTrace,
                Timestamp = context.StartTime,
                ContextType = context.ContextType,
                Environment = "InMemory"
            };

            lock (_lock)
            {
                _reports.Add(report);
            }

            _logger.LogInformation("Slow query stored in memory: {QueryId} ({Duration}ms)",
                context.QueryId, context.ExecutionTime.TotalMilliseconds);

            return Task.CompletedTask;
        }

        /// <summary>
        /// Get all stored reports for testing/debugging
        /// </summary>
        public IReadOnlyList<SlowQueryReport> GetReports()
        {
            lock (_lock)
            {
                return [.. _reports];
            }
        }

        /// <summary>
        /// Clear all stored reports
        /// </summary>
        public void ClearReports()
        {
            lock (_lock)
            {
                _reports.Clear();
            }
        }
    }
}
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Data.Common;
using System.Diagnostics;

namespace EFCore.QueryAnalyzer
{
    /// <summary>
    /// EF Core interceptor that monitors query performance and reports slow queries
    /// </summary>
    public sealed class QueryPerformanceInterceptor(
        ILogger<QueryPerformanceInterceptor> logger,
        IQueryReportingService reportingService,
        QueryAnalyzerOptions options) : DbCommandInterceptor
    {
        private readonly ILogger<QueryPerformanceInterceptor> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        private readonly IQueryReportingService _reportingService = reportingService ?? throw new ArgumentNullException(nameof(reportingService));
        private readonly QueryAnalyzerOptions _options = options ?? throw new ArgumentNullException(nameof(options));
        private readonly ConcurrentDictionary<Guid, QueryTrackingContext> _activeQueries = new();

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (_options.IsEnabled)
            {
                StartQueryTracking(command, eventData);
            }
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }

        public override async ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command,
            CommandExecutedEventData eventData,
            DbDataReader result,
            CancellationToken cancellationToken = default)
        {
            if (_options.IsEnabled)
            {
                await CompleteQueryTrackingAsync(command, eventData, cancellationToken);
            }
            return await base.ReaderExecutedAsync(command, eventData, result, cancellationToken);
        }

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result)
        {
            if (_options.IsEnabled)
            {
                StartQueryTracking(command, eventData);
            }
            return base.ReaderExecuting(command, eventData, result);
        }

        public override DbDataReader ReaderExecuted(
            DbCommand command,
            CommandExecutedEventData eventData,
            DbDataReader result)
        {
            if (_options.IsEnabled)
            {
                _ = Task.Run(async () => await CompleteQueryTrackingAsync(command, eventData, CancellationToken.None));
            }
            return base.ReaderExecuted(command, eventData, result);
        }

        public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<object> result,
            CancellationToken cancellationToken = default)
        {
            if (_options.IsEnabled)
            {
                StartQueryTracking(command, eventData);
            }
            return base.ScalarExecutingAsync(command, eventData, result, cancellationToken);
        }

        public override async ValueTask<object> ScalarExecutedAsync(
            DbCommand command,
            CommandExecutedEventData eventData,
            object result,
            CancellationToken cancellationToken = default)
        {
            if (_options.IsEnabled)
            {
                await CompleteQueryTrackingAsync(command, eventData, cancellationToken);
            }
            return await base.ScalarExecutedAsync(command, eventData, result, cancellationToken);
        }

        private void StartQueryTracking(DbCommand command, CommandEventData eventData)
        {
            try
            {
                var context = new QueryTrackingContext
                {
                    QueryId = Guid.NewGuid(),
                    CommandText = command.CommandText,
                    Parameters = ExtractParameters(command),
                    StartTime = DateTime.UtcNow,
                    Stopwatch = Stopwatch.StartNew(),
                    StackTrace = _options.CaptureStackTrace ? CaptureFilteredStackTrace() : null,
                    ConnectionId = eventData.ConnectionId,
                    ContextType = eventData.Context?.GetType().FullName ?? "Unknown",
                    CommandId = eventData.CommandId
                };

                _activeQueries[context.QueryId] = context;

                _logger.LogTrace("Query tracking started: {QueryId} - {ConnectionId}", 
                    context.QueryId, eventData.ConnectionId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error starting query tracking for connection {ConnectionId}", eventData.ConnectionId);
            }
        }

        private async Task CompleteQueryTrackingAsync(DbCommand command, CommandExecutedEventData eventData, CancellationToken cancellationToken)
        {
            try
            {
                var matchingContext = _activeQueries.Values
                    .FirstOrDefault(ctx => ctx.ConnectionId == eventData.ConnectionId && 
                                          ctx.CommandId == eventData.CommandId);

                if (matchingContext == null)
                {
                    _logger.LogTrace("No matching tracking context found for connection {ConnectionId}", eventData.ConnectionId);
                    return;
                }

                _activeQueries.TryRemove(matchingContext.QueryId, out _);

                matchingContext.Stopwatch.Stop();
                matchingContext.EndTime = DateTime.UtcNow;
                matchingContext.ExecutionTime = matchingContext.Stopwatch.Elapsed;

                _logger.LogTrace("Query tracking completed: {QueryId}, Duration: {Duration}ms",
                    matchingContext.QueryId, matchingContext.ExecutionTime.TotalMilliseconds);

                // Check if execution time exceeds threshold
                if (matchingContext.ExecutionTime.TotalMilliseconds >= _options.ThresholdMilliseconds)
                {
                    _logger.LogWarning("Slow query detected: {Duration}ms (Threshold: {Threshold}ms) - Query: {Query}",
                        matchingContext.ExecutionTime.TotalMilliseconds,
                        _options.ThresholdMilliseconds,
                        TruncateForLog(matchingContext.CommandText, 200));

                    await _reportingService.ReportSlowQueryAsync(matchingContext, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error completing query tracking for connection {ConnectionId}", eventData.ConnectionId);
            }
        }

        private Dictionary<string, object?> ExtractParameters(DbCommand command)
        {
            var parameters = new Dictionary<string, object?>();
            try
            {
                foreach (DbParameter param in command.Parameters)
                {
                    parameters[param.ParameterName] = param.Value == DBNull.Value ? null : param.Value;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error extracting parameters from command");
            }
            return parameters;
        }

        private string? CaptureFilteredStackTrace()
        {
            try
            {
                var stackTrace = Environment.StackTrace;
                var lines = stackTrace.Split('\n')
                    .Where(line => !string.IsNullOrWhiteSpace(line) &&
                                  !line.Contains("Microsoft.EntityFrameworkCore") &&
                                  !line.Contains("System.") &&
                                  !line.Contains("Microsoft.Extensions.") &&
                                  !line.Contains("EFCore.QueryAnalyzer"))
                    .Take(_options.MaxStackTraceLines)
                    .Select(line => line.Trim())
                    .ToArray();

                return lines.Length > 0 ? string.Join("\n", lines) : null;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error capturing stack trace");
                return null;
            }
        }

        private static string TruncateForLog(string text, int maxLength)
        {
            return text.Length <= maxLength ? text : text[..maxLength] + "...";
        }
    }

    /// <summary>
    /// Context for tracking individual query execution
    /// </summary>
    public sealed class QueryTrackingContext
    {
        public Guid QueryId { get; set; }
        public string CommandText { get; set; } = string.Empty;
        public Dictionary<string, object?> Parameters { get; set; } = new();
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public TimeSpan ExecutionTime { get; set; }
        public Stopwatch Stopwatch { get; set; } = new();
        public string? StackTrace { get; set; }
        public Guid ConnectionId { get; set; }
        public string ContextType { get; set; } = string.Empty;
        public Guid CommandId { get; set; }
    }

    /// <summary>
    /// Configuration options for the query analyzer
    /// </summary>
    public sealed class QueryAnalyzerOptions
    {
        /// <summary>
        /// Threshold in milliseconds for considering a query as slow (default: 1000ms)
        /// </summary>
        public double ThresholdMilliseconds { get; set; } = 1000;

        /// <summary>
        /// Whether the analyzer is enabled (default: true)
        /// </summary>
        public bool IsEnabled { get; set; } = true;

        /// <summary>
        /// Whether to capture stack traces for slow queries (default: true)
        /// </summary>
        public bool CaptureStackTrace { get; set; } = true;

        /// <summary>
        /// Maximum number of lines to capture in stack trace (default: 10)
        /// </summary>
        public int MaxStackTraceLines { get; set; } = 10;

        /// <summary>
        /// Maximum length of query text to store (default: 10000)
        /// </summary>
        public int MaxQueryLength { get; set; } = 10000;

        /// <summary>
        /// API endpoint for reporting slow queries
        /// </summary>
        public string? ApiEndpoint { get; set; }

        /// <summary>
        /// API key for authentication
        /// </summary>
        public string? ApiKey { get; set; }

        /// <summary>
        /// Timeout for API calls in milliseconds (default: 5000ms)
        /// </summary>
        public int ApiTimeoutMs { get; set; } = 5000;

        /// <summary>
        /// Whether to enable reporting in development environment (default: true)
        /// </summary>
        public bool EnableInDevelopment { get; set; } = true;

        /// <summary>
        /// Whether to enable reporting in production environment (default: false)
        /// </summary>
        public bool EnableInProduction { get; set; } = false;
    }

    /// <summary>
    /// Report model for slow query data
    /// </summary>
    public sealed class SlowQueryReport
    {
        public Guid QueryId { get; set; }
        public string RawQuery { get; set; } = string.Empty;
        public Dictionary<string, object?> Parameters { get; set; } = new();
        public double ExecutionTimeMs { get; set; }
        public string? StackTrace { get; set; }
        public DateTime Timestamp { get; set; }
        public string ContextType { get; set; } = string.Empty;
        public string Environment { get; set; } = string.Empty;
        public string? ApplicationName { get; set; }
        public string? Version { get; set; }
    }
}
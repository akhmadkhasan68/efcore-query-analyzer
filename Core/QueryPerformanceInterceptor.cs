using EFCore.QueryAnalyzer.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Data.Common;
using System.Diagnostics;
using System.Xml;

namespace EFCore.QueryAnalyzer.Core
{
    /// <summary>
    /// Enhanced interceptor that captures SQL Server execution plans
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
            CancellationToken cancellationToken = default
        )
        {
            if (_options.IsEnabled)
            {
                StartQueryTracking(command, eventData);
            }

            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result
        )
        {
            if (_options.IsEnabled)
            {
                StartQueryTracking(command, eventData);
            }

            return base.ReaderExecuting(command, eventData, result);
        }

        public override async ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command,
            CommandExecutedEventData eventData,
            DbDataReader result,
            CancellationToken cancellationToken = default
        )
        {
            if (_options.IsEnabled)
            {
                await EndQueryTrackingAsync(command, eventData, cancellationToken);
            }

            return await base.ReaderExecutedAsync(command, eventData, result, cancellationToken);
        }

        public override DbDataReader ReaderExecuted(
            DbCommand command,
            CommandExecutedEventData eventData,
            DbDataReader result
        )
        {
            if (_options.IsEnabled)
            {
                _ = Task.Run(async () => await EndQueryTrackingAsync(command, eventData, CancellationToken.None));
            }

            return base.ReaderExecuted(command, eventData, result);
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
                    CommandId = eventData.CommandId,
                    Connection = command.Connection,
                    DbContext = eventData.Context // Store the DbContext to access connection string
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

        private async Task EndQueryTrackingAsync(DbCommand command, CommandExecutedEventData eventData, CancellationToken cancellationToken)
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

                    // Capture execution plan for slow queries
                    if (_options.CaptureExecutionPlan)
                    {
                        matchingContext.ExecutionPlan = await CaptureSqlServerExecutionPlanAsync(matchingContext, cancellationToken);
                    }

                    await _reportingService.ReportSlowQueryAsync(matchingContext, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error completing query tracking for connection {ConnectionId}", eventData.ConnectionId);
            }
        }

        private async Task<QueryTrackingContextExecutionPlan?> CaptureSqlServerExecutionPlanAsync(QueryTrackingContext context, CancellationToken cancellationToken)
        {
            try
            {
                // Method 1: Try to reuse the existing connection (preferred)
                if (context.Connection?.State == System.Data.ConnectionState.Open)
                {
                    return await CaptureExecutionPlanUsingExistingConnection(context.Connection, context, cancellationToken);
                }

                // Method 2: Get connection string from DbContext
                var connectionStringFromContext = GetConnectionStringFromContext(context);
                if (!string.IsNullOrEmpty(connectionStringFromContext))
                {
                    return await CaptureExecutionPlanUsingNewConnection(connectionStringFromContext, context, cancellationToken);
                }

                // Method 3: Try to extract from configuration (fallback)
                var connectionStringFromConfiguration = GetConnectionStringFromConfiguration(context);
                if (!string.IsNullOrEmpty(connectionStringFromConfiguration))
                {
                    return await CaptureExecutionPlanUsingNewConnection(connectionStringFromConfiguration, context, cancellationToken);
                }

                _logger.LogWarning("Cannot capture execution plan: No valid connection string available for query {QueryId}", context.QueryId);

                return null;

            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error capturing SQL Server execution plan for query {QueryId}", context.QueryId);

                return null;
            }
        }

        private async Task<QueryTrackingContextExecutionPlan?> CaptureExecutionPlanUsingExistingConnection(DbConnection connection, QueryTrackingContext context, CancellationToken cancellationToken)
        {
            try
            {
                _logger.LogDebug("Capturing execution plan using existing connection for query {QueryId}", context.QueryId);

                using var command = connection.CreateCommand();
                command.CommandTimeout = _options.ExecutionPlanTimeoutSeconds;

                // Get estimated execution plan using existing connection
                command.CommandText = "SET SHOWPLAN_XML ON";
                await command.ExecuteNonQueryAsync(cancellationToken);

                try
                {
                    // Execute the query to get execution plan
                    command.CommandText = BuildParameterizedQuery(context.CommandText, context.Parameters);

                    using var reader = await command.ExecuteReaderAsync(cancellationToken);

                    if (await reader.ReadAsync(cancellationToken))
                    {
                        var executionPlanXml = reader.GetString(0);
                        return FormatExecutionPlan(executionPlanXml, DatabaseProvider.SqlServer);
                    }

                    return null;
                }
                finally
                {
                    try
                    {
                        // Always turn off SHOWPLAN_XML
                        if (connection.State == System.Data.ConnectionState.Open)
                        {
                            command.CommandText = "SET SHOWPLAN_XML OFF";
                            await command.ExecuteNonQueryAsync(cancellationToken);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to turn off SHOWPLAN_XML");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error capturing execution plan using existing connection");

                return null;
            }
        }

        private async Task<QueryTrackingContextExecutionPlan?> CaptureExecutionPlanUsingNewConnection(string connectionString, QueryTrackingContext context, CancellationToken cancellationToken)
        {
            try
            {
                _logger.LogDebug("Capturing execution plan using new connection for query {QueryId}", context.QueryId);

                // Create new connection with the full connection string
                using var connection = new Microsoft.Data.SqlClient.SqlConnection(connectionString);
                await connection.OpenAsync(cancellationToken);

                using var command = connection.CreateCommand();
                command.CommandTimeout = _options.ExecutionPlanTimeoutSeconds;

                // Get estimated execution plan
                command.CommandText = "SET SHOWPLAN_XML ON";
                await command.ExecuteNonQueryAsync(cancellationToken);

                try
                {
                    // Execute the query to get execution plan
                    command.CommandText = BuildParameterizedQuery(context.CommandText, context.Parameters);

                    using var reader = await command.ExecuteReaderAsync(cancellationToken);

                    if (await reader.ReadAsync(cancellationToken))
                    {
                        var executionPlanXml = reader.GetString(0);
                        return FormatExecutionPlan(executionPlanXml, DatabaseProvider.SqlServer);
                    }

                    return null;
                }
                finally
                {
                    try
                    {
                        // Always turn off SHOWPLAN_XML
                        if (connection.State == System.Data.ConnectionState.Open)
                        {
                            command.CommandText = "SET SHOWPLAN_XML OFF";
                            await command.ExecuteNonQueryAsync(cancellationToken);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to turn off SHOWPLAN_XML on new connection");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error capturing execution plan using new connection");
                return null;
            }
        }

        private string? GetConnectionStringFromContext(QueryTrackingContext context)
        {
            try
            {
                if (context.DbContext == null)
                    return null;

                // Method 1: Try to get from Database.GetConnectionString() (EF Core 6+)
                var database = context.DbContext.Database;

                // Use reflection to access GetConnectionString method
                var getConnectionStringMethod = database.GetType().GetMethod("GetConnectionString");
                if (getConnectionStringMethod != null)
                {
                    var connectionString = getConnectionStringMethod.Invoke(database, null) as string;
                    if (!string.IsNullOrEmpty(connectionString))
                    {
                        _logger.LogDebug("Retrieved connection string from DbContext.Database.GetConnectionString()");
                        return connectionString;
                    }
                }

                // Method 2: Try to get from Database.GetDbConnection()
                using var dbConnection = database.GetDbConnection();
                if (!string.IsNullOrEmpty(dbConnection.ConnectionString))
                {
                    _logger.LogDebug("Retrieved connection string from DbContext.Database.GetDbConnection()");
                    return dbConnection.ConnectionString;
                }

                return null;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error getting connection string from DbContext");
                return null;
            }
        }

        private string? GetConnectionStringFromConfiguration(QueryTrackingContext context)
        {
            try
            {
                // This is a fallback - you might want to implement this based on your configuration
                // For now, return null to indicate this method is not implemented
                _logger.LogDebug("Configuration-based connection string retrieval not implemented");
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error getting connection string from configuration");
                return null;
            }
        }
        private static string BuildParameterizedQuery(string query, Dictionary<string, object?> parameters)
        {
            var result = query;
            foreach (var param in parameters)
            {
                var value = param.Value switch
                {
                    null => "NULL",
                    string s => $"'{s.Replace("'", "''")}'",
                    DateTime dt => $"'{dt:yyyy-MM-dd HH:mm:ss}'",
                    bool b => b ? "1" : "0",
                    decimal d => d.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    double d => d.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    float f => f.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    _ => param.Value.ToString()
                };

                result = result.Replace(param.Key, value);
            }
            return result;
        }

        private QueryTrackingContextExecutionPlan? FormatExecutionPlan(string? rawPlan, DatabaseProvider provider)
        {
            if (string.IsNullOrEmpty(rawPlan))
                return null;

            try
            {
                return new QueryTrackingContextExecutionPlan
                {
                    DatabaseProvider = provider,
                    PlanFormat = ExecutionPlanFormatType.Xml,
                    Content = rawPlan
                };
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error formatting execution plan");

                return null;
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

        private string[]? CaptureFilteredStackTrace()
        {
            try
            {
                var stackTrace = Environment.StackTrace;
                var projectRoot = FindProjectRoot();

                var lines = stackTrace.Split('\n')
                    .Where(line => !string.IsNullOrWhiteSpace(line) && IsApplicationCode(line, projectRoot))
                    .Take(_options.MaxStackTraceLines)
                    .Select(line => ConvertToRelativePath(line.Trim()))
                    .Where(line => !string.IsNullOrEmpty(line))
                    .ToArray();

                return lines.Length > 0 ? lines : null;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error capturing stack trace");
                return null;
            }
        }

        private static bool IsApplicationCode(string stackTraceLine, string? projectRoot)
        {
            // Get application namespace from project root folder name
            var appNamespace = projectRoot != null ? Path.GetFileName(projectRoot) : null
                ?? "YourAppNamespace"; // Fallback if project root not found

            // Exclude system and framework namespaces, only include application code
            if (appNamespace != null && !stackTraceLine.Contains(appNamespace))
            {
                return false;
            }

            // If we have a project root, only include files within the project directory
            if (projectRoot != null)
            {
                var inIndex = stackTraceLine.IndexOf(" in ");
                if (inIndex != -1)
                {
                    var lineIndex = stackTraceLine.LastIndexOf(":line ");
                    if (lineIndex != -1)
                    {
                        var filePath = stackTraceLine.Substring(inIndex + 4, lineIndex - inIndex - 4);
                        return filePath.StartsWith(projectRoot, StringComparison.OrdinalIgnoreCase);
                    }
                }
            }

            return true;
        }

        private static string ConvertToRelativePath(string stackTraceLine)
        {
            try
            {
                // Find the project root directory by looking for .csproj file
                var projectRoot = FindProjectRoot();
                if (projectRoot == null)
                    return stackTraceLine;

                // Look for file path pattern in the stack trace line
                // Pattern: "at ClassName.Method() in /full/path/to/file.cs:line X"
                var inIndex = stackTraceLine.IndexOf(" in ");
                if (inIndex == -1)
                    return stackTraceLine;

                var lineIndex = stackTraceLine.LastIndexOf(":line ");
                if (lineIndex == -1)
                    return stackTraceLine;

                var fullPath = stackTraceLine.Substring(inIndex + 4, lineIndex - inIndex - 4);
                var lineInfo = stackTraceLine[lineIndex..];

                // Convert full path to relative path
                if (fullPath.StartsWith(projectRoot, StringComparison.OrdinalIgnoreCase))
                {
                    var relativePath = fullPath[projectRoot.Length..].TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    var methodInfo = stackTraceLine[..inIndex];
                    return $"{methodInfo} in {relativePath}{lineInfo}";
                }

                return stackTraceLine;
            }
            catch
            {
                // If any error occurs in path conversion, return original line
                return stackTraceLine;
            }
        }

        private static string? FindProjectRoot()
        {
            try
            {
                var currentDirectory = Directory.GetCurrentDirectory();
                var directory = new DirectoryInfo(currentDirectory);

                while (directory != null)
                {
                    if (directory.GetFiles("*.csproj").Length != 0)
                    {
                        return directory.FullName;
                    }
                    directory = directory.Parent;
                }

                // Fallback to current directory if no .csproj found
                return currentDirectory;
            }
            catch
            {
                return null;
            }
        }

        private static string TruncateForLog(string text, int maxLength)
        {
            return text.Length <= maxLength ? text : text[..maxLength] + "...";
        }
    }
}
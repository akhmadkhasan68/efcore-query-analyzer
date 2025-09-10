using EFCore.QueryAnalyzer.Core;
using EFCore.QueryAnalyzer.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Data.Common;

namespace EFCore.QueryAnalyzer.Services
{
    /// <summary>
    /// Background service that processes queued query analysis items
    /// </summary>
    public sealed class QueryAnalysisBackgroundService(
        QueryAnalysisQueue queue,
        IServiceScopeFactory serviceScopeFactory,
        ILogger<QueryAnalysisBackgroundService> logger) : BackgroundService
    {
        private readonly QueryAnalysisQueue _queue = queue ?? throw new ArgumentNullException(nameof(queue));
        private readonly IServiceScopeFactory _serviceScopeFactory = serviceScopeFactory ?? throw new ArgumentNullException(nameof(serviceScopeFactory));
        private readonly ILogger<QueryAnalysisBackgroundService> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await ProcessQueueItemsAsync(stoppingToken);

                    // Wait a short time before checking for more items
                    await Task.Delay(100, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    // Expected when cancellation is requested
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in Query Analysis Background Service execution loop");

                    // Wait a bit longer on errors to avoid tight error loops
                    await Task.Delay(1000, stoppingToken);
                }
            }

        }

        private async Task ProcessQueueItemsAsync(CancellationToken cancellationToken)
        {
            var processedCount = 0;
            const int maxBatchSize = 10; // Process up to 10 items per batch to avoid blocking too long

            while (processedCount < maxBatchSize && _queue.TryDequeue(out var context))
            {
                try
                {
                    if (context != null)
                    {
                        await ProcessSingleItemAsync(context, cancellationToken);
                        processedCount++;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing queued query analysis for QueryId: {QueryId}", context?.QueryId);
                }
            }

        }

        private async Task ProcessSingleItemAsync(QueryTrackingContext context, CancellationToken cancellationToken)
        {
            using var scope = _serviceScopeFactory.CreateScope();
            var reportingService = scope.ServiceProvider.GetRequiredService<IQueryReportingService>();
            var options = scope.ServiceProvider.GetRequiredService<QueryAnalyzerOptions>();


            try
            {
                // Capture execution plan if enabled and not already captured
                if (options.CaptureExecutionPlan && context.ExecutionPlan == null)
                {
                    try
                    {
                        context.ExecutionPlan = await CaptureSqlServerExecutionPlanAsync(context, options, cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to capture execution plan for QueryId: {QueryId}", context.QueryId);
                        // Continue with reporting even if execution plan capture fails
                    }
                }

                await reportingService.ReportSlowQueryAsync(context, cancellationToken);

            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process queued query analysis for QueryId: {QueryId}", context.QueryId);

                // Note: We don't re-queue failed items to avoid infinite loops
                // Consider implementing a dead letter queue or retry mechanism with limits in the future
            }
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {

            // Process remaining items in the queue during shutdown
            var remainingItems = 0;
            while (_queue.TryDequeue(out var context) && !cancellationToken.IsCancellationRequested)
            {
                try
                {
                    if (context != null)
                    {
                        await ProcessSingleItemAsync(context, cancellationToken);
                        remainingItems++;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error processing item during shutdown for QueryId: {QueryId}", context?.QueryId);
                }
            }


            await base.StopAsync(cancellationToken);
        }

        private async Task<QueryTrackingContextExecutionPlan?> CaptureSqlServerExecutionPlanAsync(QueryTrackingContext context, QueryAnalyzerOptions options, CancellationToken cancellationToken)
        {
            try
            {
                // Determine connection string to use
                // Priority:
                // 1. Use explicitly configured connection string in options if provided
                // 2. Reuse existing open connection if available
                // 3. Extract connection string from DbContext if available
                // 4. Fallback to configuration-based connection string (not implemented here)

                // Method 1: Use explicitly configured connection string in options
                if (options.ConnectionString != null)
                {
                    return await CaptureExecutionPlanUsingNewConnection(options.ConnectionString, context, options, cancellationToken);
                }

                // Method 2: Try to reuse the existing connection (preferred)
                if (context.Connection?.State == System.Data.ConnectionState.Open)
                {
                    return await CaptureExecutionPlanUsingExistingConnection(context.Connection, context, options, cancellationToken);
                }

                // Method 3: Get connection string from DbContext
                var connectionStringFromContext = GetConnectionStringFromContext(context);
                if (!string.IsNullOrEmpty(connectionStringFromContext))
                {
                    return await CaptureExecutionPlanUsingNewConnection(connectionStringFromContext, context, options, cancellationToken);
                }

                // Method 4: Try to extract from configuration (fallback)
                var connectionStringFromConfiguration = GetConnectionStringFromConfiguration(context);
                if (!string.IsNullOrEmpty(connectionStringFromConfiguration))
                {
                    return await CaptureExecutionPlanUsingNewConnection(connectionStringFromConfiguration, context, options, cancellationToken);
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

        private async Task<QueryTrackingContextExecutionPlan?> CaptureExecutionPlanUsingExistingConnection(DbConnection connection, QueryTrackingContext context, QueryAnalyzerOptions options, CancellationToken cancellationToken)
        {
            try
            {

                using var command = connection.CreateCommand();
                command.CommandTimeout = options.ExecutionPlanTimeoutSeconds;

                // Get estimated execution plan using existing connection
                command.CommandText = "SET SHOWPLAN_XML ON";
                await command.ExecuteNonQueryAsync(cancellationToken);

                try
                {
                    // Execute the query to get execution plan using literal values instead of parameters
                    // This is necessary because SHOWPLAN_XML doesn't work well with parameterized queries
                    var literalCommandText = SubstituteParametersWithLiterals(context.CommandText, context.Parameters);
                    command.CommandText = literalCommandText;

                    using var reader = await command.ExecuteReaderAsync(cancellationToken);

                    if (await reader.ReadAsync(cancellationToken))
                    {
                        var executionPlanXml = reader.GetString(0);
                        return FormatExecutionPlan(executionPlanXml, DatabaseProvider.SqlServer);
                    }

                    return null;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error executing command to capture execution plan using existing connection");
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

        private async Task<QueryTrackingContextExecutionPlan?> CaptureExecutionPlanUsingNewConnection(string connectionString, QueryTrackingContext context, QueryAnalyzerOptions options, CancellationToken cancellationToken)
        {
            try
            {

                // Create new connection with the full connection string
                using var connection = new Microsoft.Data.SqlClient.SqlConnection(connectionString);
                await connection.OpenAsync(cancellationToken);

                using var command = connection.CreateCommand();
                command.CommandTimeout = options.ExecutionPlanTimeoutSeconds;

                // Get estimated execution plan
                command.CommandText = "SET SHOWPLAN_XML ON";
                await command.ExecuteNonQueryAsync(cancellationToken);

                try
                {
                    // Execute the query to get execution plan using literal values instead of parameters
                    // This is necessary because SHOWPLAN_XML doesn't work well with parameterized queries
                    var literalCommandText = SubstituteParametersWithLiterals(context.CommandText, context.Parameters);
                    command.CommandText = literalCommandText;

                    using var reader = await command.ExecuteReaderAsync(cancellationToken);

                    if (await reader.ReadAsync(cancellationToken))
                    {
                        var executionPlanXml = reader.GetString(0);
                        return FormatExecutionPlan(executionPlanXml, DatabaseProvider.SqlServer);
                    }

                    return null;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error executing command to capture execution plan on new connection");
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
                        return connectionString;
                    }
                }

                // Method 2: Try to get from Database.GetConnectionString() via reflection for older EF versions
                try
                {
                    var connectionStringProperty = database.GetType().GetProperty("ConnectionString");
                    if (connectionStringProperty != null)
                    {
                        var connectionString = connectionStringProperty.GetValue(database) as string;
                        if (!string.IsNullOrEmpty(connectionString))
                        {
                            return connectionString;
                        }
                    }
                }
                catch
                {
                    // Ignore reflection errors and continue with other methods
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
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error getting connection string from configuration");
                return null;
            }
        }

        private static string SubstituteParametersWithLiterals(string commandText, Dictionary<string, object?> parameters)
        {
            if (parameters == null || parameters.Count == 0)
                return commandText;

            var result = commandText;

            foreach (var param in parameters)
            {
                var parameterName = param.Key;
                var parameterValue = param.Value;

                // Handle both @parameter and ? parameter formats
                var literalValue = ConvertToSqlLiteral(parameterValue);
                
                // Replace @parameterName with literal value
                if (parameterName.StartsWith("@"))
                {
                    result = result.Replace(parameterName, literalValue);
                }
                else
                {
                    // Handle @parameterName format even if parameter doesn't start with @
                    result = result.Replace($"@{parameterName}", literalValue);
                }
            }

            return result;
        }

        private static string ConvertToSqlLiteral(object? value)
        {
            return value switch
            {
                null => "NULL",
                string str => $"'{str.Replace("'", "''")}'", // Escape single quotes
                char ch => $"'{ch.ToString().Replace("'", "''")}'",
                bool boolean => boolean ? "1" : "0",
                byte b => b.ToString(),
                sbyte sb => sb.ToString(),
                short s => s.ToString(),
                ushort us => us.ToString(),
                int i => i.ToString(),
                uint ui => ui.ToString(),
                long l => l.ToString(),
                ulong ul => ul.ToString(),
                float f => f.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
                double d => d.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
                decimal dec => dec.ToString(System.Globalization.CultureInfo.InvariantCulture),
                DateTime dt => $"'{dt:yyyy-MM-dd HH:mm:ss.fff}'",
                DateTimeOffset dto => $"'{dto:yyyy-MM-dd HH:mm:ss.fff zzz}'",
                TimeSpan ts => $"'{ts}'",
                Guid guid => $"'{guid}'",
                byte[] bytes => $"0x{Convert.ToHexString(bytes)}",
                _ => $"'{value?.ToString()?.Replace("'", "''") ?? "NULL"}'"
            };
        }

        private static void AddParametersToCommand(DbCommand command, Dictionary<string, object?> parameters)
        {
            if (parameters == null || parameters.Count == 0)
                return;

            // Clear any existing parameters
            command.Parameters.Clear();

            foreach (var param in parameters)
            {
                var parameter = command.CreateParameter();
                parameter.ParameterName = param.Key;
                parameter.Value = param.Value ?? DBNull.Value;

                // Handle specific types that need special treatment
                if (param.Value is Guid guid)
                {
                    parameter.Value = guid;
                    parameter.DbType = System.Data.DbType.Guid;
                }
                else if (param.Value is DateTime dateTime)
                {
                    parameter.Value = dateTime;
                    parameter.DbType = System.Data.DbType.DateTime;
                }
                else if (param.Value is bool boolean)
                {
                    parameter.Value = boolean;
                    parameter.DbType = System.Data.DbType.Boolean;
                }

                command.Parameters.Add(parameter);
            }
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
    }
}
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
    /// Enhanced interceptor that captures query performance metrics and queues them for background processing
    /// </summary>
    public sealed class QueryPerformanceInterceptor(
        ILogger<QueryPerformanceInterceptor> logger,
        QueryAnalysisQueue queue,
        QueryAnalyzerOptions options) : DbCommandInterceptor
    {
        private readonly ILogger<QueryPerformanceInterceptor> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        private readonly QueryAnalysisQueue _queue = queue ?? throw new ArgumentNullException(nameof(queue));
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

            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error starting query tracking for connection {ConnectionId}", eventData.ConnectionId);
            }
        }

        private Task EndQueryTrackingAsync(DbCommand command, CommandExecutedEventData eventData, CancellationToken cancellationToken)
        {
            try
            {
                var matchingContext = _activeQueries.Values
                    .FirstOrDefault(ctx => ctx.ConnectionId == eventData.ConnectionId &&
                                          ctx.CommandId == eventData.CommandId);

                if (matchingContext == null)
                {
                    return Task.CompletedTask;
                }

                _activeQueries.TryRemove(matchingContext.QueryId, out _);

                matchingContext.Stopwatch.Stop();
                matchingContext.EndTime = DateTime.UtcNow;
                matchingContext.ExecutionTime = matchingContext.Stopwatch.Elapsed;


                // Check if execution time exceeds threshold
                if (matchingContext.ExecutionTime.TotalMilliseconds >= _options.ThresholdMilliseconds)
                {
                    _logger.LogWarning("Slow query detected: {Duration}ms (Threshold: {Threshold}ms) - Query: {Query}",
                        matchingContext.ExecutionTime.TotalMilliseconds,
                        _options.ThresholdMilliseconds,
                        TruncateForLog(matchingContext.CommandText, 200));

                    // Enqueue for background processing - this is ultra-fast and non-blocking
                    // The background service will handle execution plan capture and reporting
                    _queue.Enqueue(matchingContext);

                }

                return Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error completing query tracking for connection {ConnectionId}", eventData.ConnectionId);
                return Task.CompletedTask;
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
using EFCore.QueryAnalyzer.Core.Models;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Data.Common;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace EFCore.QueryAnalyzer.Core
{
    /// <summary>
    /// Enhanced interceptor that captures query performance metrics and queues them for background processing
    /// </summary>
    public sealed class QueryPerformanceInterceptor(
        ILogger<QueryPerformanceInterceptor> logger,
        QueryAnalysisQueue queue,
        QueryAnalyzerOptions options
        ) : DbCommandInterceptor
    {
        private readonly ILogger<QueryPerformanceInterceptor> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        private readonly QueryAnalysisQueue _queue = queue ?? throw new ArgumentNullException(nameof(queue));
        private readonly QueryAnalyzerOptions _options = options ?? throw new ArgumentNullException(nameof(options));
        private readonly ConcurrentDictionary<Guid, QueryTrackingContext> _activeQueries = new();

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

        public override DbDataReader ReaderExecuted(
            DbCommand command,
            CommandExecutedEventData eventData,
            DbDataReader result
        )
        {
            if (_options.IsEnabled)
            {
                _ = Task.Run(async () => await EndQueryTrackingAsync(eventData, CancellationToken.None));
            }

            return base.ReaderExecuted(command, eventData, result);
        }

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

        public override async ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command,
            CommandExecutedEventData eventData,
            DbDataReader result,
            CancellationToken cancellationToken = default
        )
        {
            if (_options.IsEnabled)
            {
                await EndQueryTrackingAsync(eventData, cancellationToken);
            }

            return await base.ReaderExecutedAsync(command, eventData, result, cancellationToken);
        }

        public override InterceptionResult<int> NonQueryExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result
        )
        {
            if (_options.IsEnabled)
            {
                StartQueryTracking(command, eventData);
            }

            return base.NonQueryExecuting(command, eventData, result);
        }

        public override int NonQueryExecuted(
            DbCommand command,
            CommandExecutedEventData eventData,
            int result
        )
        {
            if (_options.IsEnabled)
            {
                _ = Task.Run(async () => await EndQueryTrackingAsync(eventData, CancellationToken.None));
            }

            return base.NonQueryExecuted(command, eventData, result);
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default
        )
        {
            if (_options.IsEnabled)
            {
                StartQueryTracking(command, eventData);
            }

            return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
        }

        public override async ValueTask<int> NonQueryExecutedAsync(
            DbCommand command,
            CommandExecutedEventData eventData,
            int result,
            CancellationToken cancellationToken = default
        )
        {
            if (_options.IsEnabled)
            {
                await EndQueryTrackingAsync(eventData, cancellationToken);
            }

            return await base.NonQueryExecutedAsync(command, eventData, result, cancellationToken);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void StartQueryTracking(DbCommand command, CommandEventData eventData)
        {
            _logger.LogInformation("Start tracking query - thread {ThreadId} - connection {ConnectionId} - timestamp {Timestamp} - command text: {CommandText}",
                Environment.CurrentManagedThreadId,
                eventData.ConnectionId,
                DateTime.UtcNow.ToString("o"),
                command.CommandText);
            try
            {
                string[] stackTrace;
                var stackTraceCaptured = false;

                if (_options.CaptureStackTrace)
                {
                    stackTrace = CaptureStackTrace() ?? [];
                    stackTraceCaptured = stackTrace.Length > 0;
                }
                else
                {
                    // Stack trace capture disabled
                    stackTrace = [];
                }

                var context = new QueryTrackingContext
                {
                    QueryId = Guid.NewGuid(),
                    CommandText = command.CommandText,
                    Parameters = ExtractParameters(command),
                    StartTime = DateTime.UtcNow,
                    Stopwatch = Stopwatch.StartNew(),
                    StackTrace = stackTrace,
                    ConnectionId = eventData.ConnectionId,
                    ContextType = eventData.Context?.GetType().FullName ?? "Unknown",
                    CommandId = eventData.CommandId,
                    Connection = command.Connection,
                    DbContext = eventData.Context
                };

                _activeQueries[context.QueryId] = context;

                // Debug log for missing stack traces
                if (_options.CaptureStackTrace && !stackTraceCaptured)
                {
                    _logger.LogWarning("Stack trace capture failed for query on thread {ThreadId}. " +
                        "Query: {Query}.",
                        Environment.CurrentManagedThreadId,
                        TruncateForLog(command.CommandText, 100));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error starting query tracking for connection {ConnectionId}", eventData.ConnectionId);
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private Task EndQueryTrackingAsync(CommandExecutedEventData eventData, CancellationToken cancellationToken)
        {
            try
            {
                var matchingContext = _activeQueries.Values.FirstOrDefault(ctx =>
                {
                    return ctx.ConnectionId == eventData.ConnectionId && ctx.CommandId == eventData.CommandId;
                });

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
                    _logger.LogWarning("Slow query detected: {Duration}ms (Threshold: {Threshold}ms) - " +
                        "Query: {Query}",
                        matchingContext.ExecutionTime.TotalMilliseconds,
                        _options.ThresholdMilliseconds,
                        TruncateForLog(matchingContext.CommandText, 200));

                    // Enqueue for background processing - this is ultra-fast and non-blocking
                    // The background service will handle execution plan capture and reporting
                    _queue.Enqueue(matchingContext);
                }
                else
                {
                    // Debug log for successful queries with stack trace info
                    _logger.LogDebug("Query completed: {Duration}ms - (Thread: {ThreadId})",
                        matchingContext.ExecutionTime.TotalMilliseconds,
                        Environment.CurrentManagedThreadId);
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

        [MethodImpl(MethodImplOptions.NoInlining)]
        private string[] CaptureStackTrace()
        {
            try
            {
                var stackTrace = Environment.StackTrace;
                if (string.IsNullOrEmpty(stackTrace))
                    return [];

                var projectRoot = FindProjectRoot();

                var lines = stackTrace.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                    .Where(line => !string.IsNullOrWhiteSpace(line))
                    .Select(line => line.Trim())
                    .Where(line => IsApplicationCode(line, projectRoot))
                    .Take(_options.MaxStackTraceLines)
                    .Select(ConvertToRelativePath)
                    .Where(line => !string.IsNullOrEmpty(line))
                    .Distinct() // Remove duplicate entries
                    .ToArray();

                // Return null if no meaningful application code found
                return lines.Length > 0 ? lines : [];
            }
            catch (Exception ex)
            {
                // Log warning for stack trace capture issues (kept minimal for production)
                _logger.LogWarning(ex, "Error capturing filtered stack trace for query analysis");

                return [];
            }
        }

        private static bool IsApplicationCode(string stackTraceLine, string? projectRoot)
        {
            // Quick exclusion based on method patterns - exclude framework code
            if (IsFrameworkCode(stackTraceLine))
                return false;

            // Get application namespace from project root folder name
            var appNamespace = projectRoot != null ? Path.GetFileName(projectRoot) : null
                ?? "YourAppNamespace"; // Fallback if project root not found

            // Exclude system and framework namespaces, only include application code
            if (appNamespace != null && !stackTraceLine.Contains(appNamespace))
            {
                return false;
            }

            // Primary filter: If we have a project root, only include files within the project directory
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

            // Fallback: Use namespace-based filtering when project root isn't available
            return IsApplicationNamespace(stackTraceLine);
        }

        private static bool IsFrameworkCode(string stackTraceLine)
        {
            // Extract method information from stack trace line
            // Pattern: "at Namespace.ClassName.MethodName(...) in file.cs:line N"
            var atIndex = stackTraceLine.IndexOf("at ");
            if (atIndex == -1) return false;

            var methodStart = atIndex + 3;
            var inIndex = stackTraceLine.IndexOf(" in ");
            var methodEnd = inIndex != -1 ? inIndex : stackTraceLine.Length;

            if (methodStart >= methodEnd) return false;

            var methodInfo = stackTraceLine.Substring(methodStart, methodEnd - methodStart);

            // Exclude compiler-generated lambda methods and anonymous code
            if (IsCompilerGeneratedCode(methodInfo))
                return true;

            // Exclude known framework namespaces
            var frameworkPrefixes = new[]
            {
                // Entity Framework Core
                "Microsoft.EntityFrameworkCore",
                "Microsoft.Data.SqlClient",
                
                // ASP.NET Core
                "Microsoft.AspNetCore",
                "Microsoft.Extensions",
                
                // System namespaces
                "System.",
                "System.Linq",
                "System.Threading",
                "System.Collections",
                "System.Reflection",
                "System.Runtime",
                "System.Text.Json",
                
                // Query Analyzer itself (avoid recursive stack traces)
                "EFCore.QueryAnalyzer",
                
                // Common third-party frameworks
                "Newtonsoft.Json",
                "AutoMapper",
                "FluentValidation",
                "MediatR",
                
                // .NET Runtime internals
                "Microsoft.Extensions.DependencyInjection",
                "Microsoft.Extensions.Hosting",
                "Microsoft.Extensions.Logging",
                "Microsoft.Extensions.Configuration",
                
                // Async/await infrastructure
                "System.Runtime.CompilerServices",
                "System.Threading.Tasks"
            };

            return frameworkPrefixes.Any(prefix => methodInfo.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        }

        private static bool IsCompilerGeneratedCode(string methodInfo)
        {
            // Check for lambda methods and compiler-generated code patterns
            var compilerGeneratedPatterns = new[]
            {
                // Lambda methods
                "lambda_method",
                
                // Compiler-generated closure classes
                "<>c__DisplayClass",
                
                // Cached delegate fields (C# compiler optimization)
                "<>9__",
                
                // Anonymous methods
                "<>c.<",
                
                // Local functions
                "<>c__localFunction",
                
                // Async state machine methods
                "MoveNext()",
                
                // Dynamic method invocations
                "DynamicMethod",
                
                // Expression tree compiled methods
                "CallSite"
            };

            return compilerGeneratedPatterns.Any(pattern =>
                methodInfo.Contains(pattern, StringComparison.OrdinalIgnoreCase));
        }

        private static bool IsApplicationNamespace(string stackTraceLine)
        {
            // Extract method information
            var atIndex = stackTraceLine.IndexOf("at ");
            if (atIndex == -1) return false;

            var methodStart = atIndex + 3;
            var inIndex = stackTraceLine.IndexOf(" in ");
            var methodEnd = inIndex != -1 ? inIndex : stackTraceLine.Length;

            if (methodStart >= methodEnd) return false;

            var methodInfo = stackTraceLine[methodStart..methodEnd];

            // Try to detect user assemblies by looking for common application patterns
            var userCodeIndicators = new[]
            {
                ".Controllers.",
                ".Services.",
                ".Repositories.",
                ".Models.",
                ".Domain.",
                ".Business.",
                ".Application.",
                ".Core.",
                ".Data.",
                ".API.",
                ".Web.",
                ".Infrastructure."
            };

            // Include if it matches user code patterns
            if (userCodeIndicators.Any(indicator => methodInfo.Contains(indicator, StringComparison.OrdinalIgnoreCase)))
                return true;

            // Include if it doesn't start with known system prefixes
            var systemPrefixes = new[] { "Microsoft.", "System.", "System.Runtime" };
            return !systemPrefixes.Any(prefix => methodInfo.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
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
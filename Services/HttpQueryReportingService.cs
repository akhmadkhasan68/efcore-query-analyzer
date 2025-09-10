
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Text;
using Microsoft.Extensions.Hosting;
using EFCore.QueryAnalyzer.Core;
using EFCore.QueryAnalyzer.Core.Models;

namespace EFCore.QueryAnalyzer.Services
{
    /// <summary>
    /// Default implementation that reports slow queries to an HTTP API
    /// </summary>
    public sealed class HttpQueryReportingService : IQueryReportingService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<HttpQueryReportingService> _logger;
        private readonly QueryAnalyzerOptions _options;
        private readonly IHostEnvironment? _hostEnvironment;

        public HttpQueryReportingService(
            HttpClient httpClient,
            ILogger<HttpQueryReportingService> logger,
            QueryAnalyzerOptions options,
            IHostEnvironment? hostEnvironment = null)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _hostEnvironment = hostEnvironment;

            // Configure HttpClient timeout and User-Agent
            _httpClient.Timeout = TimeSpan.FromMilliseconds(_options.ApiTimeoutMs);
            if (!_httpClient.DefaultRequestHeaders.UserAgent.Any())
            {
                _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("EFCore.QueryAnalyzer/1.0.0");
            }
        }

        public async Task ReportSlowQueryAsync(QueryTrackingContext context, CancellationToken cancellationToken = default)
        {
            // Check if reporting should be enabled for current environment
            if (!ShouldReport())
            {
                return;
            }

            if (string.IsNullOrEmpty(_options.ApiEndpoint))
            {
                _logger.LogWarning("API endpoint not configured for slow query reporting");
                return;
            }

            try
            {
                var report = CreateReport(context);
                await SendReportAsync(report, cancellationToken);

                _logger.LogInformation("Slow query reported successfully: {QueryId} ({Duration}ms)",
                    context.QueryId, context.ExecutionTime.TotalMilliseconds);
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Query reporting cancelled: {QueryId}", context.QueryId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to report slow query: {QueryId}", context.QueryId);
            }
        }

        private bool ShouldReport()
        {
            var environment = _hostEnvironment?.EnvironmentName ??
                             Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ??
                             "Production";

            return environment.Equals("Development", StringComparison.OrdinalIgnoreCase)
                ? _options.EnableInDevelopment
                : _options.EnableInProduction;
        }

        private SlowQueryReport CreateReport(QueryTrackingContext context)
        {
            var environment = _hostEnvironment?.EnvironmentName ??
                             Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ??
                             "Unknown";

            var applicationName = _hostEnvironment?.ApplicationName ??
                                 Environment.GetEnvironmentVariable("APPLICATION_NAME") ??
                                 System.Reflection.Assembly.GetEntryAssembly()?.GetName().Name ??
                                 "Unknown";

            var version = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "Unknown";

            var report = new SlowQueryReport
            {
                QueryId = context.QueryId,
                RawQuery = TruncateQuery(context.CommandText),
                Parameters = context.Parameters,
                ExecutionTimeMs = context.ExecutionTime.TotalMilliseconds,
                StackTrace = context.StackTrace,
                Timestamp = context.StartTime,
                ContextType = context.ContextType,
                Environment = environment,
                ApplicationName = applicationName,
                Version = version,
                ExecutionPlan = context.ExecutionPlan.ToSlowQueryReportExecutionPlan(),
            };


            return report;
        }

        private async Task SendReportAsync(SlowQueryReport report, CancellationToken cancellationToken)
        {
            var json = JsonSerializer.Serialize(report, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = false
            });

            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            using var request = new HttpRequestMessage(HttpMethod.Post, _options.ApiEndpoint)
            {
                Content = content
            };

            // Add authentication header if API key is provided
            if (!string.IsNullOrEmpty(_options.ApiKey))
            {
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _options.ApiKey);
            }

            // Add project ID header if configured
            if (!string.IsNullOrEmpty(_options.ProjectId))
            {
                request.Headers.Add("X-PROJECT-ID", _options.ProjectId);
            }


            var response = await _httpClient.SendAsync(request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogError("Failed to report slow query. Status: {StatusCode}, Response: {Response}",
                    response.StatusCode, responseContent);

                response.EnsureSuccessStatusCode(); // This will throw an exception
            }
        }

        private string TruncateQuery(string query)
        {
            return query.Length > _options.MaxQueryLength
                ? query[.._options.MaxQueryLength] + "\n-- [TRUNCATED]"
                : query;
        }
    }
}
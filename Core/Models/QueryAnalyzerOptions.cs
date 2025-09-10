namespace EFCore.QueryAnalyzer.Core.Models
{
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
        /// Whether to capture execution plans for slow queries (default: false)
        /// </summary>
        public bool CaptureExecutionPlan { get; set; } = false;

        /// <summary>
        /// Connection string to use for execution plan capture 
        /// </summary>s
        public string? ConnectionString { get; set; }

        /// <summary>
        /// Timeout in seconds for execution plan capture (default: 30)
        /// </summary>
        public int ExecutionPlanTimeoutSeconds { get; set; } = 30;

        /// <summary>
        /// Maximum number of lines to capture in stack trace (default: 10)
        /// </summary>
        public int MaxStackTraceLines { get; set; } = 20;

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
        /// Project identifier to be sent as X-PROJECT-ID header
        /// </summary>
        public string? ProjectId { get; set; }

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

        // ===================== //

        /// <summary>
        /// Database provider type for execution plan capture
        /// </summary>
        public DatabaseProvider DatabaseProvider { get; set; } = DatabaseProvider.Auto;
    }
}
namespace EFCore.QueryAnalyzer.Core.Models
{
    /// <summary>
    /// Report model for slow query data with SQL Server execution plan analysis
    /// </summary>
    public sealed class SlowQueryReport
    {
        public Guid QueryId { get; set; }
        public string RawQuery { get; set; } = string.Empty;
        public Dictionary<string, object?> Parameters { get; set; } = [];
        public double ExecutionTimeMs { get; set; }
        public string[]? StackTrace { get; set; }
        public DateTime Timestamp { get; set; }
        public string ContextType { get; set; } = string.Empty;
        public string Environment { get; set; } = string.Empty;
        public string? ApplicationName { get; set; }
        public string? Version { get; set; }

        // SQL Server execution plan analysis properties
        public SlowQueryReportExecutionPlan? ExecutionPlan { get; set; }
    }

    public sealed class SlowQueryReportExecutionPlan
    {
        public string DatabaseProvider { get; set; } = Models.DatabaseProvider.Unknown.ToProviderName();

        public SlowQueryReportExecutionPlanFormat? PlanFormat { get; set; }

        public string? Content { get; set; }
    }

    public sealed class SlowQueryReportExecutionPlanFormat
    {
        public string ContentType { get; set; } = ExecutionPlanFormatType.Unknown.ToContentType();
        public string FileExtension { get; set; } = ExecutionPlanFormatType.Unknown.ToFileExtension();
        public string Description { get; set; } = ExecutionPlanFormatType.Unknown.ToDescription();
    }
}
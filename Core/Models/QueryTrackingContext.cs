using System.Data.Common;
using System.Diagnostics;
using Microsoft.EntityFrameworkCore;

namespace EFCore.QueryAnalyzer.Core.Models
{
    /// <summary>
    /// Context for tracking individual query execution
    /// </summary>
    public sealed class QueryTrackingContext
    {
        public Guid QueryId { get; set; }
        public string CommandText { get; set; } = string.Empty;
        public Dictionary<string, object?> Parameters { get; set; } = [];
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public TimeSpan ExecutionTime { get; set; }
        public Stopwatch Stopwatch { get; set; } = new();
        public string[]? StackTrace { get; set; }
        public bool StackTraceCaptured { get; set; }
        public string? StackTraceSource { get; set; } // "Environment", "CallerInfo", "Manual", etc.
        public Guid ConnectionId { get; set; }
        public string ContextType { get; set; } = string.Empty;
        public Guid CommandId { get; set; }


        // SQL Server execution plan analysis properties
        public DbConnection? Connection { get; set; }
        public DbContext? DbContext { get; set; } // Store DbContext to access connection string
        public QueryTrackingContextExecutionPlan? ExecutionPlan { get; set; }
    }

    public sealed class QueryTrackingContextExecutionPlan
    {
        public DatabaseProvider DatabaseProvider { get; set; } = DatabaseProvider.Unknown;

        public ExecutionPlanFormatType? PlanFormat { get; set; }

        public string? Content { get; set; }
    }

    public static class QueryTrackingContextExecutionPlanExtensions
    {
        public static SlowQueryReportExecutionPlan? ToSlowQueryReportExecutionPlan(this QueryTrackingContextExecutionPlan? plan)
        {
            if (plan == null)
            {
                return null;
            }

            return new SlowQueryReportExecutionPlan
            {
                DatabaseProvider = plan.DatabaseProvider.ToProviderName(),
                PlanFormat = plan.PlanFormat.HasValue ? new SlowQueryReportExecutionPlanFormat
                {
                    ContentType = plan.PlanFormat.Value.ToContentType(),
                    FileExtension = plan.PlanFormat.Value.ToFileExtension(),
                    Description = plan.PlanFormat.Value.ToDescription()
                } : null,
                Content = plan.Content
            };
        }
    }
}
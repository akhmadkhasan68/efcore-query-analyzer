namespace EFCore.QueryAnalyzer.Core.Models
{
    /// <summary>
    /// Format options for query analysis reports
    /// </summary>
    public enum ExecutionPlanFormatType
    {
        Json,
        Xml,
        Text,
        Unknown
    }

    public static class ExecutionPlanFormatTypeExtensions
    {
        /// <summary>
        /// Get the file extension for the format
        /// </summary>
        public static string ToFileExtension(this ExecutionPlanFormatType format)
        {
            return format switch
            {
                ExecutionPlanFormatType.Json => "json",
                ExecutionPlanFormatType.Xml => "xml",
                ExecutionPlanFormatType.Text => "txt",
                ExecutionPlanFormatType.Unknown => "txt",
                _ => "txt"
            };
        }

        public static string ToContentType(this ExecutionPlanFormatType format)
        {
            return format switch
            {
                ExecutionPlanFormatType.Json => "application/json",
                ExecutionPlanFormatType.Xml => "application/xml",
                ExecutionPlanFormatType.Text => "text/plain",
                ExecutionPlanFormatType.Unknown => "text/plain",
                _ => "text/plain"
            };
        }

        public static string ToDescription(this ExecutionPlanFormatType format)
        {
            return format switch
            {
                ExecutionPlanFormatType.Json => "JSON",
                ExecutionPlanFormatType.Xml => "XML",
                ExecutionPlanFormatType.Text => "Plain Text",
                ExecutionPlanFormatType.Unknown => "Plain Text",
                _ => "Plain Text"
            };
        }
    }
}
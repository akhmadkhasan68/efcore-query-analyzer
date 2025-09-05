namespace EFCore.QueryAnalyzer.Core.Models
{
    /// <summary>
    /// Database provider types for execution plan capture
    /// </summary>
    public enum DatabaseProvider
    {
        Auto,
        SqlServer,
        PostgreSQL,
        MySQL,
        Oracle,
        SQLite,
        Other,
        Unknown
    }

    public static class DatabaseProviderExtensions
    {
        /// <summary>
        /// Get the provider name string for reporting
        /// </summary>
        public static string ToProviderName(this DatabaseProvider provider)
        {
            return provider switch
            {
                DatabaseProvider.SqlServer => "SqlServer",
                DatabaseProvider.PostgreSQL => "PostgreSQL",
                DatabaseProvider.MySQL => "MySQL",
                DatabaseProvider.Oracle => "Oracle",
                DatabaseProvider.SQLite => "SQLite",
                DatabaseProvider.Other => "Other",
                DatabaseProvider.Auto => "Auto",
                _ => "Unknown"
            };
        }
    }
}
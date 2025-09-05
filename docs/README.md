# EF Core Query Analyzer

A high-performance, production-ready NuGet package for monitoring Entity Framework Core query performance and automatically reporting slow queries to your analytics platform.

## Features

- 🚀 **Zero-configuration** startup with sensible defaults
- 📊 **Real-time monitoring** of all EF Core queries
- 🎯 **Configurable thresholds** for slow query detection
- 🔍 **Stack trace capture** to identify problematic code
- 🌐 **HTTP API integration** for centralized monitoring
- 🏗️ **Multiple reporting strategies** (HTTP, In-Memory, Custom, Composite)
- 🔧 **Environment-aware** configuration (Dev/Prod)
- ⚡ **Minimal performance overhead**
- 🧪 **Built-in testing support**
- 📈 **SQL Server execution plan analysis** with detailed performance metrics
- 🗄️ **Multi-database support** (SQL Server, PostgreSQL, MySQL, Oracle, SQLite)
- 🔬 **Advanced query statistics** (row counts, CPU time, I/O metrics, parallelism)
- 🚨 **Missing index detection** and optimization recommendations
- 📋 **Query categorization** and table dependency analysis
- ⚖️ **Query cost analysis** with costliest operation identification

## Quick Start

### 1. Install the Package

```bash
dotnet add package EFCore.QueryAnalyzer
```

### 2. Configure in Program.cs

```csharp
using EFCore.QueryAnalyzer.Extensions;

var builder = WebApplication.CreateBuilder(args);

// Add the query analyzer
builder.Services.AddEFCoreQueryAnalyzer(builder.Configuration);

// Configure your DbContext with the analyzer
builder.Services.AddDbContext<MyDbContext>((serviceProvider, options) =>
{
    options.UseSqlServer(connectionString)
           .AddQueryAnalyzer(serviceProvider);
});

var app = builder.Build();
```

### 3. Configure in appsettings.json

```json
{
  "QueryAnalyzer": {
    "ThresholdMilliseconds": 1000,
    "ApiEndpoint": "https://your-monitoring-platform.com/api/slow-queries",
    "ApiKey": "your-secret-api-key",
    "CaptureStackTrace": true,
    "EnableInDevelopment": true,
    "EnableInProduction": false
  }
}
```

That's it! The analyzer will now automatically monitor your queries and report slow ones to your API.

## Query Analysis Features

### 🔍 Execution Plan Analysis
The analyzer automatically captures and parses SQL Server execution plans to provide deep insights into query performance:

- **Row Count Analysis**: Compare estimated vs. actual row counts to identify cardinality estimation issues
- **I/O Metrics**: Track logical reads, physical reads, and page reads for storage performance analysis
- **CPU Utilization**: Monitor CPU time vs. elapsed time to identify processing bottlenecks
- **Parallelism Detection**: Identify parallel query execution and degree of parallelism
- **Cost Analysis**: Determine query cost and identify the most expensive operations

### 🗄️ Multi-Database Support
Supports execution plan capture and analysis across multiple database providers:

```csharp
builder.Services.AddEFCoreQueryAnalyzer(options =>
{
    options.DatabaseProvider = DatabaseProvider.SqlServer; // or Auto for detection
    options.CaptureExecutionPlan = true;
});
```

**Supported Providers:**
- **SQL Server**: Full execution plan analysis with detailed statistics
- **PostgreSQL**: Query plan capture and basic analysis
- **MySQL**: Performance metrics and query categorization  
- **Oracle**: Execution plan parsing and cost analysis
- **SQLite**: Query analysis with limited execution plan support
- **Auto**: Automatic provider detection based on connection string

### 🚨 Performance Optimization Insights

#### Missing Index Detection
```json
{
  "statistics": {
    "missingIndexes": [
      "CREATE INDEX IX_Users_Email ON Users (Email) INCLUDE (FirstName, LastName)",
      "CREATE INDEX IX_Orders_CustomerId_Date ON Orders (CustomerId, OrderDate)"
    ]
  }
}
```

#### Query Warnings
```json
{
  "statistics": {
    "warnings": [
      "CONVERT_ISSUE: Implicit conversion on Email column affecting performance",
      "CARDINALITY_ESTIMATE: Row count estimation may be inaccurate"
    ]
  }
}
```

#### Performance Metrics
```json
{
  "statistics": {
    "estimatedRows": 100,
    "actualRows": 50000,
    "cpuTimeMs": 1250.5,
    "elapsedTimeMs": 2100.8,
    "logicalReads": 15432,
    "physicalReads": 234,
    "hasParallelism": true,
    "degreeOfParallelism": 4,
    "estimatedCost": 45.67,
    "costliestOperation": "Clustered Index Scan on Users"
  }
}
```

## Configuration Options

| Option | Type | Default | Description |
|--------|------|---------|-------------|
| `ThresholdMilliseconds` | `double` | `1000` | Threshold in ms for considering a query as slow |
| `IsEnabled` | `bool` | `true` | Whether the analyzer is enabled |
| `CaptureStackTrace` | `bool` | `true` | Whether to capture stack traces for slow queries |
| `MaxStackTraceLines` | `int` | `10` | Maximum lines in captured stack traces |
| `MaxQueryLength` | `int` | `10000` | Maximum length of query text to store |
| `ApiEndpoint` | `string?` | `null` | HTTP endpoint for reporting slow queries |
| `ApiKey` | `string?` | `null` | API key for authentication |
| `ApiTimeoutMs` | `int` | `5000` | Timeout for API calls in milliseconds |
| `EnableInDevelopment` | `bool` | `true` | Enable reporting in development environment |
| `EnableInProduction` | `bool` | `false` | Enable reporting in production environment |
| `CaptureExecutionPlan` | `bool` | `true` | Whether to capture database execution plans for analysis |
| `DatabaseProvider` | `DatabaseProvider` | `Auto` | Database provider type (Auto, SqlServer, PostgreSQL, MySQL, Oracle, SQLite) |

## Usage Scenarios

### 1. Basic Configuration

```csharp
builder.Services.AddEFCoreQueryAnalyzer(options =>
{
    options.ThresholdMilliseconds = 500;
    options.ApiEndpoint = "https://your-api.com/slow-queries";
    options.ApiKey = "your-api-key";
});
```

### 2. HTTP Reporting with Custom Client

```csharp
builder.Services.AddEFCoreQueryAnalyzerWithHttp(
    options =>
    {
        options.ThresholdMilliseconds = 750;
        options.ApiEndpoint = "https://monitoring.company.com/api/queries";
        options.ApiKey = builder.Configuration["MonitoringApiKey"];
    },
    httpClient =>
    {
        httpClient.Timeout = TimeSpan.FromSeconds(10);
        httpClient.DefaultRequestHeaders.Add("X-App-Version", "1.0.0");
    });
```

### 3. In-Memory Reporting (Testing)

```csharp
builder.Services.AddEFCoreQueryAnalyzerWithInMemory(options =>
{
    options.ThresholdMilliseconds = 100;
    options.CaptureStackTrace = true;
});
```

### 4. Custom Reporting Service

```csharp
public class MyCustomReportingService : IQueryReportingService
{
    public async Task ReportSlowQueryAsync(QueryTrackingContext context, CancellationToken cancellationToken)
    {
        // Send to your preferred destination:
        // - Database, File System, Message Queue, etc.
        await SendToMyDestination(context);
    }
}

// Register it
builder.Services.AddEFCoreQueryAnalyzerWithCustomReporting<MyCustomReportingService>(options =>
{
    options.ThresholdMilliseconds = 500;
});
```

### 5. Inline Configuration (No DI)

```csharp
var options = new DbContextOptionsBuilder<MyDbContext>()
    .UseSqlServer(connectionString)
    .AddQueryAnalyzer(analyzerOptions =>
    {
        analyzerOptions.ThresholdMilliseconds = 500;
        analyzerOptions.ApiEndpoint = "https://your-api.com/slow-queries";
    })
    .Options;

using var context = new MyDbContext(options);
```

## Report Format

When a slow query is detected, the following comprehensive JSON is sent to your API endpoint:

```json
{
  "queryId": "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
  "rawQuery": "SELECT u.Id, u.Email, u.FirstName, u.LastName FROM Users u WHERE u.Email LIKE @p0 ORDER BY u.LastName",
  "parameters": {
    "@p0": "%john%"
  },
  "executionTimeMs": 1250.5,
  "stackTrace": [
    "at MyApp.Controllers.UserController.GetUsers() in UserController.cs:line 45",
    "at MyApp.Services.UserService.FindByEmail() in UserService.cs:line 23"
  ],
  "timestamp": "2025-09-03T10:30:00Z",
  "contextType": "MyApp.Data.ApplicationDbContext",
  "environment": "Development",
  "applicationName": "MyApplication",
  "version": "1.0.0",
  
  // New execution plan analysis fields
  "executionPlan": "<ShowPlanXML xmlns=\"http://schemas.microsoft.com/sqlserver/2004/07/showplan\">...</ShowPlanXML>",
  "databaseProvider": "SqlServer",
  "queryCategory": "SELECT",
  "tableNames": ["Users"],
  
  // Detailed performance statistics
  "statistics": {
    "estimatedRows": 150,
    "actualRows": 42,
    "cpuTimeMs": 890.2,
    "elapsedTimeMs": 1250.5,
    "logicalReads": 2847,
    "physicalReads": 45,
    "pageReads": 45,
    "hasParallelism": false,
    "degreeOfParallelism": null,
    "estimatedCost": 12.34,
    "costliestOperation": "Index Seek on IX_Users_Email",
    
    // Performance optimization recommendations
    "missingIndexes": [
      "CREATE NONCLUSTERED INDEX [IX_Users_Email_LastName] ON [dbo].[Users] ([Email]) INCLUDE ([FirstName], [LastName])"
    ],
    "warnings": [
      "CARDINALITY_ESTIMATE: Estimated rows (150) significantly differ from actual rows (42)"
    ]
  }
}
```

### Field Descriptions

**Basic Fields:**
- `queryId`: Unique identifier for the query execution
- `rawQuery`: The actual SQL query that was executed
- `parameters`: Parameter values passed to the query
- `executionTimeMs`: Total execution time in milliseconds
- `stackTrace`: Code location where the slow query originated

**Analysis Fields:**
- `executionPlan`: Raw XML execution plan (SQL Server) or JSON plan (PostgreSQL)
- `databaseProvider`: Detected database provider
- `queryCategory`: Query type (SELECT, INSERT, UPDATE, DELETE)
- `tableNames`: List of tables accessed by the query

**Performance Statistics:**
- `estimatedRows` vs `actualRows`: Query optimizer estimates vs reality
- `cpuTimeMs`: CPU processing time vs total elapsed time
- `logicalReads`: Pages read from buffer cache
- `physicalReads`: Pages read from disk storage
- `hasParallelism`: Whether query used parallel execution
- `estimatedCost`: Query optimizer's cost estimation
- `costliestOperation`: Most expensive operation in the execution plan
- `missingIndexes`: Recommended indexes to improve performance
- `warnings`: Performance warnings and optimization hints

## Environment Configuration

The package automatically detects the environment and applies the appropriate settings:

- **Development**: Reporting enabled by default, detailed stack traces
- **Production**: Reporting disabled by default for safety
- **Testing**: Use in-memory reporting for unit tests

## Best Practices

### 1. Production Safety
```csharp
builder.Services.AddEFCoreQueryAnalyzer(options =>
{
    options.EnableInProduction = builder.Environment.IsProduction() && 
                                builder.Configuration.GetValue<bool>("EnableQueryAnalyzer");
    options.ThresholdMilliseconds = builder.Environment.IsProduction() ? 2000 : 500;
});
```

### 2. Conditional Registration
```csharp
if (builder.Configuration.GetValue<bool>("Features:QueryAnalyzer"))
{
    builder.Services.AddEFCoreQueryAnalyzer(builder.Configuration);
}
```

### 3. Testing Setup
```csharp
// In your test setup
services.AddEFCoreQueryAnalyzerWithInMemory();

// In your tests
var reportingService = serviceProvider.GetService<IQueryReportingService>() as InMemoryQueryReportingService;
var reports = reportingService?.GetReports();
Assert.True(reports.Any(r => r.ExecutionTimeMs > expectedThreshold));
```

## Advanced Scenarios

### Composite Reporting Service

Use the built-in `CompositeQueryReportingService` to send reports to multiple destinations simultaneously:

```csharp
// Register multiple reporting services
builder.Services.AddEFCoreQueryAnalyzer(options =>
{
    options.ThresholdMilliseconds = 500;
});

// Add individual reporting services
builder.Services.AddHttpClient<HttpQueryReportingService>();
builder.Services.AddTransient<IQueryReportingService, HttpQueryReportingService>();
builder.Services.AddTransient<IQueryReportingService, InMemoryQueryReportingService>();

// The composite service will automatically use all registered IQueryReportingService instances
builder.Services.AddTransient<CompositeQueryReportingService>();
```

### Custom Analysis with Performance Statistics

```csharp
public class PerformanceAnalysisService : IQueryReportingService
{
    public async Task ReportSlowQueryAsync(QueryTrackingContext context, CancellationToken cancellationToken)
    {
        var statistics = context.Statistics;
        if (statistics == null) return;

        // Analyze cardinality estimation issues
        if (statistics.EstimatedRows.HasValue && statistics.ActualRows.HasValue)
        {
            var estimationRatio = (double)statistics.ActualRows.Value / statistics.EstimatedRows.Value;
            if (estimationRatio > 10 || estimationRatio < 0.1)
            {
                await AlertCardinalityIssue(context, estimationRatio);
            }
        }

        // Detect I/O intensive queries
        if (statistics.PhysicalReads > 1000)
        {
            await AlertHighPhysicalReads(context);
        }

        // Check for missing indexes
        if (statistics.MissingIndexes?.Any() == true)
        {
            await RecommendIndexes(context, statistics.MissingIndexes);
        }

        // Analyze parallelism efficiency
        if (statistics.HasParallelism && statistics.CpuTimeMs.HasValue && statistics.ElapsedTimeMs.HasValue)
        {
            var parallelismEfficiency = statistics.CpuTimeMs.Value / statistics.ElapsedTimeMs.Value;
            if (parallelismEfficiency < 2) // Low parallel efficiency
            {
                await AlertParallelismInefficiency(context, parallelismEfficiency);
            }
        }
    }
}
```

### Database-Specific Configuration

```csharp
// SQL Server with detailed analysis
builder.Services.AddEFCoreQueryAnalyzer(options =>
{
    options.DatabaseProvider = DatabaseProvider.SqlServer;
    options.CaptureExecutionPlan = true;
    options.ThresholdMilliseconds = 1000;
});

// PostgreSQL with query plan capture
builder.Services.AddEFCoreQueryAnalyzer(options =>
{
    options.DatabaseProvider = DatabaseProvider.PostgreSQL;
    options.CaptureExecutionPlan = true;
    options.ThresholdMilliseconds = 800;
});

// Multi-database environment with auto-detection
builder.Services.AddEFCoreQueryAnalyzer(options =>
{
    options.DatabaseProvider = DatabaseProvider.Auto;
    options.CaptureExecutionPlan = true;
});
```

### Multiple Reporting Destinations

```csharp
public class MultiDestinationReportingService : IQueryReportingService
{
    private readonly ILogger _logger;
    private readonly HttpClient _httpClient;
    private readonly IMessageQueue _messageQueue;

    public async Task ReportSlowQueryAsync(QueryTrackingContext context, CancellationToken cancellationToken)
    {
        // Send to multiple destinations in parallel
        await Task.WhenAll(
            SendToApi(context),
            SendToQueue(context),
            LogToFile(context),
            AnalyzePerformance(context) // New analysis method
        );
    }

    private async Task AnalyzePerformance(QueryTrackingContext context)
    {
        if (context.Statistics?.MissingIndexes?.Any() == true)
        {
            await NotifyDBA(context);
        }
    }
}
```

### Conditional Reporting

```csharp
public class SmartReportingService : IQueryReportingService
{
    public async Task ReportSlowQueryAsync(QueryTrackingContext context, CancellationToken cancellationToken)
    {
        // Only report queries from specific contexts
        if (context.ContextType.Contains("CriticalDbContext"))
        {
            await _urgentReporter.ReportAsync(context);
        }
        
        // Different thresholds for different operations
        if (context.CommandText.Contains("SELECT") && context.ExecutionTime.TotalSeconds > 5)
        {
            await _selectQueryReporter.ReportAsync(context);
        }
    }
}
```

## Performance Optimization Guide

### 🔍 Understanding Query Statistics

The query analyzer provides detailed metrics to help you identify and resolve performance bottlenecks:

#### Cardinality Estimation Issues
```csharp
// Red flag: Large discrepancy between estimated and actual rows
if (statistics.EstimatedRows == 100 && statistics.ActualRows == 50000)
{
    // Indicates outdated statistics or poor query plan
    // Solution: UPDATE STATISTICS, consider query hints, or rewrite query
}
```

#### I/O Performance Analysis  
```csharp
// High physical reads indicate disk I/O bottleneck
if (statistics.PhysicalReads > statistics.LogicalReads * 0.1)
{
    // Solutions: Add missing indexes, increase buffer cache, or optimize storage
}

// High logical reads may indicate inefficient query plans
if (statistics.LogicalReads > statistics.ActualRows * 10)
{
    // Solutions: Add covering indexes or rewrite query logic
}
```

#### CPU vs I/O Bound Analysis
```csharp
var cpuRatio = statistics.CpuTimeMs / statistics.ElapsedTimeMs;

if (cpuRatio > 0.8)
{
    // CPU-bound query: Focus on query logic optimization
}
else if (cpuRatio < 0.3)  
{
    // I/O-bound query: Focus on index optimization
}
```

### 🚨 Automated Performance Alerts

Create intelligent alerting based on execution statistics:

```csharp
public class PerformanceAlertService : IQueryReportingService
{
    public async Task ReportSlowQueryAsync(QueryTrackingContext context, CancellationToken cancellationToken)
    {
        var alerts = new List<string>();
        
        // Critical performance issues
        if (context.Statistics?.ActualRows > context.Statistics?.EstimatedRows * 100)
        {
            alerts.Add("CRITICAL: Severe cardinality estimation error detected");
        }
        
        if (context.Statistics?.PhysicalReads > 10000)
        {
            alerts.Add("WARNING: Excessive disk I/O detected");
        }
        
        if (context.Statistics?.MissingIndexes?.Any() == true)
        {
            alerts.Add($"RECOMMENDATION: {context.Statistics.MissingIndexes.Length} missing indexes detected");
        }
        
        // Send alerts to monitoring system
        await SendAlertsToMonitoring(context, alerts);
    }
}
```

### 📋 Index Optimization Workflow

1. **Identify Missing Indexes**: Use the `MissingIndexes` array from query statistics
2. **Analyze Impact**: Check `EstimatedCost` reduction potential
3. **Validate Recommendations**: Test indexes on non-production environments
4. **Monitor Results**: Compare before/after performance metrics

```sql
-- Example of implementing recommended indexes
CREATE NONCLUSTERED INDEX [IX_Users_Email_LastName] 
ON [dbo].[Users] ([Email]) 
INCLUDE ([FirstName], [LastName]);

-- Monitor improvement
SELECT 
    logical_reads_before,
    logical_reads_after,
    (logical_reads_before - logical_reads_after) * 100.0 / logical_reads_before AS improvement_percent
FROM performance_comparison;
```

### 🔄 Query Optimization Patterns

#### Pattern 1: High Logical Reads with Low Row Count
```sql
-- Problem: Index scan instead of seek
SELECT * FROM Orders WHERE CustomerId = @CustomerId

-- Solution: Ensure proper indexing
CREATE INDEX IX_Orders_CustomerId ON Orders (CustomerId)
```

#### Pattern 2: Parallelism with Low Efficiency  
```sql
-- Problem: Small result set with parallel execution overhead
SELECT TOP 10 * FROM LargeTable ORDER BY Date DESC

-- Solution: Use appropriate hints or rewrite query
SELECT TOP 10 * FROM LargeTable WITH (INDEX(IX_Date_DESC)) ORDER BY Date DESC
```

### 📊 Performance Monitoring Dashboard

Create a monitoring dashboard using the captured statistics:

```csharp
public class PerformanceDashboard
{
    public async Task<DashboardData> GetPerformanceMetrics(TimeSpan period)
    {
        var reports = await GetReportsFromPeriod(period);
        
        return new DashboardData
        {
            AverageExecutionTime = reports.Average(r => r.ExecutionTimeMs),
            QueriesWithCardinalityIssues = reports.Count(r => HasCardinalityIssue(r.Statistics)),
            TopIOIntensiveQueries = reports.OrderByDescending(r => r.Statistics?.LogicalReads).Take(10),
            MostRecommendedIndexes = GetMostRecommendedIndexes(reports),
            DatabaseProviderBreakdown = reports.GroupBy(r => r.DatabaseProvider)
        };
    }
}
```

## Troubleshooting

### Common Issues

1. **No reports being sent**: Check `EnableInDevelopment`/`EnableInProduction` settings
2. **API authentication errors**: Verify `ApiKey` configuration
3. **Missing stack traces**: Ensure `CaptureStackTrace` is enabled
4. **Performance impact**: Adjust `ThresholdMilliseconds` or disable in high-load scenarios

### Logging

Enable detailed logging to troubleshoot issues:

```json
{
  "Logging": {
    "LogLevel": {
      "EFCore.QueryAnalyzer": "Debug"
    }
  }
}
```

## Requirements

- .NET 6.0 or higher
- Entity Framework Core 6.0 or higher
- ASP.NET Core (for dependency injection scenarios)

## License

MIT License - see LICENSE file for details.

## Contributing

Contributions are welcome! Please see CONTRIBUTING.md for guidelines.

---

## Sample appsettings.json

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=localhost;Database=MyApp;Trusted_Connection=true;"
  },
  "QueryAnalyzer": {
    "ThresholdMilliseconds": 1000,
    "ApiEndpoint": "https://monitoring.mycompany.com/api/slow-queries",
    "ApiKey": "sk-1234567890abcdef",
    "CaptureStackTrace": true,
    "MaxStackTraceLines": 15,
    "MaxQueryLength": 5000,
    "ApiTimeoutMs": 10000,
    "EnableInDevelopment": true,
    "EnableInProduction": false,
    "CaptureExecutionPlan": true,
    "DatabaseProvider": "Auto"
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "EFCore.QueryAnalyzer": "Information"
    }
  }
}
```

## Sample appsettings.Production.json

```json
{
  "QueryAnalyzer": {
    "ThresholdMilliseconds": 2000,
    "CaptureStackTrace": false,
    "EnableInProduction": true,
    "ApiTimeoutMs": 5000,
    "CaptureExecutionPlan": true,
    "DatabaseProvider": "SqlServer"
  }
}
```

## Database-Specific Configuration Examples

### SQL Server with Full Analysis
```json
{
  "QueryAnalyzer": {
    "DatabaseProvider": "SqlServer",
    "CaptureExecutionPlan": true,
    "ThresholdMilliseconds": 1000
  }
}
```

### PostgreSQL Configuration
```json
{
  "QueryAnalyzer": {
    "DatabaseProvider": "PostgreSQL", 
    "CaptureExecutionPlan": true,
    "ThresholdMilliseconds": 800
  }
}
```

### Multi-Database Environment
```json
{
  "QueryAnalyzer": {
    "DatabaseProvider": "Auto",
    "CaptureExecutionPlan": true,
    "ThresholdMilliseconds": 1200
  }
}
```
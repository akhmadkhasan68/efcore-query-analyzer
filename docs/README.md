# EFCore.QueryAnalyzer

A high-performance, production-ready NuGet package for monitoring Entity Framework Core query performance with automatic slow query detection, execution plan analysis, and flexible reporting capabilities.

![NuGet Version](https://img.shields.io/nuget/v/EFCore.QueryAnalyzer)
![NuGet Downloads](https://img.shields.io/nuget/dt/EFCore.QueryAnalyzer)
![.NET](https://img.shields.io/badge/.NET-6%2B-blue)
![EF Core](https://img.shields.io/badge/EF%20Core-6%2B-green)

## 🚀 Features

- **🔍 Real-time Query Monitoring** - Automatically tracks all EF Core queries with minimal overhead
- **📊 Execution Plan Analysis** - Captures and analyzes SQL Server execution plans for performance insights
- **🎯 Configurable Thresholds** - Set custom slow query detection thresholds per environment
- **🌐 Multiple Reporting Strategies** - HTTP API, In-Memory, File, and Custom reporting services
- **🔧 Environment-aware Configuration** - Different settings for Development vs Production
- **🧵 Thread-safe Operation** - Concurrent query tracking using `ConcurrentDictionary`
- **🔍 Stack Trace Capture** - Identify problematic code locations with filtered stack traces
- **🗄️ Multi-database Support** - Works with SQL Server, PostgreSQL, MySQL, Oracle, and SQLite
- **⚡ Minimal Performance Impact** - Designed for production environments with optimized overhead

## 📦 Installation

Install the package via NuGet Package Manager:

```bash
dotnet add package EFCore.QueryAnalyzer
```

Or via Package Manager Console:

```powershell
Install-Package EFCore.QueryAnalyzer
```

## 🚀 Quick Start

### 1. Basic Setup with Dependency Injection

```csharp
using EFCore.QueryAnalyzer.Extensions;

var builder = WebApplication.CreateBuilder(args);

// Add the query analyzer with configuration
builder.Services.AddEFCoreQueryAnalyzer(builder.Configuration);

// Configure your DbContext with the analyzer
builder.Services.AddDbContext<MyDbContext>((serviceProvider, options) =>
{
    options.UseSqlServer(connectionString)
           .AddQueryAnalyzer(serviceProvider);
});

var app = builder.Build();
```

### 2. Configuration in appsettings.json

```json
{
  "QueryAnalyzer": {
    "ThresholdMilliseconds": 1000,
    "ApiEndpoint": "https://your-monitoring-api.com/slow-queries",
    "ApiKey": "your-secret-api-key",
    "CaptureStackTrace": true,
    "CaptureExecutionPlan": true,
    "EnableInDevelopment": true,
    "EnableInProduction": false
  }
}
```

That's it! The analyzer will now monitor your queries and report slow ones automatically.

## ⚙️ Configuration Options

| Option | Type | Default | Description |
|--------|------|---------|-------------|
| `ThresholdMilliseconds` | `double` | `1000` | Threshold in milliseconds for slow query detection |
| `IsEnabled` | `bool` | `true` | Whether the analyzer is enabled |
| `CaptureStackTrace` | `bool` | `true` | Capture stack traces for slow queries |
| `CaptureExecutionPlan` | `bool` | `false` | Capture database execution plans |
| `MaxStackTraceLines` | `int` | `10` | Maximum lines in captured stack traces |
| `MaxQueryLength` | `int` | `10000` | Maximum query text length to store |
| `ApiEndpoint` | `string?` | `null` | HTTP endpoint for reporting slow queries |
| `ApiKey` | `string?` | `null` | API key for authentication |
| `ApiTimeoutMs` | `int` | `5000` | API request timeout in milliseconds |
| `EnableInDevelopment` | `bool` | `true` | Enable reporting in development |
| `EnableInProduction` | `bool` | `false` | Enable reporting in production |
| `DatabaseProvider` | `DatabaseProvider` | `Auto` | Database provider for execution plans |
| `ExecutionPlanTimeoutSeconds` | `int` | `30` | Timeout for execution plan capture |

## 📋 Usage Scenarios

### 1. HTTP API Reporting (Production)

```csharp
builder.Services.AddEFCoreQueryAnalyzerWithHttp(
    options =>
    {
        options.ThresholdMilliseconds = 500;
        options.ApiEndpoint = "https://monitoring.company.com/api/queries";
        options.ApiKey = builder.Configuration["MonitoringApiKey"];
        options.EnableInProduction = true;
    },
    httpClient =>
    {
        httpClient.Timeout = TimeSpan.FromSeconds(10);
        httpClient.DefaultRequestHeaders.Add("X-App-Version", "1.0.0");
    });
```

### 2. In-Memory Reporting (Testing/Development)

```csharp
builder.Services.AddEFCoreQueryAnalyzerWithInMemory(options =>
{
    options.ThresholdMilliseconds = 100;
    options.CaptureStackTrace = true;
    options.CaptureExecutionPlan = true;
});

// In tests, access the reports
var reportingService = serviceProvider.GetService<IQueryReportingService>() 
    as InMemoryQueryReportingService;
var reports = reportingService?.GetReports();
```

### 3. Custom Reporting Service

```csharp
public class DatabaseReportingService : IQueryReportingService
{
    public async Task ReportSlowQueryAsync(QueryTrackingContext context, 
        CancellationToken cancellationToken = default)
    {
        // Store in database, send to message queue, etc.
        await SaveToDatabase(context);
    }
}

// Register custom service
builder.Services.AddEFCoreQueryAnalyzerWithCustomReporting<DatabaseReportingService>(
    options => options.ThresholdMilliseconds = 750);
```

### 4. Inline Configuration (No DI)

```csharp
var options = new DbContextOptionsBuilder<MyDbContext>()
    .UseSqlServer(connectionString)
    .AddQueryAnalyzer(analyzerOptions =>
    {
        analyzerOptions.ThresholdMilliseconds = 500;
        analyzerOptions.CaptureExecutionPlan = true;
    })
    .Options;

using var context = new MyDbContext(options);
```

## 📊 Report Format

When a slow query is detected, a comprehensive JSON report is generated:

```json
{
  "queryId": "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
  "rawQuery": "SELECT u.Id, u.Email FROM Users u WHERE u.Email LIKE @p0",
  "parameters": {
    "@p0": "%john%"
  },
  "executionTimeMs": 1250.5,
  "stackTrace": [
    "at MyApp.Controllers.UserController.GetUsers() in UserController.cs:line 45",
    "at MyApp.Services.UserService.FindByEmail() in UserService.cs:line 23"
  ],
  "timestamp": "2024-01-15T10:30:00Z",
  "contextType": "MyApp.Data.ApplicationDbContext",
  "environment": "Development",
  "applicationName": "MyApplication",
  "version": "1.0.0",
  "executionPlan": {
    "databaseProvider": "SqlServer",
    "planFormat": {
      "contentType": "application/xml",
      "fileExtension": ".sqlplan",
      "description": "SQL Server XML Execution Plan"
    },
    "content": "<ShowPlanXML xmlns=\"...\">...</ShowPlanXML>"
  }
}
```

## 🔍 Execution Plan Analysis

The analyzer can capture and analyze database execution plans for deep performance insights:

### SQL Server Support

```csharp
builder.Services.AddEFCoreQueryAnalyzer(options =>
{
    options.DatabaseProvider = DatabaseProvider.SqlServer;
    options.CaptureExecutionPlan = true;
    options.ExecutionPlanTimeoutSeconds = 30;
});
```

### Multi-Database Support

```csharp
// Auto-detect database provider
options.DatabaseProvider = DatabaseProvider.Auto;

// Or specify explicitly
options.DatabaseProvider = DatabaseProvider.PostgreSQL; // MySQL, Oracle, SQLite
```

## 🌍 Environment Configuration

The analyzer automatically detects environments and applies appropriate settings:

### Development Environment
- Reporting enabled by default
- Lower thresholds for early detection
- Detailed stack traces captured
- Execution plan analysis enabled

### Production Environment
- Reporting disabled by default for safety
- Higher thresholds to reduce noise
- Minimal overhead configuration
- Optional stack trace capture

### Environment-specific Configuration

```csharp
builder.Services.AddEFCoreQueryAnalyzer(options =>
{
    if (builder.Environment.IsDevelopment())
    {
        options.ThresholdMilliseconds = 100;
        options.CaptureStackTrace = true;
        options.CaptureExecutionPlan = true;
    }
    else
    {
        options.ThresholdMilliseconds = 2000;
        options.CaptureStackTrace = false;
        options.EnableInProduction = builder.Configuration
            .GetValue<bool>("EnableQueryAnalyzerInProduction");
    }
});
```

## 🏗️ Architecture Overview

### Core Components

1. **QueryPerformanceInterceptor** - EF Core interceptor that hooks into query execution
2. **IQueryReportingService** - Interface for pluggable reporting strategies
3. **QueryTrackingContext** - Thread-safe context for tracking individual queries
4. **ServiceCollectionExtensions** - DI registration and configuration helpers

### Thread Safety

- Uses `ConcurrentDictionary<Guid, QueryTrackingContext>` for active query tracking
- Non-blocking interceptor design prevents impact on query execution
- Async reporting to avoid blocking the main execution thread

### Memory Management

- Automatic cleanup of completed query contexts
- Configurable limits on query text length and stack trace depth
- Efficient correlation of query start/end events using ConnectionId + CommandId

## 🔧 Advanced Usage

### Composite Reporting

Send reports to multiple destinations simultaneously:

```csharp
builder.Services.AddEFCoreQueryAnalyzer(options => { });
builder.Services.AddHttpClient<HttpQueryReportingService>();
builder.Services.AddTransient<IQueryReportingService, HttpQueryReportingService>();
builder.Services.AddTransient<IQueryReportingService, InMemoryQueryReportingService>();
```

### Conditional Reporting

```csharp
public class SmartReportingService : IQueryReportingService
{
    public async Task ReportSlowQueryAsync(QueryTrackingContext context, 
        CancellationToken cancellationToken)
    {
        // Only report critical context queries
        if (context.ContextType.Contains("CriticalDbContext"))
        {
            await _urgentReporter.ReportAsync(context);
        }
        
        // Different handling for different query types
        if (context.CommandText.Contains("SELECT") && 
            context.ExecutionTime.TotalSeconds > 5)
        {
            await _selectQueryReporter.ReportAsync(context);
        }
    }
}
```

### Performance Analysis

```csharp
public class PerformanceAnalysisService : IQueryReportingService
{
    public async Task ReportSlowQueryAsync(QueryTrackingContext context, 
        CancellationToken cancellationToken)
    {
        // Analyze execution plan if available
        if (context.ExecutionPlan?.Content != null)
        {
            var analysis = await AnalyzeExecutionPlan(context.ExecutionPlan);
            await RecommendOptimizations(context, analysis);
        }
        
        // Pattern-based analysis
        if (context.CommandText.Contains("SELECT *"))
        {
            await AlertSelectStarUsage(context);
        }
    }
}
```

## 🚨 Best Practices

### 1. Production Safety

```csharp
// Safe production configuration
builder.Services.AddEFCoreQueryAnalyzer(options =>
{
    options.EnableInProduction = builder.Environment.IsProduction() && 
                               builder.Configuration.GetValue<bool>("Features:QueryAnalyzer");
    options.ThresholdMilliseconds = builder.Environment.IsProduction() ? 2000 : 500;
    options.CaptureStackTrace = !builder.Environment.IsProduction();
});
```

### 2. Resource Management

```csharp
options.MaxQueryLength = 5000;          // Limit memory usage
options.MaxStackTraceLines = 5;         // Reduce overhead
options.ExecutionPlanTimeoutSeconds = 15; // Prevent hanging
```

### 3. Environment Detection

```csharp
// Use environment-specific settings
options.EnableInDevelopment = true;   // Debug in development
options.EnableInProduction = false;   // Opt-in for production
```

### 4. Testing Integration

```csharp
// Test setup
services.AddEFCoreQueryAnalyzerWithInMemory(options =>
{
    options.ThresholdMilliseconds = 1; // Capture all queries in tests
});

// Test assertions
var reports = inMemoryReporter.GetReports();
Assert.Contains(reports, r => r.RawQuery.Contains("Users"));
```

## 🐛 Troubleshooting

### Common Issues

| Issue | Cause | Solution |
|-------|--------|----------|
| No reports generated | Reporting disabled for environment | Check `EnableInDevelopment`/`EnableInProduction` |
| API authentication errors | Invalid API key | Verify `ApiKey` configuration |
| Missing stack traces | Stack trace capture disabled | Set `CaptureStackTrace = true` |
| High memory usage | Large query texts/stack traces | Reduce `MaxQueryLength` and `MaxStackTraceLines` |
| Execution plan timeouts | Database connection issues | Increase `ExecutionPlanTimeoutSeconds` |

### Debug Logging

Enable detailed logging to diagnose issues:

```json
{
  "Logging": {
    "LogLevel": {
      "EFCore.QueryAnalyzer": "Debug",
      "EFCore.QueryAnalyzer.Core.QueryPerformanceInterceptor": "Trace"
    }
  }
}
```

## 📄 Requirements

- **.NET 6.0** or higher
- **Entity Framework Core 6.0** or higher
- **ASP.NET Core** (for dependency injection scenarios)

## 📝 Sample Configuration Files

### Development Configuration

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=localhost;Database=MyApp;Trusted_Connection=true;"
  },
  "QueryAnalyzer": {
    "ThresholdMilliseconds": 100,
    "CaptureStackTrace": true,
    "CaptureExecutionPlan": true,
    "EnableInDevelopment": true,
    "DatabaseProvider": "SqlServer"
  },
  "Logging": {
    "LogLevel": {
      "EFCore.QueryAnalyzer": "Information"
    }
  }
}
```

### Production Configuration

```json
{
  "QueryAnalyzer": {
    "ThresholdMilliseconds": 2000,
    "ApiEndpoint": "https://monitoring.company.com/api/slow-queries",
    "ApiKey": "prod-api-key-here",
    "CaptureStackTrace": false,
    "EnableInProduction": true,
    "ApiTimeoutMs": 5000,
    "DatabaseProvider": "SqlServer"
  }
}
```

## 🤝 Contributing

Contributions are welcome! Please read our [Contributing Guidelines](CONTRIBUTING.md) and submit pull requests to our [GitHub repository](https://github.com/akhmadkhasan68/efcore-query-analyzer).

## 📜 License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

## 📞 Support

- **GitHub Issues**: [Report bugs or request features](https://github.com/akhmadkhasan68/efcore-query-analyzer/issues)
- **Documentation**: [Full documentation and examples](https://github.com/akhmadkhasan68/efcore-query-analyzer/wiki)
- **NuGet Package**: [EFCore.QueryAnalyzer](https://www.nuget.org/packages/EFCore.QueryAnalyzer/)

---

**Made with ❤️ for the .NET community**
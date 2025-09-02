# EF Core Query Analyzer

A high-performance, production-ready NuGet package for monitoring Entity Framework Core query performance and automatically reporting slow queries to your analytics platform.

## Features

- 🚀 **Zero-configuration** startup with sensible defaults
- 📊 **Real-time monitoring** of all EF Core queries
- 🎯 **Configurable thresholds** for slow query detection
- 🔍 **Stack trace capture** to identify problematic code
- 🌐 **HTTP API integration** for centralized monitoring
- 🏗️ **Multiple reporting strategies** (HTTP, In-Memory, Custom)
- 🔧 **Environment-aware** configuration (Dev/Prod)
- ⚡ **Minimal performance overhead**
- 🧪 **Built-in testing support**

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

When a slow query is detected, the following JSON is sent to your API endpoint:

```json
{
  "queryId": "a1b2c3d4",
  "rawQuery": "SELECT u.Id, u.Email FROM Users u WHERE u.Email LIKE @p0",
  "parameters": {
    "@p0": "%test%"
  },
  "executionTimeMs": 1250.5,
  "stackTrace": "at MyApp.Controllers.UserController.GetUsers() in UserController.cs:line 45\nat MyApp.Services.UserService.FindByEmail() in UserService.cs:line 23",
  "timestamp": "2025-09-02T10:30:00Z",
  "contextType": "MyApp.Data.ApplicationDbContext",
  "environment": "Development",
  "applicationName": "MyApplication",
  "version": "1.0.0"
}
```

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
            LogToFile(context)
        );
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
    "EnableInProduction": false
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
    "ApiTimeoutMs": 5000
  }
}
```
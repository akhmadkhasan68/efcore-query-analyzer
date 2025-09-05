using EFCore.QueryAnalyzer.Core.Models;

namespace EFCore.QueryAnalyzer.Core
{
    public interface IQueryReportingService
    {
        Task ReportSlowQueryAsync(QueryTrackingContext context, CancellationToken cancellationToken = default);
    }
}
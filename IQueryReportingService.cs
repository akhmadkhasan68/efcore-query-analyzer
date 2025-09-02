namespace EFCore.QueryAnalyzer
{
    public interface IQueryReportingService
    {
        Task ReportSlowQueryAsync(QueryTrackingContext context, CancellationToken cancellationToken = default);
    }
}
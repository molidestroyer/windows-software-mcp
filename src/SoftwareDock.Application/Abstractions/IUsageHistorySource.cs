using SoftwareDock.Domain.Usage;

namespace SoftwareDock.Application.Abstractions;

/// <summary>Port: when the user last started each program.</summary>
public interface IUsageHistorySource
{
    IReadOnlyList<UsageRecord> GetUsage();
}

using SoftwareDock.Domain.Operations;

namespace SoftwareDock.Application.Operations;

/// <summary>Progress of a batch: which item is running and what its installer last printed.</summary>
public sealed record BatchProgress(int Position, int Total, string ItemName, string? Line = null);

/// <summary>How one item of a batch ended. <see cref="Result"/> is <see langword="null"/> when it never ran.</summary>
public sealed record BatchItemResult(string Name, OperationResult? Result)
{
    public bool WasSkipped => Result is null;
}

public sealed record BatchResult(IReadOnlyList<BatchItemResult> Items)
{
    public int Succeeded => Items.Count(i => i.Result?.IsSuccess == true);

    public int Failed => Items.Count(i => i.Result is { IsSuccess: false, Status: not OperationStatus.Cancelled });

    public int Skipped => Items.Count(i => i.WasSkipped || i.Result?.Status == OperationStatus.Cancelled);

    public bool RebootRequired => Items.Any(i => i.Result?.Status == OperationStatus.SucceededRebootRequired);

    public string Summary
    {
        get
        {
            var parts = new List<string> { $"{Succeeded} of {Items.Count} done" };
            if (Failed > 0)
            {
                parts.Add($"{Failed} failed");
            }

            if (Skipped > 0)
            {
                parts.Add($"{Skipped} skipped");
            }

            if (RebootRequired)
            {
                parts.Add("restart Windows to finish");
            }

            return string.Join(" · ", parts) + ".";
        }
    }
}

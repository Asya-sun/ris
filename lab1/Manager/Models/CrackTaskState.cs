namespace Manager.Models;

public class CrackTaskState
{
    // mb lock needed
    public Guid RequestId { get; init; }

    required public string Hash { get; init; }

    public long TotalCombinations { get; set; }

    public long CheckedCombinations { get; set; }

    public List<string> FoundWords { get; set; } = new();

    public CrackStatus Status { get; set; } = CrackStatus.IN_PROGRESS;
}
namespace Manager.Models;

public class CrackTaskState
{
    // mb lock needed
    public Guid RequestId { get; init; }

    required public string Hash { get; init; }

    public double TotalCombinations { get; set; }

    public int MaxLength { get; set; }

    public double CheckedCombinations { get; set; }

    public List<string> FoundWords { get; set; } = new();

    public CrackStatus Status { get; set; } = CrackStatus.IN_PROGRESS;

    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}
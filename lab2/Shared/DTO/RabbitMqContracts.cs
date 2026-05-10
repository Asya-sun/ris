namespace Shared.DTO;

public class RabbitTaskMessage
{
    public Guid RequestId { get; set; }
    public Guid SubTaskId { get; set; }
    public string Hash { get; set; } = string.Empty;
    public int MaxLength { get; set; }
    public double StartIndex { get; set; }
    public double EndIndex { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class RabbitProgressMessage
{
    public Guid RequestId { get; set; }
    public Guid SubTaskId { get; set; }
    public Guid WorkerId { get; set; }
    public double CurrentIndex { get; set; }
    public List<string> FoundWords { get; set; } = new();
    public bool IsCompleted { get; set; }
    public DateTime Timestamp { get; set; }
}


public class DeadLetterMessage
{
    public ulong DeliveryTag { get; set; }
    public string Content { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public DateTime ReceivedAt { get; set; }
}
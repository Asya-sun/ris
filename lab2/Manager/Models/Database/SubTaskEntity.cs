using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using Manager.Models;

namespace Manager.Models.Database;

public class SubTaskEntity
{
    public Guid SubTaskId { get; set; } = Guid.NewGuid();

    [BsonElement("startIndex")]
    public long StartIndex { get; set; }

    [BsonElement("endIndex")]
    public long EndIndex { get; set; }

    [BsonElement("currentIndex")]
    public long CurrentIndex { get; set; }

    [BsonElement("workerId")]
    public Guid? WorkerId { get; set; }

    [BsonElement("status")]
    [BsonRepresentation(BsonType.String)]
    public SubTaskStatus Status { get; set; } = SubTaskStatus.PENDING;

    [BsonElement("retryCount")]
    public int RetryCount { get; set; }

    [BsonElement("startedAt")]
    public DateTime? StartedAt { get; set; }

    [BsonElement("lastHeartbeat")]
    public DateTime? LastHeartbeat { get; set; }

    [BsonElement("completedAt")]
    public DateTime? CompletedAt { get; set; }
}
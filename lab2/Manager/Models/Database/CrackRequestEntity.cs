using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using Manager.Models;

namespace Manager.Models.Database;

public class CrackRequestEntity
{
    [BsonId]
    public Guid RequestId { get; set; }

    [BsonElement("hash")]
    public string Hash { get; set; } = string.Empty;

    [BsonElement("maxLength")]
    public int MaxLength { get; set; }

    [BsonElement("status")]
    [BsonRepresentation(BsonType.String)]
    public CrackStatus Status { get; set; } = CrackStatus.PENDING;

    [BsonElement("progress")]
    public int Progress { get; set; }

    [BsonElement("foundWords")]
    public List<string> FoundWords { get; set; } = new();

    [BsonElement("totalCombinations")]
    public long TotalCombinations { get; set; }

    [BsonElement("checkedCombinations")]
    public long CheckedCombinations { get; set; }

    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; }

    [BsonElement("startedAt")]
    public DateTime? StartedAt { get; set; }

    [BsonElement("completedAt")]
    public DateTime? CompletedAt { get; set; }

    [BsonElement("lastUpdatedAt")]
    public DateTime? LastUpdatedAt { get; set; }

    [BsonElement("subTasks")]
    public List<SubTaskEntity> SubTasks { get; set; } = new();

    [BsonElement("errorMessage")]
    public string? ErrorMessage { get; set; }

    [BsonElement("version")]
    public long Version { get; set; } = 0;
}
namespace Worker.Models;

public class WorkerConfig
{
    public string WorkerName { get; init; } = "unknown name";
    public string Alphabet { get; init; } = "abcdefghijklmnopqrstuvwxyz0123456789";
    public Guid WorkerId { get; set; }
    public string StopWord { get; set; } = "bom";
}
namespace Manager.Models;

public class WorkerInfo
{
    // mb it would be better to make some 
    public Guid WorkerId { get; init; }

    // I'm sure it this field is really needed
    public string WorkerName { get; init; } = "unknown worker";

    public string Url { get; init; } = "http://worker"; // ?

}
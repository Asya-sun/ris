namespace Manager.Models;

public class ManagerConfig
{
    public int WorkerNumber { get; set; } = 3;
    public string Alphabet { get; init; } = "abcdefghijklmnopqrstuvwxyz0123456789";

    // it would be default for now
    public string WorkerUrl { get; set; } = "http://worker";
}
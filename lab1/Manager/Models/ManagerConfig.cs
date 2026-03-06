namespace Manager.Models;

public class ManagerConfig
{
    public int WorkerNumber { get; set; } = 3;
    public string Alphabet { get; init; } = "abcdefghijklmnopqrstuvwxyz0123456789";
}
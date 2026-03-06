using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using Shared.DTO;
using Worker.Models;
using System.Text;
using System.Security.Cryptography;

namespace Worker.Services;

public class HashCrackService : IHashCrackService
{
    // private readonly  WorkerConfig _config;
    private readonly char[] _alphabet;
    private readonly ILogger<HashCrackService> _logger;
    private readonly HttpClient _httpClient;

    private const int REPORT_INTERVAL = 10000;

    public HashCrackService (
        IOptions<WorkerConfig> config,
        ILogger<HashCrackService> logger,
        HttpClient httpClient)
    {
        // _config = config.Value;
        _alphabet = config.Value.Alphabet.ToCharArray();
        _logger = logger;
        _httpClient = httpClient;
    }

    public void StartTask(WorkerTaskRequest request)
    {
        Task.Run(() => ProcessTask(request));
    }

    private async Task ProcessTask(WorkerTaskRequest request)
    {
        var found = new List<string>();
        long checkedCount = 0;
        long startIndex = request.StartIndex;
        long endIndex = 0;

        
        for (long index = request.StartIndex; index <= request.EndIndex; index++)
        {
            var word = IndexToWord(index, request.MaxLength);
            checkedCount++;

            if (CalculateMD5(word) == request.Hash)
            {
                found.Add(word);
            }

            if (checkedCount % REPORT_INTERVAL == 0)
            {
                endIndex = index;
                await SendProgress(request.TastRequestId, startIndex, endIndex, found, checkedCount, false);
                startIndex = index + 1;

                /*
                * TODO:
                * think about clearing found here
                */
            }
        }
        if (checkedCount % REPORT_INTERVAL != 0)
        {
            await SendProgress(request.TastRequestId, startIndex, request.EndIndex, found, checkedCount, true);
        }
    }

    private async Task SendProgress(
    Guid taskId,
    long startIndex,
    long endIndex,
    List<string> foundWords,
    long checkedCount,
    bool isCompleted)
    {
        var dto = new WorkerTaskResponse(
            taskId,
            foundWords,
            startIndex,
            endIndex,
            checkedCount,
            isCompleted
        );

        await _httpClient.PostAsJsonAsync(
            "http://manager/internal/api/worker/result",
            dto
        );
    }

    private string IndexToWord(long index, int maxLength) 
    { 
        var sb = new StringBuilder(); 
        long baseLen = _alphabet.Length; 
        do 
        { 
            sb.Insert(0, _alphabet[index % baseLen]); 
            index /= baseLen; 
        } while (index > 0 && sb.Length < maxLength); 
        
        return sb.ToString(); 
    }

    private static string CalculateMD5(string input)
    {
        using var md5 = MD5.Create();
        var bytes = Encoding.UTF8.GetBytes(input);
        var hashBytes = md5.ComputeHash(bytes);
        return BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
    }
}


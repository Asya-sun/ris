using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using Shared.DTO;
using Worker.Models;
using System.Text;
using System.Security.Cryptography;
using Worker.Exceptions;

namespace Worker.Services;

public class HashCrackService : IHashCrackService
{
    private readonly char[] _alphabet;
    private readonly WorkerConfig _config;
    private readonly ILogger<HashCrackService> _logger;
    private const int REPORT_INTERVAL = 100000;
    private readonly double _bomIndex;

    public HashCrackService(
        WorkerConfig config,
        ILogger<HashCrackService> logger)
    {
        _config = config;
        _alphabet = config.Alphabet.ToCharArray();
        _logger = logger;

        _bomIndex = WordToIndex(_config.StopWord);
        _logger.LogInformation($"Word {_config.StopWord} has index {_bomIndex}");
    }


    public async Task ProcessTask(RabbitTaskMessage task, RabbitMqResultPublisher publisher, Guid workerId, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Worker {WorkerId} processing task {RequestId}/subtask {SubTaskId}: range [{Start}-{End}], hash={Hash}",
            workerId, task.RequestId, task.SubTaskId, task.StartIndex, task.EndIndex, task.Hash);

        bool bomInRange = task.StartIndex <= _bomIndex && _bomIndex <= task.EndIndex;

        // if (bomInRange)
        // {
        //     _logger.LogError("BOM detected in range! Task {RequestId} will fail", task.RequestId);
        //     throw new BomDetectedException($"Range contains 'bom' at index {_bomIndex}");
        // }


        var foundWords = new List<string>();
        long checkedCount = 0;
        var startTime = DateTime.UtcNow;
        var lastReportIndex = task.StartIndex;

        for (double index = task.StartIndex; index <= task.EndIndex; index++)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                _logger.LogInformation("Task {RequestId} cancelled", task.RequestId);
                break;
            }

            var word = IndexToWord(index, task.MaxLength);
            checkedCount++;

            if (word == _config.StopWord)
            {
                _logger.LogError("Stop-word '{StopWord}' encountered at index {Index}. Crashing.", word, index);
                throw new BomDetectedException($"Stop-word '{word}' encountered.");
            }

            if (CalculateMD5(word) == task.Hash)
            {
                foundWords.Add(word);
                _logger.LogInformation("Found word '{Word}' for task {RequestId}", word, task.RequestId);

                _logger.LogInformation("Sending found word to publisher...");
                publisher.PublishProgress(
                    task.RequestId, task.SubTaskId, workerId,
                    index, new List<string> { word }, false);

                _logger.LogInformation("Sent found word to publisher");
            }

            if ((index - lastReportIndex) >= REPORT_INTERVAL)
            {
                publisher.PublishProgress(
                    task.RequestId, task.SubTaskId, workerId,
                    index, foundWords, false);

                foundWords.Clear();
                lastReportIndex = index;

                var elapsed = DateTime.UtcNow - startTime;
                var speed = checkedCount / elapsed.TotalSeconds;
                _logger.LogDebug(
                    "Progress: {CheckedCount}/{TotalRange} words, speed: {Speed:F0} words/sec",
                    checkedCount, task.EndIndex - task.StartIndex + 1, speed);
            }
        }

        var totalTime = DateTime.UtcNow - startTime;
        _logger.LogInformation(
            "Completed task {RequestId}: checked {CheckedCount} words, found {FoundCount} words, time: {TotalTime:g}",
            task.RequestId, checkedCount, foundWords.Count, totalTime);

        publisher.PublishProgress(
            task.RequestId, task.SubTaskId, workerId,
            task.EndIndex, foundWords, true);
    }

    private string IndexToWord(double index, int maxLength)
    {
        var sb = new StringBuilder();
        long baseLen = _alphabet.Length;
        do
        {
            sb.Insert(0, _alphabet[(int)index % baseLen]);
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

    private double WordToIndex(string word)
    {
        double index = 0;
        long baseLen = _alphabet.Length;

        for (int i = 0; i < word.Length; i++)
        {
            int charIndex = Array.IndexOf(_alphabet, word[i]);
            index = index * baseLen + charIndex;
        }

        long totalSmaller = 0;
        for (int len = 1; len < word.Length; len++)
        {
            totalSmaller += (long)Math.Pow(baseLen, len);
        }

        return totalSmaller + index;
    }
}

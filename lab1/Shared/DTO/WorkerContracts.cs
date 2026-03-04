using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Shared.DTO
{
    public record WorkerTaskRequest
    (
        string Hash,
        int MaxLength,
        long StartIndex,
        long EndIndex
        // mb need to add string alphabet... idk...
    );

    public record WorkerTaskResponse
    (
        List<string> FoundWords,
        long CheckedCount
    );
       
}
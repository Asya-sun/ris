using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Shared.DTO
{
    public record WorkerRegisterRequest
    (
        string WorkerName,
        string Url
    );

    public record WorkerRegisterResponse
    (
        Guid WorkerId
    );

    /*
    * EndIndex is included in check by worker!!!
    */
    public record WorkerTaskRequest
    (
        Guid TaskRequestId,
        string Hash,
        int MaxLength,
        long StartIndex,
        long EndIndex
    /*
     * TODO:
     * think about add string alphabet... idk...
     * Is it needed? Idk....
     */
    );

    public record WorkerTaskResponse
    (
        Guid WorkerId,
        Guid TaskRequestId,
        List<string> FoundWords,
        long StartIndex,
        long EndIndex,
        long CheckedCount,
        bool IsRequestDone
    );

    public record CancelTaskRequest(Guid TaskId);

}
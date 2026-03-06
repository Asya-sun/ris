using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Shared.DTO
{

    /*
    * EndIndex is included in check by worker!!!
    * TODO:
    * think about it...
    */
    public record WorkerTaskRequest
    (
        Guid TastRequestId,
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
        Guid TastRequestId,
        List<string> FoundWords,
        long StartIndex,
        long EndIndex,
        long CheckedCount,
        bool IsRequestDone 
        /*
         * TODO:
         * think about name
         */
    );

}
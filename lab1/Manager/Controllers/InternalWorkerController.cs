using Microsoft.AspNetCore.Mvc;
using Manager.DTO;
using System.Text.Json;
using Shared.DTO;


namespace Manager.ManagerController
{
    [ApiController]
    [Route("/internal/api/worker")]
    public class InternalWorkerController : ControllerBase
    {
        // POST result
        [HttpPost("result")]
        public IActionResult ReceiveResult([FromBody] WorkerTaskResponse responce)
        {
            /*
            * TODO:
            * implement
            */

            return Ok();
        }

    }
    
}
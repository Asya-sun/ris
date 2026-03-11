using Microsoft.AspNetCore.Mvc;
using Manager.DTO;
using System.Text.Json;
using Shared.DTO;
using Manager.Services;


namespace Manager.ManagerController
{
    [ApiController]
    [Route("/internal/api/worker")]
    public class InternalWorkerController : ControllerBase
    {

        private readonly IManagerService _managerService;

        public InternalWorkerController(IManagerService managerService)
        {
            _managerService = managerService;
        }
        

        [HttpPost("register")]
        public async Task<ActionResult<WorkerRegisterResponse>> RegisterWorker([FromBody] WorkerRegisterRequest request)
        {
            var workerUid = await _managerService.RegisterWorker(request);

            return Ok(new WorkerRegisterResponse(workerUid));
        }


        // POST result
        [HttpPost("result")]
        public IActionResult ReceiveResult([FromBody] WorkerTaskResponse response)
        {
            _managerService.ProcessWorkerResult(response);

            return Ok();
        }

    }
    
}
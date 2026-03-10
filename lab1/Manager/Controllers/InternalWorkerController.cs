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


        // POST result
        [HttpPost("result")]
        public IActionResult ReceiveResult([FromBody] WorkerTaskResponse response)
        {
            /*
            * TODO:
            * implement

            тут обновить инфу о задаче в _taskStates
            */

            _managerService.ProcessWorkerResult(response);

            return Ok();
        }

    }
    
}
using Microsoft.AspNetCore.Mvc;
using Manager.DTO;
using System.Text.Json;
using Manager.Services;



namespace Manager.ManagerController
{
    [ApiController]
    [Route("api/hash")]
    public class HashController : ControllerBase
    {
        private readonly ILogger<HashController> _logger;
        private readonly IManagerService _managerService;
        public HashController(
            IManagerService tableManager,
            ILogger<HashController> logger)
        {
            _managerService = tableManager;
            _logger = logger;
        }

        // POST crack
        [HttpPost("crack")]
        public async Task<ActionResult<ManagerCrackResponse>> CrackHash([FromBody] ManagerCrackRequest request)
        {
            var id = await _managerService.CreateCrackTask(request);

            return Ok(new ManagerCrackResponse(id));
        }

        [HttpGet("status")]
        public async Task<ActionResult<ManagerStatusResponse>> GetCrackStatus([FromQuery] Guid crackId)
        {
            // TODO
            // check if valid (here or in GetStatus())

            var status = _managerService.GetStatus(crackId);

            return Ok(status);
        }
    }
    
}
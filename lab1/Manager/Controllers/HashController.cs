using Microsoft.AspNetCore.Mvc;
using Manager.DTO;
using System.Text.Json;



namespace Manager.ManagerController
{
    [ApiController]
    [Route("api/hash")]
    public class HashController : ControllerBase
    {
        // POST crack
        [HttpPost("crack")]
        public async Task<ActionResult<ManagerCrackResponse>> CrackHash([FromBody] ManagerCrackRequest request)
        {
            /*
            * TODO:
            * implement
            */

            var response = new ManagerCrackResponse(Guid.NewGuid());
            return Ok(response);
        }

        [HttpGet("status")]
        public async Task<ActionResult<ManagerStatusResponse>> GetCrackStatus([FromQuery] Guid crackId)
        {
            /*
            * TODO:
            * implement
            */

            var response = new ManagerStatusResponse(
                "IN_PROGRESS",
                0,
                null
            );

            return Ok(response);
        }
    }
    
}
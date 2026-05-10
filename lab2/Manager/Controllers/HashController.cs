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

        [HttpPost("crack")]
        public async Task<ActionResult<ManagerCrackResponse>> CrackHash([FromBody] ManagerCrackRequest request)
        {
            var id = await _managerService.CreateCrackTask(request);

            return Ok(new ManagerCrackResponse(id));
        }

        [HttpGet("status")]
        public async Task<ActionResult<ManagerStatusResponse>> GetCrackStatus([FromQuery] Guid crackId)
        {
            _logger.LogInformation("GetCrackStatus called with crackId: {CrackId}", crackId);

            try
            {
                if (crackId == Guid.Empty)
                {

                    _logger.LogWarning("Empty crackId received");
                    return BadRequest("Invalid crackId");
                }

                var status = await _managerService.GetStatus(crackId);
                return Ok(status);
            }
            catch (KeyNotFoundException ex)
            {

                _logger.LogWarning(ex, "Task {CrackId} not found", crackId);
                return NotFound(ex.Message);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting status for {CrackId}", crackId);
                return StatusCode(500, "Internal server error");
            }
        }



        [HttpGet("dlq")]
        public IActionResult GetDeadLetterQueue([FromServices] RabbitMqTaskPublisher rabbitPublisher)
        {
            try
            {
                var messages = rabbitPublisher.GetDeadLetterMessages(20);

                return Ok(new
                {
                    Count = messages.Count,
                    Messages = messages.Select(m => new
                    {
                        m.DeliveryTag,
                        m.Content,
                        m.ReceivedAt,
                        Preview = m.Content.Length > 200 ? m.Content.Substring(0, 200) + "..." : m.Content
                    })
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to read DLQ");
                return StatusCode(500, "Failed to read DLQ");
            }
        }
    }

}
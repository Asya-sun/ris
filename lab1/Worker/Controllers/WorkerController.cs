using Microsoft.AspNetCore.Mvc;
using Shared.DTO;
using Worker.Services;

namespace Worker.Controllers;

[ApiController]
[Route("internal/api/worker/hash/crack")]
public class WorkerController : ControllerBase
{
    private readonly IHashCrackService _service;

    public WorkerController(IHashCrackService service)
    {
        _service = service;
    }

    [HttpPost("task")]
    public IActionResult Crack([FromBody] WorkerTaskRequest request)
    {
        _service.StartTask(request);
        return Ok();
    }
}
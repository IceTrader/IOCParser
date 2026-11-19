using Microsoft.AspNetCore.Mvc;
using UI.Web.Services;

namespace UI.Web.Controllers;

[ApiController]
[Route("api/whois")]
public class WhoisStatusController : ControllerBase
{
    private readonly WhoisStatusService _service;
    public WhoisStatusController(WhoisStatusService service) => _service = service;

    [HttpGet("status")]
    public IActionResult GetStatus([FromQuery] bool refresh = false)
        => Ok(_service.GetStatus(refresh));
}
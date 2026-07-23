using Microsoft.AspNetCore.Mvc;

namespace GDriveApi.Controllers;

[Route("ping")]
[ApiController]
public class PingController : ControllerBase
{
    [HttpGet]
    public IActionResult Ping()
    {
        return Content("/pong", "text/plain");
    }
}

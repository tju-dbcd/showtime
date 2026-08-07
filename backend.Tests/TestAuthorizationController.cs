using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ShowtimeBackend.Tests;

[ApiController]
[Route("api/test-authorization")]
public sealed class TestAuthorizationController : ControllerBase
{
    [Authorize(Roles = "USER")]
    [HttpGet("user")]
    public IActionResult GetUserOnly() => Ok();
}

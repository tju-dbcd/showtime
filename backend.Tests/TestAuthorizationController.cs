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

    /// <summary>仅用于测试：触发未处理异常以验证 500 统一错误信封。</summary>
    [Authorize(Roles = "USER")]
    [HttpGet("boom")]
    public IActionResult Boom() => throw new InvalidOperationException("test boom");
}

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WebHealth.Application.Authorization;

namespace WebHealth.IntegrationTests.Support;

[ApiController]
[Route("test/authorization")]
public sealed class TestAuthorizationController : ControllerBase
{
    [HttpGet("administration")]
    [Authorize(Policy = AuthorizationPolicies.Administration)]
    public IActionResult Administration() => Ok();

    [HttpGet("audit-denial")]
    [Authorize(Policy = AuthorizationPolicies.Administration)]
    public IActionResult AuditDenial() => Ok();

    [HttpGet("operate")]
    [Authorize(Policy = AuthorizationPolicies.OperateMonitoring)]
    public IActionResult Operate() => Ok();

    [HttpGet("read-all")]
    [Authorize(Policy = AuthorizationPolicies.ReadAllOperationalData)]
    public IActionResult ReadAll() => Ok();
}

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

    [HttpGet("audit-history")]
    [Authorize(Policy = AuthorizationPolicies.ViewAuditHistory)]
    public IActionResult AuditHistory() => Ok();

    [HttpGet("registry-read")]
    [Authorize(Policy = AuthorizationPolicies.ReadRegistry)]
    public IActionResult RegistryRead() => Ok();

    [HttpGet("registry-manage")]
    [Authorize(Policy = AuthorizationPolicies.ManageRegistry)]
    public IActionResult RegistryManage() => Ok();

    [HttpGet("target-test")]
    [Authorize(Policy = AuthorizationPolicies.TestRegistryTargets)]
    public IActionResult TargetTest() => Ok();

    // No [IgnoreAntiforgeryToken]: this proves the global AutoValidateAntiforgeryTokenAttribute
    // filter (registered once in Program.cs and covering every controller, including
    // IncidentsController/MaintenanceController) rejects a token-less POST from any authenticated
    // role, without needing a database-backed request to reach a real mutation action.
    [HttpPost("antiforgery-probe")]
    [Authorize(Policy = AuthorizationPolicies.ReadRegistry)]
    public IActionResult AntiforgeryProbe() => Ok();
}

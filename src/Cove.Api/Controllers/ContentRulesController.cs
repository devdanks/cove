using Cove.Core.Auth;
using Microsoft.AspNetCore.Mvc;

namespace Cove.Api.Controllers;

[ApiController]
[Route("api/content-rules")]
public class ContentRulesController : ControllerBase
{
    private readonly IContentRuleService _service;
    private readonly ICurrentPrincipalAccessor _principalAccessor;

    public ContentRulesController(IContentRuleService service, ICurrentPrincipalAccessor principalAccessor)
    {
        _service = service;
        _principalAccessor = principalAccessor;
    }

    [HttpGet]
    [RequiresPermission(Permissions.RolesRead)]
    public async Task<IActionResult> List([FromQuery] int? roleId, CancellationToken ct) =>
        Ok(await _service.ListAsync(roleId, ct));

    [HttpPost]
    [RequiresPermission(Permissions.RolesWrite)]
    public async Task<IActionResult> Create([FromBody] CreateContentRuleRequest req, CancellationToken ct) =>
        Ok(await _service.CreateAsync(req, _principalAccessor.Current, ct));

    [HttpPut("{id:int}")]
    [RequiresPermission(Permissions.RolesWrite)]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateContentRuleRequest req, CancellationToken ct) =>
        Ok(await _service.UpdateAsync(id, req, _principalAccessor.Current, ct));

    [HttpDelete("{id:int}")]
    [RequiresPermission(Permissions.RolesWrite)]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        await _service.DeleteAsync(id, _principalAccessor.Current, ct);
        return NoContent();
    }

    [HttpGet("overrides")]
    [RequiresPermission(Permissions.RolesRead)]
    public async Task<IActionResult> ListOverrides([FromQuery] int? roleId, [FromQuery] string? entityKind, CancellationToken ct) =>
        Ok(await _service.ListOverridesAsync(roleId, entityKind, ct));

    [HttpPost("overrides")]
    [RequiresPermission(Permissions.RolesWrite)]
    public async Task<IActionResult> CreateOverride([FromBody] CreateEntityOverrideRequest req, CancellationToken ct) =>
        Ok(await _service.CreateOverrideAsync(req, _principalAccessor.Current, ct));

    [HttpDelete("overrides/{id:int}")]
    [RequiresPermission(Permissions.RolesWrite)]
    public async Task<IActionResult> DeleteOverride(int id, CancellationToken ct)
    {
        await _service.DeleteOverrideAsync(id, _principalAccessor.Current, ct);
        return NoContent();
    }
}
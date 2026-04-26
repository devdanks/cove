using Cove.Core.Auth;
using Microsoft.AspNetCore.Mvc;

namespace Cove.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class RolesController : ControllerBase
{
    private readonly IRoleService _roles;
    private readonly IPermissionRegistry _registry;
    private readonly ICurrentPrincipalAccessor _principalAccessor;

    public RolesController(IRoleService roles, IPermissionRegistry registry, ICurrentPrincipalAccessor principalAccessor)
    {
        _roles = roles;
        _registry = registry;
        _principalAccessor = principalAccessor;
    }

    [HttpGet]
    [RequiresPermission(Permissions.RolesRead)]
    public async Task<IActionResult> List(CancellationToken ct) =>
        Ok(await _roles.ListAsync(ct));

    [HttpGet("{id:int}")]
    [RequiresPermission(Permissions.RolesRead)]
    public async Task<IActionResult> Get(int id, CancellationToken ct)
    {
        var r = await _roles.GetAsync(id, ct);
        return r is null ? NotFound() : Ok(r);
    }

    [HttpGet("permissions")]
    [RequiresPermission(Permissions.RolesRead)]
    public IActionResult ListPermissions() =>
        Ok(_registry.All.OrderBy(p => p.Category).ThenBy(p => p.Key));

    [HttpPost]
    [RequiresPermission(Permissions.RolesWrite)]
    public async Task<IActionResult> Create([FromBody] CreateRoleRequest req, CancellationToken ct) =>
        Ok(await _roles.CreateAsync(req, _principalAccessor.Current, ct));

    [HttpPut("{id:int}")]
    [RequiresPermission(Permissions.RolesWrite)]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateRoleRequest req, CancellationToken ct) =>
        Ok(await _roles.UpdateAsync(id, req, _principalAccessor.Current, ct));

    [HttpDelete("{id:int}")]
    [RequiresPermission(Permissions.RolesDelete)]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        await _roles.DeleteAsync(id, _principalAccessor.Current, ct);
        return NoContent();
    }
}

using Cove.Core.Auth;
using Microsoft.AspNetCore.Mvc;

namespace Cove.Api.Controllers;

[ApiController]
[Route("api/share-links")]
public class ShareLinksController : ControllerBase
{
    private readonly IShareLinkService _service;
    private readonly ICurrentPrincipalAccessor _principalAccessor;

    public ShareLinksController(IShareLinkService service, ICurrentPrincipalAccessor principalAccessor)
    {
        _service = service;
        _principalAccessor = principalAccessor;
    }

    [HttpGet]
    [RequiresPermission(Permissions.ShareLinksWrite)]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var principal = _principalAccessor.Current;
        if (principal?.UserId is not int userId)
            return Unauthorized();

        var includeAll = principal.Has(Permissions.UsersRead);
        return Ok(await _service.ListAsync(includeAll ? null : userId, ct));
    }

    [HttpPost]
    [RequiresPermission(Permissions.ShareLinksWrite)]
    public async Task<IActionResult> Create([FromBody] CreateShareLinkRequest req, CancellationToken ct)
    {
        var principal = _principalAccessor.Current;
        if (principal?.UserId is not int)
            return Unauthorized();

        return Ok(await _service.CreateAsync(req, principal, ct));
    }

    [HttpDelete("{id:guid}")]
    [RequiresPermission(Permissions.ShareLinksWrite)]
    public async Task<IActionResult> Revoke(Guid id, CancellationToken ct)
    {
        var principal = _principalAccessor.Current;
        if (principal?.UserId is not int)
            return Unauthorized();

        await _service.RevokeAsync(id, principal, ct);
        return NoContent();
    }
}
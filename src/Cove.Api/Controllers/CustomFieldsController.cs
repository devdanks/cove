using Microsoft.AspNetCore.Mvc;
using Cove.Api.Services;
using Cove.Core.Auth;
using Cove.Core.DTOs;

namespace Cove.Api.Controllers;

[ApiController]
[Route("api/custom-fields")]
[RequiresPermission(Permissions.SystemRead)]
public sealed class CustomFieldsController(CustomFieldService customFields) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<CustomFieldDefinitionDto>>> GetDefinitions([FromQuery] string? entityType = null, CancellationToken ct = default)
    {
        try
        {
            return Ok(await customFields.GetDefinitionsAsync(entityType, ct));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost]
    [RequiresPermission(Permissions.SystemSettingsWrite)]
    public async Task<ActionResult<CustomFieldDefinitionDto>> CreateDefinition([FromBody] CustomFieldDefinitionCreateDto dto, CancellationToken ct)
    {
        try
        {
            var definition = await customFields.CreateDefinitionAsync(dto, ct);
            return CreatedAtAction(nameof(GetDefinitions), new { id = definition.Id }, definition);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPut]
    [RequiresPermission(Permissions.SystemSettingsWrite)]
    public async Task<ActionResult<List<CustomFieldDefinitionDto>>> ReplaceDefinitions([FromBody] List<CustomFieldDefinitionSyncDto> definitions, CancellationToken ct)
    {
        try
        {
            return Ok(await customFields.ReplaceDefinitionsAsync(definitions, ct));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPut("{id:int}")]
    [RequiresPermission(Permissions.SystemSettingsWrite)]
    public async Task<ActionResult<CustomFieldDefinitionDto>> UpdateDefinition(int id, [FromBody] CustomFieldDefinitionUpdateDto dto, CancellationToken ct)
    {
        try
        {
            var definition = await customFields.UpdateDefinitionAsync(id, dto, ct);
            return definition == null ? NotFound() : Ok(definition);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpDelete("{id:int}")]
    [RequiresPermission(Permissions.SystemSettingsWrite)]
    public async Task<IActionResult> DeleteDefinition(int id, CancellationToken ct)
    {
        return await customFields.DeleteDefinitionAsync(id, ct) ? NoContent() : NotFound();
    }
}
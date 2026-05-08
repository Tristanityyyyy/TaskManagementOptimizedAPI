using Microsoft.AspNetCore.Mvc;
using TaskManagement.DTOs.StickyNote;
using TaskManagement.Services;

namespace TaskManagement.Controllers.V1;

[ApiController]
[Route("api/v1/sticky-notes")]
public sealed class StickyNotesController : ApiControllerBase
{
    private readonly IStickyNotesService _notes;

    public StickyNotesController(IStickyNotesService notes)
    {
        _notes = notes;
    }

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<StickyNoteResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<IReadOnlyList<StickyNoteResponse>>> List(
        CancellationToken cancellationToken = default)
        => Ok(await _notes.ListAsync(CurrentAccount.Id, cancellationToken));

    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(StickyNoteResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<StickyNoteResponse>> Get(int id,
        CancellationToken cancellationToken = default)
    {
        var note = await _notes.FindAsync(CurrentAccount.Id, id, cancellationToken);
        if (note == null)
            return NotFound();
        return Ok(note);
    }

    [HttpPost]
    [ProducesResponseType(typeof(StickyNoteResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<StickyNoteResponse>> Create(
        [FromBody] CreateStickyNoteRequest dto,
        CancellationToken cancellationToken = default)
    {
        var created = await _notes.CreateAsync(CurrentAccount.Id, dto, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    [HttpPatch("{id:int}")]
    [ProducesResponseType(typeof(StickyNoteResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<StickyNoteResponse>> Update(int id,
        [FromBody] UpdateStickyNoteRequest dto,
        CancellationToken cancellationToken = default)
        => Ok(await _notes.UpdateAsync(CurrentAccount.Id, id, dto, cancellationToken));

    [HttpDelete("{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(int id,
        CancellationToken cancellationToken = default)
    {
        await _notes.DeleteAsync(CurrentAccount.Id, id, cancellationToken);
        return NoContent();
    }
}

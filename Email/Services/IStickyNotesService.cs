using TaskManagement.DTOs.StickyNote;

namespace TaskManagement.Services;

public interface IStickyNotesService
{
    Task<IReadOnlyList<StickyNoteResponse>> ListAsync(int requesterId,
        CancellationToken cancellationToken = default);

    Task<StickyNoteResponse?> FindAsync(int requesterId, int noteId,
        CancellationToken cancellationToken = default);

    Task<StickyNoteResponse> CreateAsync(int requesterId, CreateStickyNoteRequest dto,
        CancellationToken cancellationToken = default);

    Task<StickyNoteResponse> UpdateAsync(int requesterId, int noteId, UpdateStickyNoteRequest dto,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(int requesterId, int noteId, CancellationToken cancellationToken = default);
}

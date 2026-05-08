using Microsoft.EntityFrameworkCore;
using TaskManagement.Data;
using TaskManagement.DTOs.StickyNote;
using TaskManagement.Exceptions;
using TaskManagement.Models;

namespace TaskManagement.Services;

public sealed class StickyNotesService : IStickyNotesService
{
    private static DateTime PhTime =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow,
            TimeZoneInfo.FindSystemTimeZoneById("Asia/Manila"));

    private readonly AccountDbContext _context;

    public StickyNotesService(AccountDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<StickyNoteResponse>> ListAsync(int requesterId,
        CancellationToken cancellationToken = default)
    {
        return await _context.StickyNotes
            .AsNoTracking()
            .Where(n => n.AccountId == requesterId && !n.IsDeleted)
            .OrderByDescending(n => n.IsPinned)
            .ThenByDescending(n => n.UpdatedAt)
            .Select(n => new StickyNoteResponse
            {
                Id = n.Id,
                AccountId = n.AccountId,
                Content = n.Content,
                IsPinned = n.IsPinned,
                CreatedAt = n.CreatedAt,
                UpdatedAt = n.UpdatedAt
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<StickyNoteResponse?> FindAsync(int requesterId, int noteId,
        CancellationToken cancellationToken = default)
    {
        return await _context.StickyNotes
            .AsNoTracking()
            .Where(n => n.Id == noteId && n.AccountId == requesterId && !n.IsDeleted)
            .Select(n => new StickyNoteResponse
            {
                Id = n.Id,
                AccountId = n.AccountId,
                Content = n.Content,
                IsPinned = n.IsPinned,
                CreatedAt = n.CreatedAt,
                UpdatedAt = n.UpdatedAt
            })
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<StickyNoteResponse> CreateAsync(int requesterId, CreateStickyNoteRequest dto,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(dto.Content))
            throw new ValidationException(nameof(dto.Content), "Content cannot be empty.");

        var account = await _context.Accounts
            .AsNoTracking()
            .Where(a => a.Id == requesterId)
            .Select(a => new { a.Id, a.Name })
            .FirstOrDefaultAsync(cancellationToken);
        if (account == null)
            throw new ForbiddenException("Requester account not found.");

        var now = PhTime;
        var note = new StickyNote
        {
            AccountId = requesterId,
            Content = dto.Content.Trim(),
            IsPinned = dto.IsPinned,
            CreatedAt = now,
            UpdatedAt = now
        };

        _context.StickyNotes.Add(note);
        _context.AuditLogs.Add(new AuditLog
        {
            AccountId = requesterId,
            Action = "CREATED",
            Note = $"User {account.Name} created a note.",
            CreatedAt = now
        });
        await _context.SaveChangesAsync(cancellationToken);

        return new StickyNoteResponse
        {
            Id = note.Id,
            AccountId = note.AccountId,
            Content = note.Content,
            IsPinned = note.IsPinned,
            CreatedAt = note.CreatedAt,
            UpdatedAt = note.UpdatedAt
        };
    }

    public async Task<StickyNoteResponse> UpdateAsync(int requesterId, int noteId, UpdateStickyNoteRequest dto,
        CancellationToken cancellationToken = default)
    {
        var note = await _context.StickyNotes
            .FirstOrDefaultAsync(n => n.Id == noteId && n.AccountId == requesterId && !n.IsDeleted, cancellationToken);
        if (note == null)
            throw new NotFoundException("Note not found.");

        var oldContent = note.Content;
        var changed = false;

        if (dto.Content != null)
        {
            if (string.IsNullOrWhiteSpace(dto.Content))
                throw new ValidationException(nameof(dto.Content), "Content cannot be empty.");
            if (dto.Content != note.Content)
            {
                note.Content = dto.Content.Trim();
                changed = true;
            }
        }

        if (dto.IsPinned.HasValue && dto.IsPinned.Value != note.IsPinned)
        {
            note.IsPinned = dto.IsPinned.Value;
            changed = true;
        }

        if (!changed)
        {
            return new StickyNoteResponse
            {
                Id = note.Id,
                AccountId = note.AccountId,
                Content = note.Content,
                IsPinned = note.IsPinned,
                CreatedAt = note.CreatedAt,
                UpdatedAt = note.UpdatedAt
            };
        }

        note.UpdatedAt = PhTime;

        var account = await _context.Accounts
            .AsNoTracking()
            .Where(a => a.Id == requesterId)
            .Select(a => a.Name)
            .FirstOrDefaultAsync(cancellationToken);

        _context.AuditLogs.Add(new AuditLog
        {
            AccountId = requesterId,
            Action = "UPDATED",
            OldValue = oldContent,
            NewValue = note.Content,
            Note = $"User {account ?? "Unknown"} updated sticky note.",
            CreatedAt = note.UpdatedAt
        });
        await _context.SaveChangesAsync(cancellationToken);

        return new StickyNoteResponse
        {
            Id = note.Id,
            AccountId = note.AccountId,
            Content = note.Content,
            IsPinned = note.IsPinned,
            CreatedAt = note.CreatedAt,
            UpdatedAt = note.UpdatedAt
        };
    }

    public async Task DeleteAsync(int requesterId, int noteId, CancellationToken cancellationToken = default)
    {
        var note = await _context.StickyNotes
            .FirstOrDefaultAsync(n => n.Id == noteId && n.AccountId == requesterId && !n.IsDeleted, cancellationToken);
        if (note == null)
            throw new NotFoundException("Note not found.");

        var now = PhTime;
        note.IsDeleted = true;
        note.DeletedAt = now;
        note.UpdatedAt = now;

        var account = await _context.Accounts
            .AsNoTracking()
            .Where(a => a.Id == requesterId)
            .Select(a => a.Name)
            .FirstOrDefaultAsync(cancellationToken);

        _context.AuditLogs.Add(new AuditLog
        {
            AccountId = requesterId,
            Action = "DELETED",
            Note = $"User {account ?? "Unknown"} deleted sticky note.",
            CreatedAt = now
        });
        await _context.SaveChangesAsync(cancellationToken);
    }
}

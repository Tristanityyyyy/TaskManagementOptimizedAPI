using ClosedXML.Excel;
using iTextSharp.text;
using iTextSharp.text.pdf;
using Microsoft.EntityFrameworkCore;
using TaskManagement.Authorization;
using TaskManagement.Data;
using TaskManagement.DTOs.AuditLog;
using TaskManagement.Exceptions;
using TaskManagement.Models;

namespace TaskManagement.Services;

public sealed class AuditLogsService : IAuditLogsService
{
    private const int DefaultPageSize = 50;
    private const int MaxPageSize = 500;

    private static readonly HashSet<string> ValidActionActions = new(StringComparer.Ordinal)
    {
        "CREATED", "UPDATED", "DELETED", "RESTORED"
    };

    private static readonly HashSet<string> ValidLoginLogoutActions = new(StringComparer.Ordinal)
    {
        "Logged in", "Logged out"
    };

    private readonly AccountDbContext _context;

    public AuditLogsService(AccountDbContext context)
    {
        _context = context;
    }

    public async Task<AuditLogPageResponse> ListAsync(int requesterId,
        AuditLogKind kind,
        AuditLogFilter filter,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        await EnsureAdminAsync(requesterId, cancellationToken);
        ValidateFilter(kind, filter);

        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize <= 0 ? DefaultPageSize : pageSize, 1, MaxPageSize);

        var baseQuery = await BuildFilteredQueryAsync(kind, filter, cancellationToken);

        var totalCount = await baseQuery.CountAsync(cancellationToken);
        var totalPages = totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)pageSize);

        var items = await ProjectToResponse(baseQuery
                .OrderByDescending(l => l.CreatedAt)
                .ThenByDescending(l => l.Id)
                .Skip((page - 1) * pageSize)
                .Take(pageSize))
            .ToListAsync(cancellationToken);

        return new AuditLogPageResponse
        {
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount,
            TotalPages = totalPages,
            Items = items
        };
    }

    public async Task<AuditLogExportResult> ExportAsync(int requesterId,
        AuditLogKind kind,
        AuditLogExportFormat format,
        AuditLogFilter filter,
        CancellationToken cancellationToken = default)
    {
        await EnsureAdminAsync(requesterId, cancellationToken);
        ValidateFilter(kind, filter);

        if (kind == AuditLogKind.All)
            throw new ValidationException(nameof(kind), "Exports require kind=Action or kind=LoginLogout.");

        var baseQuery = await BuildFilteredQueryAsync(kind, filter, cancellationToken);
        var logs = await ProjectToResponse(baseQuery.OrderByDescending(l => l.CreatedAt).ThenByDescending(l => l.Id))
            .ToListAsync(cancellationToken);

        return (kind, format) switch
        {
            (AuditLogKind.Action, AuditLogExportFormat.Excel) => BuildActionLogsExcel(logs, filter),
            (AuditLogKind.Action, AuditLogExportFormat.Pdf) => BuildActionLogsPdf(logs, filter),
            (AuditLogKind.LoginLogout, AuditLogExportFormat.Excel) => BuildLoginLogoutExcel(logs, filter),
            (AuditLogKind.LoginLogout, AuditLogExportFormat.Pdf) => BuildLoginLogoutPdf(logs, filter),
            _ => throw new ValidationException(nameof(format), "Unsupported export format.")
        };
    }

    // ----------------------------------------------------------------------
    // Authorization + validation
    // ----------------------------------------------------------------------

    private async Task EnsureAdminAsync(int requesterId, CancellationToken cancellationToken)
    {
        var role = await _context.Accounts
            .AsNoTracking()
            .Where(a => a.Id == requesterId)
            .Select(a => a.Role)
            .FirstOrDefaultAsync(cancellationToken);

        if (role == null)
            throw new ForbiddenException("Requester account not found.");
        if (role != AppRoles.Admin)
            throw new ForbiddenException("Only Admins can view audit logs.");
    }

    private static void ValidateFilter(AuditLogKind kind, AuditLogFilter filter)
    {
        if (filter.From.HasValue && filter.To.HasValue && filter.To.Value < filter.From.Value)
            throw new ValidationException(nameof(filter.To), "'to' date must be on or after 'from' date.");

        if (!string.IsNullOrEmpty(filter.Action))
        {
            switch (kind)
            {
                case AuditLogKind.Action when !ValidActionActions.Contains(filter.Action):
                    throw new ValidationException(nameof(filter.Action),
                        "Action must be 'CREATED', 'UPDATED', 'DELETED', or 'RESTORED'.");
                case AuditLogKind.LoginLogout when !ValidLoginLogoutActions.Contains(filter.Action):
                    throw new ValidationException(nameof(filter.Action),
                        "Action must be either 'Logged in' or 'Logged out'.");
            }
        }
    }

    // ----------------------------------------------------------------------
    // Query building (single SQL with joins; no N+1)
    // ----------------------------------------------------------------------

    private async Task<IQueryable<AuditLog>> BuildFilteredQueryAsync(AuditLogKind kind, AuditLogFilter filter,
        CancellationToken cancellationToken)
    {
        IQueryable<AuditLog> query = _context.AuditLogs.AsNoTracking();

        query = kind switch
        {
            AuditLogKind.Action => query.Where(l =>
                l.Action == "CREATED" || l.Action == "UPDATED" ||
                l.Action == "DELETED" || l.Action == "RESTORED"),
            AuditLogKind.LoginLogout => query.Where(l =>
                l.Action == "Logged in" || l.Action == "Logged out"),
            _ => query
        };

        if (filter.UserId.HasValue)
            query = query.Where(l => l.AccountId == filter.UserId.Value);

        if (filter.TaskId.HasValue)
            query = query.Where(l => l.TaskId == filter.TaskId.Value);

        if (filter.ProjectId.HasValue)
            query = query.Where(l => l.ProjectId == filter.ProjectId.Value);

        if (!string.IsNullOrEmpty(filter.Action))
            query = query.Where(l => l.Action == filter.Action);

        if (!string.IsNullOrEmpty(filter.AccountName))
        {
            // EF translates this nested Any into an IN/EXISTS — single SQL, not N+1.
            var nameNeedle = filter.AccountName!;
            query = query.Where(l => _context.Accounts
                .Any(a => a.Id == l.AccountId && a.Name.Contains(nameNeedle)));
        }

        if (!string.IsNullOrEmpty(filter.AccountEmail))
        {
            var emailNeedle = filter.AccountEmail!;
            query = query.Where(l => _context.Accounts
                .Any(a => a.Id == l.AccountId && a.Email.Contains(emailNeedle)));
        }

        if (filter.From.HasValue)
            query = query.Where(l => l.CreatedAt >= filter.From.Value);

        if (filter.To.HasValue)
        {
            // Inclusive end-of-day for `to`.
            var endOfTo = filter.To.Value.Date.AddDays(1).AddTicks(-1);
            query = query.Where(l => l.CreatedAt <= endOfTo);
        }

        return await Task.FromResult(query);
    }

    private IQueryable<AuditLogResponse> ProjectToResponse(IQueryable<AuditLog> query)
    {
        // Explicit join on Accounts so AccountName / AccountEmail / AccountRole come back in one SQL.
        // ProjectName + MemberRole are pulled via correlated subqueries — EF translates them as
        // OUTER APPLY/scalar subqueries, still a single round-trip.
        return from l in query
               join a in _context.Accounts.AsNoTracking() on l.AccountId equals a.Id into accGroup
               from a in accGroup.DefaultIfEmpty()
               select new AuditLogResponse
               {
                   Id = l.Id,
                   ProjectId = l.ProjectId,
                   ProjectName = l.ProjectId.HasValue
                       ? _context.Projects.Where(p => p.Id == l.ProjectId)
                           .Select(p => p.Name)
                           .FirstOrDefault()
                       : null,
                   ProjectRole = a == null ? null
                       : a.Role == AppRoles.Admin
                           ? AppRoles.Admin
                           : (l.ProjectId.HasValue
                               ? (_context.ProjectMembers
                                       .Where(pm => pm.ProjectId == l.ProjectId && pm.AccountId == l.AccountId)
                                       .Select(pm => pm.Role)
                                       .FirstOrDefault()
                                   ?? a.Role)
                               : a.Role),
                   TaskId = l.TaskId,
                   AccountId = l.AccountId,
                   AccountName = a == null ? null : a.Name,
                   AccountEmail = a == null ? null : a.Email,
                   Action = l.Action,
                   OldValue = l.OldValue,
                   NewValue = l.NewValue,
                   Note = l.Note,
                   CreatedAt = l.CreatedAt
               };
    }

    // ----------------------------------------------------------------------
    // Excel exports
    // ----------------------------------------------------------------------

    private static AuditLogExportResult BuildActionLogsExcel(IReadOnlyList<AuditLogResponse> logs,
        AuditLogFilter filter)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Action Logs");

        var headers = new[]
        {
            "ID", "Project ID", "Project Name", "Project Role",
            "Account ID", "Account Name", "Account Email",
            "Action", "Old Value", "New Value", "Note", "Created At"
        };

        WriteSheetHeader(sheet, headers, filter);

        for (var i = 0; i < logs.Count; i++)
        {
            var row = i + 3;
            var log = logs[i];
            sheet.Cell(row, 1).Value = log.Id;
            sheet.Cell(row, 2).Value = log.ProjectId?.ToString() ?? "-";
            sheet.Cell(row, 3).Value = log.ProjectName ?? "-";
            sheet.Cell(row, 4).Value = log.ProjectRole ?? "-";
            sheet.Cell(row, 5).Value = log.AccountId;
            sheet.Cell(row, 6).Value = log.AccountName ?? "-";
            sheet.Cell(row, 7).Value = log.AccountEmail ?? "-";
            sheet.Cell(row, 8).Value = log.Action;
            sheet.Cell(row, 9).Value = log.OldValue ?? "-";
            sheet.Cell(row, 10).Value = log.NewValue ?? "-";
            sheet.Cell(row, 11).Value = log.Note ?? "-";
            sheet.Cell(row, 12).Value = log.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss");

            if (i % 2 == 1)
                sheet.Row(row).Style.Fill.BackgroundColor = XLColor.FromHtml("#EEF3FB");
        }

        sheet.Columns().AdjustToContents();
        return BuildExcelResult(workbook, ExportFileName("ActionLogs", filter, ".xlsx"));
    }

    private static AuditLogExportResult BuildLoginLogoutExcel(IReadOnlyList<AuditLogResponse> logs,
        AuditLogFilter filter)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Login Logout Logs");

        var headers = new[]
        {
            "ID", "Account ID", "Account Name", "Account Email", "Action", "Note", "Created At"
        };

        WriteSheetHeader(sheet, headers, filter);

        for (var i = 0; i < logs.Count; i++)
        {
            var row = i + 3;
            var log = logs[i];
            sheet.Cell(row, 1).Value = log.Id;
            sheet.Cell(row, 2).Value = log.AccountId;
            sheet.Cell(row, 3).Value = log.AccountName ?? "-";
            sheet.Cell(row, 4).Value = log.AccountEmail ?? "-";
            sheet.Cell(row, 5).Value = log.Action;
            sheet.Cell(row, 6).Value = log.Note ?? "-";
            sheet.Cell(row, 7).Value = log.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss");

            if (i % 2 == 1)
                sheet.Row(row).Style.Fill.BackgroundColor = XLColor.FromHtml("#EEF3FB");
        }

        sheet.Columns().AdjustToContents();
        return BuildExcelResult(workbook, ExportFileName("LoginLogoutLogs", filter, ".xlsx"));
    }

    private static void WriteSheetHeader(IXLWorksheet sheet, IReadOnlyList<string> headers, AuditLogFilter filter)
    {
        var rangeLabel = filter.From.HasValue || filter.To.HasValue
            ? $"Date Range: {(filter.From.HasValue ? filter.From.Value.ToString("yyyy-MM-dd") : "Start")} → {(filter.To.HasValue ? filter.To.Value.ToString("yyyy-MM-dd") : "End")}"
            : "Date Range: All";

        sheet.Cell(1, 1).Value = rangeLabel;
        sheet.Cell(1, 1).Style.Font.Italic = true;
        sheet.Cell(1, 1).Style.Font.FontColor = XLColor.DarkGray;
        sheet.Range(1, 1, 1, headers.Count).Merge();

        for (var i = 0; i < headers.Count; i++)
        {
            var cell = sheet.Cell(2, i + 1);
            cell.Value = headers[i];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#4F81BD");
            cell.Style.Font.FontColor = XLColor.White;
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        }
    }

    private static AuditLogExportResult BuildExcelResult(XLWorkbook workbook, string fileName)
    {
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return new AuditLogExportResult(
            stream.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            fileName);
    }

    // ----------------------------------------------------------------------
    // PDF exports
    // ----------------------------------------------------------------------

    private static AuditLogExportResult BuildActionLogsPdf(IReadOnlyList<AuditLogResponse> logs,
        AuditLogFilter filter)
    {
        var headers = new[]
        {
            "ID", "Project ID", "Project Name", "Project Role",
            "Account ID", "Account Name", "Account Email",
            "Action", "Old Value", "New Value", "Note", "Created At"
        };
        var widths = new[] { 3f, 5f, 9f, 8f, 5f, 8f, 11f, 7f, 8f, 8f, 12f, 9f };

        return BuildPdf("Action Log Report", headers, widths, logs, filter, log => new[]
        {
            log.Id.ToString(),
            log.ProjectId?.ToString() ?? "-",
            log.ProjectName ?? "-",
            log.ProjectRole ?? "-",
            log.AccountId.ToString(),
            log.AccountName ?? "-",
            log.AccountEmail ?? "-",
            log.Action,
            log.OldValue ?? "-",
            log.NewValue ?? "-",
            log.Note ?? "-",
            log.CreatedAt.ToString("yyyy-MM-dd HH:mm")
        }, ExportFileName("ActionLogs", filter, ".pdf"));
    }

    private static AuditLogExportResult BuildLoginLogoutPdf(IReadOnlyList<AuditLogResponse> logs,
        AuditLogFilter filter)
    {
        var headers = new[]
        {
            "ID", "Account ID", "Account Name", "Account Email", "Action", "Note", "Created At"
        };
        var widths = new[] { 4f, 6f, 12f, 16f, 8f, 18f, 11f };

        return BuildPdf("Login / Logout Report", headers, widths, logs, filter, log => new[]
        {
            log.Id.ToString(),
            log.AccountId.ToString(),
            log.AccountName ?? "-",
            log.AccountEmail ?? "-",
            log.Action,
            log.Note ?? "-",
            log.CreatedAt.ToString("yyyy-MM-dd HH:mm")
        }, ExportFileName("LoginLogoutLogs", filter, ".pdf"));
    }

    private static AuditLogExportResult BuildPdf(string title,
        IReadOnlyList<string> headers,
        float[] widths,
        IReadOnlyList<AuditLogResponse> logs,
        AuditLogFilter filter,
        Func<AuditLogResponse, IEnumerable<string>> rowSelector,
        string fileName)
    {
        using var stream = new MemoryStream();
        var document = new Document(PageSize.A4.Rotate(), 15f, 15f, 20f, 20f);
        var writer = PdfWriter.GetInstance(document, stream);
        writer.CloseStream = false;
        document.Open();

        var titleFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 14, BaseColor.DARK_GRAY);
        var subtitleFont = FontFactory.GetFont(FontFactory.HELVETICA, 9, BaseColor.GRAY);

        document.Add(new Paragraph(title, titleFont)
        {
            Alignment = Element.ALIGN_CENTER,
            SpacingAfter = 4f
        });

        var rangeLabel = filter.From.HasValue || filter.To.HasValue
            ? $"Date Range: {(filter.From.HasValue ? filter.From.Value.ToString("yyyy-MM-dd") : "Start")} → {(filter.To.HasValue ? filter.To.Value.ToString("yyyy-MM-dd") : "End")}"
            : $"Date Range: All  |  Exported: {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC";

        document.Add(new Paragraph(rangeLabel, subtitleFont)
        {
            Alignment = Element.ALIGN_CENTER,
            SpacingAfter = 12f
        });

        var table = new PdfPTable(headers.Count) { WidthPercentage = 100 };
        table.SetWidths(widths);

        var headerBg = new BaseColor(79, 129, 189);
        var headerFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 7, BaseColor.WHITE);
        var cellFont = FontFactory.GetFont(FontFactory.HELVETICA, 6, BaseColor.BLACK);
        var altBg = new BaseColor(238, 243, 251);

        foreach (var h in headers)
        {
            table.AddCell(new PdfPCell(new Phrase(h, headerFont))
            {
                BackgroundColor = headerBg,
                HorizontalAlignment = Element.ALIGN_CENTER,
                VerticalAlignment = Element.ALIGN_MIDDLE,
                Padding = 4f
            });
        }

        for (var i = 0; i < logs.Count; i++)
        {
            var bg = i % 2 == 1 ? altBg : BaseColor.WHITE;
            foreach (var v in rowSelector(logs[i]))
            {
                table.AddCell(new PdfPCell(new Phrase(v, cellFont))
                {
                    BackgroundColor = bg,
                    Padding = 3f,
                    VerticalAlignment = Element.ALIGN_MIDDLE
                });
            }
        }

        document.Add(table);
        document.Close();

        return new AuditLogExportResult(stream.ToArray(), "application/pdf", fileName);
    }

    private static string ExportFileName(string prefix, AuditLogFilter filter, string extension)
    {
        if (filter.From.HasValue || filter.To.HasValue)
        {
            var fromLabel = filter.From?.ToString("yyyyMMdd") ?? "Start";
            var toLabel = filter.To?.ToString("yyyyMMdd") ?? "End";
            return $"{prefix}_{fromLabel}_{toLabel}{extension}";
        }
        return $"{prefix}_All_{DateTime.UtcNow:yyyyMMdd_HHmmss}{extension}";
    }
}

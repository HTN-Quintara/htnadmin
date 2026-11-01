using System.Security.Cryptography;
using Htn.Api.Auth;
using Htn.Api.Common;
using Htn.Domain.Common;
using Htn.Domain.Files;
using Htn.Domain.Finance;
using Htn.Infrastructure.Audit;
using Htn.Infrastructure.Data;
using Htn.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Net.Http.Headers;

namespace Htn.Api.Features.Finance;

/// <summary>
/// The ledger (frame 24, the second sub-view): the entries, adding one with its receipt, approving, voiding and
/// transfers between funds.
/// <para>
/// The reads and the create route are the finance assistant's whole world — a goon who may add entries that stay
/// Pending. Everything else here is the CFO's. None of these routes carries <c>RequireAuthorization</c>: the finance
/// gate (<see cref="FinanceGate"/>) answers 404 to anyone outside finance before answering 403 inside it.
/// </para>
/// </summary>
public static class LedgerEndpoints
{
    private const int DefaultPageSize = 50;
    private const int MaxPageSize = 200;

    public static RouteGroupBuilder MapLedger(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/finance/ledger").WithTags("Finance");

        g.MapGet("/", List).RequireFinance(FinanceAccess.Ledger);
        g.MapGet("/export.csv", ExportCsv).RequireFinance(FinanceAccess.Board);
        g.MapGet("/{id:guid}", Detail).RequireFinance(FinanceAccess.Ledger);
        g.MapGet("/{id:guid}/receipt", Receipt).RequireFinance(FinanceAccess.Ledger);
        g.MapPost("/", Create).RequireFinance(FinanceAccess.Create)
            .RequireRateLimiting(FinanceRateLimiting.Write);
        g.MapPost("/{id:guid}/approve", Approve).RequireFinance(FinanceAccess.Approve)
            .RequireRateLimiting(FinanceRateLimiting.Write);
        g.MapPost("/{id:guid}/void", Void).RequireFinance(FinanceAccess.Post)
            .RequireRateLimiting(FinanceRateLimiting.Write).Validate<FinanceReasonRequest>();
        g.MapPost("/transfers", Transfer).RequireFinance(FinanceAccess.Post)
            .RequireRateLimiting(FinanceRateLimiting.Write).Validate<CreateTransferRequest>();

        return g;
    }

    // ---------- Reads ----------

    private static async Task<IResult> List(
        HtnDbContext db, ICurrentUser me, [AsParameters] LedgerFilter filter, CancellationToken ct)
    {
        var (page, pageSize) = Paging(filter);
        var query = Filtered(db, filter);

        var total = await query.CountAsync(ct);
        var page_ = query.OrderByDescending(e => e.EntryDate).ThenByDescending(e => e.Id)
            .Skip((page - 1) * pageSize).Take(pageSize);
        var rows = await FinanceViews.Project(db, page_).ToListAsync(ct);

        var names = await FinanceOps.ActorNamesAsync(db, FinanceViews.ActorIds(rows), ct);
        var items = rows.Select(r => FinanceViews.Entry(r, names, me.IsCfo, me.Id)).ToList();

        // The totals cover the whole filter, not the page on screen: a CFO reconciling needs the real sum.
        var posted = query.Where(e => e.Status == LedgerStatus.Posted);
        var moneyIn = await posted.SumAsync(e => e.Direction == LedgerDirection.In ? e.Amount : 0m, ct);
        var moneyOut = await posted.SumAsync(e => e.Direction == LedgerDirection.Out ? e.Amount : 0m, ct);
        var pending = await query.CountAsync(e => e.Status == LedgerStatus.Pending, ct);

        return TypedResults.Ok(new LedgerPageDto(items, page, pageSize, total,
            new LedgerTotalsDto(moneyIn, moneyOut, moneyIn - moneyOut, pending)));
    }

    private static async Task<IResult> Detail(Guid id, HtnDbContext db, ICurrentUser me, CancellationToken ct)
    {
        var row = await FinanceViews.Project(db, db.LedgerEntries.AsNoTracking().Where(e => e.Id == id))
            .FirstOrDefaultAsync(ct);
        if (row is null) return TypedResults.NotFound();
        var names = await FinanceOps.ActorNamesAsync(db, FinanceViews.ActorIds([row]), ct);
        return TypedResults.Ok(FinanceViews.Entry(row, names, me.IsCfo, me.Id));
    }

    /// <summary>
    /// Streams the receipt. Receipts are never in a public file list, so this is the only way to one: always as a
    /// download with <c>nosniff</c>, because a PDF rendered inline is a script execution context.
    /// </summary>
    private static async Task<IResult> Receipt(
        Guid id, HttpContext http, HtnDbContext db, IFileStorage storage, CancellationToken ct)
    {
        var found = await (
            from e in db.LedgerEntries.AsNoTracking()
            join f in db.StoredFiles.AsNoTracking() on e.ReceiptFileId equals f.Id
            where e.Id == id && f.Purpose == StoredFilePurposes.FinanceReceipt
            select new { f.Id, f.ContentType }).FirstOrDefaultAsync(ct);
        if (found is null) return TypedResults.NotFound();
        var stream = await storage.OpenReadAsync(found.Id, ct);
        if (stream is null) return TypedResults.NotFound();

        var headers = http.Response.Headers;
        headers[HeaderNames.XContentTypeOptions] = "nosniff";
        headers[HeaderNames.ContentSecurityPolicy] = "default-src 'none'";
        headers[HeaderNames.CacheControl] = "private, no-store";
        headers["Cross-Origin-Resource-Policy"] = "same-origin";
        var extension = found.ContentType == FileSignatures.PdfContentType ? "pdf"
            : found.ContentType == FileSignatures.PngContentType ? "png" : "jpg";
        return TypedResults.File(stream, found.ContentType, $"receipt-{id}.{extension}");
    }

    private static async Task<IResult> ExportCsv(
        HttpContext http, HtnDbContext db, IAuditLog audit, ICurrentUser me, TimeProvider clock,
        [AsParameters] LedgerFilter filter, CancellationToken ct)
    {
        var rows = await FinanceViews.Project(db, Filtered(db, filter)
                .OrderByDescending(e => e.EntryDate).ThenByDescending(e => e.Id)
                .Take(20_000))
            .ToListAsync(ct);
        var names = await FinanceOps.ActorNamesAsync(db, FinanceViews.ActorIds(rows), ct);
        var csv = FinanceCsv.Ledger(rows.Select(r => FinanceViews.Entry(r, names, me.IsCfo, me.Id)));

        // "The audit log records every add, edit, approve, void, reconcile, close and export": taking the books out
        // of the system is itself an event worth recording.
        audit.Record(me.Id, FinanceAuditActions.ExportLedger, nameof(LedgerEntry), "export",
            after: new { rows = rows.Count, filter.FundId, filter.AccountId, filter.Status, filter.From, filter.To });
        await db.SaveChangesAsync(ct);

        var now = clock.GetUtcNow();
        http.Response.Headers[HeaderNames.XContentTypeOptions] = "nosniff";
        http.Response.Headers[HeaderNames.CacheControl] = "private, no-store";
        return TypedResults.File(FinanceCsv.Bytes(csv), "text/csv; charset=utf-8",
            $"htn-ledger-{now.ToOffset(TimeSpan.FromHours(8)):yyyyMMdd}.csv");
    }

    // ---------- Writes ----------

    /// <summary>
    /// Adds an entry with its receipt. Multipart, so the request is validated by hand against the same
    /// <see cref="FinanceValidationRules"/> the JSON validators use.
    /// <para>
    /// The status is the server's to decide and the request has no field for it: the CFO's entry is posted, the
    /// finance assistant's waits for approval. A client that sends anything else is simply ignored.
    /// </para>
    /// </summary>
    private static async Task<IResult> Create(
        HttpRequest request, HtnDbContext db, IFileStorage storage, IAuditLog audit, ICurrentUser me,
        TimeProvider clock, CancellationToken ct)
    {
        var (file, uploadError) = await Uploads.ReadSingleFileAsync(request, "receipt",
            StoredFilePurposes.FinanceReceiptMaxBytes, "The receipt must be 5 MB or smaller.", ct);
        if (uploadError is not null) return uploadError;
        // Trust the bytes, never the declared type or the file name.
        if (FileSignatures.DetectAttachment(file!.Bytes) is not { } detected)
            return Problems.Field("receipt", "Upload a PNG, JPG or PDF.", FinanceErrorCodes.UnsupportedType);

        var form = request.Form;
        if (Parse(form, out var parsed, out var parseError) is false) return parseError!;

        var (categoryError, _) = (await FinanceOps.CategoryAsync(db, parsed.CategoryId, "categoryId", ct)) switch
        {
            (null, { } err) => (err, (object?)null),
            _ => (null, null),
        };
        if (categoryError is not null) return categoryError;
        if (await FinanceOps.AccountErrorAsync(db, parsed.PaymentAccountId, "paymentAccountId", ct) is { } accountError)
            return accountError;

        // One transaction, holding the period lock across the "is that month closed?" check and the insert, so a
        // close committing at the same moment cannot certify a month this entry has just landed in.
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await FinanceOps.LockPeriodsAsync(db, ct);

        var fund = await db.Funds.FirstOrDefaultAsync(f => f.Id == parsed.FundId, ct);
        if (fund is null) return Problems.Field("fundId", "That fund no longer exists.", FinanceErrorCodes.UnknownFund);
        if (fund.IsClosed) return Problems.Field("fundId", "This fund is closed.", FinanceErrorCodes.FundClosed);
        if (await FinanceOps.IsClosedAsync(db, parsed.EntryDate, ct))
            return FinanceOps.Conflict("That month is closed. Pick a date in an open month, or reopen it first.",
                FinanceErrorCodes.MonthClosed);

        var now = clock.GetUtcNow();
        var (contentType, bytes) = detected;
        var stored = StoredFile.Create(contentType, bytes.LongLength, Convert.ToHexStringLower(SHA256.HashData(bytes)),
            StoredFilePurposes.FinanceReceipt, file.FileName, now, me.Id);
        await storage.SaveAsync(stored.Id, bytes, ct);
        db.StoredFiles.Add(stored);

        var data = new NewLedgerEntry(fund.Id, parsed.PaymentAccountId, parsed.CategoryId, parsed.EntryDate,
            parsed.Direction, parsed.Amount, parsed.Reference, parsed.Description, stored.Id, LedgerSource.Manual, null);
        var entry = me.IsCfo
            ? LedgerEntry.Posted(data, me.Id, now)
            : LedgerEntry.Pending(data, me.Id!.Value, now);
        db.LedgerEntries.Add(entry);

        audit.Record(me.Id, FinanceAuditActions.LedgerCreate, nameof(LedgerEntry), entry.Id.ToString(),
            after: new
            {
                entry.FundId, entry.PaymentAccountId, entry.CategoryId, entry.EntryDate,
                direction = entry.Direction.ToString(), entry.Amount, entry.Reference, entry.Description,
                status = entry.Status.ToString(), receiptFileId = stored.Id,
            });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await OkAsync(db, entry.Id, me, ct);
    }

    private static async Task<IResult> Approve(
        Guid id, HtnDbContext db, IAuditLog audit, ICurrentUser me, TimeProvider clock, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await FinanceOps.LockPeriodsAsync(db, ct);
        var entry = await db.LedgerEntries.FirstOrDefaultAsync(e => e.Id == id, ct);
        if (entry is null) return TypedResults.NotFound();
        if (entry.Status == LedgerStatus.Voided)
            return FinanceOps.Conflict("This entry was voided.", FinanceErrorCodes.EntryVoided);
        if (entry.Status == LedgerStatus.Posted)
            return FinanceOps.Conflict("This entry is already posted.", FinanceErrorCodes.AlreadyPosted);
        // Nobody signs off their own work, the same rule the task board applies to points.
        if (entry.EnteredById == me.Id)
            return FinanceOps.Conflict("Someone else has to approve an entry you added.", FinanceErrorCodes.SelfApproval);

        var before = new { status = entry.Status.ToString() };
        entry.Approve(me.Id!.Value, clock.GetUtcNow());
        audit.Record(me.Id, FinanceAuditActions.LedgerApprove, nameof(LedgerEntry), entry.Id.ToString(),
            before, new { status = entry.Status.ToString(), entry.Amount, entry.EntryDate });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await OkAsync(db, entry.Id, me, ct);
    }

    private static async Task<IResult> Void(
        Guid id, FinanceReasonRequest req, HtnDbContext db, IAuditLog audit, ICurrentUser me,
        TimeProvider clock, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await FinanceOps.LockPeriodsAsync(db, ct);
        var entry = await db.LedgerEntries.FirstOrDefaultAsync(e => e.Id == id, ct);
        if (entry is null) return TypedResults.NotFound();
        if (entry.Status == LedgerStatus.Voided)
            return FinanceOps.Conflict("This entry is already voided.", FinanceErrorCodes.EntryVoided);
        // A surplus sweep is a fund's one and only transfer out, and Fund.SweptAt can never be unstamped, so voiding
        // it would strand the money with no way to move it again. Post a reverse transfer instead.
        if (entry.Source == LedgerSource.SurplusSweep)
            return FinanceOps.Conflict(
                "A surplus transfer cannot be voided. Record a transfer back to the event fund instead.",
                FinanceErrorCodes.SweepNotVoidable);
        if (entry.MonthCloseId is not null)
            return FinanceOps.Conflict("This month is closed. Post a correcting entry dated today instead.",
                FinanceErrorCodes.MonthClosed);

        var before = new { status = entry.Status.ToString() };
        entry.Void(req.Reason!, me.Id!.Value, clock.GetUtcNow());

        // Both legs of a transfer go together, or the money appears to vanish on one side.
        var affected = new List<Guid> { entry.Id };
        if (entry.TransferGroupId is { } group)
        {
            var legs = await db.LedgerEntries
                .Where(e => e.TransferGroupId == group && e.Id != entry.Id && e.Status != LedgerStatus.Voided)
                .ToListAsync(ct);
            foreach (var leg in legs)
            {
                leg.Void(req.Reason!, me.Id!.Value, clock.GetUtcNow());
                affected.Add(leg.Id);
            }
        }

        audit.Record(me.Id, FinanceAuditActions.LedgerVoid, nameof(LedgerEntry), entry.Id.ToString(),
            before, new { status = entry.Status.ToString(), entries = affected }, req.Reason);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await OkAsync(db, entry.Id, me, ct);
    }

    /// <summary>
    /// Moves money between funds. This is the one movement written as two rows — "transfers between funds are
    /// recorded on both sides" — sharing a transfer group id; a deferred trigger refuses a lone leg at commit.
    /// </summary>
    private static async Task<IResult> Transfer(
        CreateTransferRequest req, HtnDbContext db, IAuditLog audit, ICurrentUser me, TimeProvider clock,
        CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await FinanceOps.LockPeriodsAsync(db, ct);
        var from = await db.Funds.FirstOrDefaultAsync(f => f.Id == req.FromFundId, ct);
        var to = await db.Funds.FirstOrDefaultAsync(f => f.Id == req.ToFundId, ct);
        if (from is null) return Problems.Field("fromFundId", "That fund no longer exists.", FinanceErrorCodes.UnknownFund);
        if (to is null) return Problems.Field("toFundId", "That fund no longer exists.", FinanceErrorCodes.UnknownFund);
        if (from.IsClosed) return Problems.Field("fromFundId", "This fund is closed.", FinanceErrorCodes.FundClosed);
        if (to.IsClosed) return Problems.Field("toFundId", "This fund is closed.", FinanceErrorCodes.FundClosed);
        var date = req.EntryDate!.Value;
        if (await FinanceOps.IsClosedAsync(db, date, ct))
            return FinanceOps.Conflict("That month is closed. Pick a date in an open month, or reopen it first.",
                FinanceErrorCodes.MonthClosed);

        var now = clock.GetUtcNow();
        var (outLeg, inLeg) = LedgerEntry.Transfer(from, to, LedgerCategories.TransferId, date,
            req.Amount!.Value, req.Description!, me.Id, now);
        db.LedgerEntries.AddRange(outLeg, inLeg);
        audit.Record(me.Id, FinanceAuditActions.LedgerTransfer, nameof(LedgerEntry), outLeg.Id.ToString(),
            after: new
            {
                fromFundId = from.Id, toFundId = to.Id, outLeg.Amount, outLeg.EntryDate,
                transferGroupId = outLeg.TransferGroupId, entries = new[] { outLeg.Id, inLeg.Id },
            });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await OkAsync(db, outLeg.Id, me, ct);
    }

    // ---------- Helpers ----------

    private static async Task<IResult> OkAsync(HtnDbContext db, Guid id, ICurrentUser me, CancellationToken ct)
    {
        var row = await FinanceViews.Project(db, db.LedgerEntries.AsNoTracking().Where(e => e.Id == id)).FirstAsync(ct);
        var names = await FinanceOps.ActorNamesAsync(db, FinanceViews.ActorIds([row]), ct);
        return TypedResults.Ok(FinanceViews.Entry(row, names, me.IsCfo, me.Id));
    }

    /// <summary>
    /// The filters, applied to the entities so the database can use the indexes. Names are attached afterwards by
    /// <see cref="FinanceViews.Project"/>, on the one page being returned.
    /// </summary>
    private static IQueryable<LedgerEntry> Filtered(HtnDbContext db, LedgerFilter f)
    {
        var query = db.LedgerEntries.AsNoTracking();
        if (f.FundId is { } fund) query = query.Where(e => e.FundId == fund);
        if (f.EventId is { } ev) query = query.Where(e => db.Funds.Any(x => x.Id == e.FundId && x.EventId == ev));
        if (f.AccountId is { } account) query = query.Where(e => e.PaymentAccountId == account);
        if (f.CategoryId is { } category) query = query.Where(e => e.CategoryId == category);
        if (f.Status is { } status) query = query.Where(e => e.Status == status);
        if (f.Direction is { } direction) query = query.Where(e => e.Direction == direction);
        if (f.From is { } from) query = query.Where(e => e.EntryDate >= from);
        if (f.To is { } to) query = query.Where(e => e.EntryDate <= to);
        if (!string.IsNullOrWhiteSpace(f.Search))
        {
            var term = $"%{f.Search.Trim()}%";
            query = query.Where(e =>
                EF.Functions.ILike(e.Description, term) ||
                (e.Reference != null && EF.Functions.ILike(e.Reference, term)));
        }
        return query;
    }

    private static (int Page, int PageSize) Paging(LedgerFilter f) =>
        (Math.Max(1, f.Page ?? 1), Math.Clamp(f.PageSize ?? DefaultPageSize, 1, MaxPageSize));

    private sealed record ParsedEntry(
        Guid FundId, Guid PaymentAccountId, Guid CategoryId, DateOnly EntryDate,
        LedgerDirection Direction, decimal Amount, string? Reference, string Description);

    /// <summary>
    /// Reads and checks the multipart fields. Deliberately mirrors the FluentValidation messages, because the same
    /// mistake should read the same way whether the request was JSON or a form.
    /// </summary>
    private static bool Parse(IFormCollection form, out ParsedEntry parsed, out IResult? error)
    {
        parsed = default!;
        error = null;

        if (!Guid.TryParse(form["fundId"], out var fundId) || fundId == Guid.Empty)
            return Fail("fundId", "A fund is required.", out error);
        if (!Guid.TryParse(form["paymentAccountId"], out var accountId) || accountId == Guid.Empty)
            return Fail("paymentAccountId", "An account is required.", out error);
        if (!Guid.TryParse(form["categoryId"], out var categoryId) || categoryId == Guid.Empty)
            return Fail("categoryId", "A category is required.", out error);
        if (!DateOnly.TryParse(form["entryDate"], System.Globalization.CultureInfo.InvariantCulture, out var date)
            || date.Year is < 2020 or > 2100)
            return Fail("entryDate", "A date is required.", out error);
        if (!Enum.TryParse<LedgerDirection>(form["direction"], ignoreCase: true, out var direction)
            || !Enum.IsDefined(direction))
            return Fail("direction", "Say whether money came in or went out.", out error);
        if (!decimal.TryParse(form["amount"], System.Globalization.NumberStyles.Number,
                System.Globalization.CultureInfo.InvariantCulture, out var amount)
            || amount <= 0m || amount > FinanceValidationRules.MaxAmount)
            return Fail("amount", "An amount is required and must be more than ₱0.", out error);
        if (decimal.Round(amount, 2) != amount)
            return Fail("amount", "Use at most 2 decimal places, like 1500 or 1500.50.", out error);

        var description = form["description"].ToString().Trim();
        if (description.Length == 0) return Fail("description", "Say what the entry is for.", out error);
        if (description.Length > LedgerEntry.DescriptionMaxLength)
            return Fail("description", $"Keep the description under {LedgerEntry.DescriptionMaxLength} characters.", out error);
        var reference = form["reference"].ToString().Trim();
        if (reference.Length > LedgerEntry.ReferenceMaxLength)
            return Fail("reference", $"Keep the reference under {LedgerEntry.ReferenceMaxLength} characters.", out error);

        parsed = new ParsedEntry(fundId, accountId, categoryId, date, direction, amount,
            reference.Length == 0 ? null : reference, description);
        return true;
    }

    private static bool Fail(string field, string message, out IResult? error)
    {
        error = Problems.Field(field, message);
        return false;
    }
}

/// <summary>
/// The ledger list filters. Bound with <c>[AsParameters]</c> from the query string — a record on a GET would
/// otherwise be inferred as a body — and shared with the CSV export so the two can never drift apart.
/// </summary>
public sealed record LedgerFilter(
    Guid? FundId, Guid? EventId, Guid? AccountId, Guid? CategoryId,
    LedgerStatus? Status, LedgerDirection? Direction,
    DateOnly? From, DateOnly? To, string? Search, int? Page, int? PageSize);

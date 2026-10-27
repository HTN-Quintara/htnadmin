using Htn.Domain.Suppliers;

namespace Htn.Api.Features.Suppliers;

/// <summary>Projections from the suppliers entities to the DTOs the screens render.</summary>
public static class SuppliersViews
{
    public static SupplierDto Supplier(Supplier s, bool includeBoardOnly) => new(
        s.Id, s.Category, s.Name, s.ContactName, s.ContactEmail, s.ContactPhone, s.IssuesOr, s.Notes, s.LogoFileId, s.IsActive,
        includeBoardOnly ? new SupplierBoardOnlyDto(s.Tin, s.BankDetails) : null);

    public static SupplierDocumentDto Document(SupplierDocument d) => new(
        d.Id, d.SupplierId, d.Kind, d.FileId, d.UploadedAt, d.UploadedById, d.IsVoided, d.VoidReason);

    public static IReadOnlyList<string> SplitTags(string? tags) =>
        string.IsNullOrEmpty(tags) ? [] : tags.Split(';', StringSplitOptions.RemoveEmptyEntries);

    public static SupplierRatingDto Rating(SupplierRating r) => new(
        r.Id, r.EventSupplierId, r.RaterId, r.Stars, SplitTags(r.Tags), r.HireAgain, r.Note, r.CreatedAt);

    public static EventSupplierDto Hire(
        EventSupplier h, string supplierName, string categoryName, string ownerCommitteeName,
        IReadOnlyList<SupplierRating> ratings, bool includeBoardOnly, bool canRate, bool alreadyRatedByMe) => new(
        h.Id, h.EventId, h.SupplierId, supplierName, h.CategoryId, categoryName, h.OwnerCommitteeId, ownerCommitteeName,
        h.Status, h.Note, h.IsVoided, h.VoidReason,
        includeBoardOnly ? new EventSupplierMoneyDto(h.ContractAmount, h.PaidAmount, h.NextPaymentAmount, h.NextPaymentDue) : null,
        ratings.Count == 0 ? null : ratings.Average(r => (double)r.Stars), ratings.Count, canRate, alreadyRatedByMe);
}

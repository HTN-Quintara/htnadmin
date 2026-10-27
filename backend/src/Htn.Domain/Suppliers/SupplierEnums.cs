namespace Htn.Domain.Suppliers;

/// <summary>What a <see cref="Supplier"/> is hired for.</summary>
public enum SupplierCategory
{
    Venue,
    Catering,
    Printing,
    TechAV,
    Merch,
    Transport,
}

/// <summary>
/// The lifecycle of one <see cref="EventSupplier"/> hire. Forward-only along this exact sequence (Phase 7C
/// decision, see <see cref="EventSupplier"/>'s remarks): there is no "booking fell through" backward move —
/// void the hire and start a new one instead.
/// </summary>
public enum SupplierHireStatus
{
    Quotation,
    Booked,
    DepositPaid,
    FullyPaid,
}

/// <summary>What a <see cref="SupplierDocument"/> is.</summary>
public enum SupplierDocumentKind
{
    Bir2303,
    BusinessPermit,
    Other,
}

/// <summary>A committee lead's answer to "hire again?" on a <see cref="SupplierRating"/>.</summary>
public enum HireAgain
{
    Yes,
    Maybe,
    No,
}

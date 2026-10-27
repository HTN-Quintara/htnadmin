namespace Htn.Domain.Finance;

/// <summary>The Organization fund, or one fund per event.</summary>
public enum FundKind
{
    Organization,
    Event,
}

/// <summary>Which way money moved. The ledger is single-entry: the sign comes from here, not from a paired row.</summary>
public enum LedgerDirection
{
    In,
    Out,
}

/// <summary>
/// <see cref="Pending"/> is not money yet: only <see cref="Posted"/> rows count towards a balance, and a month cannot
/// close while anything is pending. A mistake becomes <see cref="Voided"/> with a reason; nothing is ever deleted.
/// </summary>
public enum LedgerStatus
{
    Pending,
    Posted,
    Voided,
}

/// <summary>
/// Where an entry came from. <see cref="Manual"/> is typed by the CFO or the finance assistant; everything else is
/// written by the system, which is why only the first two require a receipt.
/// <para>
/// The last five values have no producer yet. They are declared now so the string check constraint (built from
/// <c>Enum.GetNames&lt;LedgerSource&gt;()</c>) already admits them and Phases 6-8 need no migration:
/// <see cref="SponsorPayment"/> (Phase 6), <see cref="SupplierPayment"/> and <see cref="MerchSale"/> (Phase 7),
/// <see cref="MembershipPayment"/> and <see cref="PopUpWriteOff"/> (Phase 8).
/// </para>
/// </summary>
public enum LedgerSource
{
    /// <summary>Typed in by a person. A receipt is required.</summary>
    Manual,

    /// <summary>Auto-posted when a registration is marked paid. <c>SourceId</c> is the payment id.</summary>
    TicketPayment,

    /// <summary>Offsets an entry in a closed month, because the original can no longer be voided.</summary>
    Contra,

    /// <summary>One of the two legs of a transfer between funds.</summary>
    Transfer,

    /// <summary>The surplus of a closed event fund, moved to the Organization fund.</summary>
    SurplusSweep,

    /// <summary>Written when the CFO marks an expense request paid. A receipt is required.</summary>
    ExpenseRequest,

    /// <summary>Phase 6: a sponsor's cash payment.</summary>
    SponsorPayment,

    /// <summary>Phase 7: a payment to a supplier hired through an event budget.</summary>
    SupplierPayment,

    /// <summary>Phase 7: a merch sale at an event POS.</summary>
    MerchSale,

    /// <summary>Phase 8: membership dues sold as a POS item.</summary>
    MembershipPayment,

    /// <summary>Phase 8: a CFO write-off for stock missing from a pop-up store.</summary>
    PopUpWriteOff,
}

/// <summary>A budget line, and the kind of category it reports against.</summary>
public enum BudgetLineKind
{
    Income,
    Expense,
}

public enum ExpenseRequestStatus
{
    Pending,
    Approved,
    Rejected,
    Paid,
    Cancelled,
}

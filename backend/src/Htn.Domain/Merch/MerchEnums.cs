namespace Htn.Domain.Merch;

/// <summary>How a POS sale was paid. Mirrors <c>Htn.Domain.Registrations.PaymentMethod</c> minus <c>Comp</c> — a merch
/// sale is never comped, only discounted.</summary>
public enum MerchSaleMethod
{
    Cash,
    GCash,
    Bank,
}

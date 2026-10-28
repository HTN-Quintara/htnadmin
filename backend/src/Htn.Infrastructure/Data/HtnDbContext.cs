using Htn.Domain.Audit;
using Htn.Domain.CheckIn;
using Htn.Domain.Committees;
using Htn.Domain.Directory;
using Htn.Domain.Events;
using Htn.Domain.Files;
using Htn.Domain.Finance;
using Htn.Domain.Invitations;
using Htn.Domain.Organization;
using Htn.Domain.Outbox;
using Htn.Domain.Registrations;
using Htn.Domain.Agenda;
using Htn.Domain.Leads;
using Htn.Domain.Merch;
using Htn.Domain.Speakers;
using Htn.Domain.Sponsors;
using Htn.Domain.Settings;
using Htn.Domain.Suppliers;
using Htn.Domain.Tasks;
using Htn.Infrastructure.Identity;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Htn.Infrastructure.Data;

public sealed class HtnDbContext(DbContextOptions<HtnDbContext> options)
    : IdentityDbContext<AppUser, IdentityRole<Guid>, Guid>(options), IDataProtectionKeyContext
{
    public DbSet<Person> People => Set<Person>();
    public DbSet<PersonHistory> PersonHistory => Set<PersonHistory>();
    public DbSet<Position> Positions => Set<Position>();
    public DbSet<PositionAssignment> PositionAssignments => Set<PositionAssignment>();
    public DbSet<OrgSettings> OrgSettings => Set<OrgSettings>();
    public DbSet<PaymentAccount> PaymentAccounts => Set<PaymentAccount>();
    public DbSet<StoredFile> StoredFiles => Set<StoredFile>();
    public DbSet<Event> Events => Set<Event>();
    public DbSet<TicketType> TicketTypes => Set<TicketType>();
    public DbSet<Voucher> Vouchers => Set<Voucher>();
    public DbSet<Registration> Registrations => Set<Registration>();
    public DbSet<Attendee> Attendees => Set<Attendee>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<PaymentProof> PaymentProofs => Set<PaymentProof>();
    public DbSet<CheckInScan> CheckInScans => Set<CheckInScan>();
    public DbSet<Committee> Committees => Set<Committee>();
    public DbSet<CommitteeMember> CommitteeMembers => Set<CommitteeMember>();
    public DbSet<EventVolunteer> EventVolunteers => Set<EventVolunteer>();
    public DbSet<TaskItem> Tasks => Set<TaskItem>();
    public DbSet<TaskAssignee> TaskAssignees => Set<TaskAssignee>();
    public DbSet<TaskChecklistItem> TaskChecklistItems => Set<TaskChecklistItem>();
    public DbSet<TaskComment> TaskComments => Set<TaskComment>();
    public DbSet<TaskAttachment> TaskAttachments => Set<TaskAttachment>();
    public DbSet<TaskActivity> TaskActivities => Set<TaskActivity>();
    public DbSet<PointsAward> PointsAwards => Set<PointsAward>();
    public DbSet<Fund> Funds => Set<Fund>();
    public DbSet<LedgerCategory> LedgerCategories => Set<LedgerCategory>();
    public DbSet<LedgerEntry> LedgerEntries => Set<LedgerEntry>();
    public DbSet<MonthClose> MonthCloses => Set<MonthClose>();
    public DbSet<AccountReconciliation> AccountReconciliations => Set<AccountReconciliation>();
    public DbSet<EventBudget> EventBudgets => Set<EventBudget>();
    public DbSet<EventBudgetVersion> EventBudgetVersions => Set<EventBudgetVersion>();
    public DbSet<EventBudgetLine> EventBudgetLines => Set<EventBudgetLine>();
    public DbSet<ExpenseRequest> ExpenseRequests => Set<ExpenseRequest>();
    public DbSet<SponsorTier> SponsorTiers => Set<SponsorTier>();
    public DbSet<Sponsor> Sponsors => Set<Sponsor>();
    public DbSet<SponsorPerk> SponsorPerks => Set<SponsorPerk>();
    public DbSet<SponsorCompRegistration> SponsorCompRegistrations => Set<SponsorCompRegistration>();
    public DbSet<EventPartnerSettings> EventPartnerSettings => Set<EventPartnerSettings>();
    public DbSet<EventPartner> EventPartners => Set<EventPartner>();
    public DbSet<PartnerGroupLink> PartnerGroupLinks => Set<PartnerGroupLink>();
    public DbSet<Lead> Leads => Set<Lead>();
    public DbSet<Speaker> Speakers => Set<Speaker>();
    public DbSet<EventRoom> EventRooms => Set<EventRoom>();
    public DbSet<Session> Sessions => Set<Session>();
    public DbSet<SessionRoom> SessionRooms => Set<SessionRoom>();
    public DbSet<SessionSpeaker> SessionSpeakers => Set<SessionSpeaker>();
    public DbSet<SpeakerTravel> SpeakerTravel => Set<SpeakerTravel>();
    public DbSet<SpeakerLink> SpeakerLinks => Set<SpeakerLink>();
    public DbSet<SpeakerLinkAccess> SpeakerLinkAccess => Set<SpeakerLinkAccess>();
    public DbSet<SpeakerPlusOne> SpeakerPlusOnes => Set<SpeakerPlusOne>();
    public DbSet<DirectoryOrg> DirectoryOrgs => Set<DirectoryOrg>();
    public DbSet<DirectoryContact> DirectoryContacts => Set<DirectoryContact>();
    public DbSet<DirectoryContactHistory> DirectoryContactHistory => Set<DirectoryContactHistory>();
    public DbSet<EventInvitation> EventInvitations => Set<EventInvitation>();
    public DbSet<InvitationEvent> InvitationEvents => Set<InvitationEvent>();
    public DbSet<EventInvitationTemplate> EventInvitationTemplates => Set<EventInvitationTemplate>();
    public DbSet<Supplier> Suppliers => Set<Supplier>();
    public DbSet<SupplierDocument> SupplierDocuments => Set<SupplierDocument>();
    public DbSet<EventSupplier> EventSuppliers => Set<EventSupplier>();
    public DbSet<SupplierRating> SupplierRatings => Set<SupplierRating>();
    public DbSet<MerchItem> MerchItems => Set<MerchItem>();
    public DbSet<MerchVariant> MerchVariants => Set<MerchVariant>();
    public DbSet<EventMerchSettings> EventMerchSettings => Set<EventMerchSettings>();
    public DbSet<MerchSale> MerchSales => Set<MerchSale>();
    public DbSet<MerchSaleLine> MerchSaleLines => Set<MerchSaleLine>();
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();
    public DbSet<OutboxMessage> Outbox => Set<OutboxMessage>();
    public DbSet<AccountToken> AccountTokens => Set<AccountToken>();
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.ApplyConfigurationsFromAssembly(typeof(HtnDbContext).Assembly);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Money is always decimal(12,2) (PHP).
        configurationBuilder.Properties<decimal>().HavePrecision(12, 2);
        configurationBuilder.Properties<decimal?>().HavePrecision(12, 2);
        // The composite (child, event) foreign keys exist to make cross-event rows impossible, not to be searched.
        configurationBuilder.Conventions.Replace<Microsoft.EntityFrameworkCore.Metadata.Conventions.ForeignKeyIndexConvention>(
            sp => new HtnForeignKeyIndexConvention(
                sp.GetRequiredService<Microsoft.EntityFrameworkCore.Metadata.Conventions.Infrastructure.ProviderConventionSetBuilderDependencies>()));
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        GuardAppendOnly();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        GuardAppendOnly();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void GuardAppendOnly()
    {
        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.State is not (EntityState.Modified or EntityState.Deleted)) continue;
            if (entry.Entity is AuditEntry or Domain.Organization.PersonHistory or PaymentProof or CheckInScan or TaskActivity)
                throw new InvalidOperationException($"{entry.Entity.GetType().Name} is append-only.");
            // A saved budget version is frozen: the next save is a new version, which is the whole audit trail.
            if (entry.Entity is EventBudgetVersion or EventBudgetLine)
                throw new InvalidOperationException("A budget version is never edited: save a new version instead.");
            // Points are append-only like finance: a reversal stamps the row (allowed), nothing is ever deleted.
            if (entry.State == EntityState.Deleted && entry.Entity is PointsAward)
                throw new InvalidOperationException("Points awards are reversed with a reason, never deleted.");
            // Finance is append-only: approving, voiding, superseding and reopening stamp the row.
            if (entry.State == EntityState.Deleted && entry.Entity is LedgerEntry)
                throw new InvalidOperationException("Ledger entries are voided with a reason, never deleted.");
            if (entry.State == EntityState.Deleted
                && entry.Entity is Fund or LedgerCategory or MonthClose or AccountReconciliation
                    or EventBudget or ExpenseRequest)
                throw new InvalidOperationException($"{entry.Entity.GetType().Name} rows are never deleted.");
            // Sponsors and partners are append-only: deactivate, void, untag or revoke with a reason, never delete.
            if (entry.State == EntityState.Deleted
                && entry.Entity is SponsorTier or Sponsor or SponsorPerk or SponsorCompRegistration
                    or Htn.Domain.Sponsors.EventPartnerSettings or EventPartner or PartnerGroupLink)
                throw new InvalidOperationException($"{entry.Entity.GetType().Name} rows are never deleted.");
            // Leads and speakers are append-only: a lead is reopened or declined with a reason, a speaker is voided.
            if (entry.State == EntityState.Deleted && entry.Entity is Lead or Speaker)
                throw new InvalidOperationException($"{entry.Entity.GetType().Name} rows are never deleted.");
            // A room is deactivated, never deleted. (Sessions and their room/speaker link rows are deletable.)
            if (entry.State == EntityState.Deleted && entry.Entity is EventRoom)
                throw new InvalidOperationException("Event rooms are deactivated, never deleted.");
            // Speaker travel/links/plus-ones are append-only: purge, revoke or remove with a reason, never delete.
            if (entry.State == EntityState.Deleted && entry.Entity is Htn.Domain.Speakers.SpeakerTravel or SpeakerLink or SpeakerPlusOne)
                throw new InvalidOperationException($"{entry.Entity.GetType().Name} rows are never deleted.");
            if (entry.State is EntityState.Modified or EntityState.Deleted && entry.Entity is SpeakerLinkAccess)
                throw new InvalidOperationException("SpeakerLinkAccess is append-only.");
            if (entry.State == EntityState.Deleted && entry.Entity is Person)
                throw new InvalidOperationException("People are deactivated, never deleted.");
            // Directory orgs and contacts are deactivated/voided with a reason, never deleted. Their history is
            // fully append-only: no updates and no deletes, ever.
            if (entry.State == EntityState.Deleted && entry.Entity is DirectoryOrg or DirectoryContact)
                throw new InvalidOperationException($"{entry.Entity.GetType().Name} rows are never deleted.");
            if (entry.State is EntityState.Modified or EntityState.Deleted && entry.Entity is DirectoryContactHistory)
                throw new InvalidOperationException("DirectoryContactHistory is append-only.");
            // An invitation may still be hard-deleted while NotSent (undoing a pick-list mistake); once sent, every
            // send is logged and the row is permanent. InvitationEvent is fully append-only, like DirectoryContactHistory.
            if (entry.State == EntityState.Deleted && entry.Entity is EventInvitation inv && inv.Status != InvitationStatus.NotSent)
                throw new InvalidOperationException("A sent invitation is never deleted.");
            if (entry.State is EntityState.Modified or EntityState.Deleted && entry.Entity is InvitationEvent)
                throw new InvalidOperationException("InvitationEvent is append-only.");
            // Suppliers, their documents and a hire are deactivated/voided with a reason, never deleted — except a
            // hire may still be hard-deleted while it is still Quotation (nothing irreversible has happened yet).
            if (entry.State == EntityState.Deleted && entry.Entity is Supplier or SupplierDocument)
                throw new InvalidOperationException($"{entry.Entity.GetType().Name} rows are never deleted.");
            if (entry.State == EntityState.Deleted && entry.Entity is EventSupplier hire && !hire.IsQuotation)
                throw new InvalidOperationException("A hire past Quotation is voided, never deleted.");
            // A supplier rating is fully append-only: no update, no delete, ever.
            if (entry.State is EntityState.Modified or EntityState.Deleted && entry.Entity is SupplierRating)
                throw new InvalidOperationException("SupplierRating is append-only.");
            // Merch items/variants are deactivated with a reason, never deleted. A sale and its lines are fully
            // append-only, like SupplierRating/InvitationEvent: permanent the moment they are written.
            if (entry.State == EntityState.Deleted && entry.Entity is MerchItem or MerchVariant)
                throw new InvalidOperationException($"{entry.Entity.GetType().Name} rows are never deleted.");
            if (entry.State is EntityState.Modified or EntityState.Deleted && entry.Entity is MerchSale or MerchSaleLine)
                throw new InvalidOperationException($"{entry.Entity.GetType().Name} is append-only.");
            if (entry.State == EntityState.Deleted && entry.Entity is Htn.Domain.Merch.EventMerchSettings)
                throw new InvalidOperationException("EventMerchSettings rows are never deleted.");
            if (entry.State == EntityState.Deleted && entry.Entity is Position or PositionAssignment or PaymentAccount or StoredFile or Domain.Settings.OrgSettings
                    or Event or TicketType or Voucher or Registration or Attendee or Payment or PaymentProof
                    or Committee or CommitteeMember or EventVolunteer
                    or TaskItem or TaskComment or TaskAttachment)
                throw new InvalidOperationException($"{entry.Entity.GetType().Name} rows are never deleted.");
        }
    }
}

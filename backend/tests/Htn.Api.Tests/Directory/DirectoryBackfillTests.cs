using Htn.Api.Tests.Events;
using Htn.Api.Tests.Infrastructure;
using Htn.Domain.Leads;
using Htn.Domain.Sponsors;
using Htn.Infrastructure.Data;
using Htn.Infrastructure.OrgDirectory;
using Microsoft.EntityFrameworkCore;
using static Htn.Api.Tests.OrgDirectory.DirectoryTestData;
using static Htn.Api.Tests.Events.EventTestData;

namespace Htn.Api.Tests.OrgDirectory;

/// <summary>
/// The Phase 7 backfill: a sponsor, a lead and an event partner whose <c>name_key</c> matches a directory
/// organization all get linked by <see cref="DirectoryOps.BackfillAsync"/>; a non-matching name stays null.
/// </summary>
public sealed class DirectoryBackfillTests(HtnApiFactory factory) : IClassFixture<HtnApiFactory>
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    [Fact]
    public async Task Matching_rows_are_linked_and_non_matching_rows_stay_null()
    {
        var (_, board) = await Htn.Api.Tests.Committees.CommitteeTestData.SignedInUserAsync(factory, Htn.Domain.Organization.PersonLevel.Board);
        var eventId = await OnSaleAsync(board);
        var orgId = await CreateOrgAsync(board, NewOrg("Backfill Target Co", "Company"));

        var (sponsorId, leadId, partnerId, unmatchedSponsorId) = await factory.DbAsync(async db =>
        {
            var sponsor = Sponsor.Create(eventId,
                new NewSponsor(SponsorKind.Organization, "Backfill Target Co", null, false, null, null, null, null,
                    null, 1000m, null, null, 0, null), Guid.Empty, Now);
            var unmatchedSponsor = Sponsor.Create(eventId,
                new NewSponsor(SponsorKind.Organization, "Totally Unrelated Co", null, false, null, null, null, null,
                    null, 1000m, null, null, 0, null), Guid.Empty, Now);
            var lead = Lead.Create(eventId,
                new NewLead(LeadKind.Sponsor, "Backfill Target Co", null, null, null, null, null, 500m, null, null, null, null),
                Guid.Empty, Now);
            var partner = EventPartner.Tag(eventId, "Backfill Target Co", "Company", Guid.Empty, Now);

            db.Sponsors.AddRange(sponsor, unmatchedSponsor);
            db.Leads.Add(lead);
            db.EventPartners.Add(partner);
            await db.SaveChangesAsync();
            return (sponsor.Id, lead.Id, partner.Id, unmatchedSponsor.Id);
        });

        await factory.DbAsync(db => DirectoryOps.BackfillAsync(db));

        var linked = await factory.DbAsync(async db => (
            Sponsor: await db.Sponsors.AsNoTracking().Where(s => s.Id == sponsorId).Select(s => s.DirectoryOrgId).SingleAsync(),
            Lead: await db.Leads.AsNoTracking().Where(l => l.Id == leadId).Select(l => l.DirectoryOrgId).SingleAsync(),
            Partner: await db.EventPartners.AsNoTracking().Where(p => p.Id == partnerId).Select(p => p.DirectoryOrgId).SingleAsync(),
            Unmatched: await db.Sponsors.AsNoTracking().Where(s => s.Id == unmatchedSponsorId).Select(s => s.DirectoryOrgId).SingleAsync()));

        Assert.Equal(orgId, linked.Sponsor);
        Assert.Equal(orgId, linked.Lead);
        Assert.Equal(orgId, linked.Partner);
        Assert.Null(linked.Unmatched);
    }

    [Fact]
    public async Task Backfill_is_idempotent_and_never_overwrites_an_existing_link()
    {
        var (_, board) = await Htn.Api.Tests.Committees.CommitteeTestData.SignedInUserAsync(factory, Htn.Domain.Organization.PersonLevel.Board);
        var eventId = await OnSaleAsync(board);
        var orgId = await CreateOrgAsync(board, NewOrg("Idempotent Co", "Company"));

        var sponsorId = await factory.DbAsync(async db =>
        {
            var sponsor = Sponsor.Create(eventId,
                new NewSponsor(SponsorKind.Organization, "Idempotent Co", null, false, null, null, null, null,
                    null, 1000m, null, null, 0, null), Guid.Empty, Now);
            db.Sponsors.Add(sponsor);
            await db.SaveChangesAsync();
            return sponsor.Id;
        });

        await factory.DbAsync(db => DirectoryOps.BackfillAsync(db));
        var second = await factory.DbAsync(db => DirectoryOps.BackfillAsync(db));

        var linked = await factory.DbAsync(db => db.Sponsors.AsNoTracking().Where(s => s.Id == sponsorId).Select(s => s.DirectoryOrgId).SingleAsync());
        Assert.Equal(orgId, linked);
        // The second run touches nothing: the sponsor is already linked.
        Assert.Equal(0, second);
    }
}

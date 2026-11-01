using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Htn.Api.Tests.Infrastructure;
using Htn.Api.Tests.Tasks;
using static Htn.Api.Tests.Suppliers.SuppliersTestData;

namespace Htn.Api.Tests.Suppliers;

/// <summary>The global catalog: board-only writes, board-or-any-lead reads, with money fields stripped for a non-board reader.</summary>
public sealed class SuppliersAccessTests(HtnApiFactory factory) : IClassFixture<HtnApiFactory>
{
    [Fact]
    public async Task Anonymous_gets_401()
    {
        var anon = factory.NewClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync("/api/suppliers")).StatusCode);
    }

    [Fact]
    public async Task A_goon_who_leads_nothing_is_denied_every_route()
    {
        var s = await SceneAsync(factory);
        var supplierId = await CreateSupplierAsync(s.Board);

        Assert.Equal(HttpStatusCode.Forbidden, (await s.Goon.GetAsync("/api/suppliers")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await s.Goon.GetAsync($"/api/suppliers/{supplierId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await s.Goon.PostAsync("/api/suppliers", NewSupplier())).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await s.Goon.SendAsync(HttpMethod.Patch, $"/api/suppliers/{supplierId}", new { issuesOr = true }, true)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await s.Goon.PostAsync($"/api/suppliers/{supplierId}/deactivate", new { reason = "x" })).StatusCode);
    }

    [Fact]
    public async Task An_apprentice_is_denied_every_route()
    {
        var s = await SceneAsync(factory);
        Assert.Equal(HttpStatusCode.Forbidden, (await s.Apprentice.GetAsync("/api/suppliers")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await s.Apprentice.PostAsync("/api/suppliers", NewSupplier())).StatusCode);
    }

    [Fact]
    public async Task A_committee_lead_can_read_but_not_write()
    {
        var s = await SceneAsync(factory);
        var supplierId = await CreateSupplierAsync(s.Board);

        Assert.Equal(HttpStatusCode.OK, (await s.CommitteeLead.GetAsync("/api/suppliers")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await s.CommitteeLead.GetAsync($"/api/suppliers/{supplierId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await s.CommitteeLead.PostAsync("/api/suppliers", NewSupplier())).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await s.CommitteeLead.PostAsync($"/api/suppliers/{supplierId}/deactivate", new { reason = "x" })).StatusCode);
    }

    /// <summary>
    /// The money-stripping DTO shape: a committee lead's response has no <c>boardOnly</c> key on the list or detail
    /// at all, not a null one, so a client can't mistake "omitted" for "blank".
    /// </summary>
    [Fact]
    public async Task Board_only_fields_are_literally_absent_from_a_leads_response()
    {
        var s = await SceneAsync(factory);
        var supplierId = await CreateSupplierAsync(s.Board);

        var leadList = await ApiClient.JsonAsync(await s.CommitteeLead.GetAsync("/api/suppliers"));
        var leadRow = leadList.EnumerateArray().Single(x => x.GetProperty("id").GetGuid() == supplierId);
        Assert.False(leadRow.TryGetProperty("boardOnly", out _), "boardOnly must be entirely absent for a non-board reader.");

        var leadDetail = await ApiClient.JsonAsync(await s.CommitteeLead.GetAsync($"/api/suppliers/{supplierId}"));
        Assert.False(leadDetail.GetProperty("supplier").TryGetProperty("boardOnly", out _));

        var boardList = await ApiClient.JsonAsync(await s.Board.GetAsync("/api/suppliers"));
        var boardRow = boardList.EnumerateArray().Single(x => x.GetProperty("id").GetGuid() == supplierId);
        Assert.True(boardRow.TryGetProperty("boardOnly", out var boardOnly));
        Assert.Equal("123-456-789", boardOnly.GetProperty("tin").GetString());
    }

    [Fact]
    public async Task Category_and_name_are_frozen_after_creation()
    {
        var s = await SceneAsync(factory);
        var supplierId = await CreateSupplierAsync(s.Board, NewSupplier(name: "Frozen Co.", category: "Venue"));

        // PATCH doesn't even accept category/name fields — only the editable ones — so attempting the update
        // through the contract that exists confirms they stay untouched.
        var patch = await s.Board.SendAsync(HttpMethod.Patch, $"/api/suppliers/{supplierId}",
            new { contactName = "New Contact", issuesOr = false }, true);
        Assert.Equal(HttpStatusCode.OK, patch.StatusCode);
        var detail = await ApiClient.JsonAsync(patch);
        Assert.Equal("Frozen Co.", detail.GetProperty("name").GetString());
        Assert.Equal("Venue", detail.GetProperty("category").GetString());
    }

    [Fact]
    public async Task A_duplicate_name_is_refused_with_409()
    {
        var s = await SceneAsync(factory);
        await CreateSupplierAsync(s.Board, NewSupplier(name: "Unique Supplier Co."));
        var again = await s.Board.PostAsync("/api/suppliers", NewSupplier(name: "unique   supplier co"));
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal("duplicate_name", (await ApiClient.JsonAsync(again)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Deactivating_twice_is_refused()
    {
        var s = await SceneAsync(factory);
        var supplierId = await CreateSupplierAsync(s.Board);
        Assert.Equal(HttpStatusCode.NoContent, (await s.Board.PostAsync($"/api/suppliers/{supplierId}/deactivate", new { reason = "closed" })).StatusCode);
        var again = await s.Board.PostAsync($"/api/suppliers/{supplierId}/deactivate", new { reason = "again" });
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal("already_deactivated", (await ApiClient.JsonAsync(again)).GetProperty("code").GetString());
    }
}

/// <summary>Documents: upload/void are board-only, download is Suppliers.View (lead-visible, not board-only).</summary>
public sealed class SupplierDocumentsTests(HtnApiFactory factory) : IClassFixture<HtnApiFactory>
{
    [Fact]
    public async Task A_lead_can_download_but_not_upload_or_void()
    {
        var s = await SceneAsync(factory);
        var supplierId = await CreateSupplierAsync(s.Board);
        var upload = await s.Board.PostFormFileAsync($"/api/suppliers/{supplierId}/documents",
            new Dictionary<string, string?> { ["kind"] = "Bir2303" }, TaskTestData.Pdf, "bir.pdf", "application/pdf");
        Assert.Equal(HttpStatusCode.OK, upload.StatusCode);
        var docId = (await ApiClient.JsonAsync(upload)).GetProperty("id").GetGuid();

        var deniedUpload = await s.CommitteeLead.PostFormFileAsync($"/api/suppliers/{supplierId}/documents",
            new Dictionary<string, string?> { ["kind"] = "Other" }, TaskTestData.Pdf, "other.pdf", "application/pdf");
        Assert.Equal(HttpStatusCode.Forbidden, deniedUpload.StatusCode);

        var download = await s.CommitteeLead.GetAsync($"/api/suppliers/{supplierId}/documents/{docId}/download");
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal("application/pdf", download.Content.Headers.ContentType?.MediaType);
        Assert.Equal("nosniff", download.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Contains("attachment", download.Content.Headers.ContentDisposition?.DispositionType ?? download.Headers.ToString());

        var deniedVoid = await s.CommitteeLead.PostAsync($"/api/suppliers/{supplierId}/documents/{docId}/void", new { reason = "x" });
        Assert.Equal(HttpStatusCode.Forbidden, deniedVoid.StatusCode);
    }

    [Fact]
    public async Task Voiding_twice_is_refused()
    {
        var s = await SceneAsync(factory);
        var supplierId = await CreateSupplierAsync(s.Board);
        var upload = await s.Board.PostFormFileAsync($"/api/suppliers/{supplierId}/documents",
            new Dictionary<string, string?> { ["kind"] = "BusinessPermit" }, TaskTestData.Pdf, "permit.pdf", "application/pdf");
        var docId = (await ApiClient.JsonAsync(upload)).GetProperty("id").GetGuid();

        Assert.Equal(HttpStatusCode.OK, (await s.Board.PostAsync($"/api/suppliers/{supplierId}/documents/{docId}/void", new { reason = "superseded" })).StatusCode);
        var again = await s.Board.PostAsync($"/api/suppliers/{supplierId}/documents/{docId}/void", new { reason = "again" });
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal("document_already_voided", (await ApiClient.JsonAsync(again)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task An_unsupported_file_type_is_rejected()
    {
        var s = await SceneAsync(factory);
        var supplierId = await CreateSupplierAsync(s.Board);
        var upload = await s.Board.PostFormFileAsync($"/api/suppliers/{supplierId}/documents",
            new Dictionary<string, string?> { ["kind"] = "Other" }, [1, 2, 3, 4], "not-a-file.bin", "application/octet-stream");
        Assert.Equal(HttpStatusCode.BadRequest, upload.StatusCode);
    }
}

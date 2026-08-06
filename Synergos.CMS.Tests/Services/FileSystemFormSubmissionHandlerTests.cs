using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Services;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// Tests para <see cref="FileSystemFormSubmissionHandler"/> covering
/// el path nuevo de Ola 115 — IFormSubmissionReader.DeleteAsync.
/// (Batch C, Ola 209)
/// </summary>
public sealed class FileSystemFormSubmissionHandlerTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly StubHostEnvironment _env;
    private readonly FileSystemFormSubmissionHandler _sut;

    public FileSystemFormSubmissionHandlerTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "syn-forms-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
        _env = new StubHostEnvironment(_tempRoot);
        var options = Options.Create(new FormsSettings { StorageRoot = "App_Data/syn-form-submissions/" });
        _sut = new FileSystemFormSubmissionHandler(
            options,
            _env,
            NullLogger<FileSystemFormSubmissionHandler>.Instance);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            try { Directory.Delete(_tempRoot, recursive: true); }
            catch { /* best-effort */ }
        }
    }

    [Fact]
    public async Task SubmitAsync_PersistsToFile()
    {
        var result = await _sut.SubmitAsync(
            new FormSubmissionRequest(
                FormKey: "contact",
                Fields: new Dictionary<string, string> { ["name"] = "Alice", ["email"] = "a@x" },
                ClientIp: "127.0.0.1",
                UserAgent: "ua",
                Referrer: "ref",
                ReceivedAtUtc: DateTime.UtcNow),
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.NotNull(result.StorageReference);
        Assert.True(File.Exists(result.StorageReference!));
    }

    [Fact]
    public async Task DeleteAsync_RemovesPersistedFile()
    {
        // Create a known submission first.
        var receivedAt = new DateTime(2026, 4, 27, 10, 0, 0, DateTimeKind.Utc);
        await _sut.SubmitAsync(
            new FormSubmissionRequest(
                FormKey: "contact",
                Fields: new Dictionary<string, string> { ["x"] = "y" },
                ClientIp: null,
                UserAgent: null,
                Referrer: null,
                ReceivedAtUtc: receivedAt),
            CancellationToken.None);

        // Locate the persisted file via GetRecent.
        var page = _sut.GetRecent(1, 100, formKeyFilter: "contact");
        Assert.Single(page.Items);
        var item = page.Items[0];

        var deleted = await _sut.DeleteAsync("contact", item.StorageId, CancellationToken.None);

        Assert.True(deleted);
        Assert.False(File.Exists(item.StorageReference));
        // Listing should now be empty.
        var afterPage = _sut.GetRecent(1, 100, formKeyFilter: "contact");
        Assert.Empty(afterPage.Items);
    }

    [Fact]
    public async Task DeleteAsync_NonexistentFile_ReturnsFalse()
    {
        var deleted = await _sut.DeleteAsync("contact", "does-not-exist", CancellationToken.None);

        Assert.False(deleted);
    }

    [Fact]
    public async Task DeleteAsync_EmptyArgs_ReturnsFalse()
    {
        Assert.False(await _sut.DeleteAsync("", "x", CancellationToken.None));
        Assert.False(await _sut.DeleteAsync("contact", "", CancellationToken.None));
    }

    [Fact]
    public async Task DeleteAsync_PathTraversalAttempt_RejectedBySanitizer()
    {
        // Setup a file with a normal id.
        await _sut.SubmitAsync(
            new FormSubmissionRequest(
                FormKey: "contact",
                Fields: new Dictionary<string, string> { ["x"] = "y" },
                ClientIp: null,
                UserAgent: null,
                Referrer: null,
                ReceivedAtUtc: DateTime.UtcNow),
            CancellationToken.None);

        // Attempt path traversal — should fail without escaping the storage root.
        var deleted = await _sut.DeleteAsync("contact", "../../../etc/passwd", CancellationToken.None);

        Assert.False(deleted);
    }

    [Fact]
    public async Task GetSubmission_AfterDelete_ReturnsNull()
    {
        await _sut.SubmitAsync(
            new FormSubmissionRequest(
                FormKey: "contact",
                Fields: new Dictionary<string, string> { ["a"] = "b" },
                ClientIp: null,
                UserAgent: null,
                Referrer: null,
                ReceivedAtUtc: DateTime.UtcNow),
            CancellationToken.None);

        var item = _sut.GetRecent(1, 100, formKeyFilter: "contact").Items.Single();
        await _sut.DeleteAsync("contact", item.StorageId, CancellationToken.None);

        var detail = _sut.GetSubmission("contact", item.StorageId);

        Assert.Null(detail);
    }

    private sealed class StubHostEnvironment : IHostEnvironment
    {
        public StubHostEnvironment(string contentRootPath) =>
            ContentRootPath = contentRootPath;

        public string ApplicationName { get; set; } = "Synergos.CMS.Tests";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
        public string ContentRootPath { get; set; }
        public string EnvironmentName { get; set; } = "Tests";
    }
}

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Services;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// Tests para <see cref="FileSystemAuditTrailWriter"/> (Olas 153-154 +
/// 163-164, ADRs 0067 + 0071). Verifica idempotency, GetRecent, GetByDateRange
/// con file isolation per-test via temp directory.
/// </summary>
public sealed class FileSystemAuditTrailWriterTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly StubHostEnvironment _env;
    private readonly FileSystemAuditTrailWriter _sut;

    public FileSystemAuditTrailWriterTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "syn-audit-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
        _env = new StubHostEnvironment(_tempRoot);
        _sut = new FileSystemAuditTrailWriter(_env, NullLogger<FileSystemAuditTrailWriter>.Instance);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            try { Directory.Delete(_tempRoot, recursive: true); }
            catch { /* test cleanup best-effort */ }
        }
    }

    [Fact]
    public async Task WriteAsync_PersistsEvent()
    {
        var evt = new AuditEvent("id1", DateTime.UtcNow, "actor@example.com", "Actor",
            "comment.approve", "node=42 commentId=abc", "success", "");

        await _sut.WriteAsync(evt, CancellationToken.None);

        var recent = _sut.GetRecent(10);
        Assert.Single(recent);
        Assert.Equal("id1", recent[0].Id);
        Assert.Equal("comment.approve", recent[0].Action);
    }

    [Fact]
    public async Task WriteAsync_SameId_Idempotent()
    {
        var evt = new AuditEvent("dup-id", DateTime.UtcNow, "actor@example.com", "Actor",
            "comment.approve", "node=1", "success", "");

        await _sut.WriteAsync(evt, CancellationToken.None);
        await _sut.WriteAsync(evt, CancellationToken.None);  // segunda call dedupe

        var recent = _sut.GetRecent(10);
        Assert.Single(recent);
    }

    [Fact]
    public async Task GetRecent_OrderedDesc()
    {
        var now = DateTime.UtcNow;
        await _sut.WriteAsync(new AuditEvent("a", now.AddMinutes(-5), "x", "X", "act", "r", "success", ""), CancellationToken.None);
        await _sut.WriteAsync(new AuditEvent("b", now.AddMinutes(-1), "x", "X", "act", "r", "success", ""), CancellationToken.None);
        await _sut.WriteAsync(new AuditEvent("c", now.AddMinutes(-3), "x", "X", "act", "r", "success", ""), CancellationToken.None);

        var recent = _sut.GetRecent(10);
        // FileSystemAuditTrailWriter reads files desc + lines desc (LIFO)
        // — same-day file means reverse-of-write-order.
        Assert.Equal(3, recent.Count);
        Assert.Equal("c", recent[0].Id);  // last written
        Assert.Equal("b", recent[1].Id);
        Assert.Equal("a", recent[2].Id);
    }

    [Fact]
    public async Task GetRecent_ActorEmailFilter()
    {
        var now = DateTime.UtcNow;
        await _sut.WriteAsync(new AuditEvent("1", now, "alice@x", "Alice", "act", "r", "success", ""), CancellationToken.None);
        await _sut.WriteAsync(new AuditEvent("2", now, "bob@x", "Bob", "act", "r", "success", ""), CancellationToken.None);
        await _sut.WriteAsync(new AuditEvent("3", now, "alice@x", "Alice", "act", "r", "success", ""), CancellationToken.None);

        var aliceOnly = _sut.GetRecent(10, actorEmailFilter: "alice@x");

        Assert.Equal(2, aliceOnly.Count);
        Assert.All(aliceOnly, e => Assert.Equal("alice@x", e.ActorEmail));
    }

    [Fact]
    public async Task GetRecent_ActionFilter_ContainsMatch()
    {
        var now = DateTime.UtcNow;
        await _sut.WriteAsync(new AuditEvent("1", now, "x", "X", "comment.approve", "r", "success", ""), CancellationToken.None);
        await _sut.WriteAsync(new AuditEvent("2", now, "x", "X", "form.delete", "r", "success", ""), CancellationToken.None);
        await _sut.WriteAsync(new AuditEvent("3", now, "x", "X", "comment.reject", "r", "success", ""), CancellationToken.None);

        var commentOnly = _sut.GetRecent(10, actionFilter: "comment");

        Assert.Equal(2, commentOnly.Count);
        Assert.All(commentOnly, e => Assert.Contains("comment", e.Action));
    }

    [Fact]
    public void GetRecent_EmptyDirectory_ReturnsEmpty()
    {
        var recent = _sut.GetRecent(10);

        Assert.Empty(recent);
    }

    [Fact]
    public async Task GetByDateRange_FiltersToWindow()
    {
        var now = DateTime.UtcNow;
        await _sut.WriteAsync(new AuditEvent("a", now.AddHours(-1), "x", "X", "act", "r", "success", ""), CancellationToken.None);
        await _sut.WriteAsync(new AuditEvent("b", now, "x", "X", "act", "r", "success", ""), CancellationToken.None);

        var inRange = _sut.GetByDateRange(
            fromUtc: now.AddMinutes(-30),
            toUtc: now.AddMinutes(30),
            maxItems: 10);

        Assert.Single(inRange);
        Assert.Equal("b", inRange[0].Id);
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

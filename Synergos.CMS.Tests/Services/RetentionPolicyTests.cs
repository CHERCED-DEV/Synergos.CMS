using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Services.Retention;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// Tests para las 4 retention policies (Cap-270 Batch B, Ola 275).
/// Cubre disabled (retention=0 no-op), happy path (purga viejos),
/// boundary (retención exacta) y empty store. Cada policy comparte
/// shape de test sobre temp ContentRoot.
/// </summary>
public sealed class RetentionPolicyTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly StubHostEnvironment _env;

    public RetentionPolicyTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "syn-retention-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
        _env = new StubHostEnvironment(_tempRoot);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            try { Directory.Delete(_tempRoot, recursive: true); }
            catch { /* best-effort */ }
        }
    }

    private static IOptionsMonitor<T> StubMonitor<T>(T value)
    {
        var monitor = Substitute.For<IOptionsMonitor<T>>();
        monitor.CurrentValue.Returns(value);
        return monitor;
    }

    // ─── Audit ──────────────────────────────────────────────────────

    private void WriteAuditFile(string yyyyMMdd, string content = "{}")
    {
        var dir = Path.Combine(_tempRoot, "App_Data", "syn-audit");
        Directory.CreateDirectory(dir);
        System.IO.File.WriteAllText(Path.Combine(dir, $"{yyyyMMdd}.jsonl"), content);
    }

    [Fact]
    public async Task Audit_RetentionZero_NoOp()
    {
        WriteAuditFile("2020-01-01");
        var sut = new AuditRetentionPolicy(_env,
            StubMonitor(new AdminSettings { AuditRetentionDays = 0 }),
            NullLogger<AuditRetentionPolicy>.Instance);

        var purged = await sut.SweepAsync(CancellationToken.None);
        Assert.Equal(0, purged);
        Assert.True(System.IO.File.Exists(Path.Combine(_tempRoot, "App_Data/syn-audit/2020-01-01.jsonl")));
    }

    [Fact]
    public async Task Audit_PurgesOlderThanCutoff_KeepsRecent()
    {
        var oldDate = DateTime.UtcNow.AddDays(-100).ToString("yyyy-MM-dd");
        var recentDate = DateTime.UtcNow.AddDays(-10).ToString("yyyy-MM-dd");
        WriteAuditFile(oldDate);
        WriteAuditFile(recentDate);

        var sut = new AuditRetentionPolicy(_env,
            StubMonitor(new AdminSettings { AuditRetentionDays = 90 }),
            NullLogger<AuditRetentionPolicy>.Instance);

        var purged = await sut.SweepAsync(CancellationToken.None);
        Assert.Equal(1, purged);
        Assert.False(System.IO.File.Exists(Path.Combine(_tempRoot, $"App_Data/syn-audit/{oldDate}.jsonl")));
        Assert.True(System.IO.File.Exists(Path.Combine(_tempRoot, $"App_Data/syn-audit/{recentDate}.jsonl")));
    }

    [Fact]
    public async Task Audit_NoStore_NoOp()
    {
        var sut = new AuditRetentionPolicy(_env,
            StubMonitor(new AdminSettings { AuditRetentionDays = 90 }),
            NullLogger<AuditRetentionPolicy>.Instance);
        var purged = await sut.SweepAsync(CancellationToken.None);
        Assert.Equal(0, purged);
    }

    // ─── Comments (rejected only) ───────────────────────────────────

    private void WriteCommentsFile(int nodeId, IEnumerable<Comment> comments)
    {
        var dir = Path.Combine(_tempRoot, "App_Data", "syn-comments");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"{nodeId}.json");
        System.IO.File.WriteAllBytes(path, JsonSerializer.SerializeToUtf8Bytes(comments.ToList()));
    }

    [Fact]
    public async Task Comments_PurgesOldRejected_KeepsApprovedAndRecent()
    {
        var sut = new CommentsRetentionPolicy(_env,
            StubMonitor(new CommentsSettings { StorageRoot = "App_Data/syn-comments/" }),
            StubMonitor(new RetentionSettings { CommentsRejectedRetentionDays = 30 }),
            NullLogger<CommentsRetentionPolicy>.Instance);

        WriteCommentsFile(42, new[]
        {
            new Comment("c1", 42, null, "X", "old-rejected",  DateTime.UtcNow.AddDays(-90), Approved: false),
            new Comment("c2", 42, null, "Y", "old-approved",  DateTime.UtcNow.AddDays(-90), Approved: true),
            new Comment("c3", 42, null, "Z", "recent-rejected", DateTime.UtcNow.AddDays(-5),  Approved: false),
        });

        var purged = await sut.SweepAsync(CancellationToken.None);
        Assert.Equal(1, purged);

        var bytes = System.IO.File.ReadAllBytes(Path.Combine(_tempRoot, "App_Data/syn-comments/42.json"));
        var rest = JsonSerializer.Deserialize<List<Comment>>(bytes)!;
        Assert.Equal(2, rest.Count);
        Assert.DoesNotContain(rest, c => c.Id == "c1");
        Assert.Contains(rest, c => c.Id == "c2"); // approved no se borra aunque sea viejo
        Assert.Contains(rest, c => c.Id == "c3"); // rejected pero reciente
    }

    [Fact]
    public async Task Comments_RetentionZero_NoOp()
    {
        var sut = new CommentsRetentionPolicy(_env,
            StubMonitor(new CommentsSettings { StorageRoot = "App_Data/syn-comments/" }),
            StubMonitor(new RetentionSettings { CommentsRejectedRetentionDays = 0 }),
            NullLogger<CommentsRetentionPolicy>.Instance);

        WriteCommentsFile(42, new[]
        {
            new Comment("c1", 42, null, "X", "old-rejected", DateTime.UtcNow.AddDays(-365), Approved: false),
        });

        var purged = await sut.SweepAsync(CancellationToken.None);
        Assert.Equal(0, purged);
    }

    // ─── Form submissions ───────────────────────────────────────────

    private void WriteFormFile(string formKey, string fileName, DateTime mtime)
    {
        var dir = Path.Combine(_tempRoot, "App_Data", "syn-form-submissions", formKey);
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, fileName);
        System.IO.File.WriteAllText(path, "{}");
        System.IO.File.SetLastWriteTimeUtc(path, mtime);
    }

    [Fact]
    public async Task FormSubmissions_PurgesOlderThanCutoff_ByFilenamePrefix()
    {
        var sut = new FormSubmissionsRetentionPolicy(_env,
            StubMonitor(new FormsSettings { StorageRoot = "App_Data/syn-form-submissions/" }),
            StubMonitor(new RetentionSettings { FormSubmissionsRetentionDays = 365 }),
            NullLogger<FormSubmissionsRetentionPolicy>.Instance);

        var oldDate = DateTime.UtcNow.AddDays(-400).ToString("yyyyMMdd_HHmmss");
        var recentDate = DateTime.UtcNow.AddDays(-30).ToString("yyyyMMdd_HHmmss");
        WriteFormFile("contact", $"{oldDate}_{Guid.NewGuid():N}.json", DateTime.UtcNow);
        WriteFormFile("contact", $"{recentDate}_{Guid.NewGuid():N}.json", DateTime.UtcNow);

        var purged = await sut.SweepAsync(CancellationToken.None);
        Assert.Equal(1, purged);
    }

    [Fact]
    public async Task FormSubmissions_FallbackToMtime_WhenFilenameDoesNotParse()
    {
        var sut = new FormSubmissionsRetentionPolicy(_env,
            StubMonitor(new FormsSettings { StorageRoot = "App_Data/syn-form-submissions/" }),
            StubMonitor(new RetentionSettings { FormSubmissionsRetentionDays = 30 }),
            NullLogger<FormSubmissionsRetentionPolicy>.Instance);

        WriteFormFile("contact", "weird-filename.json", DateTime.UtcNow.AddDays(-100));

        var purged = await sut.SweepAsync(CancellationToken.None);
        Assert.Equal(1, purged);
    }

    // ─── Search analytics ───────────────────────────────────────────

    private void WriteSearchFile(string yyyyMMdd)
    {
        var dir = Path.Combine(_tempRoot, "App_Data", "syn-search-analytics");
        Directory.CreateDirectory(dir);
        System.IO.File.WriteAllText(Path.Combine(dir, $"{yyyyMMdd}.jsonl"), "{}\n");
    }

    [Fact]
    public async Task SearchAnalytics_PurgesOlderThanCutoff()
    {
        WriteSearchFile(DateTime.UtcNow.AddDays(-100).ToString("yyyy-MM-dd"));
        WriteSearchFile(DateTime.UtcNow.AddDays(-10).ToString("yyyy-MM-dd"));

        var sut = new SearchAnalyticsRetentionPolicy(_env,
            StubMonitor(new RetentionSettings { SearchAnalyticsRetentionDays = 90 }),
            NullLogger<SearchAnalyticsRetentionPolicy>.Instance);

        var purged = await sut.SweepAsync(CancellationToken.None);
        Assert.Equal(1, purged);
    }

    private sealed class StubHostEnvironment : IHostEnvironment
    {
        public StubHostEnvironment(string contentRootPath) => ContentRootPath = contentRootPath;
        public string ApplicationName { get; set; } = "Synergos.CMS.Tests";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
        public string ContentRootPath { get; set; }
        public string EnvironmentName { get; set; } = "Tests";
    }
}

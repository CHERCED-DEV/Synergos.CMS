using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Services;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// Tests para <see cref="FileSystemGdprRtbfCoordinator"/> (Cap-240
/// Batch B, Ola 235). Cubre el flujo RTBF end-to-end con stores
/// filesystem reales (temp dir) + IMemberService stubbed via NSubstitute.
/// </summary>
public sealed class FileSystemGdprRtbfCoordinatorTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly StubHostEnvironment _env;
    private readonly IMemberService _memberService = Substitute.For<IMemberService>();
    private readonly IMemberRosterWriter _rosterWriter = Substitute.For<IMemberRosterWriter>();
    private readonly IAuditTrailWriter _audit = Substitute.For<IAuditTrailWriter>();
    private readonly IHealthcareDataAnonymizer _healthcare = Substitute.For<IHealthcareDataAnonymizer>();
    private readonly FileSystemGdprRtbfCoordinator _sut;

    public FileSystemGdprRtbfCoordinatorTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "syn-gdpr-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
        _env = new StubHostEnvironment(_tempRoot);

        var commentsOpts = Options.Create(new CommentsSettings { StorageRoot = "App_Data/syn-comments/" });
        var formsOpts = Options.Create(new FormsSettings { StorageRoot = "App_Data/syn-form-submissions/" });

        _sut = new FileSystemGdprRtbfCoordinator(
            _memberService,
            _rosterWriter,
            _audit,
            _env,
            commentsOpts,
            formsOpts,
            _healthcare,
            NullLogger<FileSystemGdprRtbfCoordinator>.Instance);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            try { Directory.Delete(_tempRoot, recursive: true); }
            catch { /* best-effort */ }
        }
    }

    private static IMember StubMember(Guid key, string email, string name = "Alice")
    {
        var member = Substitute.For<IMember>();
        member.Key.Returns(key);
        member.Email.Returns(email);
        member.Name.Returns(name);
        return member;
    }

    private void WriteCommentsFile(int nodeId, IEnumerable<Comment> comments)
    {
        var dir = Path.Combine(_tempRoot, "App_Data", "syn-comments");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"{nodeId}.json");
        var json = JsonSerializer.SerializeToUtf8Bytes(comments.ToList());
        System.IO.File.WriteAllBytes(path, json);
    }

    private void WriteFormSubmissionFile(string formKey, string fileName, IDictionary<string, string> fields)
    {
        var dir = Path.Combine(_tempRoot, "App_Data", "syn-form-submissions", formKey);
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, fileName);
        var payload = new
        {
            formKey,
            receivedAtUtc = DateTime.UtcNow,
            clientIp = "127.0.0.1",
            userAgent = "ua",
            referrer = "ref",
            fields,
        };
        var json = JsonSerializer.SerializeToUtf8Bytes(payload);
        System.IO.File.WriteAllBytes(path, json);
    }

    [Fact]
    public async Task ProcessRequestAsync_MemberNotFound_ReturnsFailureAndAudits()
    {
        var key = Guid.NewGuid();
        _memberService.GetByKey(key).Returns((IMember?)null);

        var result = await _sut.ProcessRequestAsync(key, "admin@x", CancellationToken.None);

        Assert.False(result.MemberDeleted);
        Assert.Equal("member-not-found", result.FailureReason);
        Assert.Equal(0, result.CommentsAnonymized);
        Assert.Equal(0, result.FormSubmissionsAnonymized);
        Assert.True(result.AuditPreserved);
        await _audit.Received(1).WriteAsync(
            Arg.Is<AuditEvent>(e =>
                e.Action == "gdpr.rtbf-processed" &&
                e.Outcome == "failure" &&
                e.Detail == "member-not-found"),
            Arg.Any<CancellationToken>());
        await _rosterWriter.DidNotReceive().DeleteAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessRequestAsync_HappyPath_AnonymizesAndDeletesAndAudits()
    {
        var key = Guid.NewGuid();
        var keyStr = key.ToString();
        var email = "alice@example.com";
        var member = StubMember(key, email);
        _memberService.GetByKey(key).Returns(member);
        _rosterWriter.DeleteAsync(key, Arg.Any<CancellationToken>()).Returns(true);

        // 2 comments del Member en nodo 100, 1 ajeno; 1 comment del
        // Member en nodo 200.
        WriteCommentsFile(100, new[]
        {
            new Comment("c1", 100, MemberKey: keyStr, AuthorName: "Alice", Body: "hi", CreatedAtUtc: DateTime.UtcNow, Approved: true),
            new Comment("c2", 100, MemberKey: keyStr, AuthorName: "Alice", Body: "hi2", CreatedAtUtc: DateTime.UtcNow, Approved: true),
            new Comment("c3", 100, MemberKey: Guid.NewGuid().ToString(), AuthorName: "Bob", Body: "ajeno", CreatedAtUtc: DateTime.UtcNow, Approved: true),
        });
        WriteCommentsFile(200, new[]
        {
            new Comment("c4", 200, MemberKey: keyStr, AuthorName: "Alice", Body: "hi3", CreatedAtUtc: DateTime.UtcNow, Approved: true),
        });

        // 1 form submission con email del Member; otra con email distinto.
        WriteFormSubmissionFile("contact", "20260101_aaaa.json", new Dictionary<string, string>
        {
            ["name"] = "Alice",
            ["email"] = email,
            ["message"] = "hola",
        });
        WriteFormSubmissionFile("contact", "20260101_bbbb.json", new Dictionary<string, string>
        {
            ["name"] = "Bob",
            ["email"] = "bob@example.com",
        });

        var result = await _sut.ProcessRequestAsync(key, "admin@x", CancellationToken.None);

        Assert.True(result.MemberDeleted);
        Assert.Equal(email, result.OriginalEmail);
        Assert.Equal(3, result.CommentsAnonymized); // c1+c2+c4 (no c3)
        Assert.Equal(1, result.FormSubmissionsAnonymized); // solo el de Alice
        Assert.True(result.AuditPreserved);
        Assert.Null(result.FailureReason);

        // Verifica que Bob siga intacto en el JSON de nodo 100.
        var node100 = JsonSerializer.Deserialize<List<Comment>>(
            System.IO.File.ReadAllBytes(Path.Combine(_tempRoot, "App_Data/syn-comments/100.json")))!;
        Assert.Equal(3, node100.Count);
        var bob = node100.Single(c => c.Id == "c3");
        Assert.Equal("Bob", bob.AuthorName);
        var alice = node100.Where(c => c.Id == "c1" || c.Id == "c2").ToList();
        Assert.All(alice, c =>
        {
            Assert.Equal("[deleted]", c.AuthorName);
            Assert.Null(c.MemberKey);
        });

        // Form submission de Alice anonimizado, body de Bob intacto.
        var alicePath = Path.Combine(_tempRoot, "App_Data/syn-form-submissions/contact/20260101_aaaa.json");
        var aliceJson = System.IO.File.ReadAllText(alicePath);
        Assert.Contains("[deleted]@gdpr.local", aliceJson);
        Assert.DoesNotContain("alice@example.com", aliceJson);
        var bobPath = Path.Combine(_tempRoot, "App_Data/syn-form-submissions/contact/20260101_bbbb.json");
        var bobJson = System.IO.File.ReadAllText(bobPath);
        Assert.Contains("bob@example.com", bobJson);

        await _rosterWriter.Received(1).DeleteAsync(key, Arg.Any<CancellationToken>());
        await _audit.Received(1).WriteAsync(
            Arg.Is<AuditEvent>(e =>
                e.Action == "gdpr.rtbf-processed" &&
                e.Outcome == "success" &&
                e.ActorEmail == "admin@x"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessRequestAsync_DeleteFails_ReturnsPartialAndAudits()
    {
        var key = Guid.NewGuid();
        var member = StubMember(key, "alice@example.com");
        _memberService.GetByKey(key).Returns(member);
        _rosterWriter.DeleteAsync(key, Arg.Any<CancellationToken>()).Returns(false);

        var result = await _sut.ProcessRequestAsync(key, "admin@x", CancellationToken.None);

        Assert.False(result.MemberDeleted);
        Assert.Equal("delete-failed", result.FailureReason);
        Assert.Equal("alice@example.com", result.OriginalEmail);
        await _audit.Received(1).WriteAsync(
            Arg.Is<AuditEvent>(e => e.Outcome == "partial"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessRequestAsync_NoStoresExist_StillCompletesAndAuditsSuccess()
    {
        var key = Guid.NewGuid();
        var member = StubMember(key, "ghost@example.com");
        _memberService.GetByKey(key).Returns(member);
        _rosterWriter.DeleteAsync(key, Arg.Any<CancellationToken>()).Returns(true);

        var result = await _sut.ProcessRequestAsync(key, "admin@x", CancellationToken.None);

        Assert.True(result.MemberDeleted);
        Assert.Equal(0, result.CommentsAnonymized);
        Assert.Equal(0, result.FormSubmissionsAnonymized);
        Assert.Null(result.FailureReason);
    }

    [Fact]
    public async Task ProcessRequestAsync_IdempotentReplay_NoExtraChangesOnSecondRun()
    {
        var key = Guid.NewGuid();
        var keyStr = key.ToString();
        var member = StubMember(key, "alice@example.com");
        _memberService.GetByKey(key).Returns(member);
        _rosterWriter.DeleteAsync(key, Arg.Any<CancellationToken>()).Returns(true);

        WriteCommentsFile(50, new[]
        {
            new Comment("c1", 50, MemberKey: keyStr, AuthorName: "Alice", Body: "x", CreatedAtUtc: DateTime.UtcNow, Approved: true),
        });

        var first = await _sut.ProcessRequestAsync(key, "admin@x", CancellationToken.None);
        Assert.Equal(1, first.CommentsAnonymized);

        // Second run — el Member ya no existe (mocked) — pero los comments
        // ya están anonymized (MemberKey=null), así que el coordinator ve
        // 0 matches.
        _memberService.GetByKey(key).Returns((IMember?)null);
        var second = await _sut.ProcessRequestAsync(key, "admin@x", CancellationToken.None);
        Assert.Equal(0, second.CommentsAnonymized);
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

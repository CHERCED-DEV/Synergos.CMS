using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Services;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// Tests para <see cref="UmbracoMemberRosterWriter"/> (Olas 155-156 +
/// 181-184 + 227-228 + 232). Cubre Lock/Unlock/Delete/SetRoles +
/// SendPasswordResetAsync via NSubstitute. La inyección por interfaz
/// <see cref="IEmailTemplateRenderer"/> (Ola 231) habilita stubbing
/// directo del renderer.
/// </summary>
public sealed class UmbracoMemberRosterWriterTests
{
    private readonly IMemberService _memberService = Substitute.For<IMemberService>();
    private readonly IMemberAuthService _memberAuth = Substitute.For<IMemberAuthService>();
    private readonly IEmailService _emailService = Substitute.For<IEmailService>();
    private readonly IEmailTemplateRenderer _emailRenderer = Substitute.For<IEmailTemplateRenderer>();
    private readonly IBrandingProvider _branding = Substitute.For<IBrandingProvider>();
    private readonly IHttpContextAccessor _httpContextAccessor = Substitute.For<IHttpContextAccessor>();
    private readonly UmbracoMemberRosterWriter _sut;

    public UmbracoMemberRosterWriterTests()
    {
        _sut = new UmbracoMemberRosterWriter(
            _memberService,
            _memberAuth,
            _emailService,
            _emailRenderer,
            _branding,
            _httpContextAccessor,
            NullLogger<UmbracoMemberRosterWriter>.Instance);
    }

    private static IMember StubMember(Guid key, int id = 42, string email = "alice@example.com",
        string name = "Alice", bool isLockedOut = false, int failedAttempts = 0)
    {
        var member = Substitute.For<IMember>();
        member.Key.Returns(key);
        member.Id.Returns(id);
        member.Email.Returns(email);
        member.Name.Returns(name);
        member.IsLockedOut.Returns(isLockedOut);
        member.FailedPasswordAttempts.Returns(failedAttempts);
        return member;
    }

    [Fact]
    public async Task LockAsync_ExistingNotLockedMember_LocksAndSaves()
    {
        var key = Guid.NewGuid();
        var member = StubMember(key, isLockedOut: false);
        _memberService.GetByKey(key).Returns(member);

        var result = await _sut.LockAsync(key, CancellationToken.None);

        Assert.True(result);
        member.Received(1).IsLockedOut = true;
        _memberService.Received(1).Save(member);
    }

    [Fact]
    public async Task LockAsync_AlreadyLocked_NoOpButReturnsTrue()
    {
        var key = Guid.NewGuid();
        var member = StubMember(key, isLockedOut: true);
        _memberService.GetByKey(key).Returns(member);

        var result = await _sut.LockAsync(key, CancellationToken.None);

        Assert.True(result);
        _memberService.DidNotReceive().Save(Arg.Any<IMember>());
    }

    [Fact]
    public async Task LockAsync_MemberNotFound_ReturnsFalse()
    {
        var key = Guid.NewGuid();
        _memberService.GetByKey(key).Returns((IMember?)null);

        var result = await _sut.LockAsync(key, CancellationToken.None);

        Assert.False(result);
        _memberService.DidNotReceive().Save(Arg.Any<IMember>());
    }

    [Fact]
    public async Task UnlockAsync_LockedMember_UnlocksAndResetsFailedAttempts()
    {
        var key = Guid.NewGuid();
        var member = StubMember(key, isLockedOut: true, failedAttempts: 5);
        _memberService.GetByKey(key).Returns(member);

        var result = await _sut.UnlockAsync(key, CancellationToken.None);

        Assert.True(result);
        member.Received(1).IsLockedOut = false;
        member.Received(1).FailedPasswordAttempts = 0;
        _memberService.Received(1).Save(member);
    }

    [Fact]
    public async Task UnlockAsync_NotLocked_NoOp()
    {
        var key = Guid.NewGuid();
        var member = StubMember(key, isLockedOut: false);
        _memberService.GetByKey(key).Returns(member);

        var result = await _sut.UnlockAsync(key, CancellationToken.None);

        Assert.True(result);
        _memberService.DidNotReceive().Save(Arg.Any<IMember>());
    }

    [Fact]
    public async Task DeleteAsync_ExistingMember_HardDeletes()
    {
        var key = Guid.NewGuid();
        var member = StubMember(key);
        _memberService.GetByKey(key).Returns(member);

        var result = await _sut.DeleteAsync(key, CancellationToken.None);

        Assert.True(result);
        _memberService.Received(1).Delete(member);
    }

    [Fact]
    public async Task DeleteAsync_MemberNotFound_ReturnsFalse()
    {
        var key = Guid.NewGuid();
        _memberService.GetByKey(key).Returns((IMember?)null);

        var result = await _sut.DeleteAsync(key, CancellationToken.None);

        Assert.False(result);
        _memberService.DidNotReceive().Delete(Arg.Any<IMember>());
    }

    [Fact]
    public async Task SetRolesAsync_AddsNewRolesAndRemovesOld()
    {
        var key = Guid.NewGuid();
        var member = StubMember(key, id: 42);
        _memberService.GetByKey(key).Returns(member);
        _memberService.GetAllRoles(42).Returns(new[] { "editor", "moderator" });

        var result = await _sut.SetRolesAsync(
            key,
            new[] { "moderator", "admin" },  // remove editor, keep moderator, add admin
            CancellationToken.None);

        Assert.True(result);
        // Expect: AssignRoles(["admin"]) + DissociateRoles(["editor"]).
        _memberService.Received(1).AssignRoles(
            Arg.Is<int[]>(ids => ids.Length == 1 && ids[0] == 42),
            Arg.Is<string[]>(r => r.Length == 1 && r[0] == "admin"));
        _memberService.Received(1).DissociateRoles(
            Arg.Is<int[]>(ids => ids.Length == 1 && ids[0] == 42),
            Arg.Is<string[]>(r => r.Length == 1 && r[0] == "editor"));
    }

    [Fact]
    public async Task SetRolesAsync_NoChange_IdempotentNoOp()
    {
        var key = Guid.NewGuid();
        var member = StubMember(key, id: 42);
        _memberService.GetByKey(key).Returns(member);
        _memberService.GetAllRoles(42).Returns(new[] { "moderator", "editor" });

        var result = await _sut.SetRolesAsync(
            key,
            new[] { "editor", "moderator" },  // same set, different order
            CancellationToken.None);

        Assert.True(result);
        _memberService.DidNotReceive().AssignRoles(Arg.Any<int[]>(), Arg.Any<string[]>());
        _memberService.DidNotReceive().DissociateRoles(Arg.Any<int[]>(), Arg.Any<string[]>());
    }

    [Fact]
    public async Task SetRolesAsync_MemberNotFound_ReturnsFalse()
    {
        var key = Guid.NewGuid();
        _memberService.GetByKey(key).Returns((IMember?)null);

        var result = await _sut.SetRolesAsync(
            key,
            new[] { "admin" },
            CancellationToken.None);

        Assert.False(result);
        _memberService.DidNotReceive().AssignRoles(Arg.Any<int[]>(), Arg.Any<string[]>());
    }

    [Fact]
    public async Task SetRolesAsync_EmptyTarget_RemovesAllRoles()
    {
        var key = Guid.NewGuid();
        var member = StubMember(key, id: 42);
        _memberService.GetByKey(key).Returns(member);
        _memberService.GetAllRoles(42).Returns(new[] { "admin", "moderator" });

        var result = await _sut.SetRolesAsync(
            key,
            Array.Empty<string>(),
            CancellationToken.None);

        Assert.True(result);
        _memberService.Received(1).DissociateRoles(
            Arg.Is<int[]>(ids => ids[0] == 42),
            Arg.Is<string[]>(r => r.Length == 2 && r.Contains("admin") && r.Contains("moderator")));
        _memberService.DidNotReceive().AssignRoles(Arg.Any<int[]>(), Arg.Any<string[]>());
    }

    private void StubHttpContext(string scheme = "https", string host = "synergos.test")
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Scheme = scheme;
        ctx.Request.Host = new HostString(host);
        _httpContextAccessor.HttpContext.Returns(ctx);
    }

    [Fact]
    public async Task SendPasswordResetAsync_HappyPath_RendersAndSendsEmail()
    {
        var key = Guid.NewGuid();
        var member = StubMember(key, email: "alice@example.com", name: "Alice");
        _memberService.GetByKey(key).Returns(member);
        _memberAuth.RequestPasswordResetAsync("alice@example.com", Arg.Any<CancellationToken>())
            .Returns(new PasswordResetRequestResult(EmailExists: true, Token: "tok-123", DisplayName: "Alice"));
        _branding.GetCurrent().Returns(new BrandIdentity("default", "Synergos"));
        _emailRenderer.RenderAsync(
                Arg.Any<string>(),
                Arg.Any<PasswordResetEmailModel>(),
                Arg.Any<CancellationToken>())
            .Returns("<html>reset</html>");
        StubHttpContext();

        var result = await _sut.SendPasswordResetAsync(key, CancellationToken.None);

        Assert.True(result);
        await _memberAuth.Received(1).RequestPasswordResetAsync(
            "alice@example.com", Arg.Any<CancellationToken>());
        await _emailRenderer.Received(1).RenderAsync(
            "PasswordReset",
            Arg.Is<PasswordResetEmailModel>(m =>
                m.DisplayName == "Alice" &&
                m.SiteName == "Synergos" &&
                m.ResetUrl.StartsWith("https://synergos.test/account/reset-password") &&
                m.ResetUrl.Contains("email=alice%40example.com") &&
                m.ResetUrl.Contains("token=tok-123")),
            Arg.Any<CancellationToken>());
        await _emailService.Received(1).SendAsync(
            Arg.Is<EmailMessage>(msg =>
                msg.To == "alice@example.com" &&
                msg.Subject.Contains("Synergos") &&
                msg.BodyHtml == "<html>reset</html>"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SendPasswordResetAsync_MemberNotFound_ReturnsFalse()
    {
        var key = Guid.NewGuid();
        _memberService.GetByKey(key).Returns((IMember?)null);

        var result = await _sut.SendPasswordResetAsync(key, CancellationToken.None);

        Assert.False(result);
        await _memberAuth.DidNotReceive().RequestPasswordResetAsync(
            Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _emailService.DidNotReceive().SendAsync(
            Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SendPasswordResetAsync_MemberHasNoEmail_ReturnsFalse()
    {
        var key = Guid.NewGuid();
        var member = StubMember(key, email: "");
        _memberService.GetByKey(key).Returns(member);

        var result = await _sut.SendPasswordResetAsync(key, CancellationToken.None);

        Assert.False(result);
        await _memberAuth.DidNotReceive().RequestPasswordResetAsync(
            Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SendPasswordResetAsync_AuthReturnsNoToken_ReturnsFalseAndDoesNotSend()
    {
        var key = Guid.NewGuid();
        var member = StubMember(key, email: "ghost@example.com");
        _memberService.GetByKey(key).Returns(member);
        // EmailExists=false → defensa contra enumeration; writer no debe enviar email.
        _memberAuth.RequestPasswordResetAsync("ghost@example.com", Arg.Any<CancellationToken>())
            .Returns(new PasswordResetRequestResult(EmailExists: false, Token: null, DisplayName: null));

        var result = await _sut.SendPasswordResetAsync(key, CancellationToken.None);

        Assert.False(result);
        await _emailRenderer.DidNotReceive().RenderAsync(
            Arg.Any<string>(), Arg.Any<PasswordResetEmailModel>(), Arg.Any<CancellationToken>());
        await _emailService.DidNotReceive().SendAsync(
            Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SendPasswordResetAsync_FallbackSiteNameWhenBrandDisplayBlank()
    {
        var key = Guid.NewGuid();
        var member = StubMember(key, email: "bob@example.com", name: "Bob");
        _memberService.GetByKey(key).Returns(member);
        _memberAuth.RequestPasswordResetAsync("bob@example.com", Arg.Any<CancellationToken>())
            .Returns(new PasswordResetRequestResult(EmailExists: true, Token: "tok", DisplayName: "Bob"));
        _branding.GetCurrent().Returns(new BrandIdentity("default", "  "));
        _emailRenderer.RenderAsync(
                Arg.Any<string>(),
                Arg.Any<PasswordResetEmailModel>(),
                Arg.Any<CancellationToken>())
            .Returns("<html/>");
        StubHttpContext();

        var result = await _sut.SendPasswordResetAsync(key, CancellationToken.None);

        Assert.True(result);
        await _emailRenderer.Received(1).RenderAsync(
            "PasswordReset",
            Arg.Is<PasswordResetEmailModel>(m => m.SiteName == "Synergos"),
            Arg.Any<CancellationToken>());
    }
}

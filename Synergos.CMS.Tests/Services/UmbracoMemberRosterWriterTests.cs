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
/// 181-184 + 227-228). Cubre Lock/Unlock/Delete/SetRoles via NSubstitute
/// stubs de IMemberService + IMember. SendPasswordResetAsync deferred
/// (necesita stub adicional de Razor email renderer + IBranding +
/// IHttpContextAccessor — más simple integración test que unit).
/// </summary>
public sealed class UmbracoMemberRosterWriterTests
{
    private readonly IMemberService _memberService = Substitute.For<IMemberService>();
    private readonly IMemberAuthService _memberAuth = Substitute.For<IMemberAuthService>();
    private readonly IEmailService _emailService = Substitute.For<IEmailService>();
    private readonly IBrandingProvider _branding = Substitute.For<IBrandingProvider>();
    private readonly IHttpContextAccessor _httpContextAccessor = Substitute.For<IHttpContextAccessor>();
    private readonly UmbracoMemberRosterWriter _sut;

    public UmbracoMemberRosterWriterTests()
    {
        // RazorEmailTemplateRenderer es concreto (no interface) — pasamos
        // null! ya que los tests no exercise SendPasswordResetAsync.
        _sut = new UmbracoMemberRosterWriter(
            _memberService,
            _memberAuth,
            _emailService,
            emailRenderer: null!,
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
}

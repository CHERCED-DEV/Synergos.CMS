using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Controllers;

namespace Synergos.CMS.Tests.Controllers;

/// <summary>
/// Verifica que los sinks <c>Redirect(returnUrl)</c> de
/// <see cref="AccountController"/> están empalmados a la regla local
/// (<c>LocalReturnUrl</c>) y no depositan al visitante en otro origen
/// tras un login legítimo (open redirect → ayuda al phishing).
/// La regla en sí se testea en <c>LocalReturnUrlTests</c>.
/// </summary>
public sealed class AccountControllerReturnUrlTests
{
    private const string Email = "member@synergos.test";
    private const string Password = "pw";
    private const string Payload = "//evil.com/x";
    private static readonly Guid MemberKey = Guid.Parse("11111111-2222-3333-4444-555555555555");

    private readonly IMemberAuthService _authService = Substitute.For<IMemberAuthService>();
    private readonly IMemberAccessGate _gate = Substitute.For<IMemberAccessGate>();
    private readonly IAnalyticsTracker _analytics = Substitute.For<IAnalyticsTracker>();
    private readonly IEmailService _emailService = Substitute.For<IEmailService>();
    private readonly IEmailTemplateRenderer _emailRenderer = Substitute.For<IEmailTemplateRenderer>();
    private readonly IBrandingProvider _branding = Substitute.For<IBrandingProvider>();
    private readonly IMemberTwoFactorService _twoFactor = Substitute.For<IMemberTwoFactorService>();
    private readonly IMemoryCache _cache = new MemoryCache(new MemoryCacheOptions());

    private AccountController BuildSut() =>
        new(_authService,
            _gate,
            _analytics,
            _emailService,
            _emailRenderer,
            Options.Create(new MembersSettings()),
            _branding,
            _twoFactor,
            _cache,
            Substitute.For<ILogger<AccountController>>());

    /// <summary>Credenciales válidas, sin 2FA.</summary>
    private void CredencialesOk(bool twoFactorEnabled = false)
    {
        _authService.ValidateCredentialsAsync(Email, Password, Arg.Any<CancellationToken>())
            .Returns(new LoginValidationResult(true, Email, MemberKey, null));
        _twoFactor.IsEnabledAsync(MemberKey, Arg.Any<CancellationToken>())
            .Returns(twoFactorEnabled);
        _authService.SignInByEmailAsync(Email, Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(MemberAuthResult.Ok());
    }

    [Fact]
    public async Task LoginPost_ConReturnUrlAOtroOrigen_AterrizaEnHome()
    {
        CredencialesOk();

        var result = await BuildSut()
            .LoginPost(Email, Password, false, Payload, CancellationToken.None);

        Assert.Equal("/", Assert.IsType<RedirectResult>(result).Url);
    }

    [Fact]
    public async Task LoginPost_ConReturnUrlLocal_RespetaElDestino()
    {
        CredencialesOk();

        var result = await BuildSut()
            .LoginPost(Email, Password, false, "/account/profile?tab=x", CancellationToken.None);

        Assert.Equal("/account/profile?tab=x", Assert.IsType<RedirectResult>(result).Url);
    }

    [Fact]
    public async Task Logout_ConReturnUrlAOtroOrigen_AterrizaEnHome()
    {
        var result = await BuildSut().Logout(Payload, CancellationToken.None);

        Assert.Equal("/", Assert.IsType<RedirectResult>(result).Url);
    }

    /// <summary>
    /// El sink más escondido: el returnUrl del atacante sobrevive cacheado
    /// durante el challenge 2FA y se consume al final del flow.
    /// </summary>
    [Fact]
    public async Task TwoFactorChallenge_ConReturnUrlAOtroOrigen_AterrizaEnHome()
    {
        CredencialesOk(twoFactorEnabled: true);
        _twoFactor.VerifyAsync(MemberKey, "123456", Arg.Any<CancellationToken>())
            .Returns(VerificationResult.TotpOk);
        var sut = BuildSut();

        // Paso 1: password OK + 2FA enabled → redirect al challenge con token.
        var challenge = await sut.LoginPost(Email, Password, false, Payload, CancellationToken.None);
        var token = Assert.IsType<RedirectToActionResult>(challenge).RouteValues!["token"];

        // Paso 2: TOTP correcto → sign-in + redirect al returnUrl cacheado.
        var result = await sut.TwoFactorChallengePost(
            (string)token!, "123456", CancellationToken.None);

        Assert.Equal("/", Assert.IsType<RedirectResult>(result).Url);
    }
}

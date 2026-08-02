using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Controllers;

namespace Synergos.CMS.Tests.Controllers;

/// <summary>
/// Fija la corrección del downcast 2FA que encontró la auditoría F2 (DIP/LSP).
/// </summary>
/// <remarks>
/// <para><b>El defecto.</b> <c>ConfirmEnrollmentAsync</c> devolvía solo un enum y los recovery
/// codes salían por un canal lateral de la implementación concreta, que el controller alcanzaba
/// con <c>_twoFactor is UmbracoMemberTwoFactorService impl</c>. Si se registraba CUALQUIER otra
/// implementación de <see cref="IMemberTwoFactorService"/>, ese <c>if</c> no entraba: el member
/// quedaba enrolado y <b>sin ver sus recovery codes, sin error</b> — perdía el único mecanismo
/// de recuperación de su cuenta.</para>
///
/// <para><b>La prueba.</b> Este test usa un doble de <see cref="IMemberTwoFactorService"/> —
/// justamente lo que el downcast ignoraba— y verifica que los códigos igual llegan a la vista.
/// Antes de la corrección era imposible de escribir: no había forma de que una impl no-concreta
/// entregara los códigos.</para>
/// </remarks>
public sealed class AccountTwoFactorRecoveryCodesTests
{
    private static readonly Guid Member = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static AccountController Make(IMemberTwoFactorService twoFactor)
    {
        var gate = Substitute.For<IMemberAccessGate>();
        gate.CurrentMemberKey.Returns(Member);
        gate.CurrentMemberEmail.Returns("member@synergos.co");

        return new AccountController(
            Substitute.For<IMemberAuthService>(),
            gate,
            Substitute.For<IAnalyticsTracker>(),
            Substitute.For<IEmailService>(),
            Substitute.For<IEmailTemplateRenderer>(),
            Options.Create(new MembersSettings()),
            Substitute.For<IBrandingProvider>(),
            twoFactor,
            new MemoryCache(new MemoryCacheOptions()),
            NullLogger<AccountController>.Instance);
    }

    [Fact]
    public async Task Una_impl_NO_concreta_igual_entrega_los_recovery_codes_a_la_vista()
    {
        var codes = new[] { "AAAA-1111", "BBBB-2222" };
        var twoFactor = Substitute.For<IMemberTwoFactorService>();
        twoFactor.ConfirmEnrollmentAsync(Member, "SECRET", "123456", Arg.Any<CancellationToken>())
            .Returns(new TwoFactorEnrollmentOutcome(EnrollmentResult.Confirmed, codes));

        var controller = Make(twoFactor);
        var result = await controller.TwoFactorSetupConfirm("SECRET", "123456", CancellationToken.None);

        var view = Assert.IsType<ViewResult>(result);
        Assert.Equal("TwoFactorSetupConfirmed", view.ViewName);
        Assert.Equal(codes, view.ViewData["RecoveryCodes"]);
    }

    [Fact]
    public async Task Un_codigo_invalido_no_muestra_recovery_codes()
    {
        var twoFactor = Substitute.For<IMemberTwoFactorService>();
        twoFactor.ConfirmEnrollmentAsync(Member, "SECRET", "000000", Arg.Any<CancellationToken>())
            .Returns(new TwoFactorEnrollmentOutcome(EnrollmentResult.InvalidCode));

        var controller = Make(twoFactor);
        var result = await controller.TwoFactorSetupConfirm("SECRET", "000000", CancellationToken.None);

        var view = Assert.IsType<ViewResult>(result);
        Assert.Null(view.ViewData["RecoveryCodes"]);
    }
}

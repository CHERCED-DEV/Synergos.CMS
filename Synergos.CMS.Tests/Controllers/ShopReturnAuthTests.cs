using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Controllers;

namespace Synergos.CMS.Tests.Controllers;

/// <summary>
/// Cubre el guard de <c>POST /api/shop/return/{rmaId}/advance</c>.
/// </summary>
/// <remarks>
/// <para>El endpoint no comprobaba NADA: ni sesión, ni rol, ni dueño — mientras
/// sus dos vecinos (<c>order/{ref}/return</c> GET y POST) sí llamaban
/// <c>DenyIfForeignMember</c>. Con sólo el <c>rmaId</c>, un anónimo podía
/// caminar la devolución <c>approved → received → refunded</c>, y llegar a
/// <c>refunded</c> ejecuta <see cref="IPaymentProvider.RefundAsync"/>.</para>
///
/// <para>Hoy no mueve dinero porque el único PSP es un stub que auto-aprueba.
/// Pasa a moverlo el día que se conecte una pasarela real — el orden peor
/// posible: la puerta abierta primero, la caja después.</para>
///
/// <para><b>Los asserts miran el ARGUMENTO del seam, no el status HTTP</b>
/// (misma lección que <see cref="ShopReviewSubmissionTests"/>): un 401/403 sale
/// igual con el hueco abierto si el guard niega pero el cuerpo del método sigue
/// ejecutando. Lo que importa es que <see cref="IReturnService.AdvanceAsync"/>
/// no reciba nada.</para>
/// </remarks>
public class ShopReturnAuthTests
{
    private static readonly Guid Comprador = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private const string RmaId = "rma_deadbeef";

    private sealed record Harness(ShopCatalogController Controller, IReturnService Returns);

    private static Harness Make(bool authenticated, string? rolesHeld = null)
    {
        var gate = Substitute.For<IMemberAccessGate>();
        gate.IsAuthenticated.Returns(authenticated);
        gate.CurrentMemberKey.Returns(authenticated ? Comprador : null);

        // HasAnyRole recibe el CSV de roles PERMITIDOS por el llamante y responde
        // si el member tiene alguno. Lo modelamos por lo que el member "es".
        gate.HasAnyRole(Arg.Any<string>()).Returns(call =>
        {
            var allowed = call.Arg<string>();
            if (rolesHeld is null || allowed is null) return false;
            return allowed
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Any(r => rolesHeld
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Contains(r, StringComparer.OrdinalIgnoreCase));
        });

        var returns = Substitute.For<IReturnService>();

        var controller = new ShopCatalogController(
            Substitute.For<IProductCatalogProvider>(),
            Substitute.For<IShopOrderService>(),
            Substitute.For<IPriceFormatter>(),
            Substitute.For<IUserCollection>(),
            Substitute.For<IOrderTrackingService>(),
            returns,
            Substitute.For<IMessagingService>(),
            gate,
            Substitute.For<IShopQuery>(),
            Substitute.For<ICatalogSocialProof>());

        return new Harness(controller, returns);
    }

    private static ShopCatalogController.ReturnAdvanceBody Body(string status = "refunded") =>
        new(status, null);

    [Fact]
    public async Task Anonimo_no_puede_mover_un_RMA_y_NADA_llega_al_seam()
    {
        var h = Make(authenticated: false);

        var result = await h.Controller.AdvanceReturn(RmaId, Body(), CancellationToken.None);

        Assert.IsType<UnauthorizedObjectResult>(result);
        await h.Returns.DidNotReceiveWithAnyArgs()
            .AdvanceAsync(default!, default, default, default);
    }

    [Fact]
    public async Task Comprador_autenticado_sin_rol_de_staff_no_puede_reembolsarse_solo()
    {
        // El caso que de verdad duele: el dueño de la orden aprobándose su
        // propio reembolso. Mover el RMA es decisión del COMERCIANTE — la
        // máquina de estados de IReturnService no tiene ni una transición que
        // le corresponda al cliente.
        var h = Make(authenticated: true, rolesHeld: null);

        var result = await h.Controller.AdvanceReturn(RmaId, Body(), CancellationToken.None);

        var status = Assert.IsType<StatusCodeResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, status.StatusCode);
        await h.Returns.DidNotReceiveWithAnyArgs()
            .AdvanceAsync(default!, default, default, default);
    }

    [Fact]
    public async Task Un_rol_cualquiera_no_alcanza()
    {
        // "agente" existe (Inmobiliaria) pero no es staff de tienda. Sin esto,
        // un gate laxo dejaría pasar a cualquier miembro con cualquier rol.
        var h = Make(authenticated: true, rolesHeld: "agente,instructor");

        var result = await h.Controller.AdvanceReturn(RmaId, Body(), CancellationToken.None);

        var status = Assert.IsType<StatusCodeResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, status.StatusCode);
        await h.Returns.DidNotReceiveWithAnyArgs()
            .AdvanceAsync(default!, default, default, default);
    }

    [Fact]
    public async Task El_guard_corre_ANTES_de_validar_el_body()
    {
        // Un body inválido no debe delatar nada a quien no está autorizado: si
        // el 400 saliera antes que el 401, el endpoint confirmaría la forma del
        // contrato a un anónimo. Y sobre todo: el orden correcto es el que
        // garantiza que nada del cuerpo se ejecute.
        var h = Make(authenticated: false);

        var result = await h.Controller.AdvanceReturn(RmaId, null, CancellationToken.None);

        Assert.IsType<UnauthorizedObjectResult>(result);
    }

    [Fact]
    public async Task Staff_SI_puede_mover_el_RMA()
    {
        // El contrapeso: el guard no puede dejar el endpoint inservible.
        var h = Make(authenticated: true, rolesHeld: "admin");
        h.Returns.AdvanceAsync(RmaId, ShopReturnStatus.Refunded, null, Arg.Any<CancellationToken>())
            .Returns(new ShopReturnCase(
                RmaId, "ord_1", "line_1", "Audífonos", 1, 320_000m, "COP",
                "No me sirvió", ShopReturnStatus.Refunded,
                DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null));

        var result = await h.Controller.AdvanceReturn(RmaId, Body(), CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
        await h.Returns.Received(1)
            .AdvanceAsync(RmaId, ShopReturnStatus.Refunded, null, Arg.Any<CancellationToken>());
    }
}

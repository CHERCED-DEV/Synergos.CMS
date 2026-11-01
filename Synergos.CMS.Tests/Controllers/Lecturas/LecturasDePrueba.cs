using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using NSubstitute;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Tests.Controllers.Lecturas;

/// <summary>
/// Lo común de las pruebas de contrato de las lecturas genéricas (014-despiece-tienda, I4): el JSON tal como sale por
/// el cable, las sesiones de quien lee y los pedidos de muestra.
/// </summary>
/// <remarks>
/// Cada prueba monta el controller con el adaptador REAL de la tienda sobre servicios falsos y afirma las claves que lee
/// el adaptador del UI (<c>libs/dominio/tienda</c>): la UI es la fuente de verdad del contrato (ADR 0083).
/// </remarks>
internal static class LecturasDePrueba
{
    internal const string Yo = "compradora@synergos.co";

    internal static readonly Guid MiClave = Guid.Parse("11111111-1111-1111-1111-111111111111");

    internal static readonly Guid OtraClave = Guid.Parse("22222222-2222-2222-2222-222222222222");

    internal static readonly DateTimeOffset Ayer = new(2026, 10, 31, 15, 0, 0, TimeSpan.Zero);

    /// <summary><see cref="JsonSerializerDefaults.Web"/> es lo que configura ASP.NET: las claves que ve el navegador.</summary>
    private static readonly JsonSerializerOptions ComoAspNet = new(JsonSerializerDefaults.Web);

    internal static JsonElement Wire(IActionResult result)
    {
        var ok = Assert.IsType<OkObjectResult>(result);
        var json = JsonSerializer.Serialize(ok.Value, ok.Value!.GetType(), ComoAspNet);
        return JsonDocument.Parse(json).RootElement.Clone();
    }

    internal static int Estado(IActionResult result) => result switch
    {
        ObjectResult o => o.StatusCode ?? StatusCodes.Status200OK,
        StatusCodeResult s => s.StatusCode,
        _ => throw new InvalidOperationException(result.GetType().Name),
    };

    internal static void TieneLasClaves(JsonElement valor, params string[] claves)
    {
        foreach (var clave in claves)
        {
            Assert.True(valor.TryGetProperty(clave, out _), $"Falta «{clave}» en {valor}");
        }
    }

    internal static IMemberAccessGate Sesion(bool autenticada = true, Guid? clave = null, bool personal = false)
    {
        var sesion = Substitute.For<IMemberAccessGate>();
        sesion.IsAuthenticated.Returns(autenticada);
        sesion.CurrentMemberEmail.Returns(autenticada ? Yo : null);
        sesion.CurrentMemberKey.Returns(autenticada ? clave ?? MiClave : null);
        sesion.HasAnyRole(Arg.Any<string?>()).Returns(personal);
        return sesion;
    }

    internal static IMemberAccessGate SinSesion() => Sesion(autenticada: false);

    internal static IPriceFormatter Precios()
    {
        var precios = Substitute.For<IPriceFormatter>();
        precios.Format(Arg.Any<decimal>(), Arg.Any<string?>()).Returns(ci => $"$ {ci.ArgAt<decimal>(0)}");
        return precios;
    }

    internal static IOptionsMonitor<TiendaSettings> Tienda()
    {
        var tienda = Substitute.For<IOptionsMonitor<TiendaSettings>>();
        tienda.CurrentValue.Returns(new TiendaSettings());
        return tienda;
    }

    internal static ShopOrderLine Linea(string producto, string? variante = null, int cantidad = 1, decimal unitario = 680_000m)
        => new(producto, variante, "Audífonos Inalámbricos ANC", cantidad, unitario, unitario * cantidad, "COP");

    internal static ShopOrder Pedido(string referencia, OrderStatus estado, Guid? dueno, params ShopOrderLine[] lineas)
        => new(
            referencia,
            "SYN-ABCDEF12",
            estado,
            "Ana Compradora",
            Yo,
            lineas,
            lineas.Sum(l => l.LineTotal),
            "COP",
            null,
            Ayer,
            dueno);
}

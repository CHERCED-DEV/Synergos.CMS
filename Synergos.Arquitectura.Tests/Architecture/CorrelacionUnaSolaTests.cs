using System.Reflection;
using Microsoft.AspNetCore.Http;
using Synergos.CMS.Web.Middlewares;
using Synergos.Shared;

namespace Synergos.CMS.Tests.Architecture;

/// <summary>
/// La correlación que llega de fuera se limpia igual en el CMS y en el árbol de servicios (ADR 0140 F3).
/// </summary>
/// <remarks>
/// <para><b>Por qué hace falta.</b> El CMS copiaba la cabecera tal cual y <c>Synergos.Shared</c> la
/// limpia —letras y dígitos ASCII, hasta 32—, así que una petición del navegador con un UUID con guiones
/// quedaba registrada con un identificador en el CMS y con otro en el orquestador: dos rastros para la
/// misma compra, que es justo lo que la correlación existe para evitar (HU #28). Con la puerta, el
/// navegador la manda directo.</para>
///
/// <para><b>Se compara el RESULTADO y no el código</b>: el CMS no referencia Shared. La regla de Shared
/// es privada y se invoca por reflexión; si cambia de nombre, esto falla a gritos en vez de pasar sin
/// mirar.</para>
/// </remarks>
public sealed class CorrelacionUnaSolaTests
{
    public static TheoryData<string> Entradas() => new()
    {
        "abc",
        "trace-abc",
        "3f2504e0-4f89-11d3-9a0c-0305e82c3301",
        "3F2504E04F8911D39A0C0305E82C3301",
        "  con espacios  ",
        "ñandú-42",
        "a\r\nFALSO: línea",
        "<script>alert(1)</script>",
        new string('x', 100),
        "---",
        "ñ",
    };

    private static string Shared(string entrante)
    {
        var recortar = typeof(Correlation).GetMethod("Recortar", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.True(recortar is not null, "Synergos.Shared.Correlation ya no tiene Recortar: revisar con qué regla limpia y cruzarla acá.");
        return (string)recortar!.Invoke(null, [entrante])!;
    }

    [Theory]
    [MemberData(nameof(Entradas))]
    public void El_CMS_y_Shared_dejan_lo_mismo_de_una_correlacion_de_fuera(string entrante)
    {
        var delCms = CorrelationIdMiddleware.Normalizar(entrante);
        var deShared = Shared(entrante);

        if (delCms is null)
        {
            // No quedó nada usable: los dos generan uno propio, cada uno con su forma.
            Assert.NotEqual(entrante, deShared);
            Assert.Matches("^[0-9a-f]{16}$", deShared);
            return;
        }

        Assert.Equal(deShared, delCms);
    }

    [Fact]
    public void El_middleware_del_CMS_usa_esa_regla()
    {
        var http = new DefaultHttpContext();
        http.Request.Headers[CorrelationIdMiddleware.HeaderName] = "3f2504e0-4f89-11d3-9a0c-0305e82c3301";

        Assert.Equal(Shared("3f2504e0-4f89-11d3-9a0c-0305e82c3301"), CorrelationIdMiddleware.ResolveCorrelationId(http));
    }
}

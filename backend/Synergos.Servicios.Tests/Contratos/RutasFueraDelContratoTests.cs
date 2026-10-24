using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Synergos.CMS.Tests.Contratos;

/// <summary>
/// Una ruta de una pieza que publica contrato y queda FUERA del documento está declarada aquí, con su razón
/// (núcleo N1).
/// </summary>
/// <remarks>
/// <para><b>Por qué hace falta.</b> <c>ExcludeFromDescription()</c> saca una ruta del documento, y todos los
/// gates del contrato —deriva, suelo, sondas, compatibilidad— recorren el documento: una ruta excluida no la
/// mira ninguno. Es la salida honesta para una ruta que no se puede publicar sin mentir (la combinada de
/// recursos de Booking devuelve dos formas), y también la puerta trasera de cualquier endpoint que no quiera
/// su contrato. Por eso cada una se declara, y una que sobra o falta es rojo.</para>
/// </remarks>
public sealed class RutasFueraDelContratoTests
{
    /// <summary>Las rutas fuera del contrato: <c>{ensamblado} {método} {ruta}</c> → por qué.</summary>
    private static readonly Dictionary<string, string> Declaradas = new(StringComparer.Ordinal)
    {
        ["Synergos.Api.Booking GET v1/resources"] =
            "Devuelve un recurso (con subjectKind y subjectId) o una página (sin ellos), y un documento publica una forma por " +
            "estado: ASP.NET publicaba la página y mentía para quien busca por sujeto. La búsqueda por sujeto se publica en " +
            "/v1/resources/por-sujeto. Se conserva por el superset: la usa SaludCapabilities.",
        ["Synergos.Api.Payments POST v1/webhooks/wompi"] =
            "La llama la pasarela, no un consumidor del producto: no va detrás de la llave compartida, la protege la " +
            "firma sobre el cuerpo crudo, y su forma es la del proveedor (Wompi), no un contrato nuestro que alguien genere.",
        ["Synergos.Bff.Viajes POST v1/trips/{id}/cancel"] =
            "Su cuerpo es opcional a propósito —sin él se devuelve todo, y HttpTravelCartEngine cancela así— y el traductor " +
            "del contrato HTTP del UI sólo admite cuerpos requeridos (NV-15 de la línea 006). Ningún navegador la llama: " +
            "la llama la ruta directa del CMS. Sale del documento cuando el traductor admita un cuerpo opcional.",
    };

    private static IReadOnlyList<string> FueraDelContrato(PiezaPublicada pieza)
    {
        using var host = pieza.Levantar(conContrato: false);
        return host.Servicios.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(e => e.Metadata.GetMetadata<IExcludeFromDescriptionMetadata>() is { ExcludeFromDescription: true })
            .Select(e => $"{pieza.Ensamblado} {string.Join(",", e.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? [])} "
                       + (e.RoutePattern.RawText ?? string.Empty).TrimStart('/'))
            .ToList();
    }

    [Fact]
    public void Cada_ruta_fuera_del_contrato_esta_declarada_con_su_razon_y_ninguna_sobra()
    {
        var fuera = ContratoOpenApi.Piezas.SelectMany(FueraDelContrato).Order(StringComparer.Ordinal).ToList();

        Assert.True(fuera.SequenceEqual(Declaradas.Keys.Order(StringComparer.Ordinal), StringComparer.Ordinal),
            "Las rutas fuera del contrato no son las declaradas: ningún gate del contrato mira una ruta excluida." +
            $"{Environment.NewLine}  sin declarar: {string.Join("; ", fuera.Except(Declaradas.Keys))}" +
            $"{Environment.NewLine}  declaradas que ya no lo están: {string.Join("; ", Declaradas.Keys.Except(fuera))}");
        Assert.All(Declaradas.Values, razon => Assert.True(razon.Length > 40, "Una ruta fuera del contrato necesita su razón escrita."));
    }
}

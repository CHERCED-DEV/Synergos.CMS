using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Web.Services;

/// <summary>
/// La sección <c>Synergos:Features:Eventos</c> falla al arrancar si trae algo que nadie lee o un
/// valor fuera de rango, en los valores base o en cualquier sitio (ADR 0137).
/// </summary>
/// <remarks>
/// <para><b>Falla, no avisa.</b> El precedente de la casa —el validador de la resiliencia de los
/// webhooks— sólo escribe un aviso salvo que se encienda un modo estricto. Acá el valor es plata: una
/// comisión mal escrita que se queda en su valor por defecto cobra otra cosa, sin que nadie lo
/// note. Se registra con <c>ValidateOnStart</c>, así que el CMS no arranca.</para>
///
/// <para><b>En la recarga</b> la misma regla deja de entregar el valor nuevo y quien lee la sección
/// se queda con el último válido (<see cref="NegocioDeEventosDelSitio"/>): un dedazo en caliente no
/// tumba la venta de entradas.</para>
///
/// <para><b>Se valida lo que RIGE en cada sitio</b>, con la fusión hecha, y no cada clave suelta:
/// es lo que el sitio va a mostrar y a cobrar.</para>
/// </remarks>
public sealed class ValidadorDeNegocioDeEventos : IValidateOptions<EventosFeatureSettings>
{
    private readonly IConfiguration _config;

    public ValidadorDeNegocioDeEventos(IConfiguration config) => _config = config;

    public ValidateOptionsResult Validate(string? name, EventosFeatureSettings options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var fallos = new List<string>(ClavesDeConfiguracion.QueNadieLee(
            _config.GetSection(EventosFeatureSettings.Seccion), typeof(EventosFeatureSettings)));

        Revisar("los valores base", options.Para(null), fallos);

        foreach (var clave in options.Sitios.Keys)
        {
            if (!Guid.TryParse(clave, out var sitio))
            {
                fallos.Add($"{EventosFeatureSettings.Seccion}:Sitios:{clave}: un sitio se nombra por la "
                    + "Key (GUID) de su siteRoot.");
                continue;
            }

            Revisar($"el sitio {clave}", options.Para(sitio), fallos);
        }

        return fallos.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(fallos);
    }

    private static void Revisar(string quien, NegocioDeEventos negocio, List<string> fallos)
    {
        if (!EsApiBase(negocio.ApiBase))
        {
            fallos.Add($"ApiBase en {quien}: «{negocio.ApiBase}» tiene que ser una ruta del sitio "
                + "(«/api/eventos») o una URL http(s) absoluta.");
        }

        Porcentaje("FeePercent", quien, negocio.FeePercent, fallos);
        Porcentaje("PlatformFeePercent", quien, negocio.PlatformFeePercent, fallos);
    }

    /// <summary>
    /// De 0 a 100 y con dos decimales como mucho: con eso el carrito, el motor en proceso y el
    /// orquestador calculan la comisión con aritmética exacta y les da lo mismo.
    /// </summary>
    private static void Porcentaje(string clave, string quien, decimal valor, List<string> fallos)
    {
        if (valor is < 0m or > 100m || decimal.Round(valor, 2) != valor)
        {
            fallos.Add($"{clave} en {quien}: {valor} tiene que ir de 0 a 100 con dos decimales como mucho.");
        }
    }

    /// <summary>
    /// Una ruta del propio sitio, o una URL http(s) absoluta. <c>//host</c> y <c>/\host</c> no: el
    /// navegador los lee como otro origen disfrazado de ruta, y si va a otro origen se escribe entero.
    /// </summary>
    private static bool EsApiBase(string? valor)
    {
        if (string.IsNullOrWhiteSpace(valor) || valor.Any(char.IsWhiteSpace))
        {
            return false;
        }

        if (valor.StartsWith('/'))
        {
            return valor.Length == 1 || (valor[1] != '/' && valor[1] != '\\');
        }

        return Uri.TryCreate(valor, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);
    }
}

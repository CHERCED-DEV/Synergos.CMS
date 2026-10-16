using System.Text.Json;
using System.Text.RegularExpressions;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Web.Services.Puerta;

/// <summary>
/// Los avisos en vivo que declara un flujo (núcleo N3, NE-6 de 007): cuando una operación sale bien, la puerta
/// publica en el canal declarado lo que la respuesta trae en <c>datos</c> (o la respuesta entera).
/// </summary>
/// <remarks>
/// <para><b>Best-effort, como todo aviso después de un hecho</b> (ADR 0037/0106): la operación ya ocurrió en el
/// orquestador. Un canal que no se puede llenar o un envío que falla queda en el log y NO cambia lo que contesta
/// la puerta.</para>
///
/// <para><b>Los marcadores del canal</b> se llenan con los parámetros de la operación (los que la puerta ya
/// validó) y, si no están, con un campo de los datos de la respuesta (<c>datos.x</c>, o <c>x</c> arriba).
/// Nada del navegador entra en el canal sin pasar por el orquestador.</para>
/// </remarks>
public sealed partial class AvisoEnVivoDeLaPuerta
{
    private readonly IRealtimeNotifier _realtime;
    private readonly ILogger<AvisoEnVivoDeLaPuerta> _log;

    public AvisoEnVivoDeLaPuerta(IRealtimeNotifier realtime, ILogger<AvisoEnVivoDeLaPuerta> log)
    {
        _realtime = realtime;
        _log = log;
    }

    [GeneratedRegex(@"\{([^{}]+)\}", RegexOptions.CultureInvariant)]
    private static partial Regex Marcador();

    /// <summary>Publica los avisos de <paramref name="operacion"/>, si el flujo declara alguno. No lanza.</summary>
    public async Task PublicarAsync(
        string flujo, FlujoDeLaPuertaSettings ajustes, string operacion, IReadOnlyDictionary<string, string> parametros,
        byte[] cuerpo, CancellationToken ct)
    {
        var avisos = ajustes.EnVivo.Where(a => string.Equals(a.Operacion, operacion, StringComparison.Ordinal)).ToList();
        if (avisos.Count == 0) return;

        JsonElement? respuesta = null;
        try
        {
            if (cuerpo.Length > 0) respuesta = JsonDocument.Parse(cuerpo).RootElement.Clone();
        }
        catch (JsonException)
        {
            // Una respuesta sin JSON no tiene datos que publicar ni de dónde llenar el canal.
        }
        var datos = respuesta is { ValueKind: JsonValueKind.Object } r && r.TryGetProperty("datos", out var d) ? d : respuesta;

        foreach (var aviso in avisos)
        {
            var canal = Llenar(aviso.Canal ?? string.Empty, parametros, datos, respuesta, out var falta);
            if (falta is not null)
            {
                _log.LogWarning("Aviso en vivo de {Flujo}/{Operacion}: el canal {Canal} pide {{{Falta}}} y la operación no lo trae; no se publica.",
                    flujo, operacion, aviso.Canal, falta);
                continue;
            }

            try
            {
                await _realtime.PublishAsync(canal, aviso.Nombre ?? operacion, datos?.GetRawText() ?? "{}", ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _log.LogWarning(ex, "Aviso en vivo de {Flujo}/{Operacion} en {Canal}: no se pudo publicar (la operación SÍ ocurrió).",
                    flujo, operacion, canal);
            }
        }
    }

    private static string Llenar(
        string plantilla, IReadOnlyDictionary<string, string> parametros, JsonElement? datos, JsonElement? respuesta, out string? falta)
    {
        string? sinValor = null;
        var canal = Marcador().Replace(plantilla, m =>
        {
            var nombre = m.Groups[1].Value;
            if (parametros.TryGetValue(nombre, out var valor)) return valor;
            if ((Campo(datos, nombre) ?? Campo(respuesta, nombre)) is { } delCuerpo) return delCuerpo;
            sinValor ??= nombre;
            return m.Value;
        });
        falta = sinValor;
        return canal;
    }

    private static string? Campo(JsonElement? objeto, string nombre)
        => objeto is { ValueKind: JsonValueKind.Object } o && o.TryGetProperty(nombre, out var v)
            ? v.ValueKind switch
            {
                JsonValueKind.String => v.GetString(),
                JsonValueKind.Number => v.GetRawText(),
                _ => null,
            }
            : null;
}

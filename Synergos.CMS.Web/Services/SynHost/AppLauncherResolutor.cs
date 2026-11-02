using System.Text.Json;
using Synergos.CMS.Interfaces.SynHost;
using Umbraco.Cms.Core.Models.PublishedContent;

namespace Synergos.CMS.Web.Services.SynHost;

/// <summary>
/// <c>elementSynAppLauncher</c> → <see cref="AppLauncherProps"/>. Traduce <c>heading</c>/
/// <c>subheading</c> a <c>title</c>/<c>subtitle</c> y parsea en el servidor el JSON de apps.
/// </summary>
/// <remarks>
/// <para>Una app sin <c>name</c> no viaja y se anota: el elemento la descartaba igual, en silencio. Las
/// capacidades se aceptan como lista o como texto con comas, que es lo que el elemento aceptaba.</para>
///
/// <para>Tampoco viaja una app cuya página está apagada (<see cref="IDestinosApagados"/>, 002-produccion P11 C):
/// una vertical despublicada no se anuncia con un enlace a «página no encontrada», y al volver a publicarla
/// vuelve sola.</para>
/// </remarks>
public sealed class AppLauncherResolutor : IResolutorSynHost<AppLauncherProps>
{
    private const string AliasDeLasApps = "apps";

    private readonly IPublishedValueFallback _fallback;
    private readonly IDestinosApagados _destinos;
    private readonly ILogger<AppLauncherResolutor> _log;

    public AppLauncherResolutor(
        IPublishedValueFallback fallback,
        IDestinosApagados destinos,
        ILogger<AppLauncherResolutor> log)
    {
        _fallback = fallback;
        _destinos = destinos;
        _log = log;
    }

    public ElementoResuelto<AppLauncherProps> Resolver(IPublishedElement elemento)
    {
        var editor = new LectorDelEditor(elemento, _fallback, _log);
        return new ElementoResuelto<AppLauncherProps>(new AppLauncherProps(
            Title: editor.Texto("heading"),
            Subtitle: editor.Texto("subheading"),
            Apps: Apps(elemento, editor, editor.Texto(AliasDeLasApps))));
    }

    private IReadOnlyList<AppDelLanzador>? Apps(IPublishedElement elemento, LectorDelEditor editor, string? json)
    {
        var lista = editor.ListaJson(AliasDeLasApps, json);
        if (lista is null)
        {
            return null;
        }

        var apps = new List<AppDelLanzador>();
        foreach (var entrada in lista)
        {
            var nombre = LectorDelEditor.Cadena(entrada, "name");
            if (nombre is null)
            {
                editor.NoEsValido(AliasDeLasApps, entrada.GetRawText(), "una app con name");
                continue;
            }

            var url = LectorDelEditor.Cadena(entrada, "url");
            if (_destinos.Apagado(url, elemento, AliasDeLasApps))
            {
                continue;
            }

            apps.Add(new AppDelLanzador(
                Name: nombre,
                Id: LectorDelEditor.Cadena(entrada, "id"),
                Tagline: LectorDelEditor.Cadena(entrada, "tagline"),
                Icon: LectorDelEditor.Cadena(entrada, "icon"),
                Status: LectorDelEditor.Cadena(entrada, "status"),
                Industry: LectorDelEditor.Cadena(entrada, "industry"),
                Persona: LectorDelEditor.Cadena(entrada, "persona"),
                Capabilities: Capacidades(entrada),
                Url: url,
                DemoMode: LectorDelEditor.Cadena(entrada, "demoMode")));
        }

        return apps.Count > 0 ? apps : null;
    }

    private static IReadOnlyList<string>? Capacidades(JsonElement entrada)
    {
        if (entrada.ValueKind != JsonValueKind.Object || !entrada.TryGetProperty("capabilities", out var valor))
        {
            return null;
        }

        var capacidades = valor.ValueKind switch
        {
            JsonValueKind.Array => valor.EnumerateArray().Select(LectorDelEditor.Cadena).OfType<string>().ToList(),
            JsonValueKind.String => (valor.GetString() ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList(),
            _ => [],
        };

        return capacidades.Count > 0 ? capacidades : null;
    }
}

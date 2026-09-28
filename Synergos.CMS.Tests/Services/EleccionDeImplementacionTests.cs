using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Web.Services;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// La misma entrada da el mismo bundle, venga el registry en el orden que venga (#131).
/// </summary>
/// <remarks>
/// <para><b>Lo que este test NO afirma, y es deliberado: cuál de los dos frameworks se elige.</b>
/// El ticket lo dejó escrito —«un test que afirme el orden del diccionario codificaría el
/// defecto… el test que hay que escribir es el de la política, el día que la política exista»—
/// y la política es del repo hermano, que es quien publica. Congelarla acá sería escribir en
/// este repo una decisión del otro, que es el error que el #127 ya documentó al revés.</para>
///
/// <para><b>Lo que sí afirma es INVARIANCIA respecto del orden del JSON</b>, que sigue siendo
/// cierta bajo cualquier política futura: el mismo registry con las implementaciones declaradas
/// al revés tiene que dar el mismo bundle. Antes no la cumplía —el desempate era
/// <c>Keys.FirstOrDefault()</c>, o sea el orden de inserción, que sale del orden en que el
/// hermano escribió el fichero— así que <b>republicar cambiaba qué recibe el visitante</b>.</para>
///
/// <para><b>El fixture necesita las DOS condiciones y por eso no sirve el registry de al
/// lado:</b> dos implementaciones <b>y</b> que ninguna sea la de por defecto. Con
/// <c>angular</c> entre ellas gana el default, el desempate no se ejecuta y el defecto pasa en
/// verde — que es exactamente el estado del CDN de hoy, donde <c>badge</c> ya trae
/// <c>angular</c> y <c>preact</c>. Por eso <c>DefaultFramework</c> es <c>"svelte"</c>: un
/// despliegue que pide algo que ese elemento no publica.</para>
/// </remarks>
public sealed class EleccionDeImplementacionTests
{
    private sealed class RelojFalso : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 9, 28, 12, 0, 0, TimeSpan.Zero);
    }

    /// <summary>
    /// Un <see cref="IOptionsMonitor{T}"/> que no cambia. Es un doble sin política —cinco líneas
    /// que devuelven lo que se les dio— así que tenerlo también acá no es la duplicación que
    /// este repo persigue: lo que no se copia son las REGLAS, y aquí no hay ninguna.
    /// </summary>
    private sealed class OpcionesFijas<T> : IOptionsMonitor<T>
    {
        public OpcionesFijas(T value) => CurrentValue = value;
        public T CurrentValue { get; }
        public T Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }

    private sealed class CdnFalso : HttpMessageHandler
    {
        private readonly Dictionary<string, string> _cuerpos = new(StringComparer.Ordinal);

        public CdnFalso Con(string ruta, string json) { _cuerpos[ruta] = json; return this; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
            => Task.FromResult(_cuerpos.TryGetValue(req.RequestUri!.AbsolutePath, out var cuerpo)
                ? new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(cuerpo, Encoding.UTF8, "application/json"),
                }
                : new HttpResponseMessage(HttpStatusCode.NotFound));
    }

    /// <summary>
    /// El mismo elemento con sus dos implementaciones, declaradas en un orden y en el otro.
    /// </summary>
    private static string Registry(params string[] frameworks)
    {
        var impls = string.Join(", ", frameworks.Select(f => $"\"{f}\": {{ \"latest\": \"0.1.0\" }}"));
        return $$"""
        {
          "generated": "2026-09-28T00:00:00.000Z", "version": "0.1.0", "baseUrl": "/synergos",
          "elements": [
            {
              "name": "badge", "alias": "elementSynBadge", "tag": "synergos-badge",
              "tier": "primitive",
              "implementations": { {{impls}} }
            }
          ]
        }
        """;
    }

    private const string Manifiesto = """
    { "tag": "synergos-badge", "alias": "elementSynBadge", "version": "0.1.0",
      "tier": "primitive", "entryScript": "main.js" }
    """;

    private static async Task<string?> UrlServidaAsync(params string[] frameworks)
    {
        var cdn = new CdnFalso().Con("/synergos/registry.json", Registry(frameworks));
        foreach (var f in frameworks)
        {
            cdn.Con($"/synergos/badge/{f}/0.1.0/manifest.json", Manifiesto);
        }

        var s = new BundleRegistrySettings
        {
            Mode = "Http",
            PublicBaseUrl = "https://cdn.ejemplo.co",
            BundlesNamespace = "synergos",
            RegistryFileName = "registry.json",
            // Ninguna de las dos implementaciones: es la ÚNICA forma de que el desempate corra.
            DefaultFramework = "svelte",
            DefaultSlot = "latest",
        };

        var cliente = new HttpBundleRegistryClient(
            new HttpClient(cdn) { BaseAddress = new Uri(s.PublicBaseUrl) },
            new OpcionesFijas<BundleRegistrySettings>(s),
            NullLogger<HttpBundleRegistryClient>.Instance,
            new RelojFalso());

        return (await cliente.TryResolveAsync("synergos-badge"))?.MainEntryUri.ToString();
    }

    [Fact]
    public async Task El_bundle_servido_no_depende_del_orden_en_que_el_registry_declara_los_frameworks()
    {
        var enUnOrden = await UrlServidaAsync("preact", "angular");
        var enElOtro = await UrlServidaAsync("angular", "preact");

        Assert.NotNull(enUnOrden);
        Assert.Equal(enUnOrden, enElOtro);
    }

    // ── El helper, directo ──────────────────────────────────────────────────

    [Fact]
    public void Con_el_framework_por_defecto_presente_gana_ese_y_no_hay_desempate()
    {
        var elegido = EleccionDeImplementacion.Elegir(
            new[] { "preact", "angular" }, "angular", out var desempatado);

        Assert.Equal("angular", elegido);
        Assert.False(desempatado, "el default estaba: no hubo nada que desempatar");
    }

    /// <summary>
    /// <b>Una sola implementación no es un desempate</b>, y distinguirlo importa: el aviso
    /// existe para que un cambio de bundle deje rastro, y dispararlo en el caso normal —un
    /// elemento publicado en un framework que no es el del despliegue— lo convertiría en ruido
    /// que nadie lee.
    /// </summary>
    [Fact]
    public void Una_sola_implementacion_se_sirve_sin_avisar()
    {
        var elegido = EleccionDeImplementacion.Elegir(new[] { "preact" }, "angular", out var desempatado);

        Assert.Equal("preact", elegido);
        Assert.False(desempatado);
    }

    [Fact]
    public void Dos_implementaciones_sin_el_por_defecto_SI_avisan()
    {
        EleccionDeImplementacion.Elegir(new[] { "preact", "react" }, "angular", out var desempatado);

        Assert.True(desempatado, "servir «lo que quedaba» sin dejar rastro es lo que el #131 cerró");
    }

    [Fact]
    public void Sin_implementaciones_no_se_inventa_ninguna()
    {
        Assert.Null(EleccionDeImplementacion.Elegir(null, "angular", out _));
        Assert.Null(EleccionDeImplementacion.Elegir(Array.Empty<string>(), "angular", out _));
    }

    /// <summary>
    /// <b>El orden es ordinal y estable</b> — arbitrario y dicho como arbitrario. Lo que se
    /// prueba no es que gane «preact» sino que gane SIEMPRE el mismo, que es la propiedad que el
    /// orden del diccionario no daba.
    /// </summary>
    [Fact]
    public void El_desempate_da_lo_mismo_venga_como_venga_la_lista()
    {
        var a = EleccionDeImplementacion.Elegir(new[] { "vue", "preact", "react" }, "angular", out _);
        var b = EleccionDeImplementacion.Elegir(new[] { "react", "vue", "preact" }, "angular", out _);
        var c = EleccionDeImplementacion.Elegir(new[] { "preact", "react", "vue" }, "angular", out _);

        Assert.Equal(a, b);
        Assert.Equal(b, c);
    }
}

using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Web.Services;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// El cliente HTTP del registry del CDN (HU #20).
/// </summary>
/// <remarks>
/// <para><b>Lo que se prueba acá no es «resuelve un bundle».</b> Eso es la parte fácil y la
/// comparte con el cliente de filesystem, que lleva tiempo funcionando. Lo que se prueba es lo
/// que el transporte de red obliga a decidir:</para>
///
/// <list type="bullet">
///   <item>que un CDN caído <b>no vacíe una página</b> que se venía sirviendo bien;</item>
///   <item>que un render <b>no espere a la red</b>;</item>
///   <item>que la URL emitida sea la <b>versionada</b> y no el puntero móvil, porque de eso
///   depende que el navegador la cachee.</item>
/// </list>
/// </remarks>
public sealed class HttpBundleRegistryClientTests
{
    private sealed class RelojFalso : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 8, 3, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now;
        public void Avanzar(TimeSpan d) => _now += d;
    }

    /// <summary>Un CDN de mentira que se puede tirar y levantar a mitad de un test.</summary>
    private sealed class CdnFalso : HttpMessageHandler
    {
        private readonly Dictionary<string, string> _cuerpos = new(StringComparer.Ordinal);

        public bool Caido { get; set; }
        public int Peticiones { get; private set; }
        public List<string> Pedidas { get; } = new();

        public CdnFalso Con(string ruta, string json) { _cuerpos[ruta] = json; return this; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
        {
            Peticiones++;
            Pedidas.Add(req.RequestUri!.AbsolutePath);

            if (Caido) throw new HttpRequestException("connection refused");

            return Task.FromResult(_cuerpos.TryGetValue(req.RequestUri!.AbsolutePath, out var cuerpo)
                ? new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(cuerpo, Encoding.UTF8, "application/json"),
                }
                : new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    private const string Registry = """
    {
      "generated": "2026-08-03T23:11:40.501Z",
      "version": "0.1.0",
      "baseUrl": "/synergos",
      "elements": [
        {
          "name": "badge", "alias": "elementSynBadge", "tag": "synergos-badge", "tier": "primitive",
          "implementations": { "angular": { "latest": "0.1.0", "v0": "0.1.0" } }
        },
        {
          "name": "hero", "alias": "elementCompHero", "tag": "synergos-hero", "tier": "module",
          "implementations": { "angular": { "latest": "1.4.2" } },
          "dependencies": ["badge"]
        }
      ]
    }
    """;

    private const string ManifiestoBadge = """
    { "tag": "synergos-badge", "alias": "elementSynBadge", "framework": "angular",
      "version": "0.1.0", "tier": "primitive", "entryScript": "main.js" }
    """;

    private static (HttpBundleRegistryClient Cliente, CdnFalso Cdn, RelojFalso Reloj) Nuevo(
        BundleRegistrySettings? ajustes = null, CdnFalso? cdn = null)
    {
        cdn ??= new CdnFalso()
            .Con("/synergos/registry.json", Registry)
            .Con("/synergos/badge/angular/0.1.0/manifest.json", ManifiestoBadge);

        var s = ajustes ?? new BundleRegistrySettings
        {
            Mode = "Http",
            PublicBaseUrl = "https://cdn.ejemplo.co",
            BundlesNamespace = "synergos",
            RegistryFileName = "registry.json",
            DefaultFramework = "angular",
            DefaultSlot = "latest",
        };

        var reloj = new RelojFalso();
        var http = new HttpClient(cdn) { BaseAddress = new Uri(s.PublicBaseUrl) };

        return (
            new HttpBundleRegistryClient(
                http,
                new StaticOptionsMonitor<BundleRegistrySettings>(s),
                NullLogger<HttpBundleRegistryClient>.Instance,
                reloj),
            cdn,
            reloj);
    }

    // ── Lo que comparte con el cliente de filesystem ────────────────────────

    [Fact]
    public async Task Resuelve_un_elemento_por_tag_por_alias_y_por_nombre()
    {
        // El registry trae los tres, y quien pregunta puede tener cualquiera a mano según de
        // dónde venga: el schema usa el alias, la plantilla usa el tag.
        var (cliente, _, _) = Nuevo();

        foreach (var llave in new[] { "synergos-badge", "elementSynBadge", "badge" })
        {
            var d = await cliente.TryResolveAsync(llave);
            Assert.NotNull(d);
            Assert.Equal("synergos-badge", d!.Tag);
        }
    }

    [Fact]
    public async Task La_URL_emitida_lleva_la_VERSION_y_no_el_puntero_movil()
    {
        // Es de lo que depende que el navegador cachee. Con `/latest/` habría que revalidar en
        // cada navegación: un viaje por bundle y por página, que es cómo un CDN se convierte en
        // un sitio lento.
        var (cliente, _, _) = Nuevo();

        var d = await cliente.TryResolveAsync("synergos-badge");

        Assert.Equal("https://cdn.ejemplo.co/synergos/badge/angular/0.1.0/main.js", d!.MainEntryUri.ToString());
        Assert.DoesNotContain("/latest/", d.MainEntryUri.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Las_dependencias_salen_antes_que_el_principal()
    {
        // Un custom element que otro escribe crudo en su plantilla no pasa por el SynHost, así
        // que nadie emitiría su script: quedaría en el DOM sin registrar y sin hidratar.
        var (cliente, _, _) = Nuevo();

        var d = await cliente.TryResolveAsync("synergos-hero");

        Assert.Single(d!.Dependencies);
        Assert.Equal("https://cdn.ejemplo.co/synergos/badge/angular/0.1.0/main.js", d.Dependencies[0].ToString());
    }

    [Fact]
    public async Task Un_elemento_que_el_registry_no_conoce_da_null()
        => Assert.Null(await Nuevo().Cliente.TryResolveAsync("synergos-no-existe"));

    // ── Lo que el transporte de red obliga a decidir ────────────────────────

    [Fact]
    public async Task Si_el_CDN_se_cae_se_SIGUE_sirviendo_el_ultimo_snapshot_bueno()
    {
        // Es la decisión de fondo de este adapter. Un registry caído no puede vaciar una página
        // que se venía pintando bien: el sitio dejaría de mostrar sus componentes de golpe, y no
        // por un cambio nuestro sino porque un tercero dejó de contestar.
        var (cliente, cdn, reloj) = Nuevo();
        Assert.NotNull(await cliente.TryResolveAsync("synergos-badge"));

        cdn.Caido = true;
        reloj.Avanzar(TimeSpan.FromHours(1));   // el snapshot está más que vencido

        var d = await cliente.TryResolveAsync("synergos-badge");

        Assert.NotNull(d);
        Assert.Equal("0.1.0", d!.Version);
    }

    [Fact]
    public async Task Un_registry_VACIO_no_reemplaza_al_que_habia()
    {
        // Un registry vacío es indistinguible de uno roto desde el punto de vista del sitio:
        // todos los elementos dejarían de resolver a la vez. Aceptarlo sería apagar la página
        // por un despliegue del CDN que salió mal.
        var cdn = new CdnFalso()
            .Con("/synergos/registry.json", Registry)
            .Con("/synergos/badge/angular/0.1.0/manifest.json", ManifiestoBadge);
        var (cliente, _, reloj) = Nuevo(cdn: cdn);
        Assert.NotNull(await cliente.TryResolveAsync("synergos-badge"));

        cdn.Con("/synergos/registry.json", """{"elements":[]}""");
        reloj.Avanzar(TimeSpan.FromHours(1));

        Assert.NotNull(await cliente.TryResolveAsync("synergos-badge"));
    }

    [Fact]
    public async Task Sin_snapshot_y_con_el_CDN_caido_devuelve_null_sin_reventar()
    {
        // El caso del arranque en frío contra un CDN que no está. Tiene que degradar —el emitter
        // pinta su comentario de relleno— y no tumbar el render.
        var cdn = new CdnFalso { Caido = true };
        var (cliente, _, _) = Nuevo(cdn: cdn);

        Assert.Null(await cliente.TryResolveAsync("synergos-badge"));
    }

    [Fact]
    public async Task Veinte_bloques_en_una_pagina_leen_el_registry_UNA_vez()
    {
        // Ir a la red por elemento metería una ida y vuelta en el camino crítico de cada visita.
        // El registry se lee una vez y se sirve de memoria hasta que vence.
        var (cliente, cdn, _) = Nuevo();

        for (var i = 0; i < 20; i++) await cliente.TryResolveAsync("synergos-badge");

        // Una por el registry y una por el manifiesto del badge. Ni una más.
        Assert.Equal(2, cdn.Peticiones);
    }

    [Fact]
    public async Task El_manifiesto_versionado_se_cachea_para_SIEMPRE()
    {
        // Vale porque la ruta lleva la versión exacta, que por contrato no cambia de contenido.
        // Releerla sería trabajo sin información.
        var (cliente, cdn, reloj) = Nuevo();
        await cliente.TryResolveAsync("synergos-badge");
        var tras = cdn.Peticiones;

        reloj.Avanzar(TimeSpan.FromDays(30));
        await cliente.TryResolveAsync("synergos-badge");

        // El registry sí se releyó (venció); el manifiesto no.
        Assert.Equal(tras + 1, cdn.Peticiones);
        Assert.Single(cdn.Pedidas, r => r.EndsWith("manifest.json", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Un_manifiesto_que_no_responde_NO_pierde_el_elemento()
    {
        // Sin manifiesto se cae a `main.js`, que es lo que publica el 100% de los elementos hoy.
        // Perder el nombre del script es degradar la información; perder el elemento entero por
        // eso sería degradar el producto.
        var cdn = new CdnFalso().Con("/synergos/registry.json", Registry);   // sin manifiestos
        var (cliente, _, _) = Nuevo(cdn: cdn);

        var d = await cliente.TryResolveAsync("synergos-badge");

        Assert.NotNull(d);
        Assert.EndsWith("/main.js", d!.MainEntryUri.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task El_snapshot_se_refresca_cuando_vence_y_recoge_la_version_nueva()
    {
        // Lo contrario del test del CDN caído: si el CDN SÍ responde, publicar tiene que servir
        // de algo. Un snapshot que no se refresca nunca es una caché eterna disfrazada.
        var cdn = new CdnFalso()
            .Con("/synergos/registry.json", Registry)
            .Con("/synergos/badge/angular/0.1.0/manifest.json", ManifiestoBadge);
        var (cliente, _, reloj) = Nuevo(cdn: cdn);
        Assert.Equal("0.1.0", (await cliente.TryResolveAsync("synergos-badge"))!.Version);

        cdn.Con("/synergos/registry.json", Registry.Replace("\"latest\": \"0.1.0\"", "\"latest\": \"0.2.0\""));
        reloj.Avanzar(TimeSpan.FromMinutes(5));

        Assert.Equal("0.2.0", (await cliente.TryResolveAsync("synergos-badge"))!.Version);
    }

    /// <summary>Un <see cref="IOptionsMonitor{T}"/> que no cambia — no hace falta más acá.</summary>
    private sealed class StaticOptionsMonitor<T> : IOptionsMonitor<T>
    {
        public StaticOptionsMonitor(T value) => CurrentValue = value;
        public T CurrentValue { get; }
        public T Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}

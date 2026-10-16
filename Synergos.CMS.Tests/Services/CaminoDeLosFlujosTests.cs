using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Services.Puerta;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// El camino de cada flujo (núcleo N3, FR-034): la regla de cuál gana, el servicio que la aplica a cualquier
/// flujo, y el gate de que dos declaraciones del mismo flujo no se contradicen.
/// </summary>
/// <remarks>
/// <para><b>Qué había antes.</b> La regla (<see cref="CaminoDeCompra"/>) ya era genérica, pero su cableado era
/// de Eventos: su clave de negocio, su campo, su resolutor con el literal del flujo. Salud y Alquiler lo
/// replicaban (NN-10, NN-11) y 007 también (NE-8). Ahora un flujo declara su camino en
/// <c>Synergos:Puerta:Flujos:{flujo}:Camino</c> y su resolutor lo pide a <see cref="ICaminoDeLosFlujos"/>.</para>
/// </remarks>
public sealed class CaminoDeLosFlujosTests
{
    private const string Flujo = "eventos.compra";

    /// <summary>
    /// Las declaraciones del camino que son de un vertical y de antes de N3 —piezas del superset, declaradas—:
    /// por flujo, la sección de negocio y su clave. Un flujo nuevo declara el suyo en la puerta y no entra acá.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, (string Seccion, string Clave)> DeclaracionesDelSitio =
        new Dictionary<string, (string, string)>(StringComparer.Ordinal) { [Flujo] = ("Eventos", "PurchasePath") };

    [Theory]
    [InlineData(null, null, true, CaminoDelFlujo.Puerta)]
    [InlineData(null, null, false, CaminoDelFlujo.Directa)]
    [InlineData(null, CaminoDelFlujo.Directa, true, CaminoDelFlujo.Directa)]
    [InlineData(null, CaminoDelFlujo.Puerta, false, CaminoDelFlujo.Puerta)]
    [InlineData(CaminoDelFlujo.Directa, CaminoDelFlujo.Puerta, true, CaminoDelFlujo.Directa)]
    [InlineData(CaminoDelFlujo.Puerta, null, false, CaminoDelFlujo.Puerta)]
    public void Cual_gana_el_sitio_luego_el_flujo_luego_la_regla(string? delSitio, string? delFlujo, bool abre, string esperado)
    {
        Assert.Equal(esperado, CaminoDelFlujo.Decidir(delSitio, delFlujo, abre));
    }

    [Fact]
    public void La_regla_de_la_compra_es_la_misma_que_la_de_cualquier_flujo()
    {
        foreach (var declarado in new string?[] { null, CaminoDelFlujo.Puerta, CaminoDelFlujo.Directa })
        {
            foreach (var abre in new[] { true, false })
            {
                Assert.Equal(CaminoDelFlujo.Decidir(declarado, abre), CaminoDeCompra.Decidir(declarado, abre));
            }
        }
    }

    [Fact]
    public void El_servicio_lee_el_camino_del_flujo_y_lo_que_declara_el_sitio_gana()
    {
        var log = new LogQueCuenta();
        var caminos = Caminos(conDestino: true, camino: " Directa ", log);

        // La puerta abre el flujo, y aun así gana lo declarado en él.
        Assert.Equal(CaminoDelFlujo.Directa, caminos.Decidir(Flujo));
        Assert.Equal(0, log.Avisos);

        // Lo que declara el sitio gana; si no coincide con lo del flujo, queda en el log.
        Assert.Equal(CaminoDelFlujo.Puerta, caminos.Decidir(Flujo, "puerta"));
        Assert.Equal(1, log.Avisos);
        Assert.Equal(CaminoDelFlujo.Directa, caminos.Decidir(Flujo, "DIRECTA"));
        Assert.Equal(1, log.Avisos);

        // Sin declaración en el flujo, la regla: la puerta abre.
        Assert.Equal(CaminoDelFlujo.Puerta, Caminos(conDestino: true, camino: null, log).Decidir(Flujo));
        // Y un flujo que la puerta no conoce va por la directa.
        Assert.Equal(CaminoDelFlujo.Directa, caminos.Decidir("otro.flujo"));
    }

    [Fact]
    public void Un_camino_mal_escrito_no_deja_arrancar_y_uno_bueno_no_dice_nada()
    {
        var validador = new ValidadorDeLaPuerta(TablaDeLaPuerta.Incrustada());

        var malo = validador.Validate(null, Ajustes(camino: "atajo"));
        Assert.Contains(malo.Failures ?? [], f => f.Contains(":Camino «atajo» tiene que ser «puerta» o «directa»", StringComparison.Ordinal));

        foreach (var bueno in new[] { "puerta", " Directa ", null })
        {
            var r = validador.Validate(null, Ajustes(camino: bueno));
            Assert.DoesNotContain(r.Failures ?? [], f => f.Contains(":Camino", StringComparison.Ordinal));
        }
    }

    /// <summary>
    /// El gate de cuál gana (FR-034): en la configuración del repo, lo que declara el flujo en la puerta y lo
    /// que declara el sitio del vertical para el mismo flujo no se contradicen, en la base ni en cada entorno.
    /// </summary>
    [Fact]
    public void En_la_configuracion_del_repo_las_dos_declaraciones_del_camino_no_se_contradicen()
    {
        var web = Path.Combine(RepoRoot(), "Synergos.CMS.Web");
        var baseCfg = Leer(Path.Combine(web, "appsettings.json"));
        var entornos = Directory.EnumerateFiles(web, "appsettings.*.json")
            .Where(f => !Path.GetFileName(f).StartsWith("appsettings-", StringComparison.Ordinal))
            .ToList();

        // Hay sujeto: el flujo de la tabla está en la puerta de la base, y hay entornos que leer.
        Assert.NotNull(baseCfg?["Synergos"]?["Puerta"]?["Flujos"]?[Flujo]);
        Assert.NotEmpty(entornos);

        var errores = Contradicciones(baseCfg, null).ToList();
        foreach (var e in entornos)
        {
            errores.AddRange(Contradicciones(baseCfg, Leer(e)).Select(x => $"{Path.GetFileName(e)}: {x}"));
        }

        Assert.True(errores.Count == 0, string.Join(Environment.NewLine, errores));
    }

    [Fact]
    public void El_gate_ve_una_contradiccion_en_la_base_en_un_sitio_y_en_un_entorno()
    {
        var conCamino = JsonNode.Parse($$"""
            { "Synergos": { "Puerta": { "Flujos": { "{{Flujo}}": { "Camino": "puerta" } } },
              "Features": { "Eventos": { "PurchasePath": "Directa", "Sitios": { "b": { "PurchasePath": "puerta" } } } } } }
            """);
        Assert.Equal(["eventos.compra: el sitio declara «directa» (Synergos:Features:Eventos:PurchasePath) y la puerta, «puerta»."],
            Contradicciones(conCamino, null));

        var base0 = JsonNode.Parse("""{ "Synergos": { "Features": { "Eventos": { "PurchasePath": "directa" } } } }""");
        var entorno = JsonNode.Parse($$"""{ "Synergos": { "Puerta": { "Flujos": { "{{Flujo}}": { "Camino": "puerta" } } } } }""");
        Assert.Single(Contradicciones(base0, entorno));
        Assert.Empty(Contradicciones(base0, null));
    }

    /// <summary>Las contradicciones de la base con <paramref name="entorno"/> encima (un entorno gana a la base).</summary>
    private static IReadOnlyList<string> Contradicciones(JsonNode? baseCfg, JsonNode? entorno)
    {
        var errores = new List<string>();
        foreach (var (flujo, (seccion, clave)) in DeclaracionesDelSitio)
        {
            string? Valor(Func<JsonNode?, JsonNode?> camino)
                => CaminoDelFlujo.Normalizar((string?)(camino(entorno) ?? camino(baseCfg)));

            var delFlujo = Valor(c => c?["Synergos"]?["Puerta"]?["Flujos"]?[flujo]?["Camino"]);
            var delSitio = Valor(c => c?["Synergos"]?["Features"]?[seccion]?[clave]);
            if (CaminoDelFlujo.SeContradicen(delSitio, delFlujo))
            {
                errores.Add($"{flujo}: el sitio declara «{delSitio}» (Synergos:Features:{seccion}:{clave}) y la puerta, «{delFlujo}».");
            }

            var sitios = new[] { baseCfg, entorno }
                .SelectMany(c => (c?["Synergos"]?["Features"]?[seccion]?["Sitios"] as JsonObject)?.Select(s => s.Key) ?? [])
                .Distinct(StringComparer.OrdinalIgnoreCase);
            foreach (var sitio in sitios)
            {
                var propio = Valor(c => c?["Synergos"]?["Features"]?[seccion]?["Sitios"]?[sitio]?[clave]);
                if (CaminoDelFlujo.SeContradicen(propio, delFlujo))
                {
                    errores.Add($"{flujo}: el sitio {sitio} declara «{propio}» y la puerta, «{delFlujo}».");
                }
            }
        }
        return errores;
    }

    private static JsonNode? Leer(string ruta)
        => JsonNode.Parse(File.ReadAllText(ruta), documentOptions: new System.Text.Json.JsonDocumentOptions
        {
            CommentHandling = System.Text.Json.JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        });

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Synergos.CMS.sln")))
        {
            dir = dir.Parent;
        }
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static PuertaSettings Ajustes(string? camino)
    {
        var ajustes = new PuertaSettings();
        ajustes.Flujos[Flujo] = new FlujoDeLaPuertaSettings { Acceso = AccesoDeLaPuerta.Miembro, Camino = camino };
        return ajustes;
    }

    private static CaminoDeLosFlujos Caminos(bool conDestino, string? camino, ILogger<CaminoDeLosFlujos> log)
    {
        var monitor = Substitute.For<IOptionsMonitor<PuertaSettings>>();
        monitor.CurrentValue.Returns(Ajustes(camino));
        var puerta = new PuertaDeLosFlujos(TablaDeLaPuerta.Incrustada(), monitor, new DestinosDeLaPuerta(conDestino ? ["Eventos"] : []));
        return new CaminoDeLosFlujos(puerta, monitor, log);
    }

    private sealed class LogQueCuenta : ILogger<CaminoDeLosFlujos>
    {
        public int Avisos { get; private set; }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning)
            {
                Avisos++;
            }
        }
    }
}

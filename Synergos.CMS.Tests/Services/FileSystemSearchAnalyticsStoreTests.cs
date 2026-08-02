using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Synergos.CMS.Web.Services;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// Cubre <see cref="FileSystemSearchAnalyticsStore"/> — la analítica de búsqueda que dejó de
/// vivir en memoria.
/// </summary>
/// <remarks>
/// <para><b>Qué protegen estas pruebas.</b> El almacén anterior tenía tres defectos a la vez:
/// se perdía en cada reinicio, crecía sin tope indexado por texto del visitante, y ese texto no
/// caducaba nunca. Los tres se cierran escribiendo al día en el directorio que la retención
/// barre — así que lo primero que se fija aquí es <b>dónde</b> escribe: si el nombre del
/// archivo o del directorio se mueven, se escribe en un sitio y se purga en otro, y eso no
/// falla nunca, solo llena el disco.</para>
///
/// <para>Lo segundo es la tolerancia a basura. Es un log append-only escrito por un proceso que
/// puede morir a mitad de línea; una línea trunca no puede llevarse el tablero por delante.</para>
/// </remarks>
public sealed class FileSystemSearchAnalyticsStoreTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly StubHostEnvironment _env;

    public FileSystemSearchAnalyticsStoreTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "syn-search-analytics-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
        _env = new StubHostEnvironment(_tempRoot);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }

    private static readonly DateTime Day = new(2026, 8, 2, 10, 0, 0, DateTimeKind.Utc);

    private FileSystemSearchAnalyticsStore Store(Func<DateTime>? now = null)
        => new(_env, NullLogger<FileSystemSearchAnalyticsStore>.Instance, now ?? (() => Day));

    private string DirPath => Path.Combine(_tempRoot, "App_Data", "syn-search-analytics");

    private string FilePath(DateTime day) => Path.Combine(DirPath, day.ToString("yyyy-MM-dd") + ".jsonl");

    // ── Dónde escribe ────────────────────────────────────────────────────────

    /// <summary>
    /// Protege el CONTRATO DE UBICACIÓN con la retención. El nombre del archivo es la fecha
    /// porque es de ahí que <c>SearchAnalyticsRetentionPolicy</c> decide qué purgar; el
    /// directorio tiene que ser el mismo que ella barre. Separarlos no rompe nada visible: solo
    /// deja el texto de los visitantes acumulándose para siempre.
    /// </summary>
    [Fact]
    public void Escribe_en_el_archivo_del_dia_que_la_retencion_sabe_purgar()
    {
        Store().Record("apartamento en chapinero", 12, 34);

        Assert.True(File.Exists(FilePath(Day)));
        Assert.Equal("syn-search-analytics", FileSystemSearchAnalyticsStore.DirectoryName);
        Assert.Equal("yyyy-MM-dd", FileSystemSearchAnalyticsStore.FileDateFormat);
    }

    /// <summary>
    /// Protege que sea APPEND: una búsqueda no puede pisar a la anterior. Es lo que hace que el
    /// conteo signifique algo.
    /// </summary>
    [Fact]
    public void Cada_busqueda_agrega_una_linea_no_reemplaza_el_archivo()
    {
        var sut = Store();
        sut.Record("bogota", 3, 10);
        sut.Record("bogota", 3, 12);
        sut.Record("medellin", 5, 8);

        Assert.Equal(3, File.ReadAllLines(FilePath(Day)).Length);
        Assert.Equal(3, sut.GetTopQueries(Day.AddHours(-1), Day.AddHours(1), 10).Sum(s => s.Count));
    }

    /// <summary>
    /// Protege la supervivencia al reinicio, que es el defecto original: dos instancias
    /// distintas del almacén —como antes y después de un despliegue— ven los mismos datos.
    /// </summary>
    [Fact]
    public void Una_instancia_NUEVA_ve_lo_que_escribio_la_anterior()
    {
        Store().Record("reinicio", 1, 5);

        var despuesDelDespliegue = Store();

        var top = despuesDelDespliegue.GetTopQueries(Day.AddHours(-1), Day.AddHours(1), 10);
        Assert.Equal("reinicio", Assert.Single(top).Query);
    }

    /// <summary>
    /// Protege la normalización, heredada del almacén en memoria: recortada y en minúsculas, y
    /// lo vacío no se registra. Sin esto, "Bogotá", "bogotá " y "BOGOTÁ" serían tres consultas
    /// distintas y el top diría cualquier cosa.
    /// </summary>
    [Fact]
    public void La_consulta_se_normaliza_y_la_vacia_no_se_registra()
    {
        var sut = Store();
        sut.Record("  Bogotá  ", 2, 1);
        sut.Record("BOGOTÁ", 2, 1);
        sut.Record("   ", 0, 1);
        sut.Record(string.Empty, 0, 1);

        var top = sut.GetTopQueries(Day.AddHours(-1), Day.AddHours(1), 10);
        var unica = Assert.Single(top);
        Assert.Equal("bogotá", unica.Query);
        Assert.Equal(2, unica.Count);
    }

    // ── La ventana ───────────────────────────────────────────────────────────

    /// <summary>
    /// Protege el corte por ventana: el tablero pide 7 días y no puede recibir los de hace un
    /// mes. El filtro grueso es por nombre de archivo y el fino por marca de tiempo.
    /// </summary>
    [Fact]
    public void Solo_cuenta_lo_que_cae_dentro_de_la_ventana_pedida()
    {
        var viejo = Day.AddDays(-30);
        Store(() => viejo).Record("antigua", 1, 1);
        Store().Record("reciente", 1, 1);

        var sut = Store();
        var ultimaSemana = sut.GetTopQueries(Day.AddDays(-7), Day.AddHours(1), 10);

        Assert.Equal("reciente", Assert.Single(ultimaSemana).Query);
        Assert.Equal(2, sut.GetTopQueries(Day.AddDays(-60), Day.AddHours(1), 10).Count);
    }

    /// <summary>
    /// Protege el corte FINO dentro de un mismo día. El archivo del día entra por nombre, pero
    /// una búsqueda de esta mañana no pertenece a una ventana que empieza a mediodía.
    /// </summary>
    [Fact]
    public void Dentro_del_mismo_dia_corta_por_hora_no_por_archivo()
    {
        Store(() => Day.Date.AddHours(8)).Record("temprano", 1, 1);
        Store(() => Day.Date.AddHours(20)).Record("tarde", 1, 1);

        var top = Store().GetTopQueries(Day.Date.AddHours(12), Day.Date.AddHours(23), 10);

        Assert.Equal("tarde", Assert.Single(top).Query);
    }

    // ── El agregado ──────────────────────────────────────────────────────────

    /// <summary>
    /// Protege el orden y el tope. El desempate alfabético no es cosmético: sin él, dos
    /// consultas con el mismo conteo se turnan entre recargas y el tablero parece inestable.
    /// </summary>
    [Fact]
    public void El_top_ordena_por_conteo_y_desempata_estable()
    {
        var sut = Store();
        sut.Record("zeta", 1, 1);
        sut.Record("alfa", 1, 1);
        sut.Record("popular", 1, 1);
        sut.Record("popular", 1, 1);

        var top = sut.GetTopQueries(Day.AddHours(-1), Day.AddHours(1), 10);
        Assert.Equal(new[] { "popular", "alfa", "zeta" }, top.Select(s => s.Query));

        Assert.Equal("popular", Assert.Single(sut.GetTopQueries(Day.AddHours(-1), Day.AddHours(1), 1)).Query);
        // Un límite absurdo devuelve al menos uno, igual que hacía el almacén anterior.
        Assert.Single(sut.GetTopQueries(Day.AddHours(-1), Day.AddHours(1), 0));
    }

    /// <summary>
    /// Protege la semántica de «sin resultados»: es del ÚLTIMO intento, no de todos. Una
    /// consulta que no encontraba nada y ahora sí, dejó de ser un hueco de contenido — y ese
    /// listado existe para decidir qué contenido crear.
    /// </summary>
    [Fact]
    public void Sin_resultados_mira_el_ULTIMO_intento_no_el_historial()
    {
        var sut = Store();
        sut.Record("ya-resuelta", 0, 1);
        Store(() => Day.AddMinutes(1)).Record("ya-resuelta", 7, 1);
        sut.Record("sigue-vacia", 0, 1);

        var huecos = sut.GetTopNoResultQueries(Day.AddHours(-1), Day.AddHours(1), 10);

        Assert.Equal("sigue-vacia", Assert.Single(huecos).Query);
    }

    // ── Degradación ──────────────────────────────────────────────────────────

    /// <summary>
    /// Protege el arranque en frío: sin directorio no hay error, hay listas vacías. Es el
    /// estado de todo despliegue nuevo hasta la primera búsqueda.
    /// </summary>
    [Fact]
    public void Sin_ninguna_busqueda_todavia_devuelve_vacio_y_no_lanza()
    {
        var sut = Store();

        Assert.Empty(sut.GetTopQueries(Day.AddDays(-7), Day, 10));
        Assert.Empty(sut.GetTopNoResultQueries(Day.AddDays(-7), Day, 10));
    }

    /// <summary>
    /// Protege la tolerancia a líneas rotas. El proceso puede morir a mitad de escritura; que
    /// un renglón trunco vacíe el tablero sería un fallo silencioso —no hay excepción, solo
    /// datos que desaparecen—, que es la peor forma de fallar.
    /// </summary>
    [Fact]
    public void Una_linea_rota_se_salta_sin_llevarse_el_resto()
    {
        Store().Record("buena", 1, 1);
        File.AppendAllText(FilePath(Day), "{\"query\":\"trunca\",\"resul" + Environment.NewLine);
        File.AppendAllText(FilePath(Day), "esto no es json" + Environment.NewLine);
        File.AppendAllText(FilePath(Day), Environment.NewLine);
        Store(() => Day.AddMinutes(1)).Record("otra-buena", 1, 1);

        var top = Store().GetTopQueries(Day.AddHours(-1), Day.AddHours(1), 10);

        Assert.Equal(new[] { "buena", "otra-buena" }, top.Select(s => s.Query).OrderBy(q => q, StringComparer.Ordinal));
    }

    /// <summary>
    /// Protege que un archivo ajeno en el directorio no rompa la lectura. La retención ignora
    /// lo que no parece una fecha; esto tiene que hacer lo mismo.
    /// </summary>
    [Fact]
    public void Un_archivo_que_no_es_una_fecha_se_ignora()
    {
        Store().Record("buena", 1, 1);
        File.WriteAllText(Path.Combine(DirPath, "notas.jsonl"), "{\"query\":\"colada\",\"resultCount\":1,\"elapsedMs\":1,\"atUtc\":\"2026-08-02T10:00:00Z\"}\n");

        var top = Store().GetTopQueries(Day.AddHours(-1), Day.AddHours(1), 10);

        Assert.Equal("buena", Assert.Single(top).Query);
    }

    /// <summary>
    /// Protege lo más importante del camino de escritura: <b>registrar no puede lanzar</b>.
    /// Corre dentro de una búsqueda del visitante, y un disco lleno puede costar una métrica,
    /// nunca la búsqueda.
    /// </summary>
    [Fact]
    public void Registrar_NUNCA_lanza_aunque_el_destino_sea_imposible()
    {
        var imposible = new StubHostEnvironment(Path.Combine(_tempRoot, "archivo-no-carpeta"));
        File.WriteAllText(imposible.ContentRootPath, "soy un archivo, no un directorio");

        var sut = new FileSystemSearchAnalyticsStore(
            imposible, NullLogger<FileSystemSearchAnalyticsStore>.Instance, () => Day);

        var ex = Record.Exception(() => sut.Record("da igual", 1, 1));

        Assert.Null(ex);
    }

    private sealed class StubHostEnvironment : IHostEnvironment
    {
        public StubHostEnvironment(string contentRootPath) => ContentRootPath = contentRootPath;
        public string ApplicationName { get; set; } = "Synergos.CMS.Tests";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
        public string ContentRootPath { get; set; }
        public string EnvironmentName { get; set; } = "Tests";
    }
}

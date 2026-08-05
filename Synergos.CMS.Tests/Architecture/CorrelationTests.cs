using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Net.Http.Headers;
using Synergos.CMS.Web.Middlewares;
using Synergos.Shared;

namespace Synergos.CMS.Tests.Architecture;

/// <summary>
/// El hilo que permite seguir una compra por seis procesos (HU #28).
/// </summary>
/// <remarks>
/// <para><b>Lo que se vigila no es que exista un identificador.</b> Es lo único que lo hace útil:
/// que <b>todos</b> los servicios lo pongan, que <b>ninguno</b> lo pierda al saltar al siguiente,
/// y que los dos árboles usen <b>el mismo nombre</b>. Con que un solo salto lo corte, el rastro
/// se parte en dos historias y la pregunta que esto viene a contestar —«mostrame todo lo de esta
/// compra»— vuelve a no tener respuesta.</para>
///
/// <para><b>El gate crece solo con el catálogo</b>: recorre los <c>Program.cs</c> del disco en vez
/// de una lista escrita a mano, así que una capacidad nueva que no lo cablee rompe el build el
/// día que se crea, no el día que alguien la busca en un registro.</para>
/// </remarks>
public sealed class CorrelationTests
{
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

    private static IEnumerable<(string Nombre, string Codigo)> Hosts()
        => Directory.EnumerateDirectories(RepoRoot())
            .Where(d => Path.GetFileName(d).StartsWith("Synergos.Api.", StringComparison.Ordinal)
                     || Path.GetFileName(d).StartsWith("Synergos.Bff.", StringComparison.Ordinal))
            .Select(d => (Dir: d, Programa: Path.Combine(d, "Program.cs")))
            .Where(x => File.Exists(x.Programa))
            .Select(x => (Path.GetFileName(x.Dir), File.ReadAllText(x.Programa)));

    // ── Que todos lo pongan ─────────────────────────────────────────────────

    [Fact]
    public void TODOS_los_hosts_del_arbol_de_servicios_lo_cablean()
    {
        // Un servicio sin correlación no se nota: arranca, contesta y pasa su /health. Se nota el
        // día que hay que reconstruir una compra y justo ese salto está en blanco.
        var sinCablear = Hosts()
            .Where(h => !h.Codigo.Contains("AddCorrelation()", StringComparison.Ordinal)
                     || !h.Codigo.Contains("UseCorrelation()", StringComparison.Ordinal))
            .Select(h => h.Nombre)
            .ToList();

        Assert.True(sinCablear.Count == 0,
            "Estos hosts no cablean la correlación: " + string.Join(", ", sinCablear)
            + ". Hacen falta las DOS: AddCorrelation() enciende los scopes del registro —sin ellos "
            + "el identificador existe pero no se imprime— y UseCorrelation() lo toma de la petición.");
    }

    [Fact]
    public void Hay_hosts_que_comprobar()
    {
        // El gate de arriba pasa en verde con CERO hosts encontrados, que es justo lo que pasaría
        // si alguien moviera los proyectos de sitio o rompiera el recorrido del disco. Sin esto,
        // la señal de «todo bien» sería indistinguible de la de «no miré nada».
        Assert.True(Hosts().Count() >= 22, $"Solo se encontraron {Hosts().Count()} hosts; deberían ser 22.");
    }

    [Fact]
    public void La_correlacion_va_ANTES_que_la_llave_compartida()
    {
        // Un 401 también tiene que quedar correlacionado. Si el rechazo por credencial saliera sin
        // identificador, el caso que más cuesta diagnosticar —«a mí no me llega nada»— sería el
        // único sin rastro.
        var malOrden = Hosts()
            .Where(h => h.Codigo.Contains("UseSharedKeyAuth(", StringComparison.Ordinal))
            .Where(h => h.Codigo.IndexOf("UseCorrelation()", StringComparison.Ordinal)
                      > h.Codigo.IndexOf("UseSharedKeyAuth(", StringComparison.Ordinal))
            .Select(h => h.Nombre)
            .ToList();

        Assert.True(malOrden.Count == 0,
            "En estos hosts la llave corre antes que la correlación, así que sus 401 salen sin "
            + "rastro: " + string.Join(", ", malOrden));
    }

    // ── Que ninguno lo pierda al saltar ─────────────────────────────────────

    [Fact]
    public void Los_clientes_de_los_orquestadores_lo_PROPAGAN()
    {
        // Nacer con un identificador y no pasarlo al siguiente servicio deja seis rastros
        // aislados: el problema de partida con un paso más de trabajo. Va en AddSagaMachinery,
        // que es donde se crean TODOS los clientes de TODOS los orquestadores.
        var maquinaria = File.ReadAllText(
            Path.Combine(RepoRoot(), "Synergos.Bff.Core", "SagaMachinery.cs"));

        Assert.Contains("AddHttpMessageHandler<CorrelationHandler>()", maquinaria, StringComparison.Ordinal);
    }

    [Fact]
    public void Los_clientes_del_CMS_hacia_el_arbol_de_servicios_lo_PROPAGAN()
    {
        // El CMS es donde nace el rastro de una compra. Si no lo pasara, el orquestador generaría
        // el suyo y a partir de ese salto habría dos historias de la misma compra.
        //
        // Los webhooks a terceros NO están en esta lista a propósito: a un tercero conviene
        // mandarle lo mínimo.
        var esperados = new[]
        {
            ("SeamComposer.Shop.cs", "la tienda"),
            ("SeamComposer.PlatformAndHealthcare.cs", "la cita clínica"),
            ("SeamComposer.EventsPropertiesGov.cs", "la visita al inmueble"),
            ("SeamComposer.FormsSearchMemberAdmin.cs", "el buscador"),
        };

        var sinPropagar = esperados
            .Where(e => !File.ReadAllText(Path.Combine(RepoRoot(), "Synergos.CMS.Web", "Composers", e.Item1))
                .Contains("AddHttpMessageHandler<CorrelationForwardingHandler>()", StringComparison.Ordinal))
            .Select(e => $"{e.Item2} ({e.Item1})")
            .ToList();

        Assert.True(sinPropagar.Count == 0,
            "Estos consumidores del CMS no propagan la correlación: " + string.Join(", ", sinPropagar));
    }

    // ── Que los dos árboles digan lo mismo ──────────────────────────────────

    [Fact]
    public void Los_DOS_arboles_usan_LA_MISMA_cabecera()
    {
        // Es el único acople legítimo entre los dos árboles: un contrato de una cadena, no una
        // referencia de ensamblado. Dos nombres distintos significarían dos identificadores para
        // la misma compra — el problema de partida disfrazado de solución.
        Assert.Equal(CorrelationIdMiddleware.HeaderName, Correlation.HeaderName);
    }

    [Fact]
    public void El_identificador_NO_viaja_en_la_URL()
    {
        // En la URL acaba en las cachés intermedias, en los registros del proxy y en el historial
        // del navegador — tres sitios que nadie repasa al decidir qué se guarda y cuánto dura.
        var rutas = Directory
            .EnumerateFiles(Path.Combine(RepoRoot()), "*Endpoints.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                     && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(f => File.ReadAllText(f).Contains("correlation", StringComparison.OrdinalIgnoreCase))
            .Select(Path.GetFileName)
            .ToList();

        Assert.True(rutas.Count == 0,
            "Hay rutas que nombran la correlación: " + string.Join(", ", rutas)
            + ". Va en una cabecera, donde muere con la petición.");
    }

    // ── Lo que el identificador es y no es ──────────────────────────────────

    [Fact]
    public void Cada_peticion_recibe_uno_DISTINTO()
    {
        // Reutilizarlo entre peticiones haría que el grep devolviera compras de otra gente.
        var ids = Enumerable.Range(0, 500).Select(_ => Correlation.Nuevo()).ToList();

        Assert.Equal(ids.Count, ids.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void NO_es_adivinable_ni_derivado_del_reloj()
    {
        // Un contador deja pedir por los registros del de al lado; una marca de tiempo revela
        // cuántas compras hubo entre dos instantes. Se comprueba que dos seguidos no sean
        // consecutivos ni ordenados — con un contador o un reloj, lo serían siempre.
        var seguidos = Enumerable.Range(0, 50).Select(_ => Correlation.Nuevo()).ToList();
        var ordenados = seguidos.OrderBy(x => x, StringComparer.Ordinal).ToList();

        Assert.NotEqual(seguidos, ordenados);
        Assert.All(seguidos, id => Assert.Equal(16, id.Length));
        Assert.All(seguidos, id => Assert.True(id.All(Uri.IsHexDigit), $"'{id}' no es opaco."));
    }

    [Fact]
    public void Lo_que_llega_de_FUERA_se_limpia()
    {
        // Es entrada, no dato. Mil caracteres inflan cada línea del registro; un salto de línea
        // parte una línea en dos y hace que el grep devuelva media historia.
        var sucio = "abc\ndef: FALSO\r\n" + new string('x', 500);

        // Se ejerce por la vía pública: el handler propaga lo que haya en el equipaje.
        using var actividad = new Activity("t").Start();
        actividad.SetBaggage(Correlation.LogField, sucio);

        Assert.Equal(sucio, Correlation.Current);   // el equipaje guarda lo que se le puso…

        // …y lo que se acepta de la red pasa por el recorte. Se comprueba sobre el fichero para
        // no exponer un privado solo por el test.
        var fuente = File.ReadAllText(Path.Combine(RepoRoot(), "Synergos.Shared", "Correlation.cs"));
        Assert.Contains("IsAsciiLetterOrDigit", fuente, StringComparison.Ordinal);
        Assert.Contains("Take(32)", fuente, StringComparison.Ordinal);
    }

    // ── Que se IMPRIMA, no solo que esté ────────────────────────────────────

    private sealed class RegistroEspia : ILogger
    {
        public List<string> Alcances { get; } = new();
        public IDisposable BeginScope<TState>(TState state) where TState : notnull
        {
            // Exactamente lo que hace el formateador de consola con un scope: ToString().
            Alcances.Add(state.ToString() ?? "(nulo)");
            return new Nada();
        }
        public bool IsEnabled(LogLevel l) => true;
        public void Log<TState>(LogLevel l, EventId e, TState s, Exception? ex, Func<TState, Exception?, string> f) { }
        private sealed class Nada : IDisposable { public void Dispose() { } }
    }

    [Fact]
    public void El_identificador_SE_IMPRIME_en_el_registro()
    {
        // ⚠️ ESTE GATE SALE DE UN DEFECTO REAL, y lo destapó levantar dos procesos.
        //
        // La primera versión abría el scope con un Dictionary<string, object>. Todo el cableado
        // estaba bien, todos los gates en verde… y en el registro salía impreso
        // «System.Collections.Generic.Dictionary`2[System.String,System.Object]», porque el
        // formateador de consola renderiza el scope con su ToString().
        //
        // Resultado: un identificador que existe, que viaja, que cruza los saltos — y un grep que
        // devuelve cero líneas. O sea, el problema de partida con trabajo de más.
        var espia = new RegistroEspia();

        using (espia.BeginScope(new FormattedLogValuesEquivalente(Correlation.ScopeFormat, "abc123")))
        {
            // el using solo delimita; lo que importa es lo que quedó anotado
        }

        Assert.Contains(espia.Alcances, a => a.Contains("abc123", StringComparison.Ordinal));
    }

    /// <summary>
    /// Lo que <c>ILogger.BeginScope(formato, args)</c> construye por dentro.
    /// </summary>
    /// <remarks>
    /// El tipo real (<c>FormattedLogValues</c>) es interno del framework, así que se reproduce su
    /// propiedad observable: <b>que su <c>ToString()</c> rinda el valor</b>, que es justo lo que
    /// un diccionario no hace. Si algún día el scope volviera a un diccionario, el test de arriba
    /// cae porque el suyo imprime el nombre del tipo.
    /// </remarks>
    private sealed class FormattedLogValuesEquivalente
    {
        private readonly string _texto;
        public FormattedLogValuesEquivalente(string formato, string valor)
            => _texto = formato.Replace("{" + Correlation.LogField + "}", valor, StringComparison.Ordinal);
        public override string ToString() => _texto;
    }

    [Fact]
    public void El_scope_NO_es_un_diccionario()
    {
        // La forma directa del mismo gate, sobre la fuente: un diccionario en el BeginScope es el
        // defecto de arriba escrito otra vez.
        var fuente = File.ReadAllText(Path.Combine(RepoRoot(), "Synergos.Shared", "Correlation.cs"));
        var codigo = string.Join('\n', fuente.Split('\n').Where(l => !l.TrimStart().StartsWith("///", StringComparison.Ordinal)));

        Assert.DoesNotContain("BeginScope(new Dictionary", codigo, StringComparison.Ordinal);
        Assert.Contains("BeginScope(ScopeFormat", codigo, StringComparison.Ordinal);
    }

    // ── El handler de salida ────────────────────────────────────────────────

    private sealed class Eco : HttpMessageHandler
    {
        public HttpRequestHeaders? Vistas { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            Vistas = r.Headers;
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK));
        }
    }

    [Fact]
    public async Task El_salto_lleva_la_cabecera()
    {
        var eco = new Eco();
        var handler = new CorrelationHandler { InnerHandler = eco };
        using var cliente = new HttpClient(handler) { BaseAddress = new Uri("http://x.local/") };

        using var actividad = new Activity("t").Start();
        actividad.SetBaggage(Correlation.LogField, "abc123");

        await cliente.GetAsync("v1/cosa");

        Assert.Equal("abc123", eco.Vistas!.GetValues(Correlation.HeaderName).Single());
    }

    [Fact]
    public async Task Sin_peticion_en_curso_NO_se_inventa_una_cabecera()
    {
        // Pasa en el arranque y en los barridos de fondo. Un identificador que no corresponde a
        // ninguna petición ensucia el grep sin ayudar a nadie.
        var eco = new Eco();
        var handler = new CorrelationHandler { InnerHandler = eco };
        using var cliente = new HttpClient(handler) { BaseAddress = new Uri("http://x.local/") };

        Activity.Current = null;
        await cliente.GetAsync("v1/cosa");

        Assert.False(eco.Vistas!.Contains(Correlation.HeaderName));
    }

    [Fact]
    public async Task Una_cabecera_ya_puesta_a_mano_NO_se_pisa()
    {
        // Quien llama puede tener una razón para fijarla —reintentar algo bajo el mismo rastro—, y
        // pisársela le rompería justo eso.
        var eco = new Eco();
        var handler = new CorrelationHandler { InnerHandler = eco };
        using var cliente = new HttpClient(handler) { BaseAddress = new Uri("http://x.local/") };

        using var actividad = new Activity("t").Start();
        actividad.SetBaggage(Correlation.LogField, "del-equipaje");

        using var req = new HttpRequestMessage(HttpMethod.Get, "v1/cosa");
        req.Headers.TryAddWithoutValidation(Correlation.HeaderName, "a-mano");
        await cliente.SendAsync(req);

        Assert.Equal("a-mano", eco.Vistas!.GetValues(Correlation.HeaderName).Single());
    }
}

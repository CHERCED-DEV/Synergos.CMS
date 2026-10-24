using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Services.Academy;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// El registro de las matrículas de la puerta (010-educacion-matricula T015 y T023): anota en el libro sólo lo que el
/// orquestador devuelve CERRADO para el sujeto del miembro, y reconcilia al leer «mi aprendizaje» con techo.
/// </summary>
public sealed class RegistroDeMatriculasDeLaPuertaTests
{
    private static readonly Guid Ana = Guid.Parse("0f2e7a1c-5b3d-4c8e-9a6f-1d2b3c4e5f60");
    private const string Correo = "ana@synergos.co";
    private const string Curso = "dev-clean-architecture";

    private static StubEnrollmentService Libro()
        => new(new StubCourseCatalogProvider(new StubContentStream(new StubSocialGraphService(), new StubReactionService(),
            () => new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc))), new StubPaymentProvider());

    private static RegistroDeMatriculasDeLaPuerta Registro(OrquestadorFalso orquestador, StubEnrollmentService libro, bool hayDestino = true, TimeSpan? techo = null)
    {
        var puerta = new PuertaSettings();
        puerta.Flujos["educacion.matricula"] = new FlujoDeLaPuertaSettings { Acceso = AccesoDeLaPuerta.Miembro, SujetoKind = "educacion.alumno" };
        var monitor = Substitute.For<IOptionsMonitor<PuertaSettings>>();
        monitor.CurrentValue.Returns(puerta);
        return new RegistroDeMatriculasDeLaPuerta(orquestador, libro, libro, monitor,
            NullLogger<RegistroDeMatriculasDeLaPuerta>.Instance, hayDestino, techo);
    }

    private static object Matricula(string id, string estado = "Completed", string curso = Curso, decimal total = 320_000m)
        => new
        {
            id, flujo = "educacion.matricula", numero = id.ToUpperInvariant()[..Math.Min(8, id.Length)], estado,
            datos = new { curso = new { kind = "educacion.curso", id = curso }, plan = "full", total = new { amount = total, currency = "COP" } },
            compensacionesPendientes = 0, error = (string?)null, deshaciendo = false,
        };

    [Fact]
    public async Task Una_matricula_cerrada_se_anota_con_el_curso_y_el_total_del_orquestador()
    {
        var orquestador = new OrquestadorFalso().Con("m-1", Matricula("m-1"));
        var libro = Libro();

        var r = await Registro(orquestador, libro).AnotarAsync("m-1", Ana, "Ana", Correo, default);

        Assert.True(r.Bien, r.Mensaje);
        Assert.Equal(Curso, r.Anotacion!.CourseId);
        Assert.Equal(new CourseEnrollmentStats(1, 320_000m), await libro.GetCourseStatsAsync(Curso));
        // Leyó con el sujeto del miembro de la sesión, que es el mismo que pone la puerta.
        Assert.Equal($"educacion.alumno:{Ana:n}", orquestador.Sujetos.Single());
    }

    [Fact]
    public async Task Anotar_dos_veces_deja_una_sola_matricula()
    {
        var orquestador = new OrquestadorFalso().Con("m-1", Matricula("m-1"));
        var libro = Libro();
        var registro = Registro(orquestador, libro);

        await registro.AnotarAsync("m-1", Ana, "Ana", Correo, default);
        var otra = await registro.AnotarAsync("m-1", Ana, "Ana", Correo, default);

        Assert.True(otra.Anotacion!.YaEstaba);
        Assert.Single(await libro.GetEnrollmentsAsync(Correo));
    }

    [Theory]
    [InlineData("Running", "academy.enrollment_not_closed")]
    [InlineData("Compensated", "academy.enrollment_undone")]
    [InlineData("Compensating", "academy.enrollment_undoing")]
    public async Task Sin_cerrar_no_se_abre_el_aula(string estado, string codigo)
    {
        var orquestador = new OrquestadorFalso().Con("m-1", Matricula("m-1", estado));
        var libro = Libro();

        var r = await Registro(orquestador, libro).AnotarAsync("m-1", Ana, "Ana", Correo, default);

        Assert.Equal(409, r.Estado);
        Assert.Equal(codigo, r.Codigo);
        Assert.Empty(await libro.GetEnrollmentsAsync(Correo));
    }

    [Fact]
    public async Task La_matricula_de_otro_es_un_404_y_no_se_anota()
    {
        var libro = Libro();

        var r = await Registro(new OrquestadorFalso(), libro).AnotarAsync("de-otro", Ana, "Ana", Correo, default);

        Assert.Equal(404, r.Estado);
        Assert.Empty(await libro.GetEnrollmentsAsync(Correo));
    }

    [Fact]
    public async Task Con_el_orquestador_caido_es_transitorio()
    {
        var r = await Registro(new OrquestadorFalso { Caido = true }, Libro()).AnotarAsync("m-1", Ana, "Ana", Correo, default);

        Assert.Equal(503, r.Estado);
        Assert.True(r.Transitorio);
    }

    [Fact]
    public async Task Sin_destino_no_sale_a_la_red_y_lo_dice()
    {
        var orquestador = new OrquestadorFalso().Con("m-1", Matricula("m-1"));

        var r = await Registro(orquestador, Libro(), hayDestino: false).AnotarAsync("m-1", Ana, "Ana", Correo, default);

        Assert.Equal(503, r.Estado);
        Assert.Equal("academy.registro_no_disponible", r.Codigo);
        Assert.Equal(0, orquestador.Pedidos);
    }

    // ── Reconciliar (T023) ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Reconciliar_anota_la_cerrada_que_faltaba_y_no_cobra_nada()
    {
        var orquestador = new OrquestadorFalso().ConLista(Matricula("m-1"));
        var libro = Libro();

        await Registro(orquestador, libro).ReconciliarAsync(Ana, "Ana", Correo, default);

        var fila = Assert.Single(await libro.GetEnrollmentsAsync(Correo));
        Assert.Equal(Curso, fila.CourseId);
        Assert.Equal(["m-1"], await libro.AnotadasAsync(Correo));
    }

    [Fact]
    public async Task Reconciliar_otra_vez_no_repite_ni_toca_un_curso_ya_activo()
    {
        var orquestador = new OrquestadorFalso().ConLista(Matricula("m-1"), Matricula("m-2"));
        var libro = Libro();
        var registro = Registro(orquestador, libro);

        await registro.ReconciliarAsync(Ana, "Ana", Correo, default);
        await registro.ReconciliarAsync(Ana, "Ana", Correo, default);

        // m-2 es del MISMO curso: no hay otra fila, y no se reintenta en cada lectura.
        Assert.Single(await libro.GetEnrollmentsAsync(Correo));
        Assert.Equal(["m-1"], await libro.AnotadasAsync(Correo));
    }

    [Fact]
    public async Task Reconciliar_respeta_el_tope_de_matriculas_por_lectura()
    {
        var cursos = Enumerable.Range(1, RegistroDeMatriculasDeLaPuerta.TopeDeReconciliacion + 3)
            .Select(i => Matricula($"m-{i}", curso: $"curso-{i}")).ToArray();
        var libro = Libro();

        await Registro(new OrquestadorFalso().ConLista(cursos), libro).ReconciliarAsync(Ana, "Ana", Correo, default);

        Assert.Equal(RegistroDeMatriculasDeLaPuerta.TopeDeReconciliacion, (await libro.AnotadasAsync(Correo)).Count);
    }

    [Fact]
    public async Task Reconciliar_con_el_orquestador_lento_corta_en_su_techo_sin_fallar()
    {
        var orquestador = new OrquestadorFalso { Demora = TimeSpan.FromSeconds(5) }.ConLista(Matricula("m-1"));
        var libro = Libro();
        var reloj = System.Diagnostics.Stopwatch.StartNew();

        await Registro(orquestador, libro, techo: TimeSpan.FromMilliseconds(200)).ReconciliarAsync(Ana, "Ana", Correo, default);

        Assert.True(reloj.Elapsed < TimeSpan.FromSeconds(3), $"Tardó {reloj.Elapsed}: el techo no cortó.");
        Assert.Empty(await libro.GetEnrollmentsAsync(Correo));
    }

    [Fact]
    public async Task Sin_destino_reconciliar_no_llama_a_nadie()
    {
        var orquestador = new OrquestadorFalso().ConLista(Matricula("m-1"));

        await Registro(orquestador, Libro(), hayDestino: false).ReconciliarAsync(Ana, "Ana", Correo, default);

        Assert.Equal(0, orquestador.Pedidos);
    }

    /// <summary>El orquestador de Educación, de mentira: contesta lo que se le cargó y apunta el sujeto de cada lectura.</summary>
    internal sealed class OrquestadorFalso : IHttpClientFactory
    {
        private readonly Dictionary<string, string> _matriculas = new(StringComparer.Ordinal);
        private string _lista = """{"items":[],"total":0,"offset":0,"hasMore":false}""";

        public bool Caido { get; init; }

        public TimeSpan Demora { get; init; }

        public int Pedidos { get; private set; }

        public List<string> Sujetos { get; } = [];

        public List<string> Rutas { get; } = [];

        public OrquestadorFalso Con(string id, object matricula)
        {
            _matriculas[id] = JsonSerializer.Serialize(matricula);
            return this;
        }

        public OrquestadorFalso ConLista(params object[] matriculas)
        {
            _lista = JsonSerializer.Serialize(new { items = matriculas, total = matriculas.Length, offset = 0, hasMore = false });
            return this;
        }

        public HttpClient CreateClient(string name) => new(new Cable(this)) { BaseAddress = new Uri("http://bff-educacion.local/") };

        private sealed class Cable(OrquestadorFalso o) : HttpMessageHandler
        {
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
            {
                o.Pedidos++;
                o.Rutas.Add(r.RequestUri!.PathAndQuery);
                if (r.Headers.TryGetValues("X-Synergos-Sujeto", out var s)) o.Sujetos.Add(s.Single());
                if (o.Demora > TimeSpan.Zero) await Task.Delay(o.Demora, ct);
                if (o.Caido) throw new HttpRequestException("caído");

                var ruta = r.RequestUri.AbsolutePath;
                if (ruta == "/v1/flujos/educacion.matricula") return Json(o._lista);
                var id = Uri.UnescapeDataString(ruta.Split('/').Last());
                return o._matriculas.TryGetValue(id, out var m)
                    ? Json(m)
                    : new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("""{"code":"educacion.saga_not_found"}""", Encoding.UTF8, "application/json") };
            }

            private static HttpResponseMessage Json(string cuerpo)
                => new(HttpStatusCode.OK) { Content = new StringContent(cuerpo, Encoding.UTF8, "application/json") };
        }
    }
}

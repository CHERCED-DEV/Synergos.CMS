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
using Synergos.CMS.Web.Services.Puerta;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// La devolución de una matrícula de la PUERTA por el personal (lote 8, 010-P2 B, T036): el flujo posterior
/// <c>educacion.devolucion</c> por el reenvío de la puerta, con el sujeto del administrador y la llave de la matrícula,
/// y al completarse la baja en el libro que lee el aula.
/// </summary>
public sealed class DevolucionesDeMatriculasTests
{
    private static readonly Guid Rosa = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
    private static readonly Guid Tomas = Guid.Parse("99999999-8888-7777-6666-555555555555");
    private const string Matricula = "mat-ana-1";
    private const string Curso = "dev-clean-architecture";
    private const string Correo = "ana@synergos.co";

    // ── La pieza ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task El_admin_devuelve_por_la_puerta_con_su_sujeto_y_la_llave_de_la_matricula_y_el_libro_la_da_de_baja()
    {
        var orquestador = new Orquestador();
        var libro = await LibroConLaMatricula();

        var r = await Pieza(orquestador, Admin(Rosa), libro).DevolverAsync(Matricula, CancellationToken.None);

        Assert.True(r.Bien, r.Mensaje);
        Assert.Equal((320_000m, "COP"), (r.Devuelto, r.Moneda));
        var (metodo, ruta, cabeceras, cuerpo) = Assert.Single(orquestador.Pedidos);
        Assert.Equal(("POST", "/v1/flujos/educacion.devolucion"), (metodo, ruta));
        Assert.Equal($"educacion.personal:{Rosa:n}", cabeceras["X-Synergos-Sujeto"]);
        Assert.Equal(DevolucionesDeMatriculas.Llave(Matricula), cabeceras["Idempotency-Key"]);
        using var json = JsonDocument.Parse(cuerpo);
        Assert.Equal(Matricula, json.RootElement.GetProperty("matricula").GetString());
        // Nadie teclea el monto: lo lee el flujo de la matrícula.
        Assert.False(json.RootElement.TryGetProperty("monto", out _));
        Assert.Empty(await libro.GetEnrollmentsAsync(Correo));
        Assert.Equal(1, (await libro.DevueltasAsync(Correo))[Curso]);
    }

    [Fact]
    public async Task Otra_persona_que_devuelve_la_misma_matricula_manda_la_misma_llave()
    {
        var orquestador = new Orquestador();
        var libro = await LibroConLaMatricula();

        await Pieza(orquestador, Admin(Rosa), libro).DevolverAsync(Matricula, CancellationToken.None);
        await Pieza(orquestador, Admin(Tomas), libro).DevolverAsync(Matricula, CancellationToken.None);

        Assert.Equal(orquestador.Pedidos[0].Cabeceras["Idempotency-Key"], orquestador.Pedidos[1].Cabeceras["Idempotency-Key"]);
        Assert.NotEqual(orquestador.Pedidos[0].Cabeceras["X-Synergos-Sujeto"], orquestador.Pedidos[1].Cabeceras["X-Synergos-Sujeto"]);
        Assert.NotEqual(DevolucionesDeMatriculas.Llave(Matricula), DevolucionesDeMatriculas.Llave("mat-otra"));
    }

    [Fact]
    public async Task Sin_el_rol_del_flujo_o_sin_sesion_no_sale_nada_y_la_matricula_sigue_activa()
    {
        var orquestador = new Orquestador();
        var libro = await LibroConLaMatricula();

        var instructor = await Pieza(orquestador, Miembro(Rosa, "instructor"), libro).DevolverAsync(Matricula, CancellationToken.None);
        var sinSesion = await Pieza(orquestador, Miembro(null), libro).DevolverAsync(Matricula, CancellationToken.None);

        Assert.Equal("puerta.personal_requerido", instructor.Codigo);
        Assert.Equal("puerta.sesion_requerida", sinSesion.Codigo);
        Assert.Empty(orquestador.Pedidos);
        Assert.Single(await libro.GetEnrollmentsAsync(Correo));
    }

    [Fact]
    public async Task Sin_la_devolucion_abierta_en_el_sitio_no_sale_nada()
    {
        var orquestador = new Orquestador();
        var libro = await LibroConLaMatricula();

        var sinDestino = await Pieza(orquestador, Admin(Rosa), libro, conDestino: false).DevolverAsync(Matricula, CancellationToken.None);
        var sinEntrada = await Pieza(orquestador, Admin(Rosa), libro, declarada: false).DevolverAsync(Matricula, CancellationToken.None);

        Assert.Equal("academy.devolucion_no_disponible", sinDestino.Codigo);
        Assert.Equal("academy.devolucion_no_disponible", sinEntrada.Codigo);
        Assert.Empty(orquestador.Pedidos);
    }

    [Fact]
    public async Task Un_rechazo_del_orquestador_pasa_con_su_codigo_y_la_matricula_sigue_activa()
    {
        var orquestador = new Orquestador
        {
            Responde = _ => Orquestador.Json(HttpStatusCode.Conflict,
                """{"code":"payments.refund_over_captured","detail":"Ya se devolvió.","transient":false}""", "application/problem+json"),
        };
        var libro = await LibroConLaMatricula();

        var r = await Pieza(orquestador, Admin(Rosa), libro).DevolverAsync(Matricula, CancellationToken.None);

        Assert.False(r.Bien);
        Assert.Equal(("payments.refund_over_captured", 0m), (r.Codigo, r.Devuelto));
        Assert.Single(await libro.GetEnrollmentsAsync(Correo));
    }

    [Fact]
    public async Task Una_devolucion_que_no_termino_no_afirma_nada_ni_da_de_baja()
    {
        var orquestador = new Orquestador
        {
            Responde = _ => Orquestador.Json(HttpStatusCode.Created,
                """{"flujo":"educacion.devolucion","id":"dev-1","numero":"DEV00001","estado":"Compensating","datos":{"matricula":"mat-ana-1"},"error":null}"""),
        };
        var libro = await LibroConLaMatricula();

        var r = await Pieza(orquestador, Admin(Rosa), libro).DevolverAsync(Matricula, CancellationToken.None);

        Assert.Equal(("academy.devolucion_en_curso", true), (r.Codigo, r.Transitorio));
        Assert.Single(await libro.GetEnrollmentsAsync(Correo));
    }

    // ── Lo que manda y lee la pieza existe en el contrato publicado ───────────────────────────

    /// <summary>
    /// La operación que abre la devolución está en el contrato del orquestador como la manda la pieza: marcada para la
    /// puerta con el acceso del personal, con la llave y el sujeto requeridos, el cuerpo con <c>matricula</c>, y la
    /// respuesta con el <c>estado</c> y el <c>datos.total.amount</c> que la pieza lee.
    /// </summary>
    [Fact]
    public void Lo_que_manda_y_lee_la_devolucion_existe_en_el_contrato_publicado()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(
            Raiz(), "Synergos.CMS.Web", "docs", "contracts", "openapi", "Synergos.Bff.Educacion.json")));
        var raiz = doc.RootElement;
        var op = raiz.GetProperty("paths").GetProperty("/v1/flujos/educacion.devolucion").GetProperty("post");

        var marca = op.GetProperty(TablaDeLaPuerta.MarcaDelFlujo);
        Assert.Equal((DevolucionesDeMatriculas.Flujo, DevolucionesDeMatriculas.Operacion, TablaDeLaPuerta.AccesoDelPersonal),
            (marca.GetProperty("flujo").GetString(), marca.GetProperty("operacion").GetString(), marca.GetProperty("acceso").GetString()));
        var cabeceras = op.GetProperty("parameters").EnumerateArray()
            .Where(p => p.GetProperty("in").GetString() == "header" && p.GetProperty("required").GetBoolean())
            .Select(p => p.GetProperty("name").GetString()).ToList();
        Assert.Contains(LoQuePoneLaPuerta.CabeceraDelSujeto, cabeceras);
        Assert.Contains(LoQuePoneLaPuerta.CabeceraDeLaLlave, cabeceras);

        var entrada = Esquema(raiz, op.GetProperty("requestBody").GetProperty("content").GetProperty("application/json").GetProperty("schema"));
        Assert.True(entrada.GetProperty("properties").TryGetProperty("matricula", out _));

        var respuesta = Esquema(raiz, op.GetProperty("responses").GetProperty("201").GetProperty("content").GetProperty("application/json").GetProperty("schema"));
        Assert.True(respuesta.GetProperty("properties").TryGetProperty("estado", out _));
        var datos = Esquema(raiz, respuesta.GetProperty("properties").GetProperty("datos")).GetProperty("properties");
        var total = Esquema(raiz, datos.GetProperty("total")).GetProperty("properties");
        Assert.True(total.TryGetProperty("amount", out _) && total.TryGetProperty("currency", out _));
    }

    private static JsonElement Esquema(JsonElement raiz, JsonElement esquema)
    {
        if (esquema.TryGetProperty("oneOf", out var ramas))
        {
            esquema = ramas.EnumerateArray().First(r => !(r.TryGetProperty("type", out var t) && t.GetString() == "null"));
        }
        while (esquema.TryGetProperty("$ref", out var referencia))
        {
            esquema = raiz.GetProperty("components").GetProperty("schemas").GetProperty(referencia.GetString()!.Split('/').Last());
        }
        return esquema;
    }

    private static string Raiz()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Synergos.CMS.sln"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    // ── Los dobles ───────────────────────────────────────────────────────────────────────────

    /// <summary>El libro con una matrícula de la puerta de Ana, cobrada y activa.</summary>
    private static async Task<StubEnrollmentService> LibroConLaMatricula()
    {
        var libro = new StubEnrollmentService(new StubCourseCatalogProvider(new StubContentStream(new StubSocialGraphService(),
            new StubReactionService(), () => new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc))), new StubPaymentProvider());
        await libro.AnotarAsync(new MatriculaDeLaPuerta(Matricula, "TECTURE1", Curso, new Student("Ana", Correo), 320_000m, "COP"));
        return libro;
    }

    private static IMemberAccessGate Admin(Guid quien) => Miembro(quien, "admin");

    private static IMemberAccessGate Miembro(Guid? quien, string? rol = null)
    {
        var gate = Substitute.For<IMemberAccessGate>();
        gate.IsAuthenticated.Returns(quien is not null);
        gate.CurrentMemberKey.Returns(quien);
        gate.HasAnyRole(Arg.Any<string?>()).Returns(c => rol is not null && (c.Arg<string?>() ?? "").Split(',').Contains(rol));
        return gate;
    }

    private static DevolucionesDeMatriculas Pieza(
        Orquestador orquestador, IMemberAccessGate miembro, IMatriculasDeLaPuerta libro, bool conDestino = true, bool declarada = true)
    {
        var tabla = TablaDeLaPuerta.Incrustada();
        var puerta = new PuertaSettings();
        if (declarada)
        {
            puerta.Flujos[DevolucionesDeMatriculas.Flujo] = new FlujoDeLaPuertaSettings
            {
                Acceso = AccesoDeLaPuerta.Personal, Roles = ["admin"], SujetoKind = "educacion.personal",
            };
        }
        var monitor = Substitute.For<IOptionsMonitor<PuertaSettings>>();
        monitor.CurrentValue.Returns(puerta);
        var destinos = new DestinosDeLaPuerta(conDestino ? ["Educacion"] : []);
        return new DevolucionesDeMatriculas(
            tabla, new PuertaDeLosFlujos(tabla, monitor, destinos),
            new ReenvioDeLaPuerta(new Fabrica(orquestador), NullLogger<ReenvioDeLaPuerta>.Instance), miembro, monitor, libro,
            NullLogger<DevolucionesDeMatriculas>.Instance);
    }

    /// <summary>El orquestador de Educación: por defecto, la devolución completada por el total de la matrícula.</summary>
    private sealed class Orquestador : HttpMessageHandler
    {
        public List<(string Metodo, string Ruta, Dictionary<string, string> Cabeceras, byte[] Cuerpo)> Pedidos { get; } = [];

        public Func<HttpRequestMessage, HttpResponseMessage>? Responde { get; init; }

        public static HttpResponseMessage Json(HttpStatusCode estado, string json, string tipo = "application/json")
            => new(estado) { Content = new StringContent(json, Encoding.UTF8, tipo) };

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            var cuerpo = r.Content is null ? [] : await r.Content.ReadAsByteArrayAsync(ct);
            Pedidos.Add((r.Method.Method, r.RequestUri!.AbsolutePath,
                r.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value), StringComparer.OrdinalIgnoreCase), cuerpo));
            if (Responde is not null) return Responde(r);

            using var pedido = JsonDocument.Parse(cuerpo);
            var matricula = pedido.RootElement.GetProperty("matricula").GetString();
            return Json(HttpStatusCode.Created,
                $$$"""{"flujo":"educacion.devolucion","id":"dev-1","numero":"DEV00001","estado":"Completed","datos":{"matricula":"{{{matricula}}}","total":{"amount":320000,"currency":"COP"}},"error":null}""");
        }
    }

    private sealed class Fabrica(HttpMessageHandler cable) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            Assert.Equal(ReenvioDeLaPuerta.Cliente("Educacion"), name);
            return new HttpClient(cable, disposeHandler: false) { BaseAddress = new Uri("http://bff-educacion.local/") };
        }
    }
}

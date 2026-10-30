using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Controllers;
using Synergos.CMS.Web.Services.Puerta;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// Los actos del trámite en la puerta del CMS (009-gobierno-tramite T037 y T038): tomar y decidir con acceso de personal
/// —los roles de la consola del funcionario— y subsanar como miembro, con el mismo sujeto con que se radicó.
/// </summary>
/// <remarks>
/// La puerta es la guarda del rol (009-P6 = A): el orquestador no lo comprueba y la capacidad recibe el rol que declara el
/// paso. Sin el rol la petición no sale del CMS, tampoco la del ciudadano dueño del expediente (US4-2 y US4-4). Se prueba
/// con las entradas del <c>appsettings.json</c> que reciben todos los entornos y la tabla de los contratos incrustados.
/// </remarks>
public sealed class PuertaDeLosActosDeGobiernoTests : IDisposable
{
    private static readonly Guid Funcionaria = Guid.Parse("5b7c9d1e-2f3a-4b5c-8d6e-7f8091a2b3c4");
    private static readonly Guid Ciudadano = Guid.Parse("0a1b2c3d-4e5f-4061-8273-94a5b6c7d8e9");
    private static readonly string[] DelPersonal = ["gobierno.toma", "gobierno.decision"];

    private readonly Orquestador _orquestador = new();

    public void Dispose() => _orquestador.Dispose();

    [Fact]
    public void Las_entradas_existen_y_la_puerta_las_valida_contra_el_contrato_incrustado()
    {
        var ajustes = Ajustes();
        string[] actos = [.. DelPersonal, "gobierno.subsanacion"];
        foreach (var flujo in actos) Assert.True(ajustes.Flujos.ContainsKey(flujo), $"{flujo} no está en Synergos:Puerta:Flujos.");

        // Sólo estas entradas: las demás las vigila su propia línea. Es el validador con que el CMS no arranca.
        var solas = new PuertaSettings { Flujos = actos.ToDictionary(f => f, f => ajustes.Flujos[f], StringComparer.Ordinal) };
        var resultado = new ValidadorDeLaPuerta(TablaDeLaPuerta.Incrustada()).Validate(null, solas);
        Assert.True(resultado.Succeeded, resultado.FailureMessage);

        Assert.All(DelPersonal, flujo =>
        {
            Assert.Equal(AccesoDeLaPuerta.Personal, ajustes.Flujos[flujo].Acceso);
            Assert.Equal("gobierno.funcionario", ajustes.Flujos[flujo].SujetoKind);
        });
        // Subsanar sólo encuentra la radicación del mismo dueño: el sujeto tiene que ser el mismo con que se radicó.
        Assert.Equal(AccesoDeLaPuerta.Miembro, ajustes.Flujos["gobierno.subsanacion"].Acceso);
        Assert.Equal(ajustes.Flujos["gobierno.radicacion"].SujetoKind, ajustes.Flujos["gobierno.subsanacion"].SujetoKind);
    }

    /// <summary>
    /// Quien entra a la consola del funcionario por la ruta directa es quien toma y decide por la puerta: los mismos roles,
    /// leídos de la regla de la consola. Un rol que sólo una de las dos admitiera abriría un camino a quien la otra niega.
    /// </summary>
    [Fact]
    public void Los_roles_de_la_puerta_son_los_de_la_consola_del_funcionario()
    {
        var consola = (string)typeof(GovController).GetField("OfficerRolesCsv", BindingFlags.NonPublic | BindingFlags.Static)!
            .GetRawConstantValue()!;

        Assert.All(DelPersonal, flujo => Assert.Equal(consola.Split(','), Ajustes().Flujos[flujo].Roles));
    }

    /// <summary>
    /// Con el rol, la toma sale con la persona del funcionario —la que nombrará la historia del expediente— y una llave
    /// reemitida por la puerta: el doble clic de esa persona es la misma llave, y la misma del navegador en otra persona
    /// es otra (no cae en el acto de otro).
    /// </summary>
    [Fact]
    public async Task Con_el_rol_la_toma_sale_al_orquestador_con_la_persona_del_funcionario_y_una_llave_suya()
    {
        const string Cuerpo = """{"radicacion":"rad-1","nota":"Lo reviso yo."}""";
        var otra = Guid.Parse("7d8e9f00-1a2b-4c3d-9e4f-5a6b7c8d9e0f");

        var r = await Pedir("gobierno.toma", Funcionario(Funcionaria), Cuerpo, "toma-k1");
        await Pedir("gobierno.toma", Funcionario(Funcionaria), Cuerpo, "toma-k1");
        await Pedir("gobierno.toma", Funcionario(otra), Cuerpo, "toma-k1");

        Assert.Equal(201, r.Estado);
        var (metodo, ruta, cabeceras, cuerpo) = _orquestador.Pedidos[0];
        Assert.Equal(("POST", "/v1/flujos/gobierno.toma"), (metodo, ruta));
        Assert.Equal($"gobierno.funcionario:{Funcionaria:n}", cabeceras["X-Synergos-Sujeto"]);
        using var json = JsonDocument.Parse(cuerpo);
        Assert.Equal("rad-1", json.RootElement.GetProperty("radicacion").GetString());
        var llaves = _orquestador.Pedidos.Select(p => p.Cabeceras["Idempotency-Key"]).ToList();
        Assert.StartsWith(LoQuePoneLaPuerta.PrefijoDeLaLlave, llaves[0], StringComparison.Ordinal);
        Assert.Equal(llaves[0], llaves[1]);
        Assert.NotEqual(llaves[0], llaves[2]);
    }

    [Fact]
    public async Task Sin_el_rol_nadie_toma_ni_decide_tampoco_el_ciudadano_dueno_y_nada_sale_del_cms()
    {
        foreach (var flujo in DelPersonal)
        {
            var sinRol = await Pedir(flujo, Miembro(Ciudadano), """{"radicacion":"rad-1","decision":"approve","nota":"x"}""", "k-" + flujo);
            Assert.Equal((403, "puerta.personal_requerido"), (sinRol.Estado, sinRol.Codigo));

            var sinSesion = await Pedir(flujo, Miembro(null), """{"radicacion":"rad-1"}""", "k2-" + flujo);
            Assert.Equal(401, sinSesion.Estado);
        }

        Assert.Empty(_orquestador.Pedidos);
    }

    [Fact]
    public async Task La_subsanacion_la_pide_el_miembro_con_el_sujeto_del_ciudadano()
    {
        var r = await Pedir("gobierno.subsanacion", Miembro(Ciudadano), """{"radicacion":"rad-1"}""", "subsanacion-k1");

        Assert.Equal(201, r.Estado);
        var pedido = Assert.Single(_orquestador.Pedidos);
        Assert.Equal(("POST", "/v1/flujos/gobierno.subsanacion"), (pedido.Metodo, pedido.Ruta));
        Assert.Equal($"gobierno.ciudadano:{Ciudadano:n}", pedido.Cabeceras["X-Synergos-Sujeto"]);
    }

    // ── La puerta, como la arma el CMS ───────────────────────────────────────────────────────

    private sealed record Respuesta(int Estado, string Cuerpo)
    {
        public string? Codigo => JsonDocument.Parse(Cuerpo).RootElement.GetProperty("code").GetString();
    }

    private async Task<Respuesta> Pedir(string flujo, IMemberAccessGate miembro, string cuerpo, string llave)
    {
        var http = new DefaultHttpContext();
        http.Request.Method = "POST";
        http.Request.Scheme = "https";
        http.Request.Host = new HostString("sitio.test");
        var bytes = Encoding.UTF8.GetBytes(cuerpo);
        http.Request.Body = new MemoryStream(bytes);
        http.Request.ContentLength = bytes.Length;
        http.Request.ContentType = "application/json";
        http.Request.Headers["Sec-Fetch-Site"] = "same-origin";
        http.Request.Headers["Idempotency-Key"] = llave;
        http.Response.Body = new MemoryStream();

        var monitor = Substitute.For<IOptionsMonitor<PuertaSettings>>();
        monitor.CurrentValue.Returns(Ajustes());
        var sut = new FlujosController(
            TablaDeLaPuerta.Incrustada(), monitor, new DestinosDeLaPuerta(["Gobierno"]), miembro,
            new ReenvioDeLaPuerta(new Fabrica(_orquestador), NullLogger<ReenvioDeLaPuerta>.Instance))
        {
            ControllerContext = new ControllerContext { HttpContext = http },
        };

        var resultado = await sut.Pasar(flujo, "abrir", default);
        if (resultado is ContentResult c) return new Respuesta(c.StatusCode!.Value, c.Content!);

        Assert.IsType<EmptyResult>(resultado);
        http.Response.Body.Position = 0;
        return new Respuesta(http.Response.StatusCode, await new StreamReader(http.Response.Body).ReadToEndAsync());
    }

    private static IMemberAccessGate Funcionario(Guid quien)
    {
        var gate = Miembro(quien);
        gate.HasAnyRole(Arg.Is<string?>(r => r != null && r.Split(',', StringSplitOptions.None).Contains("funcionario"))).Returns(true);
        return gate;
    }

    private static IMemberAccessGate Miembro(Guid? quien)
    {
        var gate = Substitute.For<IMemberAccessGate>();
        gate.IsAuthenticated.Returns(quien is not null);
        gate.CurrentMemberKey.Returns(quien);
        return gate;
    }

    /// <summary><c>Synergos:Puerta</c> del <c>appsettings.json</c> base, enlazada como la enlaza el CMS.</summary>
    private static PuertaSettings Ajustes()
        => new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(RepoRoot(), "Synergos.CMS.Web", "appsettings.json"), optional: false)
            .Build()
            .GetSection("Synergos:Puerta")
            .Get<PuertaSettings>()!;

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Synergos.CMS.sln"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    /// <summary>El orquestador de Gobierno: contesta el acto completado.</summary>
    private sealed class Orquestador : HttpMessageHandler
    {
        public List<(string Metodo, string Ruta, Dictionary<string, string> Cabeceras, byte[] Cuerpo)> Pedidos { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            Pedidos.Add((r.Method.Method, r.RequestUri!.AbsolutePath,
                r.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value), StringComparer.OrdinalIgnoreCase),
                r.Content is null ? [] : await r.Content.ReadAsByteArrayAsync(ct)));
            return new HttpResponseMessage(HttpStatusCode.Created)
            {
                Content = new StringContent("""{"id":"acto-1","estado":"Completed","datos":{"estado":"in-review"}}""", Encoding.UTF8, "application/json"),
            };
        }
    }

    private sealed class Fabrica(HttpMessageHandler cable) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            Assert.Equal(ReenvioDeLaPuerta.Cliente("Gobierno"), name);
            return new HttpClient(cable, disposeHandler: false) { BaseAddress = new Uri("http://bff-gobierno.local/") };
        }
    }
}

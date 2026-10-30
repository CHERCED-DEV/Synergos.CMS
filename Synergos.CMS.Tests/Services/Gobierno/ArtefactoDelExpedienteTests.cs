using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Services.Gobierno;

namespace Synergos.CMS.Tests.Services.Gobierno;

/// <summary>
/// El artefacto del expediente de una radicación por la puerta (009-gobierno-tramite T027): guarda las respuestas sólo
/// sobre una radicación en curso del miembro —lo comprueba el orquestador con su sujeto—, acuña el radicado una vez, y
/// al reconciliar confirma la completada y borra la deshecha.
/// </summary>
public sealed class ArtefactoDelExpedienteTests
{
    private static readonly Guid Ana = Guid.Parse("4b8f2c1e-7d3a-4e9b-a1f6-0c2d3e4f5a6b");
    private static readonly Guid Beto = Guid.Parse("9e1d2c3b-4a5f-4e6d-8c7b-1a2b3c4d5e6f");
    private const string Tramite = "trm-certificado-residencia";

    private static GovCitizen Ciudadano(Guid miembro) => new("Ana Ciudadana", "ana@ejemplo.co", "1020304050", null, miembro);

    private static Dictionary<string, string> Respuestas(string direccion = "Calle 10 # 20-30")
        => new(StringComparer.Ordinal)
        {
            ["nombreCompleto"] = "Ana Ciudadana",
            ["cedula"] = "1020304050",
            ["correo"] = "ana@ejemplo.co",
            ["direccion"] = direccion,
        };

    internal static (ArtefactoDelExpediente Artefacto, StubApplicationService Casos) Nuevo(OrquestadorDeGobiernoFalso orquestador, bool hayDestino = true)
    {
        var casos = new StubApplicationService(new StubTramiteCatalogProvider(), new StubGovFeeCalculator(), new StubPaymentProvider());
        var puerta = new PuertaSettings();
        puerta.Flujos["gobierno.radicacion"] = new FlujoDeLaPuertaSettings { Acceso = AccesoDeLaPuerta.Miembro, SujetoKind = "gobierno.ciudadano" };
        var monitor = Substitute.For<IOptionsMonitor<PuertaSettings>>();
        monitor.CurrentValue.Returns(puerta);
        return (new ArtefactoDelExpediente(orquestador, casos, monitor, NullLogger<ArtefactoDelExpediente>.Instance, hayDestino), casos);
    }

    [Fact]
    public async Task Una_radicacion_suya_y_abierta_guarda_las_respuestas_con_un_radicado_que_nadie_ve_hasta_completarse()
    {
        var orquestador = new OrquestadorDeGobiernoFalso().Con("rad-1", Ana, "Running");
        var (artefacto, casos) = Nuevo(orquestador);

        var r = await artefacto.GuardarRespuestasAsync("rad-1", Ciudadano(Ana), Respuestas(), default);

        Assert.True(r.Bien, r.Mensaje);
        Assert.Matches(@"^SG-\d{4}-\d{6}$", r.Expediente!.Radicado);
        Assert.False(r.Expediente.Confirmado);
        // Leyó con el sujeto del miembro de la sesión, que es el mismo que pone la puerta.
        Assert.Equal($"gobierno.ciudadano:{Ana:n}", orquestador.Sujetos.Single());
        Assert.Null(casos.FindCase(r.Expediente.Radicado));
        Assert.DoesNotContain(casos.ListCases(), c => c.Radicado == r.Expediente.Radicado);
    }

    [Fact]
    public async Task Repetir_mientras_sigue_abierta_reemplaza_las_respuestas_y_conserva_el_radicado()
    {
        var orquestador = new OrquestadorDeGobiernoFalso().Con("rad-1", Ana, "Running", acepta: true);
        var (artefacto, casos) = Nuevo(orquestador);
        var primera = await artefacto.GuardarRespuestasAsync("rad-1", Ciudadano(Ana), Respuestas(), default);

        var segunda = await artefacto.GuardarRespuestasAsync("rad-1", Ciudadano(Ana), Respuestas("Carrera 7 # 8-9"), default);
        await casos.ConfirmarAsync("rad-1");

        Assert.True(segunda.Bien, segunda.Mensaje);
        Assert.Equal(primera.Expediente!.Radicado, segunda.Expediente!.Radicado);
        var expediente = casos.FindCase(segunda.Expediente.Radicado)!;
        Assert.Equal("Carrera 7 # 8-9", expediente.FormData["direccion"]);
        Assert.Equal(Ana, expediente.Citizen.MemberKey);
        // Lo que cotizó y aceptó, de la saga; no del navegador.
        Assert.Equal(65_500m, expediente.FeeMinor);
        Assert.True(expediente.NotificationConsent!.Accepted);
        Assert.Equal("2026-10-v1", expediente.NotificationConsent.TextVersion);
        Assert.Single(casos.ListCases(), c => c.Radicado == segunda.Expediente.Radicado);
    }

    [Fact]
    public async Task La_radicacion_de_otro_ciudadano_no_existe_y_no_se_guarda_nada()
    {
        var orquestador = new OrquestadorDeGobiernoFalso().Con("rad-beto", Beto, "Running");
        var (artefacto, casos) = Nuevo(orquestador);

        var r = await artefacto.GuardarRespuestasAsync("rad-beto", Ciudadano(Ana), Respuestas(), default);

        Assert.Equal(StatusCodes.Status404NotFound, r.Estado);
        Assert.Equal("gobierno.saga_not_found", r.Codigo);
        Assert.Null(await casos.DeLaRadicacionAsync("rad-beto"));
    }

    [Theory]
    [InlineData("Completed")]
    [InlineData("Compensated")]
    public async Task Una_radicacion_cerrada_no_recibe_respuestas(string estado)
    {
        var orquestador = new OrquestadorDeGobiernoFalso().Con("rad-1", Ana, estado);
        var (artefacto, casos) = Nuevo(orquestador);

        var r = await artefacto.GuardarRespuestasAsync("rad-1", Ciudadano(Ana), Respuestas(), default);

        Assert.Equal(StatusCodes.Status409Conflict, r.Estado);
        Assert.Equal("gov.radicacion_cerrada", r.Codigo);
        Assert.Equal(estado, r.EstadoDeLaRadicacion);
        Assert.Null(await casos.DeLaRadicacionAsync("rad-1"));
    }

    [Fact]
    public async Task Al_reconciliar_la_deshecha_borra_sus_respuestas_y_la_completada_aparece()
    {
        var orquestador = new OrquestadorDeGobiernoFalso().Con("rad-1", Ana, "Running").Con("rad-2", Ana, "Running");
        var (artefacto, casos) = Nuevo(orquestador);
        var deshecha = await artefacto.GuardarRespuestasAsync("rad-1", Ciudadano(Ana), Respuestas(), default);
        var completada = await artefacto.GuardarRespuestasAsync("rad-2", Ciudadano(Ana), Respuestas(), default);
        orquestador.Con("rad-1", Ana, "Compensated").Con("rad-2", Ana, "Completed");

        await artefacto.ReconciliarAsync(e => e.Dueno == Ana, default);

        Assert.Null(await casos.DeLaRadicacionAsync("rad-1"));
        Assert.DoesNotContain(casos.ListCases(), c => c.Radicado == deshecha.Expediente!.Radicado);
        Assert.True((await casos.DeLaRadicacionAsync("rad-2"))!.Confirmado);
        Assert.Contains(casos.ListCases(), c => c.Radicado == completada.Expediente!.Radicado);
    }

    [Fact]
    public async Task Una_pregunta_obligatoria_vacia_se_nombra_y_no_se_guarda_nada()
    {
        var orquestador = new OrquestadorDeGobiernoFalso().Con("rad-1", Ana, "Running");
        var (artefacto, casos) = Nuevo(orquestador);

        var r = await artefacto.GuardarRespuestasAsync("rad-1", Ciudadano(Ana), Respuestas(direccion: " "), default);

        Assert.Equal(StatusCodes.Status400BadRequest, r.Estado);
        Assert.Equal("gov.respuestas_incompletas", r.Codigo);
        Assert.Contains("Dirección de residencia", r.Mensaje, StringComparison.Ordinal);
        Assert.Null(await casos.DeLaRadicacionAsync("rad-1"));
    }

    [Fact]
    public async Task Sin_destino_del_orquestador_no_sale_a_la_red()
    {
        var orquestador = new OrquestadorDeGobiernoFalso().Con("rad-1", Ana, "Running");
        var (artefacto, _) = Nuevo(orquestador, hayDestino: false);

        var r = await artefacto.GuardarRespuestasAsync("rad-1", Ciudadano(Ana), Respuestas(), default);
        await artefacto.ReconciliarAsync(_ => true, default);

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, r.Estado);
        Assert.Equal("gov.radicacion_no_disponible", r.Codigo);
        Assert.Equal(0, orquestador.Pedidos);
    }

    /// <summary>
    /// El orquestador de Gobierno, como lo contesta la puerta: cada radicación es de su dueño —a otro sujeto, 404—, y
    /// sin sujeto la petición no se puede leer (400).
    /// </summary>
    internal sealed class OrquestadorDeGobiernoFalso : IHttpClientFactory
    {
        private readonly Dictionary<string, (Guid Dueno, string Json)> _radicaciones = new(StringComparer.Ordinal);

        public int Pedidos { get; private set; }

        public List<string> Sujetos { get; } = [];

        public List<string> Rutas { get; } = [];

        public OrquestadorDeGobiernoFalso Con(string id, Guid dueno, string estado, bool? acepta = true)
        {
            _radicaciones[id] = (dueno, JsonSerializer.Serialize(new
            {
                id,
                flujo = "gobierno.radicacion",
                numero = id.ToUpperInvariant(),
                estado,
                datos = new
                {
                    tramite = new { kind = "gobierno.tramite", id = Tramite },
                    total = new { amount = 65_500m, currency = "COP" },
                    notificacionElectronica = acepta,
                    versionDelTexto = "2026-10-v1",
                },
                compensacionesPendientes = 0,
                error = (string?)null,
                deshaciendo = false,
            }));
            return this;
        }

        public HttpClient CreateClient(string name) => new(new Cable(this)) { BaseAddress = new Uri("http://bff-gobierno.local/") };

        private sealed class Cable(OrquestadorDeGobiernoFalso o) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
            {
                o.Pedidos++;
                o.Rutas.Add($"{r.Method.Method} {r.RequestUri!.AbsolutePath}");
                if (!r.Headers.TryGetValues("X-Synergos-Sujeto", out var s))
                {
                    return Task.FromResult(Problema(HttpStatusCode.BadRequest, "gobierno.sujeto_requerido"));
                }
                var sujeto = s.Single();
                o.Sujetos.Add(sujeto);
                var id = Uri.UnescapeDataString(r.RequestUri!.AbsolutePath.Split('/').Last());
                return Task.FromResult(o._radicaciones.TryGetValue(id, out var rad) && sujeto == $"gobierno.ciudadano:{rad.Dueno:n}"
                    ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(rad.Json, Encoding.UTF8, "application/json") }
                    : Problema(HttpStatusCode.NotFound, "gobierno.saga_not_found"));
            }

            private static HttpResponseMessage Problema(HttpStatusCode estado, string codigo)
                => new(estado) { Content = new StringContent($$"""{"code":"{{codigo}}"}""", Encoding.UTF8, "application/problem+json") };
        }
    }
}

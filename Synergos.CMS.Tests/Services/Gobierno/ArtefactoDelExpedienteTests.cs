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
    internal static readonly Guid Ana = Guid.Parse("4b8f2c1e-7d3a-4e9b-a1f6-0c2d3e4f5a6b");
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
        var alDia = await artefacto.AlDiaAsync("rad-1", default);

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, r.Estado);
        Assert.Equal("gov.radicacion_no_disponible", r.Codigo);
        Assert.False(alDia);
        Assert.Equal(0, orquestador.Pedidos);
    }

    // ── Al día con el proceso (T040) ────────────────────────────────────────────────────────

    /// <summary>Guarda, completa y confirma una radicación de Ana: el expediente de la puerta ya existe.</summary>
    internal static async Task<string> Confirmado(ArtefactoDelExpediente artefacto, StubApplicationService casos, OrquestadorDeGobiernoFalso orquestador, string radicacion = "rad-1")
    {
        orquestador.Con(radicacion, Ana, "Running");
        var r = await artefacto.GuardarRespuestasAsync(radicacion, Ciudadano(Ana), Respuestas(), default);
        orquestador.Con(radicacion, Ana, "Completed");
        await casos.ConfirmarAsync(radicacion);
        return r.Expediente!.CaseId;
    }

    /// <summary>
    /// El proceso manda (FR-028): al ponerse al día, el estado, la historia y la resolución del expediente son los del
    /// proceso, aunque la proyección dijera otra cosa; la lectura es la de la cara del funcionario (sin sujeto).
    /// </summary>
    [Fact]
    public async Task Al_ponerse_al_dia_el_estado_la_historia_y_la_resolucion_son_los_del_proceso()
    {
        var orquestador = new OrquestadorDeGobiernoFalso();
        var (artefacto, casos) = Nuevo(orquestador);
        var caso = await Confirmado(artefacto, casos, orquestador);
        orquestador.ConProceso("rad-1", "approved",
            ("take", "in-review", "gobierno.funcionario:f-1", "Lo reviso yo."), ("approve", "approved", "gobierno.funcionario:f-2", "Cumple."));

        var alDia = await artefacto.AlDiaAsync("rad-1", default);

        Assert.True(alDia);
        var expediente = casos.FindCase(caso)!;
        Assert.Equal(CaseStatus.Resuelto, expediente.Status);
        Assert.Equal(("approve", "Cumple.", "gobierno.funcionario:f-2"),
            (expediente.Decision!.Outcome, expediente.Decision.Note, expediente.Decision.DecidedBy));
        Assert.Contains("GET /v1/expedientes/rad-1", orquestador.Rutas);
        Assert.Equal(new string?[] { null }, orquestador.CarasDelExpediente);
    }

    /// <summary>Sin respuesta del proceso queda lo último que se sabe: nada se borra ni se inventa.</summary>
    [Fact]
    public async Task Sin_respuesta_del_proceso_queda_lo_ultimo_que_se_sabe()
    {
        var orquestador = new OrquestadorDeGobiernoFalso();
        var (artefacto, casos) = Nuevo(orquestador);
        var caso = await Confirmado(artefacto, casos, orquestador);
        orquestador.ConProceso("rad-1", "in-review", ("take", "in-review", "gobierno.funcionario:f-1", null));
        await artefacto.AlDiaAsync("rad-1", default);
        orquestador.ProcesoCaido = true;

        var alDia = await artefacto.AlDiaAsync("rad-1", default);

        Assert.False(alDia);
        Assert.Equal(CaseStatus.EnRevision, casos.FindCase(caso)!.Status);
    }

    /// <summary>
    /// Una reconciliación pone al día lo confirmado que le toca y nada más: la bandeja de Ana no lee el expediente de
    /// Beto. La lectura no lleva sujeto: quién mira lo decide quien elige lo que toca.
    /// </summary>
    [Fact]
    public async Task La_reconciliacion_pone_al_dia_lo_confirmado_que_le_toca()
    {
        var orquestador = new OrquestadorDeGobiernoFalso();
        var (artefacto, casos) = Nuevo(orquestador);
        var deAna = await Confirmado(artefacto, casos, orquestador, "rad-ana");
        orquestador.Con("rad-beto", Beto, "Running");
        var guardada = await artefacto.GuardarRespuestasAsync("rad-beto", Ciudadano(Beto), Respuestas(), default);
        orquestador.Con("rad-beto", Beto, "Completed");
        await casos.ConfirmarAsync("rad-beto");
        orquestador.ConProceso("rad-ana", "in-review", ("take", "in-review", "gobierno.funcionario:f-1", null));
        orquestador.ConProceso("rad-beto", "in-review", ("take", "in-review", "gobierno.funcionario:f-1", null));

        await artefacto.ReconciliarAsync(e => e.Dueno == Ana, default);

        Assert.Equal(CaseStatus.EnRevision, casos.FindCase(deAna)!.Status);
        Assert.Equal(CaseStatus.Radicado, casos.FindCase(guardada.Expediente!.CaseId)!.Status);
        Assert.Equal(new string?[] { null }, orquestador.CarasDelExpediente);
        Assert.Contains("GET /v1/expedientes/rad-ana", orquestador.Rutas);
        Assert.DoesNotContain("GET /v1/expedientes/rad-beto", orquestador.Rutas);
    }

    /// <summary>
    /// El orquestador de Gobierno, como lo contesta la puerta: cada radicación es de su dueño —a otro sujeto, 404—, y
    /// sin sujeto la petición no se puede leer (400). La lectura del expediente (T040) admite además la cara del
    /// funcionario, sin sujeto.
    /// </summary>
    internal sealed class OrquestadorDeGobiernoFalso : IHttpClientFactory
    {
        private readonly Dictionary<string, (Guid Dueno, string Json)> _radicaciones = new(StringComparer.Ordinal);
        private readonly Dictionary<string, (string Estado, List<object> Historia)> _procesos = new(StringComparer.Ordinal);

        /// <summary>Los actos que llegaron por la puerta (T039): el flujo, el sujeto, la llave y el cuerpo.</summary>
        public List<(string Flujo, string? Sujeto, string? Llave, string Cuerpo)> Actos { get; } = [];

        /// <summary>El código con que la capacidad rechaza el próximo acto (409); nulo, lo hace.</summary>
        public string? ActoRechaza { get; set; }

        public int Pedidos { get; private set; }

        public List<string> Sujetos { get; } = [];

        public List<string> Rutas { get; } = [];

        /// <summary>El sujeto de cada lectura del expediente; nulo, la cara del funcionario.</summary>
        public List<string?> CarasDelExpediente { get; } = [];

        /// <summary>La capacidad de procesos detrás del orquestador no contesta: la lectura del expediente da 503.</summary>
        public bool ProcesoCaido { get; set; }

        /// <summary>El expediente de una radicación en su proceso: el estado y la historia, con el seudónimo de quien actuó.</summary>
        public OrquestadorDeGobiernoFalso ConProceso(string id, string estado, params (string Transicion, string Hacia, string Quien, string? Nota)[] historia)
        {
            _procesos[id] = ("submitted", []);
            foreach (var paso in historia) Disparar(id, paso.Transicion, paso.Hacia, paso.Quien, paso.Nota);
            _procesos[id] = (estado, _procesos[id].Historia);
            return this;
        }

        private void Disparar(string id, string transicion, string hacia, string quien, string? nota)
        {
            var (desde, historia) = _procesos[id];
            historia.Add(new
            {
                transicion, desde, hacia, porKind = quien.Split(':')[0], porId = quien.Split(':')[1], nota,
                en = DateTimeOffset.UtcNow.AddMinutes(historia.Count),
            });
            _procesos[id] = (hacia, historia);
        }

        private string Proceso(string id) => JsonSerializer.Serialize(new
        {
            radicacion = id, expediente = "inst-" + id, proceso = "gov.tramite.v2", estado = _procesos[id].Estado, historia = _procesos[id].Historia,
        });

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
                if (r.RequestUri.AbsolutePath.StartsWith("/v1/expedientes/", StringComparison.Ordinal)) return Task.FromResult(Expediente(r));
                if (r.Method == HttpMethod.Post && r.RequestUri.AbsolutePath.StartsWith("/v1/flujos/gobierno.", StringComparison.Ordinal))
                {
                    return Task.FromResult(Acto(r, r.RequestUri.AbsolutePath["/v1/flujos/".Length..]));
                }
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

            /// <summary>La lectura del expediente: con sujeto, sólo el del dueño; sin él, la cara del funcionario.</summary>
            private HttpResponseMessage Expediente(HttpRequestMessage r)
            {
                var cara = r.Headers.TryGetValues("X-Synergos-Sujeto", out var s) ? s.Single() : null;
                o.CarasDelExpediente.Add(cara);
                if (o.ProcesoCaido) return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);

                var id = Uri.UnescapeDataString(r.RequestUri!.AbsolutePath.Split('/').Last());
                if (!o._radicaciones.TryGetValue(id, out var rad) || (cara is not null && cara != $"gobierno.ciudadano:{rad.Dueno:n}"))
                {
                    return Problema(HttpStatusCode.NotFound, "gobierno.expediente_no_encontrado");
                }
                return o._procesos.ContainsKey(id)
                    ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(o.Proceso(id), Encoding.UTF8, "application/json") }
                    : Problema(HttpStatusCode.Conflict, "gobierno.expediente_sin_proceso");
            }

            /// <summary>Un acto por la puerta: lo anota, y lo hace en el proceso —o lo rechaza con el código guionado—.</summary>
            private HttpResponseMessage Acto(HttpRequestMessage r, string flujo)
            {
                var cuerpo = r.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                var sujeto = r.Headers.TryGetValues("X-Synergos-Sujeto", out var s) ? s.Single() : null;
                o.Actos.Add((flujo, sujeto, r.Headers.TryGetValues("Idempotency-Key", out var k) ? k.Single() : null, cuerpo));
                if (o.ActoRechaza is { } codigo)
                {
                    o.ActoRechaza = null;
                    return Problema(HttpStatusCode.Conflict, codigo);
                }

                using var pedido = JsonDocument.Parse(cuerpo);
                var radicacion = pedido.RootElement.GetProperty("radicacion").GetString()!;
                var (transicion, hacia) = flujo switch
                {
                    "gobierno.toma" => ("take", "in-review"),
                    "gobierno.subsanacion" => ("resubmit", "in-review"),
                    _ => pedido.RootElement.GetProperty("decision").GetString() switch
                    {
                        "approve" => ("approve", "approved"),
                        "reject" => ("reject", "rejected"),
                        _ => ("request-info", "info-requested"),
                    },
                };
                if (!o._procesos.ContainsKey(radicacion)) o._procesos[radicacion] = ("submitted", []);
                o.Disparar(radicacion, transicion, hacia, sujeto!,
                    pedido.RootElement.TryGetProperty("nota", out var nota) && nota.ValueKind == JsonValueKind.String ? nota.GetString() : null);
                return new HttpResponseMessage(HttpStatusCode.Created)
                {
                    Content = new StringContent(JsonSerializer.Serialize(new
                    {
                        id = $"acto-{o.Actos.Count}", flujo, numero = $"ACTO{o.Actos.Count:D4}", estado = "Completed",
                        datos = new { radicacion, estado = hacia }, compensacionesPendientes = 0, error = (string?)null, deshaciendo = false,
                    }), Encoding.UTF8, "application/json"),
                };
            }

            private static HttpResponseMessage Problema(HttpStatusCode estado, string codigo)
                => new(estado) { Content = new StringContent($$"""{"code":"{{codigo}}"}""", Encoding.UTF8, "application/problem+json") };
        }
    }
}

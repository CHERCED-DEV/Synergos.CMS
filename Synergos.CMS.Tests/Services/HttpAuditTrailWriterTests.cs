using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Services;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// La bitácora que además sale de esta máquina (HU #15).
/// </summary>
/// <remarks>
/// La capacidad se simula con un <see cref="HttpMessageHandler"/> guionado: es la única forma de
/// provocar a voluntad que <c>Api.Audit</c> rechace, o que no conteste — que es el caso que la HU
/// obliga a resolver por escrito y no con un <c>catch</c> vacío.
/// </remarks>
public sealed class HttpAuditTrailWriterTests
{
    // ── El doble del escritor local ─────────────────────────────────────────

    private sealed class Local : IAuditTrailWriter
    {
        public List<AuditEvent> Escritos { get; } = new();

        public Task WriteAsync(AuditEvent evt, CancellationToken cancellationToken)
        {
            Escritos.Add(evt);
            return Task.CompletedTask;
        }

        public IReadOnlyList<AuditEvent> GetRecent(int maxItems, string? actorEmailFilter = null, string? actionFilter = null)
            => Escritos.Take(maxItems).ToList();

        public IReadOnlyList<AuditEvent> GetByDateRange(DateTime fromUtc, DateTime toUtc, int maxItems,
            string? actorEmailFilter = null, string? actionFilter = null)
            => Escritos.Take(maxItems).ToList();

        public AuditEvent? GetById(string id) => Escritos.FirstOrDefault(e => e.Id == id);
    }

    private sealed class Capacidad : HttpMessageHandler
    {
        public HttpStatusCode Estado { get; set; } = HttpStatusCode.Created;
        public string Codigo { get; set; } = "audit.bad_action";

        /// <summary>Si viene, la capacidad no contesta: se cae la conexión.</summary>
        public bool Caida { get; set; }

        public List<(string Uri, string? Idem, string Body, string? Identidad)> Llamadas { get; } = new();

        /// <summary>Si la capacidad no tiene llave para comprobar lo que le presenten.</summary>
        public bool SinLlaveDeVerificacion { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
        {
            if (Caida) throw new HttpRequestException("guionado: caída");

            var identidad = req.Headers.TryGetValues("X-Synergos-Identity", out var t) ? t.FirstOrDefault() : null;

            Llamadas.Add((
                req.RequestUri!.PathAndQuery,
                req.Headers.TryGetValues("Idempotency-Key", out var v) ? v.FirstOrDefault() : null,
                req.Content is null ? string.Empty : await req.Content.ReadAsStringAsync(ct),
                identidad));

            // Presentar una prueba donde no hay con qué verificarla se RECHAZA — no se ignora.
            if (SinLlaveDeVerificacion && !string.IsNullOrWhiteSpace(identidad))
            {
                return new HttpResponseMessage(HttpStatusCode.BadRequest)
                {
                    Content = new StringContent(
                        """{"code":"identity.token_not_verifiable","detail":"sin llave"}""",
                        Encoding.UTF8, "application/json"),
                };
            }

            return Estado == HttpStatusCode.Created
                ? new HttpResponseMessage(HttpStatusCode.Created)
                {
                    Content = new StringContent("""{"id":"e-1"}""", Encoding.UTF8, "application/json"),
                }
                : new HttpResponseMessage(Estado)
                {
                    Content = new StringContent($$"""{"code":"{{Codigo}}","detail":"guionado"}""",
                        Encoding.UTF8, "application/json"),
                };
        }
    }

    private sealed class Fabrica : IHttpClientFactory
    {
        private readonly Capacidad _h;
        public Fabrica(Capacidad h) => _h = h;
        public HttpClient CreateClient(string name)
            => new(_h, disposeHandler: false) { BaseAddress = new Uri("http://audit.local/") };
    }

    /// <summary>Emisor guionado. <c>null</c> = el despliegue no sabe emitir (el default).</summary>
    private sealed class Emisor : IIdentityTokenIssuer
    {
        public string? Token { get; set; }

        public List<IdentitySubject> Pedidos { get; } = new();

        public Task<string?> IssueAsync(IdentitySubject subject, CancellationToken cancellationToken = default)
        {
            Pedidos.Add(subject);
            return Task.FromResult(Token);
        }
    }

    private sealed class OptionsMonitorFalso : IOptionsMonitor<AuditSettings>
    {
        public OptionsMonitorFalso(AuditSettings v) => CurrentValue = v;
        public AuditSettings CurrentValue { get; }
        public AuditSettings Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<AuditSettings, string?> l) => null;
    }

    private static (HttpAuditTrailWriter Svc, Local Loc, Capacidad Cap) Nuevo()
    {
        var (svc, loc, cap, _) = NuevoConEmisor();
        return (svc, loc, cap);
    }

    private static (HttpAuditTrailWriter Svc, Local Loc, Capacidad Cap, Emisor Ide) NuevoConEmisor(string? token = null)
    {
        var cap = new Capacidad();
        var loc = new Local();
        var ide = new Emisor { Token = token };
        var svc = new HttpAuditTrailWriter(
            loc, new Fabrica(cap),
            new OptionsMonitorFalso(new AuditSettings()),
            NullLogger<HttpAuditTrailWriter>.Instance,
            ide);
        return (svc, loc, cap, ide);
    }

    private static AuditEvent Asiento(
        string action = "gov.notification.open",
        string recurso = "n-1",
        string correo = "Ana@Entidad.GOV.co",
        string afirmacion = IdentityAssertions.CmsSession)
        => new(
            Id: "evt-1",
            OccurredAtUtc: new DateTime(2026, 9, 5, 10, 0, 0, DateTimeKind.Utc),
            ActorEmail: correo,
            ActorName: "Ana Pérez",
            Action: action,
            Resource: recurso,
            Outcome: "failure",
            Detail: "not_the_addressee",
            Assertion: afirmacion);

    private static JsonElement Cuerpo(Capacidad cap, int i = 0)
        => JsonDocument.Parse(cap.Llamadas[i].Body).RootElement;

    // ── Lo durable primero ──────────────────────────────────────────────────

    [Fact]
    public async Task El_asiento_queda_local_y_ademas_se_reenvia()
    {
        var (svc, loc, cap) = Nuevo();

        await svc.WriteAsync(Asiento(), CancellationToken.None);

        Assert.Single(loc.Escritos);
        Assert.Equal("evt-1", loc.Escritos[0].Id);
        Assert.Single(cap.Llamadas);
        Assert.Equal("/v1/entries", cap.Llamadas[0].Uri);
    }

    /// <summary>
    /// Con la capacidad caída, el asiento SIGUE guardado y se lee.
    /// </summary>
    /// <remarks>
    /// Es la razón entera de que el JSONL no se sustituya. Si el reenvío fuera lo primero —o lo
    /// único—, cada corte de red se llevaría por delante el rastro que sí se podía conservar, y el
    /// administrador vería una bitácora con huecos sin saber que los tiene.
    /// </remarks>
    [Fact]
    public async Task Con_la_capacidad_caida_el_asiento_sigue_guardado()
    {
        var (svc, loc, cap) = Nuevo();
        cap.Caida = true;

        await svc.WriteAsync(Asiento(), CancellationToken.None);

        Assert.Equal("evt-1", loc.Escritos[0].Id);
        Assert.NotNull(svc.GetById("evt-1"));
    }

    /// <summary>Un fallo del reenvío NO se le devuelve a quien auditó.</summary>
    /// <remarks>
    /// Convertir la caída de la bitácora en un 500 rompería la acción que se estaba auditando —
    /// aprobar un comentario, rechazar un acceso— por un servicio auxiliar. El rastro no se pierde:
    /// queda local, y el hueco queda anotado.
    /// </remarks>
    [Fact]
    public async Task El_fallo_del_reenvio_no_rompe_la_accion_que_se_auditaba()
    {
        var (svc, _, cap) = Nuevo();
        cap.Caida = true;

        var ex = await Record.ExceptionAsync(() => svc.WriteAsync(Asiento(), CancellationToken.None));

        Assert.Null(ex);
    }

    // ── El hueco queda escrito ──────────────────────────────────────────────

    [Fact]
    public async Task Un_reenvio_caido_deja_asiento_del_hueco()
    {
        var (svc, loc, cap) = Nuevo();
        cap.Caida = true;

        await svc.WriteAsync(Asiento(), CancellationToken.None);

        Assert.Equal(2, loc.Escritos.Count);
        var hueco = loc.Escritos[1];
        Assert.Equal(HttpAuditTrailWriter.ForwardFailureAction, hueco.Action);
        Assert.Equal("failure", hueco.Outcome);
        // El hueco NOMBRA al que no llegó: sin eso queda escrito «algo falló», que es casi lo
        // mismo que no escribir nada.
        Assert.Equal("evt-1", hueco.Resource);
        Assert.Contains("gov.notification.open", hueco.Detail, StringComparison.Ordinal);
    }

    /// <summary>Un rechazo de la capacidad también es un hueco, y dice cuál.</summary>
    [Fact]
    public async Task Un_reenvio_rechazado_deja_el_codigo_en_el_hueco()
    {
        var (svc, loc, cap) = Nuevo();
        cap.Estado = HttpStatusCode.BadRequest;
        cap.Codigo = "audit.bad_action";

        await svc.WriteAsync(Asiento(), CancellationToken.None);

        Assert.Contains("audit.bad_action", loc.Escritos[1].Detail, StringComparison.Ordinal);
    }

    /// <summary>Un 401 nombra la llave, que es lo que hay que arreglar.</summary>
    [Fact]
    public async Task Un_401_dice_que_revisar()
    {
        var (svc, loc, cap) = Nuevo();
        cap.Estado = HttpStatusCode.Unauthorized;

        await svc.WriteAsync(Asiento(), CancellationToken.None);

        Assert.Contains("Synergos:Audit:ApiKey", loc.Escritos[1].Detail, StringComparison.Ordinal);
    }

    /// <summary>
    /// El asiento del hueco no sale a la red: con la capacidad caída sería un lazo sin fin.
    /// </summary>
    [Fact]
    public async Task El_asiento_del_hueco_no_intenta_reenviarse()
    {
        var (svc, loc, cap) = Nuevo();
        cap.Caida = true;

        await svc.WriteAsync(Asiento(), CancellationToken.None);

        // Dos asientos locales (el original y el hueco) y UN solo intento de red.
        Assert.Equal(2, loc.Escritos.Count);

        cap.Caida = false;
        await svc.WriteAsync(Asiento(action: HttpAuditTrailWriter.ForwardFailureAction), CancellationToken.None);

        Assert.Equal(3, loc.Escritos.Count);
        Assert.Empty(cap.Llamadas);
    }

    // ── Lo que viaja, y lo que no ───────────────────────────────────────────

    [Fact]
    public async Task El_correo_no_viaja_pero_el_seudonimo_agrupa()
    {
        var (svc, _, cap) = Nuevo();

        await svc.WriteAsync(Asiento(correo: "Ana@Entidad.GOV.co"), CancellationToken.None);

        var body = cap.Llamadas[0].Body;
        Assert.DoesNotContain("Ana@Entidad", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Ana Pérez", body, StringComparison.Ordinal);

        // Y el seudónimo es el MISMO aunque el correo venga escrito de otra forma: si dependiera
        // de mayúsculas o de espacios, la bitácora dejaría de agrupar a la misma persona.
        var uno = Cuerpo(cap).GetProperty("actorId").GetString();

        cap.Llamadas.Clear();
        await svc.WriteAsync(Asiento(correo: "  ana@entidad.gov.co "), CancellationToken.None);

        Assert.Equal(uno, Cuerpo(cap).GetProperty("actorId").GetString());
    }

    /// <summary>Un asiento sin persona detrás sigue teniendo actor: la capacidad lo exige.</summary>
    /// <remarks>
    /// <c>audit.actor_required</c> rechaza lo anónimo, y con razón: una bitácora de acciones
    /// anónimas no contesta la única pregunta que se le hace. Un proceso también es un actor.
    /// </remarks>
    [Fact]
    public async Task Un_asiento_del_sistema_viaja_como_actor_nombrado()
    {
        var (svc, _, cap) = Nuevo();

        await svc.WriteAsync(Asiento(correo: ""), CancellationToken.None);

        Assert.Equal("sistema", Cuerpo(cap).GetProperty("actorId").GetString());
    }

    /// <summary>
    /// La afirmación viaja en SU campo, el que la capacidad comprueba y guarda.
    /// </summary>
    /// <remarks>
    /// Desde la #72 <c>Api.Audit</c> la resuelve y la persiste como <c>ActedWith</c>. Dejarla
    /// además en <c>details</c> daría dos sitios que pueden discrepar sobre un mismo hecho, y el
    /// opaco —que nadie comprueba— le ganaría al comprobado.
    /// </remarks>
    [Fact]
    public async Task La_afirmacion_viaja_en_el_campo_que_la_capacidad_comprueba()
    {
        var (svc, _, cap) = Nuevo();

        await svc.WriteAsync(Asiento(afirmacion: IdentityAssertions.CmsSession), CancellationToken.None);

        Assert.Equal("CmsSession", Cuerpo(cap).GetProperty("assertion").GetString());
        Assert.False(Cuerpo(cap).GetProperty("details").TryGetProperty("assertion", out _));
    }

    /// <summary>
    /// Un asiento que no registra afirmación viaja con el SUELO, no sin nada.
    /// </summary>
    /// <remarks>
    /// <para><c>CmsSession</c> significa «nos fiamos de quien llama», o sea la <i>ausencia</i> de
    /// comprobación — que es exactamente lo que hay en un asiento que no registra ninguna. No es
    /// inventar una comprobación; inventarla sería mandar <c>IdentityToken</c>.</para>
    ///
    /// <para>Y no se puede omitir: desde la #72 la capacidad rechaza con
    /// <c>access_requires_identity</c>, así que cada asiento del CMS anterior a este campo se
    /// volvería un hueco anotado — ruido en vez de rastro.</para>
    /// </remarks>
    [Fact]
    public async Task Un_asiento_sin_afirmacion_viaja_con_el_suelo()
    {
        var (svc, _, cap) = Nuevo();

        await svc.WriteAsync(Asiento(afirmacion: IdentityAssertions.None), CancellationToken.None);

        Assert.Equal("CmsSession", Cuerpo(cap).GetProperty("assertion").GetString());
    }

    [Fact]
    public async Task El_reenvio_lleva_la_llave_del_asiento()
    {
        var (svc, _, cap) = Nuevo();

        await svc.WriteAsync(Asiento(), CancellationToken.None);

        Assert.Equal("cms-audit-evt-1", cap.Llamadas[0].Idem);
    }

    /// <summary>
    /// Un asiento sin recurso no se descarta: se nombra la ausencia.
    /// </summary>
    /// <remarks>
    /// <c>Api.Audit</c> exige las dos partes del <c>Ref</c> y rechazaría con
    /// <c>audit.target_required</c>. Mandarlo vacío convertiría un asiento perfectamente útil
    /// —«quién hizo qué»— en un hueco anotado, que es ruido en vez de rastro.
    /// </remarks>
    [Fact]
    public async Task Un_asiento_sin_recurso_nombra_la_ausencia()
    {
        var (svc, loc, cap) = Nuevo();

        await svc.WriteAsync(Asiento(recurso: "  "), CancellationToken.None);

        Assert.Equal("(sin recurso)", Cuerpo(cap).GetProperty("targetId").GetString());
        Assert.Single(loc.Escritos);
    }

    /// <summary>
    /// Un detalle largo se recorta acá y no lo rechaza la capacidad.
    /// </summary>
    /// <remarks>
    /// Al revés, un volcado de excepción en <c>Detail</c> convertiría cada asiento grande en un
    /// hueco por <c>audit.detail_too_long</c> — y justo los asientos grandes son los interesantes.
    /// </remarks>
    [Fact]
    public async Task Un_detalle_largo_se_recorta_y_el_asiento_llega()
    {
        var (svc, loc, cap) = Nuevo();
        var largo = Asiento() with { Detail = new string('x', 900) };

        await svc.WriteAsync(largo, CancellationToken.None);

        Assert.Equal(512, Cuerpo(cap).GetProperty("details").GetProperty("detail").GetString()!.Length);
        // Y lo local NO se recorta: el recorte es del contrato de la capacidad, no del rastro.
        Assert.Equal(900, loc.Escritos[0].Detail.Length);
        Assert.Single(loc.Escritos);
    }

    // ── Quién actuó, presentado y no sólo declarado ─────────────────────────

    /// <summary>
    /// Sin emisor —el default— el asiento sale sin firmar y llega igual.
    /// </summary>
    /// <remarks>
    /// Es el camino del clon limpio y el de <c>Api.Identity</c> caída: sin token se sigue
    /// declarando, que es lo que se hacía antes de la HU #14. Un asiento no se pierde porque la
    /// identidad no esté.
    /// </remarks>
    [Fact]
    public async Task Sin_emisor_el_asiento_sale_sin_firmar_y_llega()
    {
        var (svc, loc, cap, _) = NuevoConEmisor(token: null);

        await svc.WriteAsync(Asiento(), CancellationToken.None);

        Assert.Null(cap.Llamadas[0].Identidad);
        Assert.Single(loc.Escritos);
    }

    /// <summary>
    /// Con emisor, el asiento va firmado y el sujeto del token ES el actor.
    /// </summary>
    /// <remarks>
    /// La capacidad rechaza un token que nombre a otro (<c>token_subject_mismatch</c>), y eso es
    /// justo lo que lo vuelve prueba y no adorno: firmar con otro sujeto no fallaría acá — fallaría
    /// allá, y convertiría cada asiento en un hueco.
    /// </remarks>
    [Fact]
    public async Task El_asiento_va_firmado_y_el_sujeto_del_token_es_el_actor()
    {
        var (svc, _, cap, ide) = NuevoConEmisor(token: "tok-abc");

        await svc.WriteAsync(Asiento(), CancellationToken.None);

        Assert.Equal("tok-abc", cap.Llamadas[0].Identidad);

        var pedido = Assert.Single(ide.Pedidos);
        Assert.Equal("cms.actor", pedido.Kind);
        Assert.Equal(Cuerpo(cap).GetProperty("actorId").GetString(), pedido.Id);

        // Y el correo tampoco viaja dentro del sujeto del token.
        Assert.DoesNotContain("@", pedido.Id, StringComparison.Ordinal);
    }

    /// <summary>
    /// Si la capacidad no puede comprobar el token, el asiento se repite SIN firmar y llega.
    /// </summary>
    /// <remarks>
    /// <para>Es el principio de la #72 sostenido: «parar la bitácora cuando falla la identidad
    /// convierte una caída en un hueco en el registro, que es peor que un asiento débil». Sin este
    /// reintento, presentar identidad a una capacidad sin llave de verificación perdería
    /// <b>todos</b> los asientos — el cambio que quería fortalecerlos los habría borrado.</para>
    ///
    /// <para>Y queda como <c>CmsSession</c>, que es lo que este lado siempre pudo respaldar: no se
    /// inventa nada, se deja de probar algo que allá no se puede comprobar.</para>
    /// </remarks>
    [Fact]
    public async Task Si_la_capacidad_no_puede_comprobarlo_el_asiento_se_repite_sin_firmar()
    {
        var (svc, loc, cap, _) = NuevoConEmisor(token: "tok-abc");
        cap.SinLlaveDeVerificacion = true;

        await svc.WriteAsync(Asiento(), CancellationToken.None);

        Assert.Equal(2, cap.Llamadas.Count);
        Assert.Equal("tok-abc", cap.Llamadas[0].Identidad);
        Assert.Null(cap.Llamadas[1].Identidad);

        // Y NO quedó hueco: el asiento llegó.
        Assert.Single(loc.Escritos);
    }

    /// <summary>
    /// Un token rechazado por OTRA causa no se repite sin firmar: queda el hueco.
    /// </summary>
    /// <remarks>
    /// Un token vencido o de otro sujeto son fallos de este lado. Repetirlos sin firma los
    /// escondería para siempre detrás de un asiento débil, y el defecto seguiría ahí — pareciendo
    /// que todo funciona, que es lo peor que puede hacer un reintento.
    /// </remarks>
    [Fact]
    public async Task Un_rechazo_de_identidad_por_otra_causa_no_se_repite_sin_firmar()
    {
        var (svc, loc, cap, _) = NuevoConEmisor(token: "tok-abc");
        cap.Estado = HttpStatusCode.BadRequest;
        cap.Codigo = "identity.token_subject_mismatch";

        await svc.WriteAsync(Asiento(), CancellationToken.None);

        Assert.Single(cap.Llamadas);
        Assert.Equal(2, loc.Escritos.Count);
        Assert.Contains("token_subject_mismatch", loc.Escritos[1].Detail, StringComparison.Ordinal);
    }

    /// <summary>El asiento del hueco tampoco pide token: no sale a la red.</summary>
    [Fact]
    public async Task El_asiento_del_hueco_no_pide_token()
    {
        var (svc, _, cap, ide) = NuevoConEmisor(token: "tok-abc");
        cap.Caida = true;

        await svc.WriteAsync(Asiento(), CancellationToken.None);

        // Un solo intento de red ⇒ un solo token pedido. El hueco no genera otro.
        Assert.Single(ide.Pedidos);
    }

    // ── Leer nunca sale a la red ────────────────────────────────────────────

    [Fact]
    public async Task Leer_no_toca_la_capacidad_ni_cuando_esta_viva()
    {
        var (svc, _, cap) = Nuevo();
        await svc.WriteAsync(Asiento(), CancellationToken.None);
        cap.Llamadas.Clear();

        Assert.Single(svc.GetRecent(10));
        Assert.Single(svc.GetByDateRange(DateTime.UtcNow.AddDays(-1), DateTime.UtcNow, 10));
        Assert.NotNull(svc.GetById("evt-1"));

        Assert.Empty(cap.Llamadas);
    }
}

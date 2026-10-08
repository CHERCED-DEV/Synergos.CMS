using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Synergos.Api.Notifications.Domain;
using Synergos.Bff.Core;
using Synergos.Bff.Core.Flow;
using Synergos.Bff.Pasos;
using Synergos.CMS.Tests.Architecture;   // Proyectos: la raíz del repo, resuelta del disco (#136)
using Synergos.Core;

namespace Synergos.CMS.Tests.Bff;

/// <summary>
/// La plantilla del aviso de compensación colgada: la que el código PIDE, contra la que el
/// despliegue APROVISIONA (#174).
/// </summary>
/// <remarks>
/// <para><b>Por qué los tests de compensación no lo vieron.</b> Cada uno configura su propia
/// <c>TemplateKey</c> —<c>salud.compensacion.colgada</c>, <c>tienda…</c>— contra un doble que
/// contesta 200 a lo que sea. Ninguno ejercitaba la clave de POR DEFECTO, que es la que piden los
/// cuatro orquestadores desplegados, contra lo que el despliegue siembra — y el despliegue no
/// sembraba nada. El aviso que cierra el lazo salía <c>template_not_found</c> el día que hacía
/// falta.</para>
///
/// <para><b>Una sola fuente para los marcadores, y es el código.</b> Lo que se cruza no es una
/// lista escrita acá ni <see cref="CompensationAlert.Placeholders"/>: es el cuerpo que
/// <see cref="CompensationAlert.RaiseAsync"/> MANDA de verdad, capturado en el cable. Del otro lado,
/// la plantilla se lee de <c>tools/provisionar.plantillas.json</c> —el mismo fichero que publica
/// <c>provisionar.sh</c>— y se juzga con la regla de la propia capacidad
/// (<see cref="NotificationRules.Fill"/>), no con una expresión copiada.</para>
///
/// <para><b>La mutación que lo pone rojo</b> es la del ticket: añadir un marcador al aviso sin
/// tocar la plantilla. Y la del doc 09: una plantilla con <c>{cita}</c>.</para>
/// </remarks>
public sealed class PlantillaDelAvisoTests
{
    private static readonly DateTimeOffset Inicio = new(2026, 9, 29, 10, 0, 0, TimeSpan.Zero);

    private sealed record Plantilla(string Key, string Channel, string Subject, string Body);

    /// <summary>Una saga rendida, con una compensación colgada. El dominio da igual: el aviso es de Bff.Core.</summary>
    private sealed record SagaRendida(string Id, DateTimeOffset StartedAtUtc, IReadOnlyList<Compensation> Compensations) : ISaga
    {
        public SagaStatus Status => SagaStatus.CompensationFailed;
        public DateTimeOffset? AlertedAtUtc => null;
        public int AlertsSent => 0;
    }

    private static SagaRendida UnaSaga(string id = "saga-1") => new(id, Inicio, new[]
    {
        new Compensation("c1", "ReleaseBookingHold", "hold-1", "no se confirmó",
            Attempts: CompensationLimits.MaxAttempts, LastError: "booking.unreachable"),
    });

    private static AlertOptions Guardia(string? clave = null) => new()
    {
        ToKind = "salud.guardia",
        ToId = "operaciones",
        Address = "guardia@ejemplo.co",
        TemplateKey = clave,
    };

    /// <summary>Apunta el cuerpo del aviso y contesta como la capacidad.</summary>
    private sealed class Cable : HttpMessageHandler, IHttpClientFactory
    {
        public string? Cuerpo { get; private set; }

        public HttpClient CreateClient(string name)
            => new(this, disposeHandler: false) { BaseAddress = new Uri($"http://{name}.local/") };

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Cuerpo = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.Created)
            {
                Content = JsonContent.Create(new { id = "d1", status = "Queued" }),
            };
        }
    }

    /// <summary>Lo que el aviso manda de verdad: la clave que pide y los valores que rellena.</summary>
    private static async Task<(string Clave, Dictionary<string, string> Valores)> LoQueElAvisoManda(string? claveConfigurada = null)
    {
        var cable = new Cable();
        var aviso = new CompensationAlert(cable, new SagaVocabulary("salud", "la cita"), Options.Create(Guardia(claveConfigurada)));

        Assert.Null(await aviso.RaiseAsync(UnaSaga(), CancellationToken.None));

        using var cuerpo = JsonDocument.Parse(cable.Cuerpo!);
        var clave = cuerpo.RootElement.GetProperty("templateKey").GetString()!;
        var valores = cuerpo.RootElement.GetProperty("values").EnumerateObject()
            .ToDictionary(p => p.Name, p => p.Value.GetString()!, StringComparer.Ordinal);
        return (clave, valores);
    }

    /// <summary>Lo que declara <c>tools/provisionar.plantillas.json</c>, sin las entradas que son sólo comentario.</summary>
    private static List<Plantilla> Declaradas()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(Proyectos.Raiz(), "tools", "provisionar.plantillas.json")));
        var declaradas = doc.RootElement.EnumerateArray()
            .Where(e => e.EnumerateObject().Any(p => p.Name != "_"))
            .Select(e => new Plantilla(
                e.GetProperty("key").GetString()!, e.GetProperty("channel").GetString()!,
                e.GetProperty("subject").GetString()!, e.GetProperty("body").GetString()!))
            .ToList();

        Assert.True(declaradas.Count > 0,
            "tools/provisionar.plantillas.json no declara ninguna plantilla: este test estaría "
            + "cruzando el aviso contra el vacío.");
        return declaradas;
    }

    private static Plantilla LaQuePide(string clave)
    {
        var p = Declaradas().SingleOrDefault(d => d.Key == clave);
        Assert.True(p is not null,
            $"El aviso pide la plantilla «{clave}» y tools/provisionar.plantillas.json no la declara: "
            + "en un servidor limpio saldría notifications.template_not_found.");
        return p!;
    }

    /// <summary>
    /// La plantilla del fichero como la guarda la capacidad: el canal se lee igual que
    /// <c>POST /v1/templates</c>, sin distinguir mayúsculas. Es el canal lo que decide si el cuerpo
    /// es HTML (#175), así que no es un dato que el test pueda inventar.
    /// </summary>
    private static Template ComoLaGuardaLaCapacidad(Plantilla p)
    {
        Assert.True(Enum.TryParse<Channel>(p.Channel, ignoreCase: true, out var canal),
            $"La plantilla «{p.Key}» declara el canal «{p.Channel}», que Api.Notifications no conoce.");
        return new Template("provisionada", p.Key, canal, p.Subject, p.Body);
    }

    /// <summary>
    /// Cada orquestador pide una plantilla que el despliegue aprovisiona.
    /// </summary>
    /// <remarks>
    /// <para>La clave que pide cada uno se DERIVA: de <c>compose.prod.yml</c> —el generado, que es
    /// lo que corre— sale si alguno configura <c>&lt;Vertical&gt;__Alerts__TemplateKey</c>, y la
    /// resolución (configurada o por defecto) la hace el propio <see cref="CompensationAlert"/>.
    /// Hoy ninguno la configura y los cuatro piden la de por defecto; el día que uno configure otra
    /// sin declararla, esto lo nombra.</para>
    /// </remarks>
    [Fact]
    public async Task Cada_orquestador_pide_una_plantilla_que_el_despliegue_aprovisiona()
    {
        var compose = File.ReadAllText(Path.Combine(Proyectos.Raiz(), "compose.prod.yml"));
        var secciones = Regex.Matches(compose, @"(?m)^\s+(\w+)__Alerts__ToKind:")
            .Select(m => m.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        // Red de seguridad: tantas secciones de aviso como orquestadores en el disco. Si el
        // descubrimiento se rompe, esto no puede quedar vigilando cero orquestadores.
        //
        // Un orquestador es el Bff.* que tiene PUNTO DE ENTRADA, el mismo criterio de
        // `ContainerBuildTests` y de `service-matrix.mjs`. Excluía `Bff.Core` por nombre, y la
        // segunda biblioteca (`Bff.Pasos`, ADR 0140) lo puso rojo contando un orquestador sin
        // aviso que no es un orquestador: una lista de excepciones crece con cada biblioteca, una
        // propiedad del disco se mantiene sola.
        var orquestadores = Proyectos.Todos("Synergos.Bff.")
            .Where(d => File.Exists(Path.Combine(d, "Program.cs")))
            .Select(d => Path.GetFileName(d))
            .ToList();
        Assert.True(secciones.Count == orquestadores.Count && secciones.Count > 0,
            $"compose.prod.yml configura el aviso en {secciones.Count} sección(es) "
            + $"({string.Join(", ", secciones)}) y hay {orquestadores.Count} orquestador(es) en el disco "
            + $"({string.Join(", ", orquestadores)}).");

        var declaradas = Declaradas().Select(d => d.Key).ToHashSet(StringComparer.Ordinal);
        var faltan = new List<string>();

        foreach (var seccion in secciones)
        {
            var m = Regex.Match(compose, $@"(?m)^\s+{seccion}__Alerts__TemplateKey:\s*(?<v>.+?)\s*$");
            string? configurada = null;
            if (m.Success)
            {
                // `${VAR:-valor}` vale su default, que es lo que corre si nadie pone la variable.
                var v = m.Groups["v"].Value.Trim('"');
                var d = Regex.Match(v, @"^\$\{\w+:-(?<d>[^}]*)\}$");
                configurada = d.Success ? d.Groups["d"].Value : v;
            }

            var (clave, _) = await LoQueElAvisoManda(configurada);
            if (!declaradas.Contains(clave)) faltan.Add($"{seccion} pide «{clave}»");
        }

        Assert.True(faltan.Count == 0,
            "Hay orquestadores que piden una plantilla que tools/provisionar.plantillas.json no "
            + $"declara: {string.Join("; ", faltan)}. En un servidor limpio, su aviso de compensación "
            + "colgada sale notifications.template_not_found — justo el día que hace falta (#174).");
    }

    /// <summary>
    /// Cada marcador que la plantilla usa, el aviso lo manda — juzgado con la regla de la capacidad.
    /// </summary>
    /// <remarks>
    /// Es el defecto del doc 09: una plantilla con <c>{cita}</c>, que el aviso dejó de mandar al
    /// promover la máquina a <c>Bff.Core</c>. <c>Api.Notifications</c> no manda un hueco: rechaza
    /// con <c>missing_placeholder</c>, y el aviso no sale nunca.
    /// </remarks>
    [Fact]
    public async Task Cada_marcador_de_la_plantilla_lo_manda_el_aviso()
    {
        var (clave, valores) = await LoQueElAvisoManda();
        var plantilla = ComoLaGuardaLaCapacidad(LaQuePide(clave));

        var relleno = NotificationRules.Fill(plantilla, valores);
        Assert.True(relleno.IsOk,
            $"La plantilla «{clave}» no se puede rellenar con lo que el aviso manda "
            + $"({string.Join(", ", valores.Keys.Select(k => "{" + k + "}"))}): "
            + $"{relleno.Rejection?.Message} Cada aviso saldría notifications.missing_placeholder.");
    }

    /// <summary>
    /// Codificar los valores (#175) no cambia el correo de la guardia: con lo que el aviso manda,
    /// sale el mismo cuerpo que antes, con el marcado de la plantilla intacto.
    /// </summary>
    /// <remarks>
    /// <para><b>Lo que se compara es contra el relleno CRUDO</b> —cada marcador por su valor, tal
    /// cual—, que es lo que la capacidad hacía antes del #175. Con valores sin marcado los dos
    /// tienen que coincidir byte a byte: si no, codificar habría tocado algo que no era de un
    /// valor —el <c>&lt;p&gt;</c> de la plantilla, o las tildes de un valor normal—, y la guardia
    /// recibiría un correo roto o un rastro ilegible.</para>
    ///
    /// <para>Es una afirmación sobre lo que el aviso manda HOY con esta saga. El
    /// <c>{pendientes}</c> real lleva el <c>LastError</c> de una capacidad, que puede traer
    /// comillas o un <c>&lt;</c> de un tercero: ése sí sale codificado, y es a propósito.</para>
    /// </remarks>
    [Fact]
    public async Task Con_lo_que_el_aviso_manda_el_cuerpo_sale_igual_que_el_relleno_crudo()
    {
        var (clave, valores) = await LoQueElAvisoManda();
        var plantilla = ComoLaGuardaLaCapacidad(LaQuePide(clave));
        Assert.True(NotificationRules.BodyIsHtml(plantilla.Channel),
            $"La plantilla «{clave}» ya no es de correo: este test comparaba un cuerpo HTML.");

        var crudo = Regex.Replace(plantilla.Body, @"\{(\w+)\}", m => valores[m.Groups[1].Value]);
        var relleno = NotificationRules.Fill(plantilla, valores);

        Assert.True(relleno.IsOk, relleno.Rejection?.Message);
        Assert.Equal(crudo, relleno.Value.Body);
    }

    /// <summary>
    /// Cada marcador que el aviso manda, la plantilla lo usa.
    /// </summary>
    /// <remarks>
    /// <para>La otra mitad, y la que falla en silencio: un valor que el aviso manda y la plantilla
    /// no pinta no rompe nada —la capacidad ignora lo que sobra— y la guardia recibe un correo sin
    /// el dato. Se comprueba quitando cada valor y pidiéndole a la capacidad que rellene: si la
    /// plantilla lo usa, <see cref="NotificationRules.Fill"/> tiene que rechazar.</para>
    /// </remarks>
    [Fact]
    public async Task Cada_marcador_que_el_aviso_manda_lo_usa_la_plantilla()
    {
        var (clave, valores) = await LoQueElAvisoManda();
        var plantilla = ComoLaGuardaLaCapacidad(LaQuePide(clave));

        var sinUsar = valores.Keys
            .Where(marcador => NotificationRules.Fill(plantilla,
                valores.Where(v => v.Key != marcador).ToDictionary(v => v.Key, v => v.Value, StringComparer.Ordinal)).IsOk)
            .ToList();

        Assert.True(sinUsar.Count == 0,
            $"El aviso manda {string.Join(", ", sinUsar.Select(m => "{" + m + "}"))} y la plantilla "
            + $"«{clave}» no lo usa: la guardia recibe el correo sin ese dato, y nada falla. "
            + "Añadilo a tools/provisionar.plantillas.json: provisionar.sh lo publica como versión nueva "
            + "(#179), y --verificar dice que difiere hasta que se corra.");
    }

    /// <summary>
    /// Contra <c>Api.Notifications</c> DE VERDAD: sin la plantilla el aviso no sale, y con la que
    /// se aprovisiona llega hasta el transporte.
    /// </summary>
    /// <remarks>
    /// <para>Se publica EXACTAMENTE lo que publica <c>provisionar.sh</c> —los cuatro campos del
    /// fichero por <c>POST /v1/templates</c>— y se reintenta el MISMO aviso, con la misma llave: el
    /// rechazo por plantilla ausente no la consume, así que sembrar después arregla el reintento.</para>
    ///
    /// <para><b>Por qué <c>transport_not_configured</c> es el verde.</b> El arnés no tiene
    /// credencial de correo, así que lo más lejos que puede llegar un aviso es a pedírselo al
    /// transporte. Llegar ahí es haber pasado la plantilla y sus marcadores; con el proceso vivo y un
    /// proveedor de mentira, el mismo aviso sale <c>Accepted</c> (ver el informe del #174).</para>
    /// </remarks>
    [Fact]
    public async Task Contra_la_capacidad_real_sin_plantilla_no_sale_y_con_la_aprovisionada_llega_al_transporte()
    {
        using var arnes = new ArnesDeCapacidades()
            .Levanta<Synergos.Api.Notifications.Contracts.SaveTemplateRequest>(CompensationAlert.Capability);
        var aviso = new CompensationAlert(arnes, new SagaVocabulary("salud", "la cita"), Options.Create(Guardia()));

        var sinPlantilla = await aviso.RaiseAsync(UnaSaga(), CancellationToken.None);
        Assert.Equal("notifications.template_not_found", sinPlantilla?.Code);

        using var http = arnes.CreateClient(CompensationAlert.Capability);
        foreach (var p in Declaradas())
        {
            using var peticion = new HttpRequestMessage(HttpMethod.Post, "v1/templates")
            {
                Content = JsonContent.Create(new { key = p.Key, channel = p.Channel, subject = p.Subject, body = p.Body }),
            };
            peticion.Headers.Add("Idempotency-Key", $"prueba:{p.Key}");
            using var r = await http.SendAsync(peticion);
            Assert.True(r.StatusCode == HttpStatusCode.Created,
                $"Api.Notifications no aceptó la plantilla «{p.Key}» del fichero: {(int)r.StatusCode} "
                + await r.Content.ReadAsStringAsync());
        }

        var conPlantilla = await aviso.RaiseAsync(UnaSaga(), CancellationToken.None);
        Assert.Equal("notifications.transport_not_configured", conPlantilla?.Code);

        // Lo que se habría mandado queda en el rastro, y va sin un solo marcador por rellenar.
        var entregas = await http.GetFromJsonAsync<JsonElement>("v1/deliveries?toKind=salud.guardia&toId=operaciones");
        var entrega = Assert.Single(entregas.GetProperty("items").EnumerateArray());
        var asunto = entrega.GetProperty("subject").GetString()!;
        Assert.Contains("saga-1", asunto, StringComparison.Ordinal);
        Assert.DoesNotMatch(@"\{\w+\}", asunto);
    }

    // ── El aviso al comprador de entradas (ADR 0140 F3) ─────────────────────

    /// <summary>Apunta lo que el paso de aviso manda: la clave de la plantilla y los valores.</summary>
    private sealed class PuertoQueApunta : INotificationsPort
    {
        public string? Plantilla { get; private set; }
        public IReadOnlyDictionary<string, string>? Valores { get; private set; }

        public Task<Result<string>> AvisarAsync(
            Ref destinatario, string direccion, string plantilla, IReadOnlyDictionary<string, string> valores,
            IdempotencyKey llave, CancellationToken ct)
        {
            Plantilla = plantilla;
            Valores = valores;
            return Task.FromResult(Result.Ok("d1"));
        }
    }

    /// <summary>
    /// La plantilla de las entradas usa justo los valores que el paso <c>notifications.avisar</c>
    /// manda: ni uno que no mande, ni uno que mande y no se lea.
    /// </summary>
    /// <remarks>
    /// Los valores son fijos y los define el paso, no un mapeo en el JSON; así que lo que se cruza es
    /// lo que el paso MANDA, capturado en su puerto, contra la plantilla del fichero juzgada con la
    /// regla de la capacidad. Un marcador que el paso no manda deja cada aviso en
    /// <c>missing_placeholder</c>; uno que manda y nadie lee es un dato de más en el rastro.
    /// </remarks>
    [Fact]
    public async Task La_plantilla_de_las_entradas_usa_justo_los_valores_que_manda_el_paso()
    {
        const string clave = "eventos.entradas.confirmadas";
        var puerto = new PuertoQueApunta();
        var definicion = new PasoDef("avisar", "notifications.avisar", new[] { "comprador", "contacto", "total" },
            Array.Empty<string>(), "avisar", null, null, null, null, PasoDef.Seguir, null, clave);
        var contexto = new FlowContext()
            .Set("comprador", Ref.Create("eventos.comprador", "m1"))
            .Set("contacto", new Contacto("ana@ejemplo.co", "Ana", "https://sitio/compra?id=s-1", "Teatro"))
            .Set("total", Money.Of(10_000m, "COP"));

        await new PasoAvisar(puerto).EjecutarAsync(
            new EntradaDePaso("s-1", Ref.Create("eventos.compra", "s-1"), definicion, contexto, null, null), CancellationToken.None);

        Assert.Equal(clave, puerto.Plantilla);
        var plantilla = ComoLaGuardaLaCapacidad(LaQuePide(clave));
        var relleno = NotificationRules.Fill(plantilla, puerto.Valores!);
        Assert.True(relleno.IsOk, $"La plantilla «{clave}» no se rellena con lo que manda el paso: {relleno.Rejection?.Message}");

        var sinLeer = puerto.Valores!.Keys
            .Where(k => !plantilla.Subject.Contains("{" + k + "}", StringComparison.Ordinal)
                        && !plantilla.Body.Contains("{" + k + "}", StringComparison.Ordinal))
            .ToList();
        Assert.True(sinLeer.Count == 0,
            $"El paso manda {string.Join(", ", sinLeer.Select(k => "{" + k + "}"))} y la plantilla «{clave}» no lo usa.");
    }
}

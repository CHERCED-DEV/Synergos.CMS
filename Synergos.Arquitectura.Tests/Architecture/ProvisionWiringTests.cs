using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Synergos.Bff.Core;
using Synergos.CMS.Application.Services.Impl;

namespace Synergos.CMS.Tests.Architecture;

/// <summary>
/// El estado que hay que sembrar en un servidor nuevo (<c>tools/provisionar.sh</c>), cruzado
/// contra lo que el CMS espera encontrar publicado.
/// </summary>
/// <remarks>
/// <para><b>Qué vigila, y por qué nada de esto falla al arrancar.</b> Las definiciones de proceso
/// y los recursos de <c>Api.Booking</c> son un paso de DESPLIEGUE: sin ellos los servicios
/// levantan sanos, contestan <c>/health</c> y pasan la prueba de humo — y la primera persona que
/// intenta avanzar un pedido se lleva un <c>definition_not_found</c>. Los cinco pasos vivían como
/// prosa en los comentarios de <c>.env.example</c>, marcados por el propio repo como «OJO, paso de
/// DESPLIEGUE que no es código», y no aparecían en el documento que dice qué hace el arquitecto a
/// mano (#114).</para>
///
/// <para><b>Los dos defectos que encontró correrlo contra las capacidades vivas</b>, y que ningún
/// test suelto habría visto porque los dos contestan <b>200</b>:</para>
///
/// <list type="number">
///   <item><b>El monto se publicaba en CERO.</b> El manifiesto se leía con
///   <c>IFS=$'\t'</c>, y bash cuenta el tabulador como <i>IFS whitespace</i>: una racha de
///   tabuladores es UN separador. Una entrada <c>precio</c> no lleva <c>capacity</c> ni
///   <c>timeZoneId</c>, así que sus campos se corrían dos puestos y el monto llegaba vacío —
///   publicado como <c>0</c> por un <c>${monto:-0}</c>. Cada oferta de viaje, gratis. Hoy el
///   separador es <c>0x1F</c> y un monto que no sea numérico se RECHAZA en vez de caer a cero.</item>
///
///   <item><b>Y una vez arreglado eso, el precio seguía en cero</b>: la llave de idempotencia
///   salía sólo del sujeto, y <c>SetPrice</c> mira el libro <b>antes</b> que nada y devuelve el
///   precio anterior. O sea que cambiar la tarifa en el manifiesto y volver a correr el script no
///   hacía nada, contestando 200 y diciendo «+ puesto precio». Hoy la llave lleva la HUELLA del
///   monto — <c>feedback_seeded_content_needs_fingerprint</c> aplicado a una tarifa.</item>
/// </list>
///
/// <para><b>El primero se prueba EJECUTANDO el script</b>, no leyéndolo: <c>--autoprueba</c> corre
/// el lector de verdad sobre un manifiesto de mentira y comprueba dónde caen los campos. No habla
/// con nadie, así que corre en CI sin levantar una capacidad.</para>
/// </remarks>
public sealed class ProvisionWiringTests
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

    private static string Guion()
        => string.Join('\n', File.ReadAllLines(Path.Combine(RepoRoot(), "tools", "provisionar.sh"))
            .Where(l => !l.TrimStart().StartsWith('#')));

    /// <summary>
    /// El lector del manifiesto alinea los campos — comprobado corriéndolo.
    /// </summary>
    [Fact]
    public void El_manifiesto_se_lee_con_los_campos_alineados()
    {
        var psi = new ProcessStartInfo("bash")
        {
            WorkingDirectory = RepoRoot(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            // El guion escribe UTF-8; sin esto, en Windows el mensaje de un rojo sale ilegible (#170).
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8,
        };
        psi.ArgumentList.Add(Path.Combine("tools", "provisionar.sh"));
        psi.ArgumentList.Add("--autoprueba");

        using var p = Process.Start(psi)!;
        var salida = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
        p.WaitForExit();

        Assert.True(p.ExitCode == 0,
            "tools/provisionar.sh --autoprueba falló. Es el lector del manifiesto de despliegue: "
            + "si los campos se corren, una oferta se publica a CERO y la capacidad contesta 201."
            + Environment.NewLine + salida);
    }

    /// <summary>
    /// La llave de idempotencia de un precio lleva el monto dentro.
    /// </summary>
    /// <remarks>
    /// No se puede probar ejecutando sin una capacidad viva, así que se mira el cableado. La
    /// mutación que reproduce el defecto es quitar <c>:$monto</c> de la llave — que compila,
    /// contesta 200 y congela la tarifa para siempre.
    /// </remarks>
    [Fact]
    public void La_llave_de_un_precio_lleva_la_huella_del_monto()
    {
        var guion = Guion();
        var m = Regex.Match(guion, @"/v1/prices""[^\n]*""(provisionar:precio:[^""]*)""");

        Assert.True(m.Success,
            "No se encontró la llave de idempotencia con la que provisionar.sh publica un precio. "
            + "Si se reescribió esa línea, mové este gate con ella.");

        Assert.True(m.Groups[1].Value.Contains("$monto", StringComparison.Ordinal),
            $"La llave del precio es «{m.Groups[1].Value}» y no lleva el monto. `SetPrice` consulta "
            + "el libro de idempotencia ANTES de aplicar nada y devuelve el precio anterior, así "
            + "que con una llave derivada sólo del sujeto la tarifa queda CONGELADA en la primera "
            + "que se publicó: cambiarla en el manifiesto contesta 200 y no hace nada. Verificado "
            + "en vivo contra Api.Pricing (#114).");
    }

    /// <summary>
    /// Un recurso se BUSCA por sujeto antes de crearse.
    /// </summary>
    /// <remarks>
    /// Verificado en vivo contra <c>Api.Booking</c>: dos POST del mismo sujeto con llaves de
    /// idempotencia distintas dan <b>dos</b> recursos, los dos con 201, y el cupo del médico queda
    /// partido en dos sin que nada falle.
    /// </remarks>
    [Fact]
    public void Un_recurso_se_busca_por_sujeto_antes_de_crearse()
    {
        var guion = Guion();

        var buscar = guion.IndexOf("/v1/resources?subjectKind=", StringComparison.Ordinal);
        var crear = guion.IndexOf("POST \"$BOOKING_URL/v1/resources\"", StringComparison.Ordinal);

        Assert.True(buscar >= 0 && crear >= 0,
            "provisionar.sh ya no busca o ya no crea recursos de Api.Booking: mové este gate.");

        Assert.True(buscar < crear,
            "provisionar.sh crea el recurso sin buscarlo antes por sujeto. `RegisterResource` NO "
            + "reusa el id por sujeto: dos POST con llaves de idempotencia distintas dan DOS "
            + "recursos para el mismo médico —comprobado en vivo, los dos con 201— y el cupo queda "
            + "partido en dos sin que nada falle.");
    }

    /// <summary>
    /// Qué pipelines publica el script, en orden, tal como los escribe.
    /// </summary>
    private static Dictionary<string, string[]> PipelinesDelGuion()
        => Regex.Matches(Guion(), @"pipeline_json ""\$TRACKING_PREFIX\.(\w+)""\s+([^)""]+)\)")
            .ToDictionary(
                m => m.Groups[1].Value,
                m => m.Groups[2].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries),
                StringComparer.Ordinal);

    /// <summary>
    /// Los cuatro pipelines que se publican son los cuatro que el CMS tiene en C#, etapa por
    /// etapa y en orden.
    /// </summary>
    /// <remarks>
    /// <para><b>Es la mitad que no se ve.</b> Con <c>Synergos:Tracking:Mode=Api</c> el CMS valida
    /// cada avance contra la definición publicada. Añadir «en aduana» al <c>ShopPipeline</c> de C#
    /// y no tocar el script deja un pedido que no puede avanzar — y no falla al desplegar: falla
    /// el día que a alguien le toca esa etapa, con un <c>transition_not_allowed</c> que no dice
    /// que el problema es el aprovisionamiento.</para>
    ///
    /// <para><b>Se cruza contra los pipelines por REFLEXIÓN y no contra una lista escrita acá</b>:
    /// una copia de las etapas dentro del gate sería la tercera, y las tres se desviarían por
    /// separado — que es el problema que la HU #46 vino a quitar cuando estaban copiadas cuatro
    /// veces en C#.</para>
    /// </remarks>
    [Theory]
    [InlineData("shop")]
    [InlineData("travel")]
    [InlineData("events")]
    [InlineData("academy")]
    public void Los_pipelines_publicados_son_los_que_el_CMS_recorre(string dominio)
    {
        var enCodigo = dominio switch
        {
            "shop" => StubOrderTrackingService.ShopPipeline,
            "travel" => TravelCartService.TravelPipeline,
            "events" => StubEventTicketingService.EventPipeline,
            "academy" => StubEnrollmentService.AcademyPipeline,
            _ => throw new ArgumentOutOfRangeException(nameof(dominio)),
        };

        var guion = PipelinesDelGuion();

        Assert.True(guion.ContainsKey(dominio),
            $"tools/provisionar.sh no publica la definición de seguimiento de «{dominio}». "
            + $"Se encontraron: {string.Join(", ", guion.Keys)}. Sin ella, con "
            + "Synergos:Tracking:Mode=Api ningún pedido de ese dominio avanza.");

        var esperadas = enCodigo.Select(e => e.Stage).ToArray();

        Assert.True(esperadas.SequenceEqual(guion[dominio], StringComparer.Ordinal),
            $"El pipeline «{dominio}» que se publica no es el que el CMS recorre." + Environment.NewLine
            + $"  en C#:              {string.Join(" → ", esperadas)}" + Environment.NewLine
            + $"  en provisionar.sh:  {string.Join(" → ", guion[dominio])}" + Environment.NewLine
            + "Con Tracking:Mode=Api el avance se valida contra la definición publicada: una etapa "
            + "que está en C# y no en la definición deja el pedido clavado ahí, y no falla al "
            + "desplegar — falla el día que a alguien le toca esa etapa.");
    }

    // ── La plantilla del aviso de compensación colgada (#174) ────────────────
    //
    // Estos tres CORREN el guion de verdad contra una Api.Notifications de mentira sobre un
    // puerto de verdad: el guion habla con `curl`, así que un doble en proceso no le sirve. Lo que
    // se prueba es el FLUJO del guion —buscar, paginar, decidir, publicar—; las reglas de la
    // capacidad las prueba `PlantillaDelAvisoTests` contra la capacidad real, en la otra suite.

    /// <summary>La clave que el aviso pide cuando nadie configura otra — del código, no copiada.</summary>
    private static string ClaveDelAviso => CompensationAlert.DefaultTemplateKey;

    /// <summary>
    /// <c>--verificar</c> en un servidor sin la plantilla sale ROJO y la nombra.
    /// </summary>
    /// <remarks>
    /// Es el defecto #174 entero: en un servidor limpio el aviso que cierra el lazo de una
    /// compensación colgada salía <c>template_not_found</c>, y <c>--verificar</c> no lo veía
    /// porque sólo miraba lo que él mismo sembraba. Mutación: quitar la sección de plantillas del
    /// guion lo pone verde con la plantilla faltando — y esto rojo.
    /// </remarks>
    [Fact]
    public async Task Verificar_sin_la_plantilla_del_aviso_sale_rojo_y_la_nombra()
    {
        await using var notificaciones = await NotificationsDeMentira.Levantar();

        var (codigo, salida) = await Correr(notificaciones.Url, "--verificar");

        Assert.True(codigo != 0,
            "provisionar.sh --verificar salió verde con Api.Notifications SIN la plantilla del aviso. "
            + "En un servidor así, el aviso de una compensación colgada sale template_not_found el "
            + "día que hace falta (#174)." + Environment.NewLine + salida);
        Assert.Contains($"FALTA  plantilla {ClaveDelAviso}", salida, StringComparison.Ordinal);
        Assert.Empty(notificaciones.Publicadas);
    }

    /// <summary>
    /// La plantilla que enseñaba el doc 09 —<c>{cita}</c>— sale ROJA, nombrando el marcador.
    /// </summary>
    /// <remarks>
    /// <para>Está publicada, así que un «¿existe?» la daría por buena, y cada aviso saldría
    /// <c>missing_placeholder</c>: <c>{cita}</c> dejó de mandarse al promover la máquina a
    /// <c>Bff.Core</c>. Y no se puede arreglar publicando encima —la capacidad no reescribe una
    /// plantilla viva—, así que lo único útil es decirlo, y en rojo.</para>
    /// </remarks>
    [Fact]
    public async Task Verificar_con_la_plantilla_que_ensenaba_el_doc_09_sale_rojo_y_nombra_el_marcador()
    {
        await using var notificaciones = await NotificationsDeMentira.Levantar(
            Plantilla(ClaveDelAviso, "Compensación colgada {cita}", "{cita} desde {desde}: {pendientes}"));

        var (codigo, salida) = await Correr(notificaciones.Url, "--verificar");

        Assert.True(codigo != 0,
            "provisionar.sh --verificar dio por buena una plantilla que usa {cita}, que el aviso no "
            + "manda: cada aviso saldría notifications.missing_placeholder." + Environment.NewLine + salida);
        Assert.Contains("{cita}", salida, StringComparison.Ordinal);
        Assert.Contains("missing_placeholder", salida, StringComparison.Ordinal);
    }

    /// <summary>
    /// Publicar pone la plantilla declarada UNA vez —aunque haya que paginar para buscarla— y la
    /// segunda corrida la da por buena sin tocarla.
    /// </summary>
    /// <remarks>
    /// <para><b>La de mentira sirve UNA fila por página</b>, pida lo que pida el guion, y lleva
    /// otra plantilla delante: la del aviso cae en la segunda página. Una búsqueda que mirara sólo
    /// la primera diría «falta» con la plantilla puesta, y la segunda corrida publicaría otra vez.
    /// </para>
    ///
    /// <para><b>Y lo publicado es lo declarado, letra por letra</b>: el cuerpo viaja escapado en
    /// ASCII para no depender de la página de códigos, y eso sólo vale si al otro lado vuelve a ser
    /// «Compensación».</para>
    /// </remarks>
    [Fact]
    public async Task Publicar_pone_la_plantilla_UNA_vez_y_la_segunda_corrida_no_la_toca()
    {
        await using var notificaciones = await NotificationsDeMentira.Levantar(
            Plantilla("otra.plantilla.cualquiera", "asunto", "cuerpo"));

        var primera = await Correr(notificaciones.Url);
        Assert.True(primera.Codigo == 0, primera.Salida);

        var (llave, cuerpo) = Assert.Single(notificaciones.Publicadas);
        Assert.StartsWith($"provisionar:plantilla:{ClaveDelAviso}:", llave, StringComparison.Ordinal);

        var declarada = Declaradas().Single(d => d["key"] == ClaveDelAviso);
        foreach (var campo in new[] { "key", "channel", "subject", "body" })
        {
            Assert.Equal(declarada[campo], cuerpo[campo]);
        }

        // La segunda corrida pasa por el MISMO veredicto que `--verificar` —encontrarla y
        // compararla—, así que una tercera con `--verificar` costaría segundos y no diría más.
        var segunda = await Correr(notificaciones.Url);
        Assert.True(segunda.Codigo == 0, segunda.Salida);
        Assert.True(notificaciones.Publicadas.Count == 1,
            "La segunda corrida volvió a publicar la plantilla: no la encontró porque estaba en la "
            + "segunda página. Api.Notifications contestaría key_taken y nadie sabría por qué."
            + Environment.NewLine + segunda.Salida);
        Assert.Contains($"✓ plantilla {ClaveDelAviso}", segunda.Salida, StringComparison.Ordinal);
    }

    /// <summary>Las plantillas que declara <c>tools/provisionar.plantillas.json</c>, sin los comentarios.</summary>
    private static List<Dictionary<string, string>> Declaradas()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepoRoot(), "tools", "provisionar.plantillas.json")));
        return doc.RootElement.EnumerateArray()
            .Where(e => e.EnumerateObject().Any(p => p.Name != "_"))
            .Select(e => e.EnumerateObject()
                .Where(p => p.Name != "_")
                .ToDictionary(p => p.Name, p => p.Value.GetString() ?? string.Empty, StringComparer.Ordinal))
            .ToList();
    }

    private static Dictionary<string, string> Plantilla(string clave, string asunto, string cuerpo)
        => new(StringComparer.Ordinal)
        {
            ["id"] = Guid.NewGuid().ToString("n"),
            ["key"] = clave,
            ["channel"] = "Email",
            ["subject"] = asunto,
            ["body"] = cuerpo,
        };

    /// <summary>
    /// Corre <c>tools/provisionar.sh</c> contra <paramref name="url"/>, con un manifiesto de
    /// entidades vacío: lo que se mira acá son las plantillas.
    /// </summary>
    private static async Task<(int Codigo, string Salida)> Correr(string url, params string[] extra)
    {
        var vacio = Path.Combine(Path.GetTempPath(), "provisionar-vacio-" + Guid.NewGuid().ToString("n") + ".json");
        await File.WriteAllTextAsync(vacio, "[]");
        try
        {
            var psi = new ProcessStartInfo("bash")
            {
                WorkingDirectory = RepoRoot(),
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = System.Text.Encoding.UTF8,
                StandardErrorEncoding = System.Text.Encoding.UTF8,
            };
            psi.ArgumentList.Add(Path.Combine("tools", "provisionar.sh"));
            foreach (var a in extra) psi.ArgumentList.Add(a);
            psi.ArgumentList.Add("--base");
            psi.ArgumentList.Add(url);
            psi.ArgumentList.Add("--manifiesto");
            psi.ArgumentList.Add(vacio.Replace('\\', '/'));
            psi.Environment["SYNERGOS_API_KEY"] = "llave-del-gate";

            using var p = Process.Start(psi)!;
            var salida = p.StandardOutput.ReadToEndAsync();
            var error = p.StandardError.ReadToEndAsync();
            // Con plazo: un guion colgado no puede colgar la suite. `curl` ya lleva el suyo por
            // petición, así que esto sólo salta si algo que no es la red se atasca.
            using var plazo = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            try
            {
                await p.WaitForExitAsync(plazo.Token);
            }
            catch (OperationCanceledException)
            {
                p.Kill(entireProcessTree: true);
                throw;
            }
            return (p.ExitCode, await salida + await error);
        }
        finally
        {
            File.Delete(vacio);
        }
    }

    /// <summary>
    /// Una <c>Api.Notifications</c> de mentira sobre un puerto de verdad.
    /// </summary>
    /// <remarks>
    /// Contesta sólo lo que el guion pregunta: las definiciones de proceso ya están (no son el
    /// sujeto), las plantillas se listan <b>de a una por página</b> y un POST se apunta. No aplica
    /// ninguna regla de la capacidad, y a propósito: esas las prueba la capacidad real en
    /// <c>PlantillaDelAvisoTests</c>. Un doble que las imitara sería un segundo modelo escrito a
    /// mano de las mismas reglas (lo que <c>ArnesDeCapacidades</c> explica que no hay que hacer).
    /// </remarks>
    private sealed class NotificationsDeMentira : IAsyncDisposable
    {
        private readonly WebApplication _app;
        private readonly List<Dictionary<string, string>> _plantillas;

        private NotificationsDeMentira(WebApplication app, List<Dictionary<string, string>> plantillas)
        {
            _app = app;
            _plantillas = plantillas;
        }

        public List<(string Llave, Dictionary<string, string> Cuerpo)> Publicadas { get; } = new();

        public string Url => _app.Urls.Single();

        public static async Task<NotificationsDeMentira> Levantar(params Dictionary<string, string>[] plantillas)
        {
            var builder = WebApplication.CreateSlimBuilder();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Logging.ClearProviders();
            var app = builder.Build();
            var yo = new NotificationsDeMentira(app, plantillas.ToList());

            app.MapGet("/v1/definitions/{clave}", (string clave) => Results.Ok(new { key = clave }));

            app.MapGet("/v1/templates", (int? offset) =>
            {
                var desde = offset ?? 0;
                var fila = yo._plantillas.Skip(desde).Take(1).ToList();
                return Results.Ok(new
                {
                    items = fila,
                    total = yo._plantillas.Count,
                    offset = desde,
                    hasMore = desde + fila.Count < yo._plantillas.Count,
                });
            });

            app.MapPost("/v1/templates", async (HttpRequest peticion) =>
            {
                var leido = await JsonSerializer.DeserializeAsync<Dictionary<string, string>>(peticion.Body);
                var cuerpo = new Dictionary<string, string>(leido!, StringComparer.Ordinal);
                yo.Publicadas.Add((peticion.Headers["Idempotency-Key"].ToString(), cuerpo));
                yo._plantillas.Add(new Dictionary<string, string>(cuerpo, StringComparer.Ordinal) { ["id"] = Guid.NewGuid().ToString("n") });
                return Results.Created("/v1/templates/x", cuerpo);
            });

            await app.StartAsync();
            return yo;
        }

        public async ValueTask DisposeAsync()
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }
}

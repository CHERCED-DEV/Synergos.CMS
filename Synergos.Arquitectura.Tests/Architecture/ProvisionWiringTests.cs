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
    /// La llave de idempotencia con que se REVISA un recurso lleva la huella de su agenda (NP-2 de 008).
    /// </summary>
    /// <remarks>
    /// Es la regla del precio sobre la agenda: <c>ReviseResource</c> consulta el libro de idempotencia antes de nada, así
    /// que con una llave sin la huella, cambiar el horario en el manifiesto contestaría 200 y dejaría el de la primera
    /// revisión para siempre.
    /// </remarks>
    [Fact]
    public void La_llave_de_la_revision_de_un_recurso_lleva_la_huella_de_su_agenda()
    {
        var m = Regex.Match(Guion(), @"/revise""[^\n]*""(provisionar:revisar:[^""]*)""");

        Assert.True(m.Success,
            "No se encontró la llave con la que provisionar.sh revisa un recurso. Si se reescribió esa línea, mové este gate con ella.");
        Assert.True(m.Groups[1].Value.Contains("$huella", StringComparison.Ordinal),
            $"La llave de la revisión es «{m.Groups[1].Value}» y no lleva la huella de la agenda: cambiar el horario en el "
            + "manifiesto contestaría 200 sin cambiar nada.");
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
    /// <c>Bff.Core</c>. <c>--verificar</c> no publica: lo dice, y en rojo. Publicar sí la arregla
    /// desde #179 (la capacidad versiona): ver la prueba de abajo.</para>
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

        // Cada plantilla declarada UNA vez (desde la F3 de la ADR 0140 el fichero declara más de
        // una); la del aviso se mira entera.
        Assert.Equal(Declaradas().Count, notificaciones.Publicadas.Count);
        var (llave, cuerpo) = Assert.Single(notificaciones.Publicadas, p => p.Llave.StartsWith(
            $"provisionar:plantilla:{ClaveDelAviso}:", StringComparison.Ordinal));

        var declarada = Declaradas().Single(d => d["key"] == ClaveDelAviso);
        foreach (var campo in new[] { "key", "channel", "subject", "body" })
        {
            Assert.Equal(declarada[campo], cuerpo[campo]);
        }

        // La segunda corrida pasa por el MISMO veredicto que `--verificar` —encontrarla y
        // compararla—, así que una tercera con `--verificar` costaría segundos y no diría más.
        var segunda = await Correr(notificaciones.Url);
        Assert.True(segunda.Codigo == 0, segunda.Salida);
        Assert.True(notificaciones.Publicadas.Count == Declaradas().Count,
            "La segunda corrida volvió a publicar la plantilla: no la encontró porque estaba en la "
            + "segunda página, y cada corrida dejaría otra publicación de lo mismo."
            + Environment.NewLine + segunda.Salida);
        Assert.Contains($"✓ plantilla {ClaveDelAviso}", segunda.Salida, StringComparison.Ordinal);
    }

    /// <summary>
    /// Una plantilla publicada que difiere de la declarada: <c>--verificar</c> la da en rojo, y
    /// publicar pone la declarada UNA vez —como versión nueva— y la siguiente corrida ya no toca
    /// nada (#179).
    /// </summary>
    /// <remarks>
    /// <para>Antes la siembra la «respetaba y lo decía», porque la capacidad no tenía cómo
    /// reescribirla: una plantilla mal publicada sólo se arreglaba tocando su almacén a mano.
    /// Ahora lo declarado en el repo manda, y la versión vieja queda en la capacidad, consultable
    /// por su id.</para>
    /// </remarks>
    [Fact]
    public async Task Una_plantilla_que_difiere_sale_roja_al_verificar_y_publicar_la_converge_una_vez()
    {
        var declarada = Declaradas().Single(d => d["key"] == ClaveDelAviso);
        await using var notificaciones = await NotificationsDeMentira.Levantar(
            Plantilla(ClaveDelAviso, declarada["subject"] + " (texto viejo)", declarada["body"]));

        var verificar = await Correr(notificaciones.Url, "--verificar");
        Assert.True(verificar.Codigo != 0,
            "provisionar.sh --verificar dio por buena una plantilla que difiere de la declarada." + Environment.NewLine + verificar.Salida);
        Assert.Contains($"FALTA  plantilla {ClaveDelAviso}", verificar.Salida, StringComparison.Ordinal);
        Assert.Empty(notificaciones.Publicadas);

        var primera = await Correr(notificaciones.Url);
        Assert.True(primera.Codigo == 0, primera.Salida);
        Assert.Equal(Declaradas().Count, notificaciones.Publicadas.Count);
        var (_, cuerpo) = Assert.Single(notificaciones.Publicadas, p => p.Llave.StartsWith(
            $"provisionar:plantilla:{ClaveDelAviso}:", StringComparison.Ordinal));
        Assert.Equal(declarada["subject"], cuerpo["subject"]);
        Assert.Contains("versión nueva", primera.Salida, StringComparison.Ordinal);

        var segunda = await Correr(notificaciones.Url);
        Assert.True(segunda.Codigo == 0, segunda.Salida);
        Assert.True(notificaciones.Publicadas.Count == Declaradas().Count,
            "La segunda corrida volvió a publicar: no vio la versión que puso la primera." + Environment.NewLine + segunda.Salida);
        Assert.Contains($"✓ plantilla {ClaveDelAviso}", segunda.Salida, StringComparison.Ordinal);
    }

    /// <summary>
    /// Un recurso que ya está pero sin la agenda que declara el manifiesto —registrado cuando el guion escribía siempre
    /// <c>"opening":[]</c>—: publicar lo REVISA, con la agenda entera y la huella en la llave. Y uno que no está se
    /// registra CON su agenda (NP-2 de 008: el horario de visitas de un inmueble).
    /// </summary>
    /// <remarks>
    /// <para>UNA corrida, y las plantillas ya publicadas y de a todas por página: lo que se mira acá es el recurso, y cada
    /// corrida y cada página de más son intérpretes de Python contra el plazo de dos minutos. Que <c>--verificar</c> lo da
    /// en rojo, y que una segunda corrida no lo revisa otra vez, se comprobó a mano contra una agenda de mentira (bitácora
    /// de 008, 2026-10-22); el veredicto que decide las dos cosas es el del comparador, y ése lo corre la autoprueba.</para>
    /// </remarks>
    [Fact]
    public async Task Publicar_revisa_un_recurso_sin_la_agenda_del_manifiesto_y_registra_uno_nuevo_con_ella()
    {
        await using var capacidades = await NotificationsDeMentira.Levantar(
            Declaradas().Select(d => Plantilla(d["key"], d["subject"], d["body"])).ToArray());
        capacidades.PorPagina = 100;
        capacidades.Recursos.Add(new Dictionary<string, object?>
        {
            ["id"] = "rec-1", ["subjectKind"] = "realty.listado", ["subjectId"] = "listado-x", ["capacity"] = 1,
            ["timeZoneId"] = "America/Bogota", ["opening"] = Array.Empty<object>(), ["cancellationNoticeMinutes"] = 0,
            ["slotMinutes"] = null,
        });
        const string manifiesto = """
            [{"tipo":"recurso","subjectKind":"realty.listado","subjectId":"listado-x","capacity":1,"timeZoneId":"America/Bogota",
              "opening":[{"day":"Monday","from":"09:00","to":"10:00"},{"day":"Monday","from":"11:00","to":"12:00"}],
              "cancellationNoticeMinutes":120,"slotMinutes":60},
             {"tipo":"recurso","subjectKind":"realty.listado","subjectId":"listado-nuevo","capacity":1,"timeZoneId":"America/Bogota",
              "opening":[{"day":"Tuesday","from":"09:00","to":"10:00"}],"slotMinutes":60}]
            """;

        var publicar = await CorrerConManifiesto(capacidades.Url, manifiesto);
        Assert.True(publicar.Codigo == 0, publicar.Salida);
        Assert.Contains("+ puesto recurso realty.listado/listado-x revisado", publicar.Salida, StringComparison.Ordinal);
        Assert.Equal(2, capacidades.EscriturasDeAgenda.Count);
        var (ruta, llave, cuerpo) = capacidades.EscriturasDeAgenda.Single(e => e.Ruta.EndsWith("/revise", StringComparison.Ordinal));
        Assert.Equal("/v1/resources/rec-1/revise", ruta);
        Assert.StartsWith("provisionar:revisar:realty.listado:listado-x:", llave, StringComparison.Ordinal);
        Assert.Equal(1, cuerpo.GetProperty("capacity").GetInt32());
        Assert.Equal(2, cuerpo.GetProperty("opening").GetArrayLength());
        Assert.Equal(120, cuerpo.GetProperty("cancellationNoticeMinutes").GetInt32());
        Assert.Equal(60, cuerpo.GetProperty("slotMinutes").GetInt32());

        var (_, _, nuevo) = capacidades.EscriturasDeAgenda.Single(e => e.Ruta == "/v1/resources");
        Assert.Equal("listado-nuevo", nuevo.GetProperty("subjectId").GetString());
        Assert.Equal("America/Bogota", nuevo.GetProperty("timeZoneId").GetString());
        Assert.Equal("Tuesday", nuevo.GetProperty("opening")[0].GetProperty("day").GetString());
        Assert.Equal(60, nuevo.GetProperty("slotMinutes").GetInt32());
        Assert.Empty(capacidades.Publicadas);
    }

    /// <summary>
    /// Las definiciones del trámite salen del catálogo de procesos (009, NP-1): con la v2 sin publicar —un servidor
    /// provisionado antes, cuando la v1 salía de un heredoc del guion— <c>--verificar</c> sale rojo y la nombra, y publicar
    /// pone la v2 tal como la declara el fichero, con su llave, y nada más.
    /// </summary>
    /// <remarks>
    /// <para>Antes, la v1 vivía en el guion y la v2 sólo en el fichero: un servidor nuevo quedaba con la v1 y
    /// <c>bff-gobierno</c>, que monta el fichero como su catálogo, con la v2. La primera radicación por la puerta habría
    /// salido <c>definition_not_found</c>. Mutación: volver a publicar la v1 desde el guion lo pone en rojo.</para>
    /// </remarks>
    [Fact]
    public async Task Las_definiciones_salen_del_catalogo_y_la_v2_que_falta_se_publica_tal_cual()
    {
        await using var capacidades = await NotificationsDeMentira.Levantar();
        capacidades.Definiciones = new HashSet<string>(StringComparer.Ordinal)
        {
            "gov.tramite", "tracking.shop", "tracking.travel", "tracking.events", "tracking.academy",
        };

        // Sin plantillas (`[]`, que el guion admite para quien trae su propio fichero): lo que se mira son las
        // definiciones, y cada plantilla es un intérprete de Python contra el plazo de dos minutos de cada corrida.
        var sinPlantillas = Path.Combine(Path.GetTempPath(), "provisionar-sin-plantillas-" + Guid.NewGuid().ToString("n") + ".json");
        await File.WriteAllTextAsync(sinPlantillas, "[]");
        try
        {
            var plantillas = new[] { "--plantillas", sinPlantillas.Replace('\\', '/') };

            var verificar = await Correr(capacidades.Url, ["--verificar", .. plantillas]);
            Assert.True(verificar.Codigo != 0,
                "provisionar.sh --verificar salió verde con Api.Workflow SIN la v2 del trámite." + Environment.NewLine + verificar.Salida);
            Assert.Contains("FALTA  definición gov.tramite.v2", verificar.Salida, StringComparison.Ordinal);
            Assert.Matches(@"(?m)^✓ definición gov\.tramite\r?$", verificar.Salida);
            Assert.Empty(capacidades.DefinicionesPublicadas);

            var publicar = await Correr(capacidades.Url, plantillas);
            Assert.True(publicar.Codigo == 0, publicar.Salida);
            var (llave, cuerpo) = Assert.Single(capacidades.DefinicionesPublicadas);
            Assert.Equal("provisionar:def:gov.tramite.v2", llave);
            Assert.True(System.Text.Json.Nodes.JsonNode.DeepEquals(
                    DelCatalogo("gov.tramite.v2"), System.Text.Json.Nodes.JsonNode.Parse(cuerpo.GetRawText())),
                "Lo publicado no es la v2 que declara tools/provisionar.procesos.json: " + cuerpo.GetRawText());
        }
        finally
        {
            File.Delete(sinPlantillas);
        }
    }

    /// <summary>Una definición del catálogo de procesos, con los campos que se publican.</summary>
    private static System.Text.Json.Nodes.JsonObject DelCatalogo(string clave)
    {
        var catalogo = System.Text.Json.Nodes.JsonNode.Parse(
            File.ReadAllText(Path.Combine(RepoRoot(), "tools", "provisionar.procesos.json")))!.AsArray();
        var entrada = catalogo.OfType<System.Text.Json.Nodes.JsonObject>().Single(e => (string?)e["key"] == clave);

        var publicada = new System.Text.Json.Nodes.JsonObject();
        foreach (var campo in new[] { "key", "initialState", "finalStates", "transitions" })
        {
            publicada[campo] = entrada[campo]!.DeepClone();
        }
        return publicada;
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
    private static Task<(int Codigo, string Salida)> Correr(string url, params string[] extra)
        => CorrerConManifiesto(url, "[]", extra);

    /// <summary>
    /// Lo mismo, con el manifiesto de entidades que se le pase. Lleva OTRO nombre a propósito: como
    /// sobrecarga de <see cref="Correr(string, string[])"/>, C# elegía ésta para
    /// <c>Correr(url, "--verificar")</c> —tiene más parámetros declarados— y «--verificar» acababa
    /// escrito como manifiesto: las pruebas de verificar corrían el guion en modo publicar.
    /// </summary>
    private static async Task<(int Codigo, string Salida)> CorrerConManifiesto(string url, string manifiesto, params string[] extra)
    {
        var vacio = Path.Combine(Path.GetTempPath(), "provisionar-vacio-" + Guid.NewGuid().ToString("n") + ".json");
        await File.WriteAllTextAsync(vacio, manifiesto);
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
    /// sujeto), las plantillas se listan <b>de a una por página</b> y un POST se apunta. De la
    /// capacidad imita sólo el contrato de la LISTA —trae la versión vigente de cada clave, así
    /// que un POST sobre una clave publicada la reemplaza en la lista (#179)—, no sus reglas: ésas
    /// las prueba la capacidad real en <c>PlantillaDelAvisoTests</c> y
    /// <c>NotificationServiceTests</c>. Un doble que las imitara sería un segundo modelo escrito a
    /// mano de las mismas reglas (lo que <c>ArnesDeCapacidades</c> explica que no hay que hacer).
    /// De la agenda contesta lo mismo de poco: el recurso por sujeto, y registrar y revisar se
    /// apuntan (NP-2 de 008); las reglas del horario las prueba <c>Api.Booking</c>.
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

        /// <summary>Cuántas plantillas por página contesta la lista: de a una, salvo que el test pida otra cosa.</summary>
        public int PorPagina { get; set; } = 1;

        /// <summary>Los recursos que ya están en la agenda, como los devuelve <c>Api.Booking</c>.</summary>
        public List<Dictionary<string, object?>> Recursos { get; } = new();

        /// <summary>Lo que el guion escribió en la agenda: la ruta, la llave y el cuerpo.</summary>
        public List<(string Ruta, string Llave, JsonElement Cuerpo)> EscriturasDeAgenda { get; } = new();

        /// <summary>Las definiciones de proceso que ya están; <c>null</c> = todas (no son el sujeto de la prueba).</summary>
        public HashSet<string>? Definiciones { get; set; }

        /// <summary>Lo que el guion publicó en <c>POST /v1/definitions</c>: la llave y el cuerpo.</summary>
        public List<(string Llave, JsonElement Cuerpo)> DefinicionesPublicadas { get; } = new();

        public string Url => _app.Urls.Single();

        public static async Task<NotificationsDeMentira> Levantar(params Dictionary<string, string>[] plantillas)
        {
            var builder = WebApplication.CreateSlimBuilder();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Logging.ClearProviders();
            var app = builder.Build();
            var yo = new NotificationsDeMentira(app, plantillas.ToList());

            app.MapGet("/v1/definitions/{clave}", (string clave) =>
                yo.Definiciones is null || yo.Definiciones.Contains(clave) ? Results.Ok(new { key = clave }) : Results.NotFound());

            app.MapPost("/v1/definitions", async (HttpRequest peticion) =>
            {
                var cuerpo = await JsonSerializer.DeserializeAsync<JsonElement>(peticion.Body);
                yo.DefinicionesPublicadas.Add((peticion.Headers["Idempotency-Key"].ToString(), cuerpo));
                yo.Definiciones?.Add(cuerpo.GetProperty("key").GetString()!);
                return Results.Created("/v1/definitions/x", cuerpo);
            });

            app.MapGet("/v1/templates", (int? offset) =>
            {
                var desde = offset ?? 0;
                var fila = yo._plantillas.Skip(desde).Take(yo.PorPagina).ToList();
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
                yo._plantillas.RemoveAll(p => string.Equals(p["key"], cuerpo["key"], StringComparison.OrdinalIgnoreCase));
                yo._plantillas.Add(new Dictionary<string, string>(cuerpo, StringComparer.Ordinal) { ["id"] = Guid.NewGuid().ToString("n") });
                return Results.Created("/v1/templates/x", cuerpo);
            });

            app.MapGet("/v1/resources", (string? subjectKind, string? subjectId) =>
                yo.Recursos.FirstOrDefault(r => Equals(r["subjectKind"], subjectKind) && Equals(r["subjectId"], subjectId)) is { } r
                    ? Results.Ok(r)
                    : Results.NotFound());

            app.MapPost("/v1/resources", async (HttpRequest peticion) =>
            {
                var cuerpo = await JsonSerializer.DeserializeAsync<JsonElement>(peticion.Body);
                yo.EscriturasDeAgenda.Add(("/v1/resources", peticion.Headers["Idempotency-Key"].ToString(), cuerpo));
                return Results.Created("/v1/resources/nuevo", new { id = "nuevo" });
            });

            app.MapPost("/v1/resources/{id}/revise", async (string id, HttpRequest peticion) =>
            {
                var cuerpo = await JsonSerializer.DeserializeAsync<JsonElement>(peticion.Body);
                yo.EscriturasDeAgenda.Add(($"/v1/resources/{id}/revise", peticion.Headers["Idempotency-Key"].ToString(), cuerpo));
                return Results.Ok(new { resource = new { id }, outside = Array.Empty<object>() });
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

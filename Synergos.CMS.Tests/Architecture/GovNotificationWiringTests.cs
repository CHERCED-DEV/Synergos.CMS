namespace Synergos.CMS.Tests.Architecture;

/// <summary>
/// Contra qué se notifica un acto administrativo, y qué NO puede pasar (HU #62).
/// </summary>
/// <remarks>
/// <para>Cuatro cosas que el compilador no ve y que, rotas, <b>no rompen nada visiblemente</b> —
/// que es lo peor que puede pasarle a una notificación:</para>
///
/// <list type="number">
///   <item>que el acto no se pueda leer sin registrar el acceso — si el cuerpo saliera en el
///   listado, el acuse pasaría a ser decoración y el término no empezaría nunca;</item>
///   <item>que un fallo de la capacidad no se vea como «notificado» — la entidad creería que un
///   término empezó y el ciudadano no habría recibido nada;</item>
///   <item>que la afirmación de identidad la escriba la CAPACIDAD y no este lado, que es el
///   defecto #42 aplicado a un registro que sostiene un plazo legal;</item>
///   <item>que el stub siga siendo el default, que es el camino del clon limpio.</item>
/// </list>
///
/// <para><b>Y va por su propio interruptor</b>, no por <c>Synergos:Gob:Mode</c>: notificar y
/// decidir son capacidades distintas (<c>Api.Messaging</c> y <c>Api.Workflow</c>), y un despliegue
/// puede querer una sin la otra. Juntarlas obligaría a encender las dos para probar una.</para>
/// </remarks>
public sealed class GovNotificationWiringTests
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

    /// <summary>El fichero sin comentarios: la prosa explica el código, no lo es.</summary>
    private static string SinComentarios(string ruta)
    {
        Assert.True(File.Exists(ruta), $"No existe {ruta}: revisar este gate.");
        return string.Join('\n', File.ReadAllLines(ruta).Select(l =>
        {
            var t = l.TrimStart();
            if (t.StartsWith("//", StringComparison.Ordinal) || t.StartsWith("*", StringComparison.Ordinal))
            {
                return string.Empty;
            }
            var i = l.IndexOf("//", StringComparison.Ordinal);
            return i >= 0 ? l[..i] : l;
        }));
    }

    private static string Cliente() => SinComentarios(Path.Combine(
        RepoRoot(), "Synergos.CMS.Web", "Services", "HttpGovActNotificationService.cs"));

    private static string Controlador() => SinComentarios(Path.Combine(
        RepoRoot(), "Synergos.CMS.Web", "Controllers", "GovController.cs"));

    private static string Composer() => SinComentarios(Path.Combine(
        RepoRoot(), "Synergos.CMS.Web", "Composers", "SeamComposer.EventsPropertiesGov.cs"));

    /// <summary>
    /// El acto no se lee sin registrar el acceso.
    /// </summary>
    /// <remarks>
    /// <para>Es la HU entera. La bandeja lista qué actos hay —hace falta saber que existen— pero
    /// destaparlos es lo que <c>POST .../open</c> hace, y sólo él. Si el listado trajera el cuerpo,
    /// el ciudadano se enteraría de lo resuelto sin que nada quedara registrado: la entidad no
    /// podría sostener cuándo accedió, que es lo único que esto existe para poder sostener.</para>
    ///
    /// <para>Se mira que el mapeo tenga la puerta (<c>revealBody</c> + <c>n.Opened</c>) y que la
    /// bandeja NO la abra. Un <c>revealBody: true</c> en el listado es exactamente el defecto.</para>
    /// </remarks>
    [Fact]
    public void La_bandeja_no_destapa_el_acto_sin_abrir()
    {
        var controlador = Controlador();

        Assert.Contains("var visible = revealBody || n.Opened;", controlador, StringComparison.Ordinal);
        Assert.Contains("Body: visible ? n.Body : null", controlador, StringComparison.Ordinal);
        Assert.Contains("DocumentRef: visible ? n.DocumentRef : null", controlador, StringComparison.Ordinal);

        var bandeja = controlador.IndexOf("GetForCitizenAsync(actorKey", StringComparison.Ordinal);
        Assert.True(bandeja > 0, "La bandeja ya no lista por member: revisar este gate antes que el código.");

        var trozo = controlador[bandeja..Math.Min(controlador.Length, bandeja + 400)];
        Assert.Contains("revealBody: false", trozo, StringComparison.Ordinal);
        Assert.DoesNotContain("revealBody: true", trozo, StringComparison.Ordinal);
    }

    /// <summary>
    /// Abrir es POST. Un GET lo dispara un prefetch.
    /// </summary>
    /// <remarks>
    /// Abrir un acto ESCRIBE: arranca un término. Con un GET lo arrancaría el prefetch del
    /// navegador, un rastreador o un antivirus que sigue el enlace de un correo — y el ciudadano
    /// perdería días sin haber leído nada. No es purismo REST: es que la acción tiene efecto.
    /// </remarks>
    [Fact]
    public void Abrir_un_acto_no_es_un_GET()
    {
        var controlador = Controlador();

        Assert.Contains("[HttpPost(\"notification/{id}/open\")]", controlador, StringComparison.Ordinal);
        Assert.DoesNotContain("[HttpGet(\"notification/{id}/open\")]", controlador, StringComparison.Ordinal);
    }

    /// <summary>
    /// El destinatario sale del EXPEDIENTE, no del cuerpo de la petición.
    /// </summary>
    /// <remarks>
    /// El radicado es secuencial (<c>SG-2026-000001</c>): cualquier identificador que viaje en la
    /// petición se enumera contando. Es la misma decisión de ADR 0103 para radicar y listar, y el
    /// gate la fija en el tipo — un <c>MemberKey</c> o un correo en <c>NotifyActRequest</c> sería
    /// dejar que el funcionario elija a quién le notifica un acto que no es suyo.
    /// </remarks>
    [Fact]
    public void La_peticion_de_notificar_no_nombra_al_destinatario()
    {
        var peticion = typeof(Synergos.CMS.Web.Controllers.GovController.NotifyActRequest);

        var sospechosas = peticion.GetProperties()
            .Where(p => p.Name.Contains("Member", StringComparison.OrdinalIgnoreCase)
                     || p.Name.Contains("Citizen", StringComparison.OrdinalIgnoreCase)
                     || p.Name.Contains("Email", StringComparison.OrdinalIgnoreCase)
                     || p.PropertyType == typeof(Guid) || p.PropertyType == typeof(Guid?))
            .Select(p => p.Name)
            .ToList();

        Assert.True(sospechosas.Count == 0,
            "Notificar no puede aceptar destinatario en el cuerpo: sale del expediente. "
            + string.Join(", ", sospechosas));

        Assert.Contains("citizenMemberKey: citizenKey", Controlador(), StringComparison.Ordinal);
        Assert.Contains("detail.Citizen.MemberKey is not Guid citizenKey", Controlador(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Con la capacidad caída NO se da por notificado ni por abierto.
    /// </summary>
    /// <remarks>
    /// Es la forma de #27 y de #44: caer al camino local en silencio convertiría una caída en un
    /// término que la entidad cree empezado y el ciudadano nunca vio. Se mira que ningún
    /// <c>catch</c> devuelva una notificación y que el cliente HTTP no instancie el stub.
    /// </remarks>
    [Fact]
    public void El_camino_HTTP_no_cae_al_stub_en_silencio()
    {
        var cliente = Cliente();

        // Nombra la constante del stub para el nombre de la familia del almacén —comparten
        // fichero en disco a propósito— pero no lo INSTANCIA: eso sería el camino de vuelta.
        Assert.DoesNotContain("new StubGovActNotificationService", cliente, StringComparison.Ordinal);

        foreach (var bloque in cliente.Split("catch", StringSplitOptions.None).Skip(1))
        {
            var hasta = bloque.IndexOf("\n        }", StringComparison.Ordinal);
            var cuerpo = hasta > 0 ? bloque[..hasta] : bloque;
            Assert.DoesNotContain("return notificacion", cuerpo, StringComparison.Ordinal);
            Assert.DoesNotContain("return guardada", cuerpo, StringComparison.Ordinal);
        }

        // Y un acuse que la capacidad no devuelve NO se da por bueno.
        Assert.Contains("no devolvió el acuse", cliente, StringComparison.Ordinal);
    }

    /// <summary>
    /// Con qué se afirmó la identidad lo dice la CAPACIDAD, no este lado.
    /// </summary>
    /// <remarks>
    /// <para>Es el defecto #42 aplicado a un registro que sostiene un plazo legal. Este lado
    /// declara <c>CmsSession</c> —lo más débil que se puede afirmar sin prueba— y presenta el
    /// token si el despliegue sabe emitirlo; quien decide si eso vale como <c>IdentityToken</c> es
    /// <c>Api.Messaging</c>, que lo verifica. Escribir acá la afirmación «porque es lo que
    /// mandamos» dejaría el registro mintiendo hacia abajo: diría verificado sin que nadie
    /// verificara nada.</para>
    ///
    /// <para>Por eso lo que se guarda sale de <c>acuse.Assertion</c> y no de una constante.</para>
    /// </remarks>
    [Fact]
    public void La_afirmacion_que_se_guarda_la_devuelve_la_capacidad()
    {
        var cliente = Cliente();

        Assert.Contains("OpenedWith = acuse.Assertion", cliente, StringComparison.Ordinal);
        Assert.DoesNotContain("OpenedWith = GovActAssertions.", cliente, StringComparison.Ordinal);

        // Y el token se PRESENTA, no se declara: la cabecera de identidad viaja cuando la hay.
        Assert.Contains("IdentityHeader", cliente, StringComparison.Ordinal);
        Assert.Contains("_identidad.IssueAsync", cliente, StringComparison.Ordinal);
    }

    /// <summary>
    /// La llave de idempotencia del acto es ESTABLE entre procesos.
    /// </summary>
    /// <remarks>
    /// <c>string.GetHashCode()</c> parece servir y no sirve: .NET lo aleatoriza por proceso, así
    /// que el mismo acto reintentado tras un reinicio traería otra llave y se publicaría dos
    /// veces — dos plazos para el mismo acto, que es justo lo que la idempotencia evita acá.
    /// </remarks>
    [Fact]
    public void La_llave_del_acto_no_depende_del_proceso()
    {
        Assert.DoesNotContain("GetHashCode", Cliente(), StringComparison.Ordinal);

        // Y la huella es la misma en dos llamadas, que es lo que el reintento necesita.
        var a = Synergos.CMS.Web.Services.HttpGovActNotificationService.Huella("Se resuelve NEGAR.");
        var b = Synergos.CMS.Web.Services.HttpGovActNotificationService.Huella("Se resuelve NEGAR.");
        Assert.Equal(a, b);
        Assert.NotEqual(a, Synergos.CMS.Web.Services.HttpGovActNotificationService.Huella("Se resuelve CONCEDER."));
    }

    /// <summary>El stub sigue siendo el default: un clon limpio notifica sin levantar nada.</summary>
    [Fact]
    public void El_stub_sigue_siendo_el_default()
    {
        var composer = Composer();

        var condicion = composer.IndexOf("\"Synergos:Gob:Notifications:Mode\"", StringComparison.Ordinal);
        Assert.True(condicion > 0, "El composer ya no decide el modo de notificación: revisar este gate.");

        var rama = composer[condicion..];
        var otra = rama.IndexOf("else", StringComparison.Ordinal);
        Assert.True(otra > 0, "Sin rama else no hay camino por defecto: el clon limpio se quedaría sin seam.");

        Assert.Contains("HttpGovActNotificationService(", rama[..otra], StringComparison.Ordinal);
        Assert.Contains("StubGovActNotificationService(", rama[otra..], StringComparison.Ordinal);
        Assert.Contains("\"Api\"", rama[..otra], StringComparison.Ordinal);

        var settings = File.ReadAllText(Path.Combine(
            RepoRoot(), "Synergos.CMS.Application", "Configuration", "GovNotificationSettings.cs"));
        Assert.Contains("Mode { get; init; } = \"Local\"", settings, StringComparison.Ordinal);
    }

    /// <summary>
    /// Su propio interruptor, separado del de decidir.
    /// </summary>
    /// <remarks>
    /// Notificar va contra <c>Api.Messaging</c> y decidir contra <c>Api.Workflow</c>: son dos
    /// capacidades. Colgar las dos de <c>Synergos:Gob:Mode</c> obligaría a levantar las dos para
    /// probar una, y a apagar la notificación el día que hubiera que apagar el motor de trámites.
    /// </remarks>
    [Fact]
    public void Notificar_y_decidir_no_comparten_interruptor()
    {
        var composer = Composer();

        var notificacion = composer.IndexOf(
            "IGovActNotificationService>(sp => new HttpGovActNotificationService", StringComparison.Ordinal);
        Assert.True(notificacion > 0, "Ya no se registra el cliente HTTP de notificación: revisar este gate.");

        var desde = composer.LastIndexOf("if (string.Equals(builder.Config[", notificacion, StringComparison.Ordinal);
        Assert.True(desde > 0);
        var guardia = composer[desde..notificacion];

        Assert.Contains("Synergos:Gob:Notifications:Mode", guardia, StringComparison.Ordinal);
        Assert.DoesNotContain("\"Synergos:Gob:Mode\"", guardia, StringComparison.Ordinal);
    }

    /// <summary>La sección se ENLAZA, o los <c>Kind</c> se quedan en su default en silencio.</summary>
    /// <remarks>
    /// Sin <c>Configure&lt;GovNotificationSettings&gt;</c> el cliente recibe uno recién construido:
    /// lo que no viaja por el <c>HttpClient</c> —el <c>Kind</c> de la entidad y el del ciudadano—
    /// se queda en su valor por defecto sin que nada avise, y los hilos quedarían abiertos a nombre
    /// de participantes que el despliegue no eligió. Es el olvido que arrastraron #24, #25 y #36.
    /// </remarks>
    [Fact]
    public void La_seccion_de_notificaciones_se_enlaza()
    {
        Assert.Contains("Configure<GovNotificationSettings>(", Composer(), StringComparison.Ordinal);
        Assert.Contains("\"Synergos:Gob:Notifications\"", Composer(), StringComparison.Ordinal);
    }
}

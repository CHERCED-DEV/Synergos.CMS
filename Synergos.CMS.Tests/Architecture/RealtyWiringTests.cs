namespace Synergos.CMS.Tests.Architecture;

/// <summary>
/// La visita al inmueble contra <c>Api.Booking</c>, como invariante ejecutable (HU #33a).
/// </summary>
/// <remarks>
/// <para><b>Este vertical es el primero que le habla a una capacidad de frente</b>, y por eso
/// tiene más que vigilar que los dos anteriores. Tienda (#24) y Salud (#25) van contra su
/// orquestador porque su flujo toca varias capacidades. Acá no: una visita no se cobra, así que
/// no hay orden que respetar ni nada que deshacer. Un BFF sería una saga de un paso.</para>
///
/// <para><b>Lo delicado es que esa excepción no se ensanche.</b> `SaludWiringTests` prohíbe que
/// el CMS llame a <c>Api.Booking</c> de frente y exime exactamente a este cliente; si el cliente
/// creciera un cobro o un aviso, la exención pasaría a tapar justo lo que aquel gate existe para
/// impedir. De ahí el primer test de abajo.</para>
/// </remarks>
public sealed class RealtyWiringTests
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

    /// <summary>El mismo fichero, <b>sin comentarios</b>.</summary>
    /// <remarks>
    /// <para><b>Hace falta, y lo demostró una mutación.</b> Estos ficheros explican en prosa lo que
    /// hacen, así que un gate que busque <c>subjectKind</c> sobre el texto crudo lo encuentra en el
    /// <c>&lt;remarks&gt;</c> aunque el código haya dejado de usarlo. Se cableó el recurso por
    /// convención y el gate siguió en VERDE: estaba leyendo la explicación, no la implementación.
    /// Es el mismo error que ya se cometió con el registro del barrido (HU #29).</para>
    /// </remarks>
    private static string CodigoDelCliente()
        => string.Join('\n', File.ReadAllLines(Path.Combine(
                RepoRoot(), "Synergos.CMS.Web", "Services", "HttpVisitSchedulingService.cs"))
            .Select(l =>
            {
                var t = l.TrimStart();
                if (t.StartsWith("//", StringComparison.Ordinal)
                    || t.StartsWith("///", StringComparison.Ordinal)
                    || t.StartsWith("*", StringComparison.Ordinal))
                {
                    return string.Empty;
                }
                var i = l.IndexOf("//", StringComparison.Ordinal);
                return i >= 0 ? l[..i] : l;
            }));

    private static string Composer()
        => File.ReadAllText(Path.Combine(
            RepoRoot(), "Synergos.CMS.Web", "Composers", "SeamComposer.EventsPropertiesGov.cs"));

    [Fact]
    public void El_cliente_directo_toca_UNA_SOLA_capacidad()
    {
        // ES LA CONDICIÓN DE LA EXENCIÓN, no un detalle. Hablarle a una capacidad sin orquestador
        // está bien mientras no haya un segundo paso que pueda fallar dejando el primero hecho.
        // En cuanto aparezca —cobrar la visita, mandarle el recordatorio— hay algo que deshacer, y
        // eso es un BFF.
        //
        // Se buscan las rutas de las OTRAS capacidades, no sus nombres: `Api.Payments` en un
        // comentario es prosa, `v1/payments` en una petición es una segunda capacidad.
        var ajenas = new[]
        {
            "v1/payments", "v1/deliveries", "v1/orders", "v1/carts", "v1/items",
            "v1/shipments", "v1/grants", "v1/quotes", "v1/documents", "v1/threads",
        };

        var codigo = CodigoDelCliente();
        var encontradas = ajenas.Where(r => codigo.Contains(r, StringComparison.OrdinalIgnoreCase)).ToList();

        Assert.True(encontradas.Count == 0,
            "El cliente directo de Realty toca más de una capacidad (" + string.Join(", ", encontradas)
            + "). Eso ya es un flujo con algo que deshacer: va por un orquestador, no por acá. "
            + "Y mientras esté así, la exención de SaludWiringTests tapa justo lo que aquel gate "
            + "existe para impedir.");
    }

    [Fact]
    public void El_stub_sigue_siendo_el_default()
    {
        // El camino de un clon limpio: sin capacidades levantadas, el portal inmobiliario
        // funciona. Cambiar el default convertiría «no configurado» en «roto».
        var composer = Composer();
        var settings = File.ReadAllText(Path.Combine(
            RepoRoot(), "Synergos.CMS.Application", "Configuration", "RealtySettings.cs"));

        Assert.Contains("StubVisitSchedulingService", composer, StringComparison.Ordinal);
        Assert.Contains("\"Api\"", composer, StringComparison.Ordinal);
        Assert.Contains("Mode { get; init; } = \"Stub\"", settings, StringComparison.Ordinal);
    }

    [Fact]
    public void El_identificador_del_recurso_NO_se_adivina()
    {
        // La lección de la HU #25, que costó una vuelta entera: el id del recurso lo GENERA
        // Api.Booking, así que ninguna convención del CMS puede acertarlo. Se resuelve
        // preguntando por el sujeto.
        //
        // SIN COMENTARIOS: la explicación del fichero cita la ruta, así que sobre el texto crudo
        // este gate pasaba en verde con el recurso cableado a mano.
        var codigo = CodigoDelCliente();

        Assert.Contains("subjectKind", codigo, StringComparison.Ordinal);
        Assert.Contains("subjectId", codigo, StringComparison.Ordinal);
        Assert.DoesNotContain("ResourceIdPrefix", codigo, StringComparison.Ordinal);
    }

    [Fact]
    public void A_la_capacidad_NO_le_viaja_el_correo_del_interesado()
    {
        // Api.Booking cuenta cupo; no necesita saber quién es. Mandarle direcciones de correo las
        // esparce sin ninguna ganancia — y las copias de la HU #31 ya llevan bastantes datos
        // personales. Lo que viaja es un seudónimo estable.
        var codigo = CodigoDelCliente();

        Assert.Contains("Seudonimo(contacto.Email)", codigo, StringComparison.Ordinal);
        Assert.DoesNotContain("heldForId = contacto.Email", codigo, StringComparison.Ordinal);
    }

    [Fact]
    public void Cableado_NO_hay_dos_relojes_sobre_el_mismo_hold()
    {
        // El desajuste (c) de la HU #33. El barrido del CMS marca Expired sus PROPIAS reservas;
        // Api.Booking vence las suyas sola y de forma perezosa. Si el cliente cableado creara
        // además una reserva del lado del CMS, habría dos relojes sobre lo mismo y ganaría el que
        // corriera antes.
        var codigo = CodigoDelCliente();

        Assert.DoesNotContain("IReservationService", codigo, StringComparison.Ordinal);
        Assert.DoesNotContain("ExpireStaleHolds", codigo, StringComparison.Ordinal);
    }

    [Fact]
    public void La_agenda_la_sigue_derivando_el_CMS()
    {
        // El reparto que la HU #33 tenía sin resolver: qué franjas EXISTEN es del negocio
        // inmobiliario; si una sigue libre es de la capacidad. Que el cliente derive la agenda con
        // el mismo tipo que el stub es lo que garantiza que los dos modos ofrezcan lo mismo.
        var codigo = CodigoDelCliente();

        Assert.Contains("VisitAgenda.For", codigo, StringComparison.Ordinal);
        Assert.Contains("VisitAgenda.Find", codigo, StringComparison.Ordinal);

        var stub = File.ReadAllText(Path.Combine(
            RepoRoot(), "Synergos.CMS.Application", "Services", "Impl", "StubVisitSchedulingService.cs"));
        Assert.Contains("VisitAgenda.For", stub, StringComparison.Ordinal);
    }

    [Fact]
    public void La_seccion_de_configuracion_se_ENLAZA()
    {
        // Sin el Configure<>, el cliente recibe un RealtySettings recién construido y todo lo que
        // no viaja por el HttpClient se queda en su default EN SILENCIO: configurarlo no haría
        // nada y nadie sabría por qué. Es el olvido que arrastraban Tienda y Salud desde las
        // HU #24 y #25, corregido en el mismo commit.
        foreach (var (fichero, seccion) in new[]
        {
            ("SeamComposer.EventsPropertiesGov.cs", "RealtySettings"),
            ("SeamComposer.PlatformAndHealthcare.cs", "SaludSettings"),
            ("SeamComposer.Shop.cs", "TiendaSettings"),
        })
        {
            var composer = File.ReadAllText(Path.Combine(RepoRoot(), "Synergos.CMS.Web", "Composers", fichero));

            Assert.True(composer.Contains($"Configure<{seccion}>", StringComparison.Ordinal),
                $"{fichero} no enlaza {seccion}: lo que se configure ahí se ignora en silencio.");
        }
    }
}

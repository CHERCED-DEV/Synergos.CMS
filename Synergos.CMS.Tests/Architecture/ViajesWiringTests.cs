namespace Synergos.CMS.Tests.Architecture;

/// <summary>
/// La reserva de hotel se lleva contra el ORQUESTADOR, y solo la vía hotel (HU #36).
/// </summary>
/// <remarks>
/// <para><b>Por qué acá hace falta un orquestador.</b> Apartar, cobrar y confirmar pueden fallar
/// a la mitad: si el cobro no sale hay que soltar el cupo, y si la confirmación falla después de
/// capturar hay que devolver la plata. Llamando a <c>Api.Booking</c> y <c>Api.Payments</c> por
/// separado, el CMS estaría reimplementando la máquina de sagas — y peor, porque no tiene dónde
/// anotar una compensación pendiente.</para>
///
/// <para><b>Y por qué SOLO la vía hotel.</b> Lo era porque el carrito multi-producto no llevaba
/// fechas —ni el seam, ni el DTO HTTP, ni el motor en proceso— y un apartado de
/// <c>Api.Booking</c> ES una ventana sobre un recurso: cablearlo exigía inventárselas, que es el
/// error que costó una vuelta en la HU #25. <b>La HU #40 se las puso</b>, así que ese motivo ya
/// no está en pie.</para>
///
/// <para>Lo que queda en pie es otro, y es el que sostiene la frontera hoy: <b>no existe la
/// evidencia con procesos vivos</b> de que tres ítems en fechas distintas aparten tres ventanas
/// distintas y las tres vuelvan con el cobro caído a la mitad. Ver
/// <c>El_carrito_lleva_periodo_y_el_cableado_sigue_pendiente</c>, que es donde el cambio de
/// motivo está escrito con detalle — y que ahora exige que el periodo sea OBLIGATORIO, porque
/// un <c>Start</c> opcional deja compilar a un cliente que fallará más tarde y más lejos.</para>
/// </remarks>
public sealed class ViajesWiringTests
{
    /// <summary>Las capacidades que la reserva usa POR DEBAJO y que el CMS no llama de frente.</summary>
    private static readonly string[] Prohibidas = { "v1/payments", "v1/quotes", "v1/holds", "v1/resources" };

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

    private static string Cliente()
        => SinComentarios(Path.Combine(RepoRoot(), "Synergos.CMS.Web", "Services", "HttpHotelBookingService.cs"));

    private static string Composer()
        => SinComentarios(Path.Combine(RepoRoot(), "Synergos.CMS.Web", "Composers", "SeamComposer.TravelAndBooking.cs"));

    [Fact]
    public void El_cliente_habla_SOLO_con_el_orquestador()
    {
        var codigo = Cliente();
        Assert.True(codigo.Length > 2000, "El cliente es sospechosamente corto: revisar este gate.");

        foreach (var ruta in Prohibidas)
        {
            Assert.False(codigo.Contains(ruta, StringComparison.Ordinal),
                $"El cliente de Viajes llama a '{ruta}' de frente. Reservar puede fallar a la "
                + "mitad y el CMS no tiene dónde anotar una compensación pendiente: va contra "
                + "Bff.Viajes o no va.");
        }

        Assert.Contains("v1/trips", codigo, StringComparison.Ordinal);
    }

    /// <summary>
    /// Los sustantivos de hotel se quedan de este lado: allá va un producto opaco.
    /// </summary>
    /// <remarks>
    /// <c>RoomTypeCode</c>, <c>RatePlanCode</c> y <c>GuestName</c> no significan nada en ninguna
    /// capacidad —lo dice la propia HU #36— y meterlos en el cuerpo los haría viajar hasta
    /// <c>Api.Booking</c>, que dejaría de servir al siguiente dominio.
    /// </remarks>
    [Fact]
    public void El_CMS_anota_los_sustantivos_de_hotel_de_SU_lado()
    {
        var codigo = Cliente();

        // Guarda lo que el orquestador no puede guardar…
        Assert.Contains("_store.WriteAsync(", codigo, StringComparison.Ordinal);
        Assert.Contains("RoomTypeCode:", codigo, StringComparison.Ordinal);

        // …y lo hace AL APARTAR, no al cobrar: entre las dos cosas puede caerse el proceso, y una
        // habitación apartada cuyo huésped se perdió no se reconstruye desde ningún lado.
        var apartar = codigo.IndexOf("public async Task<Reservation> HoldAsync", StringComparison.Ordinal);
        var cobrar = codigo.IndexOf("public async Task<HotelPaymentResult?> PayAsync", StringComparison.Ordinal);
        Assert.True(apartar >= 0 && cobrar > apartar, "Cambió la forma del cliente: revisar este gate.");
        Assert.Contains("GuardarAsync(", codigo[apartar..cobrar], StringComparison.Ordinal);
    }

    /// <summary>La penalidad se calcula acá: es política comercial, no regla de plataforma.</summary>
    [Fact]
    public void La_penalidad_la_pone_el_CMS_y_viaja_ya_calculada()
    {
        var codigo = Cliente();

        Assert.Contains("_cancellationPolicy.Evaluate(", codigo, StringComparison.Ordinal);
        Assert.Contains("retain", codigo, StringComparison.Ordinal);
    }

    /// <summary>El stub sigue siendo el default: un clon limpio reserva sin levantar nada.</summary>
    [Fact]
    public void El_default_es_el_motor_en_proceso()
    {
        var composer = Composer();

        Assert.Contains("\"Synergos:Viajes:Mode\"", composer, StringComparison.Ordinal);

        var condicion = composer.IndexOf("\"Synergos:Viajes:Mode\"", StringComparison.Ordinal);
        var elseAt = composer.IndexOf("else", condicion, StringComparison.Ordinal);
        Assert.True(elseAt > 0, "El cableado de Viajes no tiene rama por defecto.");

        var rama = composer[condicion..elseAt];
        Assert.Contains("HttpHotelBookingService", rama, StringComparison.Ordinal);
        Assert.DoesNotContain("StubHotelBookingService", rama, StringComparison.Ordinal);

        Assert.Equal("Stub", new Synergos.CMS.Application.Configuration.ViajesSettings().Mode);
    }

    /// <summary>
    /// El contrato del carrito YA lleva periodo, y el cableado sigue pendiente a conciencia.
    /// </summary>
    /// <remarks>
    /// <para>Esta línea era, hasta la HU #40, «el contrato sigue sin fechas». Se cayó sola al
    /// añadirlas —que es justo para lo que estaba puesta— y se REESCRIBE en vez de borrarse,
    /// porque la frontera que vigilaba no desapareció: se movió.</para>
    ///
    /// <para><b>Lo que ahora se exige.</b> Que el periodo esté en el contrato y sea OBLIGATORIO.
    /// Con <c>Start</c>/<c>End</c> opcionales un cliente viejo compila y su carrito falla más
    /// tarde y más lejos, ya contra <c>Api.Booking</c>. El gate impide esa regresión, que es la
    /// que parece más suave y es más traicionera.</para>
    ///
    /// <para><b>Y lo que sigue prohibido.</b> Cablear el carrito a <c>Bff.Viajes</c> sin haber
    /// verificado con procesos vivos que tres ítems en fechas distintas apartan tres ventanas
    /// distintas, y que con el cobro caído a mitad las tres vuelven. El contrato ya lo permite;
    /// la evidencia todavía no existe. Cuando exista, esta línea se borra a conciencia.</para>
    /// </remarks>
    [Fact]
    public void El_carrito_lleva_periodo_y_el_cableado_sigue_pendiente()
    {
        var composer = Composer();
        var condicion = composer.IndexOf("\"Synergos:Viajes:Mode\"", StringComparison.Ordinal);
        var elseAt = composer.IndexOf("else", condicion, StringComparison.Ordinal);
        var rama = composer[condicion..elseAt];

        Assert.DoesNotContain("ITravelCartService", rama, StringComparison.Ordinal);

        var item = SinComentarios(Path.Combine(
            RepoRoot(), "Synergos.CMS.Interfaces", "ITravelCartService.cs"));
        var declaracion = item[item.IndexOf("record TravelCartItem(", StringComparison.Ordinal)..];
        declaracion = declaracion[..declaracion.IndexOf(");", StringComparison.Ordinal)];

        // El periodo está, y con hora: un vuelo no es una noche.
        Assert.Contains("DateTimeOffset Start", declaracion, StringComparison.Ordinal);
        Assert.Contains("DateTimeOffset End", declaracion, StringComparison.Ordinal);

        // Y no es opcional: `DateTimeOffset?` dejaría pasar el carrito sin fechas.
        Assert.DoesNotContain("DateTimeOffset? Start", declaracion, StringComparison.Ordinal);
        Assert.DoesNotContain("DateTimeOffset? End", declaracion, StringComparison.Ordinal);
    }

    /// <summary>La sección se ENLAZA, o el Kind del viajero se queda en su default en silencio.</summary>
    [Fact]
    public void La_seccion_de_configuracion_se_ENLAZA()
    {
        Assert.Contains(
            "services.Configure<ViajesSettings>(builder.Config.GetSection(\"Synergos:Viajes\"))",
            Composer(), StringComparison.Ordinal);
    }
}

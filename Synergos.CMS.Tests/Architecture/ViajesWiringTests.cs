namespace Synergos.CMS.Tests.Architecture;

/// <summary>
/// Viajes se lleva contra el ORQUESTADOR — las DOS vías, hotel y carrito (HU #36 + #40).
/// </summary>
/// <remarks>
/// <para><b>Por qué acá hace falta un orquestador.</b> Apartar, cobrar y confirmar pueden fallar
/// a la mitad: si el cobro no sale hay que soltar el cupo, y si la confirmación falla después de
/// capturar hay que devolver la plata. Llamando a <c>Api.Booking</c> y <c>Api.Payments</c> por
/// separado, el CMS estaría reimplementando la máquina de sagas — y peor, porque no tiene dónde
/// anotar una compensación pendiente.</para>
///
/// <para><b>Este párrafo decía «y por qué SOLO la vía hotel», y ya no es cierto.</b> Lo era
/// porque el carrito multi-producto no llevaba fechas —ni el seam, ni el DTO HTTP, ni el motor
/// en proceso— y un apartado de <c>Api.Booking</c> ES una ventana sobre un recurso: cablearlo
/// exigía inventárselas, que es el error que costó una vuelta en la HU #25. La HU #40 lo tumbó
/// en tres rebanadas —el periodo obligatorio, el modo de fallo que elige quien vende, y el motor
/// aparte— y la tercera cableó el carrito. <b>Hoy las dos vías van contra el orquestador.</b></para>
///
/// <para><b>Y por eso mismo la frontera cambió de sitio, no desapareció.</b> Lo que este fichero
/// vigila ahora no es «cuál de las dos vías puede cablearse» sino <b>por dónde cruza cada una</b>:
/// el carrito pasa por el <b>motor</b> y no por el servicio entero, porque sustituir un
/// <c>ITravelCartService</c> completo obligaría a reimplementar el expediente —viajero, código de
/// confirmación, timeline, rastro de la cancelación— del lado del orquestador, y la segunda copia
/// divergiría. El detalle está en
/// <c>El_carrito_cruza_por_el_motor_y_el_periodo_sigue_siendo_obligatorio</c>.</para>
///
/// <para><b>Nota para quien venga después</b>, porque esta prosa ya se quedó atrás tres veces
/// seguidas mientras las tres rebanadas entraban: <c>git</c> la mezcla limpia siempre —nadie más
/// la toca— así que nada avisa cuando deja de ser verdad. Si estás cambiando un test de aquí
/// abajo, releé esto antes de irte.</para>
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

    private static string Carrito()
        => SinComentarios(Path.Combine(RepoRoot(), "Synergos.CMS.Web", "Services", "HttpTravelCartEngine.cs"));

    private static string Cable()
        => SinComentarios(Path.Combine(RepoRoot(), "Synergos.CMS.Web", "Services", "ViajesWire.cs"));

    private static string Composer()
        => SinComentarios(Path.Combine(RepoRoot(), "Synergos.CMS.Web", "Composers", "SeamComposer.TravelAndBooking.cs"));

    [Fact]
    public void El_cliente_habla_SOLO_con_el_orquestador()
    {
        // Las DOS vías: la reserva de hotel y el carrito multi-producto (#40). Y el cable que
        // comparten, que es por donde se colaría una llamada suelta sin que ninguno de los dos
        // clientes la nombrara.
        foreach (var (que, codigo) in new[]
                 {
                     ("la vía hotel", Cliente()), ("el carrito", Carrito()), ("el cable", Cable()),
                 })
        {
            Assert.True(codigo.Length > 2000, $"{que} es sospechosamente corto: revisar este gate.");

            foreach (var ruta in Prohibidas)
            {
                Assert.False(codigo.Contains(ruta, StringComparison.Ordinal),
                    $"En Viajes, {que} llama a '{ruta}' de frente. Reservar puede fallar a la "
                    + "mitad y el CMS no tiene dónde anotar una compensación pendiente: va contra "
                    + "Bff.Viajes o no va.");
            }
        }

        Assert.Contains("v1/trips", Cliente(), StringComparison.Ordinal);
        Assert.Contains("v1/trips", Carrito(), StringComparison.Ordinal);
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

        // Y el carrito igual: el motor en proceso es el camino del clon limpio.
        Assert.DoesNotContain("InProcessTravelCartEngine", rama, StringComparison.Ordinal);
        Assert.Contains("InProcessTravelCartEngine", composer[elseAt..], StringComparison.Ordinal);

        Assert.Equal("Stub", new Synergos.CMS.Application.Configuration.ViajesSettings().Mode);
    }

    /// <summary>
    /// El carrito multi-producto también cruza, y el periodo sigue siendo obligatorio.
    /// </summary>
    /// <remarks>
    /// <para>Esta línea decía, hasta la HU #40, «el cableado sigue pendiente», y antes de eso «el
    /// contrato sigue sin fechas». Se REESCRIBE por segunda vez en vez de borrarse, porque la
    /// frontera que vigila no desapareció: se movió otra vez. Lo que exigía —que el periodo
    /// estuviera y fuera obligatorio— se conserva íntegro: <b>es la condición que hizo posible el
    /// cableado</b>, y volverlo opcional dejaría compilar un carrito sin fechas que falla más
    /// tarde y más lejos, ya contra <c>Api.Booking</c>.</para>
    ///
    /// <para><b>Y lo que ahora se exige además:</b> que el carrito cruce por el MOTOR y no por el
    /// servicio entero. Registrar un <c>ITravelCartService</c> distinto en la rama <c>Bff</c>
    /// obligaría a reimplementar allá el expediente —el viajero, el código de confirmación, el
    /// timeline, el rastro de la cancelación— y la segunda copia divergiría de la primera.</para>
    /// </remarks>
    [Fact]
    public void El_carrito_cruza_por_el_motor_y_el_periodo_sigue_siendo_obligatorio()
    {
        var composer = Composer();
        var condicion = composer.IndexOf("\"Synergos:Viajes:Mode\"", StringComparison.Ordinal);
        var elseAt = composer.IndexOf("else", condicion, StringComparison.Ordinal);
        var rama = composer[condicion..elseAt];

        Assert.Contains("HttpTravelCartEngine", rama, StringComparison.Ordinal);

        // Por el motor, no por el servicio: el expediente es uno solo para los dos caminos.
        Assert.DoesNotContain("ITravelCartService>", rama, StringComparison.Ordinal);

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

    /// <summary>
    /// El carrito pide confirmación PARCIAL, y devuelve lo que no se cumplió.
    /// </summary>
    /// <remarks>
    /// <para><b>Las dos mitades de la misma decisión, y ninguna sirve sola</b> (#40). Pedir
    /// parcial sin ordenar la devolución sería peor que no pedirla: el ítem caído se suelta, el
    /// comprador se queda sin él <i>y</i> sin su plata — y nada falla, que es lo que lo hace
    /// difícil de ver. Ordenar la devolución sin pedir parcial no compila nunca ese camino.</para>
    ///
    /// <para><b>Y el monto lo pone el CMS</b>: el orquestador cotiza el viaje entero de una vez,
    /// a propósito, así que no sabe cuánto vale el ítem caído. Es la misma forma que la penalidad
    /// de cancelar — llega calculada de quien conoce el precio.</para>
    /// </remarks>
    [Fact]
    public void El_carrito_pide_PARCIAL_y_devuelve_lo_no_cumplido()
    {
        var carrito = Carrito();

        Assert.Contains("partialConfirm = true", carrito, StringComparison.Ordinal);

        // Se mira el USO y no la declaración: buscar «/refund» a secas pasa en verde con el
        // método escrito y nadie llamándolo. Es la lección que costó una mutación en la rebanada
        // anterior — un gate que encuentra su propia declaración no vigila nada.
        Assert.Contains("await DevolverAsync(viajeId", carrito, StringComparison.Ordinal);
        Assert.Contains("/refund", carrito, StringComparison.Ordinal);

        // El monto sale de los precios que guarda el CMS, no de la respuesta del orquestador.
        Assert.Contains("noCumplido += line.Price", carrito, StringComparison.Ordinal);

        // Y va con su moneda: un monto sin moneda es el defecto que Money existe para impedir.
        Assert.Contains("currency = ", carrito, StringComparison.Ordinal);
    }

    /// <summary>
    /// El expediente del carrito NO se lo lleva el orquestador.
    /// </summary>
    /// <remarks>
    /// El orquestador no guarda quién viaja ni cómo se llama lo que compró —lección de #35 y del
    /// defecto #47: lo que se escribe en el disco de otro servicio hay que decidirlo, no dejarlo
    /// pasar—. Así que el viajero cruza SEUDONIMIZADO y el resto del expediente se queda acá.
    /// </remarks>
    [Fact]
    public void El_viajero_cruza_seudonimizado()
    {
        var carrito = Carrito();

        Assert.Contains("ViajesWire.TravellerId(guest.Email)", carrito, StringComparison.Ordinal);
        Assert.DoesNotContain("travellerId = guest.Email", carrito, StringComparison.Ordinal);

        // Y el pseudónimo es un hash, no el correo recortado.
        Assert.Contains("SHA256.HashData", Cable(), StringComparison.Ordinal);
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

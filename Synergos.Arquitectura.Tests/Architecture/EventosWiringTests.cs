namespace Synergos.CMS.Tests.Architecture;

/// <summary>
/// Las entradas se compran contra el ORQUESTADOR, nunca contra las capacidades sueltas (HU #35).
/// </summary>
/// <remarks>
/// <para><b>Por qué acá sí hace falta un orquestador y en la visita al inmueble no</b> (#33a).
/// La pregunta no es cuántas capacidades toca: es si hay algo que deshacer cuando el segundo
/// paso falla. Una visita no se cobra, así que apartar el cupo es todo. Una entrada sí: si el
/// cobro falla hay que soltar el aforo, y si el consumo falla DESPUÉS de capturar hay que
/// devolver la plata. Cablear <c>Api.Inventory</c> + <c>Api.Payments</c> por separado desde el
/// CMS es el atajo natural —son dos llamadas obvias y cada una funciona—, y lo que no se ve al
/// escribirlas es que el CMS <b>no tiene dónde anotar una compensación pendiente</b>.</para>
///
/// <para>Y hay un detalle que solo se ve habiéndolo sufrido: <b>la compensación cambia de
/// carácter</b>. Antes de capturar, deshacer el pago es «liberar»; después es «devolver». Antes
/// de consumir, soltar el aforo es «liberar el apartado»; después es «ajustar el pozo», porque
/// el apartado ya no existe. Eso está resuelto en <c>Bff.Eventos</c> y reimplementarlo saldría
/// mal.</para>
///
/// <para><b>Y la segunda mitad, que es propia de este vertical:</b> el artefacto NO viaja. El
/// firmante del QR vive del lado del contenido, así que emitir, transferir y escanear no pueden
/// depender del orquestador — quien ya pagó no se queda fuera del recinto porque un servicio
/// esté caído.</para>
/// </remarks>
public sealed class EventosWiringTests
{
    /// <summary>Las capacidades que la compra usa POR DEBAJO y que el CMS no llama de frente.</summary>
    private static readonly string[] Prohibidas =
    {
        "v1/payments", "v1/quotes", "v1/items", "v1/holds",
    };

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

    private static string CodigoDelCliente()
        => SinComentarios(Path.Combine(
            RepoRoot(), "Synergos.CMS.Web", "Services", "HttpEventTicketingService.cs"));

    /// <summary>El fichero sin comentarios: la prosa explica el código, no lo es.</summary>
    private static string SinComentarios(string ruta)
        => string.Join('\n', File.ReadAllLines(ruta).Select(l =>
        {
            var t = l.TrimStart();
            if (t.StartsWith("//", StringComparison.Ordinal) || t.StartsWith('*'))
            {
                return string.Empty;
            }
            var i = l.IndexOf("//", StringComparison.Ordinal);
            return i >= 0 ? l[..i] : l;
        }));

    private static string Composer()
        => SinComentarios(Path.Combine(
            RepoRoot(), "Synergos.CMS.Web", "Composers", "SeamComposer.EventsPropertiesGov.cs"));

    [Fact]
    public void El_cliente_habla_SOLO_con_el_orquestador()
    {
        var codigo = CodigoDelCliente();

        foreach (var ruta in Prohibidas)
        {
            Assert.False(codigo.Contains(ruta, StringComparison.Ordinal),
                $"El cliente de Eventos llama a '{ruta}' de frente. Comprar una entrada puede "
                + "fallar a la mitad y el CMS no tiene dónde anotar una compensación pendiente: "
                + "va contra Bff.Eventos o no va.");
        }

        Assert.Contains("v1/ticket-purchases", codigo, StringComparison.Ordinal);
    }

    /// <summary>Lo confirmado no depende del orquestador: ni «mis entradas», ni transferir, ni volver a pedirlas.</summary>
    /// <remarks>
    /// <para>Es lo que hace que un BFF caído no deje a nadie fuera de un concierto que ya pagó. Si
    /// «mis entradas» o transferir empezaran a salir a la red, esa propiedad se pierde sin que nada
    /// más cambie de aspecto.</para>
    ///
    /// <para><b>Reescrito en la ADR 0140 F3</b>: con la puerta, el artefacto SÍ habla con el orquestador
    /// —para leer una compra que no se confirmó todavía— y la regla pasa a ser «lo confirmado no toca la
    /// red»: el artefacto busca lo emitido en el registro ANTES de leer la compra, y sólo reconcilia lo
    /// pendiente. Lo prueba con un contador de llamadas <c>EventosArtefactoTests</c>.</para>
    /// </remarks>
    [Fact]
    public void Lo_confirmado_no_toca_la_red()
    {
        var codigo = CodigoDelCliente();

        foreach (var metodo in new[] { "GetTicketsAsync", "TransferTicketAsync" })
        {
            var at = codigo.IndexOf(metodo, StringComparison.Ordinal);
            Assert.True(at >= 0, $"Falta {metodo} en el cliente: revisar este gate.");

            var cuerpo = codigo[at..codigo.IndexOf(';', at)];
            Assert.Contains("_ledger.", cuerpo, StringComparison.Ordinal);
            Assert.DoesNotContain("HttpRequestMessage", cuerpo, StringComparison.Ordinal);
        }

        var artefacto = CodigoDelArtefacto();
        var entradas = Cuerpo(artefacto, "public async Task<ResultadoDelArtefacto> EntradasAsync");
        var confirmado = entradas.IndexOf("EventOrderStatus.Confirmed", StringComparison.Ordinal);
        var leer = entradas.IndexOf("LeerAsync(", StringComparison.Ordinal);
        Assert.True(confirmado >= 0 && leer > confirmado,
            "Las entradas ya emitidas se buscan en el registro ANTES de leer la compra en el orquestador.");

        var reconciliar = Cuerpo(artefacto, "public async Task ReconciliarAsync");
        Assert.True(reconciliar.IndexOf("EventOrderStatus.Pending", StringComparison.Ordinal) is var p && p >= 0
                    && p < reconciliar.IndexOf("LeerAsync(", StringComparison.Ordinal),
            "Reconciliar mira sólo lo PENDIENTE: lo confirmado no sale a la red.");
    }

    private static string CodigoDelArtefacto()
        => SinComentarios(Path.Combine(RepoRoot(), "Synergos.CMS.Web", "Services", "ArtefactoDeEventos.cs"));

    /// <summary>El cuerpo de un método: desde su firma hasta la firma del siguiente miembro público o privado.</summary>
    private static string Cuerpo(string codigo, string firma)
    {
        var desde = codigo.IndexOf(firma, StringComparison.Ordinal);
        Assert.True(desde >= 0, $"No se encontró «{firma}»: revisar este gate.");
        var hasta = new[] { "\n    public ", "\n    private " }
            .Select(m => codigo.IndexOf(m, desde + firma.Length, StringComparison.Ordinal))
            .Where(i => i > 0)
            .DefaultIfEmpty(codigo.Length)
            .Min();
        return codigo[desde..hasta];
    }

    /// <summary>
    /// El CMS recuerda a los asistentes de su lado, porque la saga NO los lleva, y los anota ANTES de
    /// cerrar la compra.
    /// </summary>
    /// <remarks>
    /// <para>No es un detalle de implementación: es la consecuencia directa de que el orquestador
    /// no cargue datos personales. Si el CMS dejara de anotarlos, la compra existiría del lado del
    /// BFF y de este lado no habría a quién nombrar en la entrada.</para>
    ///
    /// <para><b>Reescrito en la ADR 0140 F3</b>: por la puerta, comprar y cerrar los hace el
    /// orquestador, así que el CMS anota por el ARTEFACTO, sobre una compra en curso
    /// (<c>Running</c>) y antes de guardar; cerrada, ya no se corrige. La ruta vieja sigue anotando al
    /// comprar hasta su retiro (paso 19).</para>
    /// </remarks>
    [Fact]
    public void El_CMS_anota_los_asistentes_antes_de_cerrar_por_el_artefacto()
    {
        var anotar = Cuerpo(CodigoDelArtefacto(), "public async Task<ResultadoDelArtefacto> AnotarAsistentesAsync");
        var enCurso = anotar.IndexOf("\"Running\"", StringComparison.Ordinal);
        var guardar = anotar.IndexOf("_ledger.AnotarPendienteAsync(", StringComparison.Ordinal);
        Assert.True(enCurso >= 0 && guardar > enCurso, "Los asistentes se anotan sólo sobre una compra en curso.");
        Assert.Contains("Emparejar(", anotar, StringComparison.Ordinal);

        var codigo = CodigoDelCliente();
        var comprar = codigo.IndexOf("public async Task<EventCheckoutResult> CheckoutAsync", StringComparison.Ordinal);
        var confirmar = codigo.IndexOf("public async Task<EventConfirmationResult> ConfirmAsync", StringComparison.Ordinal);
        Assert.True(comprar >= 0 && confirmar > comprar, "Cambió la forma del cliente: revisar este gate.");
        var cuerpoDeComprar = codigo[comprar..confirmar];
        Assert.Contains("_ledger.AnotarPendienteAsync(", cuerpoDeComprar, StringComparison.Ordinal);
        Assert.Contains("new PersistedEventOrder(", cuerpoDeComprar, StringComparison.Ordinal);
    }

    /// <summary>
    /// Una saga, una orden (ADR 0140 F3): ningún camino que anota o confirma la orden de una compra del
    /// orquestador la escribe por su cuenta; pasa por el registro, que lo hace bajo el cerrojo de la saga.
    /// </summary>
    /// <remarks>
    /// Lo prueban con peticiones a la vez <c>UnaSagaUnaOrdenTests</c>; esto vigila que un camino nuevo
    /// —o uno viejo reescrito— no vuelva a leer, decidir y escribir sin el cerrojo, que es la forma en
    /// que dos peticiones simultáneas dejaban dos órdenes confirmadas de la misma compra.
    /// </remarks>
    [Fact]
    public void Ningun_camino_escribe_la_orden_de_una_saga_saltandose_el_registro()
    {
        Assert.DoesNotContain("_ledger.SaveAsync(", CodigoDelArtefacto(), StringComparison.Ordinal);

        var codigo = CodigoDelCliente();
        var comprar = codigo.IndexOf("public async Task<EventCheckoutResult> CheckoutAsync", StringComparison.Ordinal);
        var avisar = codigo.IndexOf("private Task AvisarAsync", StringComparison.Ordinal);
        Assert.True(comprar >= 0 && avisar > comprar, "Cambió la forma del cliente: revisar este gate.");
        var comprarYConfirmar = codigo[comprar..avisar];
        Assert.DoesNotContain("_ledger.SaveAsync(", comprarYConfirmar, StringComparison.Ordinal);
        Assert.Contains("_ledger.ConfirmarAsync(", comprarYConfirmar, StringComparison.Ordinal);
    }

    /// <summary>El stub sigue siendo el default: un clon limpio vende entradas sin levantar nada.</summary>
    [Fact]
    public void El_default_es_el_motor_en_proceso()
    {
        var composer = Composer();

        Assert.Contains("\"Synergos:Eventos:Mode\"", composer, StringComparison.Ordinal);
        Assert.Contains("\"Bff\"", composer, StringComparison.Ordinal);

        // El camino HTTP está DENTRO del if; el stub, en el else. Al revés —o sin else— un clon
        // limpio arrancaría apuntando a un servicio que nadie levantó.
        var condicion = composer.IndexOf("\"Synergos:Eventos:Mode\"", StringComparison.Ordinal);
        var elseAt = composer.IndexOf("else", condicion, StringComparison.Ordinal);
        Assert.True(elseAt > 0, "El cableado de Eventos no tiene rama por defecto.");

        var rama = composer[condicion..elseAt];
        Assert.Contains("HttpEventTicketingService", rama, StringComparison.Ordinal);
        Assert.DoesNotContain("StubEventTicketingService>()", rama, StringComparison.Ordinal);

        Assert.Equal("Stub", new Synergos.CMS.Application.Configuration.EventosSettings().Mode);
    }

    /// <summary>La sección se ENLAZA, o lo que no viaja por el HttpClient se queda en su default.</summary>
    /// <remarks>
    /// Es el olvido que arrastraban Tienda (#24) y Salud (#25): configurar el Kind del comprador
    /// no hacía nada y nadie sabía por qué.
    /// </remarks>
    [Fact]
    public void La_seccion_de_configuracion_se_ENLAZA()
    {
        Assert.Contains(
            "services.Configure<EventosSettings>(builder.Config.GetSection(\"Synergos:Eventos\"))",
            Composer(), StringComparison.Ordinal);
    }

    /// <summary>
    /// La configuración de negocio (ADR 0137) se enlaza, se valida AL ARRANCAR, y la leen los DOS
    /// motores de compra — no sólo el resolver que la muestra.
    /// </summary>
    /// <remarks>
    /// <para>La sección se enchufa con <c>AddSeccionDeNegocio</c>, que junta el enlace, el validador
    /// y <c>ValidateOnStart</c> (lo prueba <c>NegocioDeEventosTests</c>: una clave mal escrita no deja
    /// arrancar). Lo que queda por olvidar está acá: un motor sin <c>negocio:</c> vuelve a cobrar sin
    /// la comisión que el carrito muestra, que es el defecto que el piloto cerró (#194). El motor que
    /// NO está encendido es justo el que nadie mira.</para>
    /// </remarks>
    [Fact]
    public void La_configuracion_de_negocio_se_valida_al_arrancar_y_la_leen_los_dos_motores()
    {
        var composer = Composer();

        Assert.Equal("Synergos:Features:Eventos", Synergos.CMS.Application.Configuration.EventosFeatureSettings.Seccion);
        Assert.Matches(
            @"AddSeccionDeNegocio<EventosFeatureSettings, NegocioDeEventos>\(\s*builder\.Config\.GetSection\(""Synergos:Features:Eventos""\)\)",
            composer);

        foreach (var motor in new[] { "new StubEventTicketingService(", "new HttpEventTicketingService(" })
        {
            var desde = composer.IndexOf(motor, StringComparison.Ordinal);
            Assert.True(desde > 0, $"No se encontró {motor} en el composer.");
            var construccion = composer[desde..composer.IndexOf(';', desde)];
            Assert.True(construccion.Contains("negocio:", StringComparison.Ordinal),
                $"{motor.TrimEnd('(')[4..]} se construye sin la configuración de negocio: cobra sin la comisión "
                + "que el carrito le muestra al comprador.");
        }
    }
}

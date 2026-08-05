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
            if (t.StartsWith("//", StringComparison.Ordinal) || t.StartsWith("*", StringComparison.Ordinal))
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

    /// <summary>El artefacto no depende del orquestador, ni para emitirse ni para escanearse.</summary>
    /// <remarks>
    /// Es lo que hace que un BFF caído no deje a nadie fuera de un concierto que ya pagó. Si
    /// «mis entradas» o transferir empezaran a salir a la red, esa propiedad se pierde sin que
    /// nada más cambie de aspecto.
    /// </remarks>
    [Fact]
    public void Mis_entradas_y_transferir_NO_tocan_la_red()
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
    }

    /// <summary>
    /// El CMS recuerda a los asistentes de su lado, porque la saga NO los lleva.
    /// </summary>
    /// <remarks>
    /// <para>No es un detalle de implementación: es la consecuencia directa de que el orquestador
    /// no cargue datos personales. Si el cliente dejara de anotarlos, la compra existiría del
    /// lado del BFF y de este lado no habría de dónde emitir ni a quién nombrar en la entrada —
    /// y el fallo aparecería al confirmar, no al comprar.</para>
    ///
    /// <para>Se comprueba además que lo anote AL COMPRAR y no al confirmar: entre las dos cosas
    /// puede caerse el proceso, y una compra pagada cuyos asistentes se perdieron no se puede
    /// reconstruir desde ningún lado.</para>
    /// </remarks>
    [Fact]
    public void El_CMS_anota_los_asistentes_al_COMPRAR()
    {
        var codigo = CodigoDelCliente();

        var comprar = codigo.IndexOf("public async Task<EventCheckoutResult> CheckoutAsync", StringComparison.Ordinal);
        var confirmar = codigo.IndexOf("public async Task<EventConfirmationResult> ConfirmAsync", StringComparison.Ordinal);
        Assert.True(comprar >= 0 && confirmar > comprar, "Cambió la forma del cliente: revisar este gate.");

        var cuerpoDeComprar = codigo[comprar..confirmar];
        Assert.Contains("_ledger.SaveAsync(", cuerpoDeComprar, StringComparison.Ordinal);
        Assert.Contains("new PersistedEventOrder(", cuerpoDeComprar, StringComparison.Ordinal);
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
}

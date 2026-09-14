namespace Synergos.CMS.Tests.Architecture;

/// <summary>
/// La tienda del CMS compra contra el ORQUESTADOR, nunca contra las capacidades sueltas (HU #24).
/// </summary>
/// <remarks>
/// <para><b>Por qué esto necesita un gate y no basta con haberlo hecho bien una vez.</b> Cablear
/// el CMS a <c>Api.Inventory</c> + <c>Api.Payments</c> + <c>Api.Orders</c> por separado es el
/// atajo natural: son tres llamadas obvias y cada una funciona. Lo que no se ve al escribirlas es
/// que el checkout entero <b>puede fallar a la mitad</b> — si el cobro falla hay que soltar el
/// stock apartado— y que el CMS <b>no tiene dónde anotar una compensación pendiente</b>. Lo que
/// saldría de ese atajo no es «acoplamiento feo»: es stock apartado que nadie suelta y plata
/// cobrada sin pedido.</para>
///
/// <para>Y hay un detalle que solo se ve habiéndolo sufrido: <b>la compensación cambia de
/// carácter al capturar</b>. Antes de capturar, deshacer el pago es «liberar»; después, es
/// «devolver». Eso ya está resuelto en <c>Bff.Core</c> y reimplementarlo saldría mal.</para>
///
/// <para><b>Tosco a propósito</b>, como los demás gates del repo: mira los nombres de las
/// capacidades en las URL del CMS. No atrapa a un adversario, atrapa el atajo de un martes.</para>
/// </remarks>
public sealed class ShopWiringTests
{
    /// <summary>
    /// Las capacidades que el checkout usa POR DEBAJO y que el CMS no puede llamar de frente.
    /// </summary>
    /// <remarks>
    /// <c>Api.Cart</c> NO está: abrir una canasta y ponerle líneas es lo que el BFF exige recibir
    /// —<c>POST /v1/purchases</c> toma un <c>cartId</c>—, y nada de eso hay que deshacerlo si
    /// falla: una canasta abierta y nunca comprada vence sola. No hay saga que reimplementar,
    /// así que no hay nada que prohibir.
    /// </remarks>
    private static readonly string[] Prohibidas =
    {
        "v1/payments", "v1/orders", "v1/items", "v1/holds", "v1/shipments", "v1/quotes",
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

    private static IEnumerable<string> FuentesDelCms()
        => new[] { "Synergos.CMS.Web", "Synergos.CMS.Application", "Synergos.CMS.Interfaces" }
            .Select(p => Path.Combine(RepoRoot(), p))
            .Where(Directory.Exists)
            .SelectMany(d => Directory.EnumerateFiles(d, "*.cs", SearchOption.AllDirectories))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                     && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

    /// <summary>Quita comentarios de línea: acá la prosa NOMBRA las rutas prohibidas para explicarlas.</summary>
    private static string SinComentarios(string ruta)
        => string.Join('\n', File.ReadAllLines(ruta)
            .Select(l =>
            {
                var t = l.TrimStart();
                if (t.StartsWith("//", StringComparison.Ordinal) || t.StartsWith("///", StringComparison.Ordinal)
                    || t.StartsWith("*", StringComparison.Ordinal))
                {
                    return string.Empty;
                }
                var i = l.IndexOf("//", StringComparison.Ordinal);
                return i >= 0 ? l[..i] : l;
            }));

    [Fact]
    public void El_CMS_no_llama_a_las_capacidades_del_checkout_de_frente()
    {
        // Lo que tiene que poner esto en rojo: alguien resuelve «me falta el stock acá» con un
        // GET a Api.Inventory. Funciona, y deja el CMS a un paso de estar orquestando una saga
        // que no sabe deshacer.
        var infracciones = new List<string>();

        foreach (var f in FuentesDelCms())
        {
            // La MISMA exención que en SaludWiringTests, y por la misma razón: agendar una visita
            // a un inmueble (HU #33a) toca UNA sola capacidad —no se cobra, no avisa—, así que no
            // hay saga que orquestar ni nada que deshacer. `v1/holds` aparece ahí legítimamente.
            //
            // Y no es un hueco: RealtyWiringTests comprueba que ese cliente siga sin tocar una
            // segunda capacidad. El día que le entre un cobro, aquel gate cae antes que éste.
            if (Path.GetFileName(f) == "HttpVisitSchedulingService.cs") continue;

            var codigo = SinComentarios(f);
            foreach (var ruta in Prohibidas)
            {
                if (codigo.Contains($"\"{ruta}", StringComparison.OrdinalIgnoreCase)
                    || codigo.Contains($"/{ruta}", StringComparison.OrdinalIgnoreCase))
                {
                    infracciones.Add($"{Path.GetFileName(f)} → {ruta}");
                }
            }
        }

        Assert.True(infracciones.Count == 0,
            "El CMS está llamando a una capacidad del checkout de frente: "
            + string.Join(", ", infracciones.Distinct(StringComparer.Ordinal))
            + ". Comprar va contra Bff.Tienda (POST /v1/purchases): reservar + cobrar + crear el "
            + "pedido pueden fallar a la mitad, y el CMS no tiene dónde anotar una compensación "
            + "pendiente.");
    }

    [Fact]
    public void El_cliente_de_la_tienda_apunta_al_orquestador()
    {
        // El complemento del anterior: que la ruta que SÍ se usa sea la del BFF. Sin esto, borrar
        // la llamada entera también pasaría el gate de arriba.
        var cliente = File.ReadAllText(
            Path.Combine(RepoRoot(), "Synergos.CMS.Web", "Services", "HttpShopOrderService.cs"));

        Assert.Contains("v1/purchases", cliente, StringComparison.Ordinal);
    }

    /// <summary>
    /// La devolución se la pide a QUIEN TIENE LA PLATA, no al proveedor de pagos (#57).
    /// </summary>
    /// <remarks>
    /// <para><b>Éste es el defecto, y no fallaba ruidosamente.</b> El RMA llamaba a
    /// <c>IPaymentProvider.RefundAsync</c> con <c>ShopOrder.PaymentSessionId</c>. Con la tienda
    /// cableada ese identificador es el de la <b>saga</b> del orquestador —el id de
    /// <c>Api.Payments</c> no sale de allá a propósito—, así que el proveedor local no lo conocía
    /// y el caso no llegaba nunca a reembolsado.</para>
    ///
    /// <para><b>Y el mapa del cableado tenía mal la razón.</b> Decía que <c>StubReturnService</c>
    /// iba a un orquestador por «dos pasos con plata en medio»; mirando el código, el segundo
    /// paso —marcar el RMA— es una escritura LOCAL, y un reembolso no se compensa. La pregunta
    /// que decide no es «¿qué hay que deshacer?» sino <b>«¿quién tiene la plata?»</b>.</para>
    ///
    /// <para>Se mira el fichero sin comentarios, porque la prosa de arriba NOMBRA la ruta
    /// prohibida para explicarla — un gate que leyera el texto entero se pondría rojo por su
    /// propia explicación.</para>
    /// </remarks>
    [Fact]
    public void La_devolucion_no_le_pide_la_plata_al_proveedor_local()
    {
        var rma = SinComentarios(Path.Combine(
            RepoRoot(), "Synergos.CMS.Application", "Services", "Impl", "StubReturnService.cs"));

        Assert.DoesNotContain("_payments.RefundAsync", rma, StringComparison.Ordinal);
        Assert.DoesNotContain("PaymentSessionId", rma, StringComparison.Ordinal);

        // Y se la pide al seam de órdenes, que es quien sabe contra qué se cobró.
        Assert.Contains("_orders.RefundAsync", rma, StringComparison.Ordinal);
    }

    /// <summary>
    /// El camino HTTP devuelve con LLAVE de idempotencia.
    /// </summary>
    /// <remarks>
    /// Devolver plata es un movimiento RELATIVO: un reintento tras un timeout sin llave devuelve
    /// dos veces. Es la misma razón por la que el ajuste relativo de <c>Api.Inventory</c> la exige
    /// y el absoluto no (#30) — y acá el daño es peor, porque no se nota hasta que cuadran caja.
    /// </remarks>
    [Fact]
    public void La_devolucion_por_HTTP_lleva_llave()
    {
        var cliente = SinComentarios(Path.Combine(
            RepoRoot(), "Synergos.CMS.Web", "Services", "HttpShopOrderService.cs"));

        var refund = cliente.IndexOf("purchases/{Uri.EscapeDataString(orderRef)}/refund", StringComparison.Ordinal);
        Assert.True(refund > 0, "El cliente ya no llama a la devolución del orquestador: revisar este gate.");

        var trozo = cliente[refund..Math.Min(cliente.Length, refund + 700)];
        Assert.Contains("Idempotency-Key", trozo, StringComparison.Ordinal);

        // Y lo devuelto sale de la RESPUESTA, no del monto que pedimos: dar por bueno lo nuestro
        // marcaría el caso reembolsado por una cifra que quizá nadie movió.
        Assert.Contains("compra.Refunded", trozo, StringComparison.Ordinal);
    }

    [Fact]
    public void El_stub_sigue_siendo_el_default()
    {
        // Un clon limpio tiene que arrancar y vender sin levantar seis servicios. Si el default
        // se moviera a Bff, `git clone && dotnet run` dejaría de tener tienda — y el síntoma
        // sería un checkout que falla, no un error de arranque que alguien lea.
        var composer = File.ReadAllText(
            Path.Combine(RepoRoot(), "Synergos.CMS.Web", "Composers", "SeamComposer.Shop.cs"));

        Assert.Contains("StubShopOrderService", composer, StringComparison.Ordinal);

        // El modo Bff es OPT-IN: se entra por una comparación explícita contra "Bff", así que
        // ausencia de config = stub.
        Assert.Contains("\"Bff\"", composer, StringComparison.Ordinal);

        var settings = File.ReadAllText(
            Path.Combine(RepoRoot(), "Synergos.CMS.Application", "Configuration", "TiendaSettings.cs"));
        Assert.Contains("Mode { get; init; } = \"Stub\"", settings, StringComparison.Ordinal);
    }

    // ── La identidad de quien compra (HU #14) ───────────────────────────────

    /// <summary>El cuerpo del método que abre la canasta en <c>Api.Cart</c>.</summary>
    private static string AbrirCanasta()
    {
        var cliente = SinComentarios(Path.Combine(
            RepoRoot(), "Synergos.CMS.Web", "Services", "HttpShopOrderService.cs"));

        var i = cliente.IndexOf("private async Task<string> AbrirCanastaAsync(", StringComparison.Ordinal);
        Assert.True(i > 0, "Cambió el método que abre la canasta: revisar este gate.");

        var fin = cliente.IndexOf("private async Task<PurchaseDto> ComprarAsync(", i, StringComparison.Ordinal);
        Assert.True(fin > i, "No se pudo delimitar el método que abre la canasta: revisar este gate.");
        return cliente[i..fin];
    }

    /// <summary>
    /// Abrir la canasta PRESENTA identidad y declara el suelo (HU #14).
    /// </summary>
    /// <remarks>
    /// <para><b>El gate mira el cableado, no la regla</b>, que es la lección de la rebanada 5: los
    /// tests del cliente pasan en verde con la cabecera quitada si nadie mira que se ponga. Y sin
    /// la cabecera, <c>Api.Cart</c> vuelve a creerle al CMS de quién es la canasta — el defecto
    /// #42 reaparecido un piso más arriba.</para>
    ///
    /// <para><b>Y lo que se declara es siempre el SUELO.</b> Escribir <c>IdentityToken</c> desde
    /// este lado porque el despliegue sepa emitir tokens guardaría como hecho lo que nadie
    /// verificó: quien sube la afirmación es la capacidad, y sólo tras comprobar la firma.</para>
    /// </remarks>
    [Fact]
    public void Abrir_la_canasta_PRESENTA_identidad_y_declara_el_suelo()
    {
        var cuerpo = AbrirCanasta();

        Assert.Contains("IdentityHeader", cuerpo, StringComparison.Ordinal);
        Assert.Contains("_identidad.IssueAsync(", cuerpo, StringComparison.Ordinal);

        // El suelo, y sólo el suelo.
        Assert.Contains("assertion = IdentityAssertions.CmsSession", cuerpo, StringComparison.Ordinal);
        Assert.DoesNotContain("IdentityAssertions.IdentityToken", cuerpo, StringComparison.Ordinal);

        // El sujeto del token es el DUEÑO de la canasta. Firmar por otro no fallaría acá: fallaría
        // en la capacidad (token_subject_mismatch) y dejaría de poder comprarse.
        Assert.Contains("new IdentitySubject(BuyerKind, buyerId", cuerpo, StringComparison.Ordinal);
    }

    /// <summary>
    /// Sólo se pide token cuando hay SESIÓN; el invitado no.
    /// </summary>
    /// <remarks>
    /// El identificador de un invitado es un seudónimo de un correo que escribió en un formulario
    /// y que nadie comprobó. Pedir un token para él haría que la capacidad anotara
    /// <c>IdentityToken</c> sobre una identidad que nadie verificó — el defecto #42, con la firma
    /// tapándolo mejor.
    /// </remarks>
    [Fact]
    public void Solo_se_pide_token_cuando_hay_sesion()
    {
        var cuerpo = AbrirCanasta();

        var guarda = cuerpo.IndexOf("customer.MemberKey is Guid", StringComparison.Ordinal);
        var emision = cuerpo.IndexOf("_identidad.IssueAsync(", StringComparison.Ordinal);

        Assert.True(guarda > 0, "Desapareció la guarda de sesión: un invitado conseguiría token.");
        Assert.True(emision > guarda, "La emisión quedó fuera de la guarda de sesión.");
    }

    /// <summary>
    /// Un rechazo por identidad NO se degrada a un reintento sin firma.
    /// </summary>
    /// <remarks>
    /// <para><b>Es la decisión opuesta a la de la bitácora, y a propósito.</b> Allá el asiento se
    /// repite sin firmar porque perder un rastro es peor que un rastro débil: un hueco no se nota.
    /// Acá una canasta abierta sin comprobar quedaría atribuida a un miembro por la sola palabra
    /// de quien llamó, nadie audita una canasta y vence sola a los siete días — el hueco sería
    /// permanente y silencioso. Fallar se ve, y lo ve quien está comprando.</para>
    ///
    /// <para>El gate mira que el cliente <b>reconozca</b> el rechazo de identidad y no lo trate
    /// como un rechazo de negocio: eso es lo que impide que el motivo de un defecto de despliegue
    /// salga por pantalla como si fuera algo que el comprador puede resolver.</para>
    /// </remarks>
    [Fact]
    public void Un_rechazo_por_identidad_no_se_reintenta_sin_firma()
    {
        var cliente = SinComentarios(Path.Combine(
            RepoRoot(), "Synergos.CMS.Web", "Services", "HttpShopOrderService.cs"));

        Assert.Contains("IdentityCodePrefix", cliente, StringComparison.Ordinal);

        // Y NO hay un segundo intento sin firma: el de la bitácora se llama así, y acá no va.
        Assert.DoesNotContain("firmado: false", cliente, StringComparison.Ordinal);
    }
}

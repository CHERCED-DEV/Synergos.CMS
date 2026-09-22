using System.Text.RegularExpressions;

namespace Synergos.CMS.Tests.Architecture;

/// <summary>
/// El vertical Alquiler (#147, piloto 2): equipos que se llevan, vuelven, y llevan una garantía
/// RETENIDA mientras están fuera.
/// </summary>
/// <remarks>
/// <para><b>Lo que este vertical añade y ningún otro tiene</b> es un movimiento de dinero que se
/// AUTORIZA para no cobrarse: los cuatro flujos existentes autorizan para capturar, y acá el
/// desenlace normal es <i>anular</i>. Los dos primeros dientes son los que el spec predijo, y
/// que hiciera falta escribirlos es el resultado que el #147 buscaba: el molde tenía un hueco
/// que los pilotos 0 y 1 no tocaron.</para>
///
/// <para>El resto son la FORMA del molde aplicada a este vertical: el interruptor, su default, el
/// cliente cableado, y las dos invariantes del eje 3 (#153) — el artefacto vive fuera del seam
/// de la transacción y su REGISTRO no sale a la red.</para>
/// </remarks>
public sealed class AlquilerWiringTests
{
    private static string Dir(params string[] partes) => Proyectos.Ruta(partes);

    /// <summary>
    /// El fichero SIN comentarios.
    /// </summary>
    /// <remarks>
    /// <b>Seguro barato: medido, hoy no cambia el resultado</b> de ningún diente de esta clase
    /// —los regex van sobre símbolos que ningún comentario de acá escribe—. Va puesto porque los
    /// <c>&lt;remarks&gt;</c> de las piezas que vigila SÍ citan las formas prohibidas, y el día
    /// que un diente busque una subcadena suelta sería load-bearing
    /// (<c>a_precaution_you_did_not_measure_is_a_claim_you_should_not_make</c>).
    /// </remarks>
    private static string SinComentarios(string ruta)
        => string.Join('\n', File.ReadAllLines(ruta).Select(l =>
        {
            var t = l.TrimStart();
            if (t.StartsWith("//", StringComparison.Ordinal)
                || t.StartsWith('*')
                || t.StartsWith("/*", StringComparison.Ordinal))
            {
                return string.Empty;
            }

            var i = l.IndexOf("//", StringComparison.Ordinal);
            return i >= 0 ? l[..i] : l;
        }));

    private static string FuenteDe(string fichero, params string[] carpeta)
    {
        var ruta = Path.Combine(Dir(carpeta), fichero);
        Assert.True(File.Exists(ruta), $"No existe {fichero}: revisar este gate.");
        return SinComentarios(ruta);
    }

    // ── Los dos que el spec PREDIJO ─────────────────────────────────────────

    [Fact]
    public void La_garantia_se_ANULA_y_no_se_captura_cuando_no_hay_dano()
    {
        // El desenlace normal de una garantía es soltarla, y eso se lee en que devolver deja
        // DepositHeld en cero cobrando sólo el daño. Un motor que capturara la garantía entera y
        // devolviera la diferencia movería plata dos veces y dejaría al arrendatario esperando
        // un reembolso que nadie le prometió — y no fallaría nada.
        var motor = FuenteDe("StubEquipmentRentalService.cs",
            "Synergos.CMS.Application", "Services", "Impl");

        Assert.Contains("DepositHeld = 0m", motor, StringComparison.Ordinal);
        Assert.Contains("DamageCharged = monto", motor, StringComparison.Ordinal);

        // Y el rechazo existe: cobrar por encima de lo retenido sería inventar una deuda.
        Assert.Contains("damage_exceeds_deposit", motor, StringComparison.Ordinal);
    }

    /// <summary>
    /// La saga se CIERRA al reservar: no sigue viva mientras el equipo está fuera.
    /// </summary>
    /// <remarks>
    /// <para><b>Es el segundo gate que el spec predijo</b>, y lo que vigila no es una convención:
    /// una saga en <c>Running</c> la barre <c>CompensationSweeper</c> a los
    /// <c>Sweep:AbandonAfterMinutes</c> —<b>60 por defecto</b>, y un alquiler dura días—, así que
    /// dejarla abierta hasta la devolución soltaría la ventana y anularía la garantía de un
    /// alquiler <b>vivo</b>, con el equipo ya en la obra y sin que nada fallara.</para>
    ///
    /// <para><b>Los dos números se DERIVAN, no se escriben</b>: el tope de días sale de
    /// <c>AlquilerSettings</c> y el plazo de abandono de <c>SweepOptions</c>. Con las dos cifras a
    /// mano, el día que alguien suba el plazo a un mes este diente seguiría exigiendo un cierre
    /// que ya no haría falta — pediría un cambio que no arregla nada, y un gate así se desactiva
    /// (<c>a_path_is_not_a_name_and_a_flat_tree_hides_it</c>).</para>
    ///
    /// <para><b>Y el segundo diente es el que no se puede satisfacer con un literal:</b> cerrar y
    /// dejar compensaciones pendientes no sirve de nada, porque <c>WithPendingCompensations</c>
    /// las recoge igual sin mirar el estado. Cerrar es las dos cosas a la vez.</para>
    /// </remarks>
    [Fact]
    public void La_saga_de_un_alquiler_NO_sigue_viva_mientras_el_equipo_esta_fuera()
    {
        var topeDias = Numero(
            FuenteDe("AlquilerSettings.cs", "Synergos.CMS.Application", "Configuration"),
            @"MaxRentalDays\s*\{\s*get;\s*init;\s*\}\s*=\s*(\d+)");
        var abandonoMinutos = Numero(
            FuenteDe("CompensationSweeper.cs", "Synergos.Bff.Core"),
            @"AbandonAfterMinutes\s*\{\s*get;\s*set;\s*\}\s*=\s*(\d+)");

        // Si un día el plazo de abandono superara al alquiler más largo, este diente pediría un
        // cambio que no arregla nada. Se para y se dice, en vez de exigir por costumbre.
        Assert.True(topeDias * 24 * 60 > abandonoMinutos,
            $"Un alquiler dura hasta {topeDias} días y el barrido abandona a los "
            + $"{abandonoMinutos} minutos. Si eso dejó de ser cierto, este diente sobra: borralo "
            + "en vez de relajarlo.");

        var flujo = FuenteDe("RentalFlow.cs", "Synergos.Bff.Alquiler", "Domain");
        var reserva = CuerpoDe(flujo, "public async Task<Result<RentalSaga>> ReserveAsync");

        Assert.Contains("SagaStatus.Completed", reserva, StringComparison.Ordinal);
        Assert.Contains("Compensations = Array.Empty<Compensation>()", reserva, StringComparison.Ordinal);

        // Y cerrar el alquiler opera sobre una saga YA cerrada: no es un paso de la saga.
        var cierre = CuerpoDe(flujo, "private async Task<Result<RentalSaga>> CerrarAsync");
        Assert.Contains("saga.Status != SagaStatus.Completed", cierre, StringComparison.Ordinal);
    }

    /// <summary>El primer número que casa, o el test dice que el corte dejó de ver.</summary>
    private static int Numero(string fuente, string patron)
    {
        var m = Regex.Match(fuente, patron);
        Assert.True(m.Success, $"No se encontró «{patron}»: revisar este gate, no relajarlo.");
        return int.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// El cuerpo de un método, de su firma al siguiente miembro de la clase.
    /// </summary>
    /// <remarks>
    /// Se corta por el siguiente <c>///</c> o <c>[Fact]</c>-menos firma y no por un MODIFICADOR:
    /// seguir <c>"private "</c> deja el gate ciego el día que alguien cambia la visibilidad, que
    /// es exactamente lo que le pasó al del #154 media hora después de escribirlo.
    /// </remarks>
    private static string CuerpoDe(string fuente, string firma)
    {
        var i = fuente.IndexOf(firma, StringComparison.Ordinal);
        Assert.True(i >= 0, $"No se encontró «{firma}»: revisar este gate.");

        // El siguiente miembro empieza en una línea con cuatro espacios de sangría y una firma;
        // basta con el siguiente `\n    ///` o `\n    [` o `\n    public`/`private`/`internal`.
        var resto = fuente[(i + firma.Length)..];
        var corte = Regex.Match(resto, @"\n    (///|\[|public |private |internal |})");
        return corte.Success ? resto[..corte.Index] : resto;
    }

    [Fact]
    public void El_contrato_guarda_la_garantia_del_MOMENTO_y_no_la_relee()
    {
        // Un comprobante es una foto de lo acordado. Si releyera la cifra viva, el papel que
        // alguien imprimió cambiaría solo el día de la discusión — que es exactamente cuando
        // tiene que decir lo que decía.
        var emisor = FuenteDe("EquipmentAgreementIssuer.cs",
            "Synergos.CMS.Application", "Services", "Impl");

        Assert.Contains("DepositHeld: rental.DepositHeld", emisor, StringComparison.Ordinal);
        Assert.DoesNotContain("_ledger.GetAsync", emisor, StringComparison.Ordinal);
    }

    // ── La forma del molde ──────────────────────────────────────────────────

    [Fact]
    public void El_interruptor_existe_y_su_default_NO_es_el_valor_cableado()
    {
        var settings = FuenteDe("AlquilerSettings.cs", "Synergos.CMS.Application", "Configuration");

        Assert.Matches(new Regex(@"Mode\s*\{\s*get;\s*init;\s*\}\s*=\s*""Stub"""), settings);
        Assert.DoesNotContain(@"= ""Bff""", settings, StringComparison.Ordinal);
    }

    [Fact]
    public void El_composer_ENLAZA_las_dos_secciones_y_la_del_sello_va_anidada()
    {
        // Sin Configure<> el cliente recibe un POCO recién construido y lo que no viaja por el
        // HttpClient se queda en su default en silencio (#24, #25). Y la del sello va ANIDADA:
        // una sección hermana acaba a UNA letra de la del vertical (#154).
        var composer = FuenteDe("SeamComposer.Alquiler.cs", "Synergos.CMS.Web", "Composers");

        Assert.Contains(@"Configure<AlquilerSettings>(builder.Config.GetSection(""Synergos:Alquiler"")",
            composer, StringComparison.Ordinal);
        Assert.Contains(@"Configure<AgreementSettings>(builder.Config.GetSection(""Synergos:Alquiler:Agreement"")",
            composer, StringComparison.Ordinal);
    }

    [Fact]
    public void El_cliente_cableado_lo_registra_el_composer()
    {
        // Una clase que ningún composer registra no es una conexión: es código que nadie
        // ejecuta (`a_named_list_beats_a_count`).
        var composer = FuenteDe("SeamComposer.Alquiler.cs", "Synergos.CMS.Web", "Composers");

        Assert.Contains("new HttpEquipmentRentalService(", composer, StringComparison.Ordinal);
        Assert.Contains("new StubEquipmentRentalService(", composer, StringComparison.Ordinal);
    }

    [Fact]
    public void El_catalogo_de_Alquiler_NO_sale_a_la_red()
    {
        // El eje 1 sale del árbol de contenido y cablearlo a Api.Catalog sería un RETROCESO: el
        // dato ya tiene dueño (`a_vertical_is_three_axes_and_only_one_crosses`).
        foreach (var fichero in new[] { "StubEquipmentCatalogProvider.cs", "CatalogEquipmentCatalogProvider.cs" })
        {
            var fuente = FuenteDe(fichero, "Synergos.CMS.Application", "Services", "Impl");
            Assert.DoesNotContain("HttpClient", fuente, StringComparison.Ordinal);
        }

        var source = FuenteDe("UmbracoEquipmentCatalogSource.cs",
            "Synergos.CMS.Web", "Services", "Catalog");
        Assert.DoesNotContain("HttpClient", source, StringComparison.Ordinal);
    }

    [Fact]
    public void El_REGISTRO_del_artefacto_NO_sale_a_la_red()
    {
        // La invariante del eje 3 (#153): lo que puede salir es el SELLO —la custodia de la
        // llave— y nunca el índice de emitidos. Con el orquestador caído, quien alquiló sigue
        // viendo su contrato y cuánto se le retuvo.
        var registro = FuenteDe("EquipmentAgreementLedger.cs",
            "Synergos.CMS.Application", "Services", "Impl");

        Assert.DoesNotContain("HttpClient", registro, StringComparison.Ordinal);
        Assert.DoesNotContain("IHttpClientFactory", registro, StringComparison.Ordinal);
    }

    [Fact]
    public void El_artefacto_vive_FUERA_del_seam_de_la_transaccion()
    {
        // Un emisor dentro del motor en proceso no lo puede usar el cliente cableado, y copiarlo
        // sería «un comprobante con dos definiciones» (#153). Lo destapó el cableado de Eventos:
        // sin ese corte, la cara de organizador quedaba leyendo un almacén vacío.
        var motor = FuenteDe("StubEquipmentRentalService.cs",
            "Synergos.CMS.Application", "Services", "Impl");
        var cliente = FuenteDe("HttpEquipmentRentalService.cs", "Synergos.CMS.Web", "Services");

        foreach (var (nombre, fuente) in new[] { ("el motor en proceso", motor), ("el cliente", cliente) })
        {
            Assert.False(fuente.Contains("EquipmentAgreementIssuer", StringComparison.Ordinal),
                $"{nombre} nombra al emisor del contrato: el artefacto tiene que vivir FUERA del "
                + "seam del eje 2, o sus dos implementaciones no lo pueden compartir (#153).");
        }
    }

    [Fact]
    public void El_seam_del_eje_2_NO_tiene_operacion_de_lectura()
    {
        // Es una decisión, no un olvido: la bandeja la sirve el registro de este lado, así que
        // con el orquestador caído quien alquiló sigue viendo su contrato. Forma del #46 y #62.
        var seam = FuenteDe("IEquipmentRentalService.cs", "Synergos.CMS.Interfaces");

        Assert.DoesNotContain("Task<Rental?> GetAsync", seam, StringComparison.Ordinal);
        Assert.DoesNotContain("ListAsync", seam, StringComparison.Ordinal);
    }

    [Fact]
    public void El_sello_NO_se_llama_Stub()
    {
        // Llamar provisional al firmante de un comprobante afirma algo falso sobre lo único que
        // lo hace valer (#155). La implementación en proceso ES la de verdad.
        var impl = Dir("Synergos.CMS.Application", "Services", "Impl");

        Assert.True(File.Exists(Path.Combine(impl, "HmacAgreementSigner.cs")));
        Assert.False(File.Exists(Path.Combine(impl, "StubAgreementSigner.cs")),
            "El firmante del contrato no es un doble de nada: no se llama Stub (#155).");
    }

    [Fact]
    public void La_custodia_de_la_llave_NO_es_una_cuarta_copia()
    {
        // El paso S8 del molde dice «la custodia: la llave, cifrada con IDataProtector» como si
        // cada vertical escribiera la suya — y eso FABRICA la duplicación que §0.B.17 prohíbe.
        // Con el tercer consumidor se promovió: los tres delegan en la misma custodia.
        var web = Dir("Synergos.CMS.Web", "Services");
        var providers = Directory.EnumerateFiles(web, "*SigningKeyProvider.cs")
            .Select(f => (Nombre: Path.GetFileName(f), Fuente: SinComentarios(f)))
            .ToList();

        Assert.True(providers.Count >= 3,
            $"Se esperaban al menos tres custodias de llave y hay {providers.Count}: "
            + "si se movieron de carpeta, este diente pasa en verde sin mirar nada.");

        var propios = providers
            .Where(p => !p.Fuente.Contains("new CustodiaDeLlaveDeFirma(", StringComparison.Ordinal))
            .Select(p => p.Nombre)
            .ToList();

        Assert.True(propios.Count == 0,
            "Estas custodias reimplementan lo que CustodiaDeLlaveDeFirma ya hace: "
            + string.Join(", ", propios)
            + ". El mismo SUJETO y la misma POLÍTICA es la misma cosa (§0.B.17, #147).");
    }
}

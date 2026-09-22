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

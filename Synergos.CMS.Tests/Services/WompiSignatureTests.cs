using System.Security.Cryptography;
using System.Text;
using Synergos.CMS.Application.Services.Impl;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// Cubre <see cref="WompiSignature"/> — las dos firmas de Wompi.
/// </summary>
/// <remarks>
/// La investigación de PSPs pone la firma mal calculada como el error nº1 de
/// integración en producción: el algoritmo de concatenación es frágil y un
/// carácter de más la invalida sin ningún mensaje útil del otro lado. Estos
/// tests fijan el algoritmo contra un SHA256 calculado aparte, no contra la
/// propia implementación.
/// </remarks>
public sealed class WompiSignatureTests
{
    /// <summary>SHA256 hex en minúsculas, calculado sin pasar por el sujeto bajo prueba.</summary>
    private static string Expected(string raw)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw))).ToLowerInvariant();

    // ── Firma de integridad ──────────────────────────────────────────────

    [Fact]
    public void Integridad_concatena_referencia_monto_moneda_y_secreto_en_ese_orden()
    {
        // El ejemplo textual de la documentación de Wompi:
        // "sk8-438k4-xmxm392" + "2490000" + "COP" + secreto.
        var actual = WompiSignature.Integrity(
            reference: "sk8-438k4-xmxm392",
            amountInCents: 2_490_000,
            currency: "COP",
            integritySecret: "prod_integrity_secret");

        Assert.Equal(Expected("sk8-438k4-xmxm3922490000COPprod_integrity_secret"), actual);
    }

    [Fact]
    public void Integridad_con_expiracion_la_mete_ANTES_del_secreto()
    {
        // Si la expiración va en la transacción pero no en la firma (o al
        // revés), Wompi la rechaza. El orden no es negociable.
        var actual = WompiSignature.Integrity(
            "ref-1", 100_000, "COP", "secreto", expiresAtIso8601: "2026-08-01T12:00:00.000Z");

        Assert.Equal(Expected("ref-1100000COP2026-08-01T12:00:00.000Zsecreto"), actual);
    }

    [Fact]
    public void Integridad_normaliza_la_moneda_a_MAYUSCULAS()
    {
        Assert.Equal(
            WompiSignature.Integrity("r", 1000, "COP", "s"),
            WompiSignature.Integrity("r", 1000, "cop", "s"));
    }

    [Fact]
    public void Integridad_devuelve_hex_en_minusculas_de_64_caracteres()
    {
        var sig = WompiSignature.Integrity("r", 1000, "COP", "s");

        Assert.Equal(64, sig.Length);
        Assert.Equal(sig.ToLowerInvariant(), sig);
        Assert.Matches("^[0-9a-f]{64}$", sig);
    }

    [Fact]
    public void Integridad_rechaza_un_monto_no_positivo()
    {
        // Un monto en cero o negativo no es un pago: es un bug del llamante, y
        // firmarlo lo dejaría viajar hasta el PSP disfrazado de válido.
        Assert.Throws<ArgumentOutOfRangeException>(
            () => WompiSignature.Integrity("r", 0, "COP", "s"));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => WompiSignature.Integrity("r", -1, "COP", "s"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Integridad_exige_secreto(string? secret)
        => Assert.ThrowsAny<ArgumentException>(
            () => WompiSignature.Integrity("r", 1000, "COP", secret!));

    [Fact]
    public void Cambiar_UN_solo_caracter_cambia_la_firma()
    {
        var a = WompiSignature.Integrity("ref-1", 100_000, "COP", "s");
        var b = WompiSignature.Integrity("ref-2", 100_000, "COP", "s");
        var c = WompiSignature.Integrity("ref-1", 100_001, "COP", "s");

        Assert.NotEqual(a, b);
        Assert.NotEqual(a, c);
    }

    // ── Checksum de evento ───────────────────────────────────────────────

    [Fact]
    public void Checksum_concatena_valores_luego_timestamp_luego_secreto()
    {
        var actual = WompiSignature.EventChecksum(
            new[] { "12345", "APPROVED", "2490000" },
            timestamp: 1530291411,
            eventsSecret: "eventos_secret");

        Assert.Equal(
            Expected("12345APPROVED24900001530291411eventos_secret"),
            actual);
    }

    [Fact]
    public void Checksum_trata_una_propiedad_AUSENTE_como_vacia_y_no_como_null()
    {
        // La ausencia es parte del mensaje firmado; escribir "null" cambiaría
        // el hash y rechazaría eventos legítimos.
        var conNull = WompiSignature.EventChecksum(new string?[] { "a", null, "c" }, 1, "s");
        var conVacio = WompiSignature.EventChecksum(new[] { "a", "", "c" }, 1, "s");

        Assert.Equal(conVacio, conNull);
        Assert.Equal(Expected("ac1s"), conNull);
    }

    [Fact]
    public void El_ORDEN_de_las_propiedades_importa()
    {
        // Por eso el método recibe los valores ya resueltos en orden: la lista
        // de nombres viene en el propio evento (signature.properties) y la doc
        // advierte que no se hardcodee, porque varía por tipo de evento.
        var ab = WompiSignature.EventChecksum(new[] { "a", "b" }, 1, "s");
        var ba = WompiSignature.EventChecksum(new[] { "b", "a" }, 1, "s");

        Assert.NotEqual(ab, ba);
    }

    [Fact]
    public void Checksum_sin_propiedades_sigue_siendo_calculable()
    {
        var actual = WompiSignature.EventChecksum(Array.Empty<string>(), 42, "s");

        Assert.Equal(Expected("42s"), actual);
    }

    // ── Comparación ──────────────────────────────────────────────────────

    [Fact]
    public void ChecksumMatches_acepta_el_mismo_valor_sin_importar_mayusculas_ni_espacios()
    {
        Assert.True(WompiSignature.ChecksumMatches("ABC123", "abc123"));
        Assert.True(WompiSignature.ChecksumMatches("abc123", "  abc123  "));
    }

    [Fact]
    public void ChecksumMatches_rechaza_distinto_vacio_y_null()
    {
        Assert.False(WompiSignature.ChecksumMatches("abc", "abd"));
        Assert.False(WompiSignature.ChecksumMatches("abc", null));
        Assert.False(WompiSignature.ChecksumMatches(null, "abc"));
        Assert.False(WompiSignature.ChecksumMatches("", ""));
    }

    [Fact]
    public void ChecksumMatches_no_se_cae_con_longitudes_distintas()
    {
        // FixedTimeEquals exige mismo largo; si no se maneja, un checksum
        // truncado tumbaría el receptor en vez de rechazarse.
        Assert.False(WompiSignature.ChecksumMatches("abc", "abcdef"));
    }

    // ── Centavos ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData(1000.00, 100_000L)]
    [InlineData(0.01, 1L)]
    [InlineData(249_000.00, 24_900_000L)]
    public void ToCents_convierte_a_centavos_enteros(decimal pesos, long cents)
        => Assert.Equal(cents, WompiSignature.ToCents(pesos));

    [Fact]
    public void ToCents_REDONDEA_en_vez_de_truncar()
    {
        // Truncar regala hasta un centavo por transacción; sobre volumen deja de
        // ser un redondeo y pasa a ser un descuadre contra el PSP.
        Assert.Equal(1235L, WompiSignature.ToCents(12.345m));
        Assert.Equal(1234L, WompiSignature.ToCents(12.344m));
    }
}

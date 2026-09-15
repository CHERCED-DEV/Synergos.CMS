using Synergos.CMS.Web.Services;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// Cubre <see cref="PrecioAutorado"/> — la única regla del repo sobre qué texto tecleado por un
/// editor es un precio (#123).
/// </summary>
/// <remarks>
/// <para><b>Por qué el fixture lleva EXACTAMENTE estos dos casos.</b> Con <c>"89000"</c> a secas,
/// el <c>decimal.TryParse(raw, NumberStyles.Number, InvariantCulture)</c> de antes y esta regla
/// dan el mismo número: el defecto pasaría en VERDE. Los dos que lo exigen son los que el repo ya
/// midió en vivo:</para>
/// <list type="number">
///   <item><c>"$89000"</c> — no parsea, y el código viejo caía a <c>0m</c>: mercancía comprable
///   a precio CERO.</item>
///   <item><c>"49.000"</c> — SÍ parsea, y da <b>49</b>, porque en <c>InvariantCulture</c> el
///   punto es separador DECIMAL. Es el peor de los dos porque no es un fallo: es un precio
///   plausible equivocado por 1000× que ninguna guarda de «&gt; 0» ve. <i>Verificado en vivo:
///   el Tote salió a 49.</i></item>
/// </list>
///
/// <para><b>Y el vacío está acá con su propio caso</b> porque es donde las políticas se
/// separan: esta función dice <c>false</c>, y son el catálogo (omite), Eventos y Educación
/// (gratis) quienes deciden qué significa. Si el vacío devolviera <c>true</c> con 0, esa decisión
/// se habría tomado acá dentro para los ocho llamadores (#120).</para>
/// </remarks>
public sealed class PrecioAutoradoTests
{
    /// <summary>Lo que un editor teclea y NO es inequívocamente un precio.</summary>
    [Theory]
    [InlineData("$89000")]      // medido en vivo: caía a 0m → comprable gratis
    [InlineData("49.000")]      // medido en vivo: daba 49 → el Tote salió a 49
    [InlineData("49,000")]
    [InlineData("89.000,50")]
    [InlineData("89000 COP")]
    [InlineData("89 000")]
    [InlineData("-89000")]      // NumberStyles.Number acepta el signo; un precio negativo no existe
    [InlineData("89000.00")]
    [InlineData("1e5")]
    [InlineData("(89000)")]     // contabilidad: NumberStyles.Number lo lee como -89000
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void UnTextoAmbiguo_NoEsUnPrecio(string? raw)
    {
        Assert.False(PrecioAutorado.EsInequivoco(raw, out var precio));

        // El cero de salida NO es un precio: es «no hay dato». Se comprueba porque leerlo sin
        // mirar el bool es exactamente el defecto que esto cierra.
        Assert.Equal(0m, precio);
    }

    [Theory]
    [InlineData("89000", 89000)]
    [InlineData("49000", 49000)]
    [InlineData("  89000  ", 89000)]   // el editor deja espacios al pegar; eso no es ambiguo
    [InlineData("0", 0)]               // dígitos: es un precio. Si 0 vale o no lo decide el llamador
    [InlineData("000089000", 89000)]
    public void UnTextoDeSoloDigitos_EsUnPrecio(string raw, int esperado)
    {
        Assert.True(PrecioAutorado.EsInequivoco(raw, out var precio));
        Assert.Equal(esperado, precio);
    }

    /// <summary>
    /// Una ristra de dígitos que no cabe en un <c>decimal</c> no es un precio, y sale en cero.
    /// </summary>
    /// <remarks>
    /// Es el único camino donde «solo dígitos» y «parsea» no coinciden. Sin la segunda
    /// comprobación, <c>precio</c> quedaría con lo que dejó el <c>TryParse</c> fallido.
    /// </remarks>
    [Fact]
    public void Una_ristra_de_digitos_que_no_cabe_en_un_decimal_no_es_un_precio()
    {
        Assert.False(PrecioAutorado.EsInequivoco(new string('9', 40), out var precio));
        Assert.Equal(0m, precio);
    }
}

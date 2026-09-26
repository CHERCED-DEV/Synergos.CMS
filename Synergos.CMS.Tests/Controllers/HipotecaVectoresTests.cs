using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Controllers;

namespace Synergos.CMS.Tests.Controllers;

/// <summary>
/// <c>POST /api/realty/mortgage</c> contra los vectores de oro compartidos con el UI (#167).
/// </summary>
/// <remarks>
/// <para><b>Qué se rompió.</b> El <c>&lt;remarks&gt;</c> de <see cref="IMortgageCalculator"/>
/// afirmaba que «el cálculo base es el mismo en cliente y servidor», y era FALSO: las dos
/// implementaciones eran la misma fórmula con la tasa a <b>100×</b> de distancia, porque el
/// borde leía <c>annualRate</c> como fracción y la app la mandaba en porcentaje. Medido con las
/// dos implementaciones reales sobre el primer vector: el borde contestaba <b>240.000.000</b> al
/// mes donde la app pinta <b>2.642.606,72</b>. Y la diagonal —borde-con-fracción y
/// app-con-porcentaje dando el mismo número— es lo que prueba que lo único roto era la unidad.</para>
///
/// <para><b>Por qué un fichero compartido y no una tabla acá.</b> Dos derivaciones independientes
/// de la misma verdad que tienen que coincidir es lo único que distingue «las dos calculadoras
/// concuerdan» de «el <c>&lt;remarks&gt;</c> dice que concuerdan» — el movimiento de
/// <c>IdentityGateTests</c> (#14), que deriva su lista del disco en vez de leérsela al generador.
/// Una tabla escrita acá sólo vigilaría a este árbol, que es el que no se movió.</para>
///
/// <para><b>Y las expectativas no salen de ninguna de las dos implementaciones</b>, o esto sería
/// una foto: detectaría que se separan y no que las dos están mal a la vez. Se derivaron de la
/// fórmula cerrada del sistema francés con aritmética decimal de 50 dígitos. El fichero lo dice y
/// dice cómo.</para>
///
/// <para><b>El test va por el BORDE y no por el seam</b>, porque lo que se arregló es la
/// conversión y vive en el controller: llamar a <see cref="IMortgageCalculator.Calculate"/>
/// directo pasaría en verde con el defecto puesto. Y se afirma sobre el JSON <b>serializado</b>
/// con <see cref="JsonSerializerDefaults.Web"/> —lo que usa MVC— porque lo que el otro lado lee
/// es la clave, no la propiedad de C#: la mutación real de la deriva es un
/// <c>JsonPropertyName</c>, que compila.</para>
/// </remarks>
public sealed class HipotecaVectoresTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// El fichero de vectores, en la carpeta que <c>CLAUDE.md</c> §3 declara «la ÚNICA superficie
    /// de acople» con el UI.
    /// </summary>
    internal static string RutaDeVectores()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Synergos.CMS.sln")))
        {
            dir = dir.Parent;
        }
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "Synergos.CMS.Web", "docs", "contracts", "mortgage-vectors.json");
    }

    internal sealed record Vector(
        string Nombre,
        decimal Price,
        decimal DownPayment,
        int TermMonths,
        decimal AnnualRatePercent,
        decimal Monthly,
        decimal Principal,
        decimal ConLaUnidadMal);

    internal static IReadOnlyList<Vector> Vectores()
    {
        var doc = JsonDocument.Parse(File.ReadAllText(RutaDeVectores()));
        return doc.RootElement.GetProperty("vectores").EnumerateArray()
            .Select(v => new Vector(
                v.GetProperty("nombre").GetString()!,
                v.GetProperty("price").GetDecimal(),
                v.GetProperty("downPayment").GetDecimal(),
                v.GetProperty("termMonths").GetInt32(),
                v.GetProperty("annualRatePercent").GetDecimal(),
                v.GetProperty("monthly").GetDecimal(),
                v.GetProperty("principal").GetDecimal(),
                v.GetProperty("conLaUnidadMal").GetDecimal()))
            .ToList();
    }

    private static RealtyController Borde()
    {
        var formatter = Substitute.For<IPriceFormatter>();
        formatter.Format(Arg.Any<decimal>(), Arg.Any<string?>()).Returns("$ 0");
        var gate = Substitute.For<IMemberAccessGate>();
        return new RealtyController(
            Substitute.For<IPropertyCatalogProvider>(),
            Substitute.For<IVisitSchedulingService>(),
            // La calculadora DE VERDAD, no un doble: lo que se vigila es el número.
            new StubMortgageCalculator(),
            Substitute.For<ILeadCaptureService>(),
            Substitute.For<IUserCollection>(),
            Substitute.For<ISavedSearchService>(),
            formatter,
            gate,
            new RealtyVisitLedger());
    }

    private static JsonElement Pedir(RealtyController.MortgageRequest cuerpo)
    {
        var ok = Assert.IsType<OkObjectResult>(Borde().Mortgage(cuerpo));
        return JsonSerializer.SerializeToElement(ok.Value, Web);
    }

    [Fact]
    public void Hay_vectores_que_cruzar()
    {
        // Red de seguridad. Sin esto, un fichero movido o renombrado dejaría el Theory de abajo
        // sin casos y la suite en VERDE sin haber comprobado un solo número — el modo de fallo
        // caro: un rojo se arregla y un verde sobre el vacío se hereda (#136).
        Assert.True(File.Exists(RutaDeVectores()),
            $"No está {RutaDeVectores()}: el cruce con el UI no tiene fixture. NO se salta — se rechaza.");
        Assert.True(Vectores().Count >= 6,
            $"Se leyeron {Vectores().Count} vectores y el fichero declara seis: el lector dejó de ver.");
    }

    [Theory]
    [MemberData(nameof(Casos))]
    public void El_borde_da_la_cuota_del_vector(string nombre)
    {
        var v = Vectores().Single(x => x.Nombre == nombre);

        var json = Pedir(new RealtyController.MortgageRequest(
            v.Price, v.DownPayment, v.TermMonths, v.AnnualRatePercent));

        Assert.Equal(v.Monthly, json.GetProperty("monthly").GetDecimal());
        Assert.Equal(v.Principal, json.GetProperty("principal").GetDecimal());
    }

    /// <summary>
    /// El resultado NO puede ser el de la unidad equivocada.
    /// </summary>
    /// <remarks>
    /// Es lo que impide que el vector de tasa cero —donde porcentaje y fracción dan el MISMO
    /// número— se cuente como cobertura de la unidad. Ahí los dos valores coinciden a propósito y
    /// el caso se excluye diciéndolo, en vez de aflojar el assert para todos.
    /// </remarks>
    [Theory]
    [MemberData(nameof(Casos))]
    public void El_borde_no_da_la_cuota_de_la_unidad_equivocada(string nombre)
    {
        var v = Vectores().Single(x => x.Nombre == nombre);
        if (v.Monthly == v.ConLaUnidadMal)
        {
            // Tasa cero: la recta P/n no depende de la unidad. El vector cubre la rama, no el
            // defecto, y el fichero lo dice.
            Assert.Equal(0m, v.AnnualRatePercent);
            return;
        }

        var json = Pedir(new RealtyController.MortgageRequest(
            v.Price, v.DownPayment, v.TermMonths, v.AnnualRatePercent));

        Assert.NotEqual(v.ConLaUnidadMal, json.GetProperty("monthly").GetDecimal());
    }

    /// <summary>
    /// Los totales se comprueban contra ESTA implementación y no contra la del UI.
    /// </summary>
    /// <remarks>
    /// Los dos árboles totalizan con métodos distintos, los dos correctos: acá se SUMA el cuadro
    /// completo redondeado a centavos con la última cuota absorbiendo el redondeo —por eso el
    /// saldo cierra en cero exacto— y allá se multiplica la cuota sin redondear por el plazo,
    /// porque el UI construye sólo las primeras filas y no tiene qué sumar. Medido sobre el primer
    /// vector: cinco centavos sobre 634 millones. Está escrito en el fichero de vectores para que
    /// nadie lo lea como deriva, y acá se vigila lo que sí es propiedad de este lado: que los
    /// totales CUADREN con su propio cuadro.
    /// </remarks>
    [Theory]
    [MemberData(nameof(Casos))]
    public void Los_totales_cuadran_con_su_propio_cuadro(string nombre)
    {
        var v = Vectores().Single(x => x.Nombre == nombre);
        var json = Pedir(new RealtyController.MortgageRequest(
            v.Price, v.DownPayment, v.TermMonths, v.AnnualRatePercent));

        var filas = json.GetProperty("schedule").EnumerateArray().ToList();
        Assert.Equal(v.TermMonths, filas.Count);

        var pagado = filas.Sum(f => f.GetProperty("payment").GetDecimal());
        var interes = filas.Sum(f => f.GetProperty("interest").GetDecimal());
        var capital = filas.Sum(f => f.GetProperty("principal").GetDecimal());

        Assert.Equal(json.GetProperty("totalPaid").GetDecimal(), pagado);
        Assert.Equal(json.GetProperty("totalInterest").GetDecimal(), interes);
        Assert.Equal(v.Principal, capital);
        // El saldo cierra en cero: es la razón por la que la última cuota absorbe el redondeo.
        Assert.Equal(0m, filas[^1].GetProperty("balance").GetDecimal());
    }

    /// <summary>
    /// La tasa AUSENTE se rechaza. Cero es «sin interés» y es legítimo; la ausencia no.
    /// </summary>
    /// <remarks>
    /// Con el record posicional anterior —<c>decimal AnnualRate</c>— un cuerpo sin la clave dejaba
    /// <c>0</c>, y la calculadora sólo rechaza <c>&lt; 0</c>: caía a la rama «sin interés» y
    /// devolvía la recta <c>P/n</c>, un número plausible sin un error. Eso es lo que habría hecho
    /// SILENCIOSO un renombrado de la clave hecho en un solo árbol
    /// (<c>feedback_an_omitted_key_can_be_an_assertion</c>), así que este test es la mitad que
    /// vuelve seguro el arreglo, no un extra.
    /// <para>Se deserializa del JSON del cliente y no se construye el record a mano: lo que hay
    /// que ejercitar es lo que el BINDER deja, y un <c>null</c> escrito en C# no prueba que la
    /// clave ausente llegue como <c>null</c>.</para>
    /// </remarks>
    [Theory]
    [InlineData("""{"price":300000000,"downPayment":60000000,"termMonths":240}""", false)]
    [InlineData("""{"price":300000000,"downPayment":60000000,"termMonths":240,"annualRatePercent":null}""", false)]
    [InlineData("""{"price":300000000,"downPayment":60000000,"termMonths":240,"annualRate":12}""", false)]
    [InlineData("""{"price":300000000,"downPayment":60000000,"termMonths":240,"annualRatePercent":0}""", true)]
    public void Sin_tasa_se_rechaza_y_cero_se_acepta(string cuerpo, bool aceptado)
    {
        var request = JsonSerializer.Deserialize<RealtyController.MortgageRequest>(cuerpo, Web);
        Assert.NotNull(request);

        var resultado = Borde().Mortgage(request);

        if (aceptado)
        {
            var ok = Assert.IsType<OkObjectResult>(resultado);
            var json = JsonSerializer.SerializeToElement(ok.Value, Web);
            // Sin interés: la recta 240.000.000 / 240.
            Assert.Equal(1_000_000m, json.GetProperty("monthly").GetDecimal());
        }
        else
        {
            Assert.IsType<BadRequestObjectResult>(resultado);
        }
    }

    public static TheoryData<string> Casos()
    {
        var data = new TheoryData<string>();
        foreach (var v in Vectores())
        {
            data.Add(v.Nombre);
        }
        return data;
    }
}

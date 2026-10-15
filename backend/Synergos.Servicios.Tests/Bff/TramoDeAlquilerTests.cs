using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Synergos.Bff.Alquiler.Clients;
using Synergos.Bff.Alquiler.Domain;
using Synergos.Bff.Core;
using Synergos.Bff.Core.Flow;
using Synergos.Core;
using Synergos.Shared;

namespace Synergos.CMS.Tests.Bff;

/// <summary>
/// <c>alquiler.tramo</c> (línea 005, T009): el tramo por duración y los límites de la oferta, antes de tocar
/// ninguna capacidad.
/// </summary>
/// <remarks>
/// La oferta se siembra en su registro y no se publica: aquí se prueba la REGLA del paso. Que lo publicado llegue
/// a las capacidades es <see cref="OfertaDeAlquilerTests"/>.
/// </remarks>
public sealed class TramoDeAlquilerTests : IDisposable
{
    private readonly string _raiz = Path.Combine(Path.GetTempPath(), "tramo-alquiler-" + Guid.NewGuid().ToString("N"));
    private readonly OfertaDeAlquiler _ofertas;

    private static readonly PasoDef Definicion = new(
        "tramo", "alquiler.tramo", ["equipo", "unidades", "ventana"], ["aCotizar", "garantiaACotizar", "recursoSujeto"],
        null, null, null, null, null);

    public TramoDeAlquilerTests()
    {
        // Tarifa base, desde 7 y desde 28: tres valores distintos de «desde» para que elegir mal se vea.
        new JsonCollectionStore<OfertaDeAlquilerRegistrada>(Path.Combine(_raiz, "ofertas"), "equipos", o => o.Equipo)
            .Put(new OfertaDeAlquilerRegistrada("andamio-6m", "COP", 40_000m,
                [new TramoDeTarifa(7, 30_000m), new TramoDeTarifa(28, 24_000m)], 250_000m,
                MinDias: 2, MaxDias: 45, Unidades: 3, Retirada: false, DateTimeOffset.UnixEpoch));
        _ofertas = new OfertaDeAlquiler(new AlquilerCapabilities(new SinRed()),
            Options.Create(new SagaStorageOptions { Root = _raiz }), TimeProvider.System, NullLogger<OfertaDeAlquiler>.Instance);
    }

    public void Dispose()
    {
        _ofertas.Dispose();
        try { if (Directory.Exists(_raiz)) Directory.Delete(_raiz, recursive: true); }
        catch (IOException) { /* un temporal */ }
    }

    /// <summary>Una fábrica que falla si alguien sale a la red: el paso no llama a ninguna capacidad.</summary>
    private sealed class SinRed : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => throw new InvalidOperationException($"El paso salió a la red ({name}).");
    }

    /// <summary>
    /// Una ventana de esos días. Cero días es una ventana de doce horas el mismo día: una vacía ni se puede
    /// construir (<see cref="TimeWindow"/> la rechaza), así que la entrada ya la habría devuelto como inválida.
    /// </summary>
    private static TimeWindow Dias(int dias)
    {
        var inicio = new DateTimeOffset(2026, 11, 2, 0, 0, 0, TimeSpan.Zero);
        return TimeWindow.Of(inicio, dias == 0 ? inicio.AddHours(12) : inicio.AddDays(dias));
    }

    private static IReadOnlyList<FlowContext> Unidades(params int[] numeros)
        => numeros.Select(n => new FlowContext().Set("unidad", n)).ToList();

    private Task<SalidaDePaso> Ejecutar(string equipo, IReadOnlyList<FlowContext> unidades, TimeWindow ventana, int maxDias = 30)
        => new PasoTramo(_ofertas, maxDias).EjecutarAsync(new EntradaDePaso(
            "alq-1", Ref.Create("alquiler.renta", "alq-1"), Definicion,
            new FlowContext().Set("equipo", equipo).Set("unidades", unidades).Set("ventana", ventana), null, null),
            CancellationToken.None);

    [Theory]
    [InlineData(6, "andamio-6m@1", 12)]   // sin tramo que la cubra: la tarifa base
    [InlineData(7, "andamio-6m@7", 14)]   // justo en el tramo
    [InlineData(29, "andamio-6m@28", 58)] // el MÁS ALTO que la cubre, no el primero
    public async Task Cotiza_el_tramo_mas_alto_que_cubre_la_duracion_por_dias_por_unidades(int dias, string tramo, int cantidad)
    {
        var r = await Ejecutar("andamio-6m", Unidades(1, 2), Dias(dias), maxDias: 0);

        Assert.Equal(PasoResultado.Continuar, r.Control);
        var alquiler = Assert.IsAssignableFrom<IReadOnlyList<(Ref Sujeto, int Cantidad)>>(r.Valores[0]);
        Assert.Equal((Ref.Create(RentalSubject.KindTarifa, tramo), cantidad), Assert.Single(alquiler));

        var garantia = Assert.IsAssignableFrom<IReadOnlyList<(Ref Sujeto, int Cantidad)>>(r.Valores[1]);
        Assert.Equal((Ref.Create(RentalSubject.KindGarantiaUnidad, "andamio-6m"), 2), Assert.Single(garantia));
        Assert.Equal(RentalSubject.For("andamio-6m"), r.Valores[2]);
    }

    [Theory]
    [InlineData("otro-equipo", 7, new[] { 1 }, 30, "alquiler.equipment_not_offered")]
    [InlineData("andamio-6m", 0, new[] { 1 }, 30, "alquiler.bad_window")]
    [InlineData("andamio-6m", 31, new[] { 1 }, 30, "alquiler.window_too_long")]
    [InlineData("andamio-6m", 1, new[] { 1 }, 30, "alquiler.window_out_of_bounds")]
    [InlineData("andamio-6m", 7, new int[0], 30, "alquiler.bad_quantity")]
    [InlineData("andamio-6m", 7, new[] { 1, 2, 3, 4 }, 30, "alquiler.bad_quantity")]  // más que las unidades del equipo
    [InlineData("andamio-6m", 7, new[] { 1, 1 }, 30, "alquiler.bad_quantity")]        // repetida
    [InlineData("andamio-6m", 7, new[] { 2 }, 30, "alquiler.bad_quantity")]           // no es 1..N
    public async Task Rechaza_antes_de_tocar_nada_con_un_codigo_por_causa(
        string equipo, int dias, int[] unidades, int maxDias, string codigo)
    {
        var r = await Ejecutar(equipo, Unidades(unidades), Dias(dias), maxDias);

        Assert.Equal(PasoResultado.Abortar, r.Control);
        Assert.Equal(codigo, r.Rechazo!.Code);
    }

    [Fact]
    public async Task Una_oferta_retirada_ya_no_se_alquila()
    {
        Assert.True((await _ofertas.RetirarAsync("andamio-6m", CancellationToken.None)).IsOk);

        var r = await Ejecutar("andamio-6m", Unidades(1), Dias(7));

        Assert.Equal("alquiler.equipment_not_offered", r.Rechazo!.Code);
    }
}

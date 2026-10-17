using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Composers;

namespace Synergos.CMS.Tests.Architecture;

/// <summary>
/// El interruptor de la cancelación de lo confirmado (línea 005, T044; NN-8, aclaración D-005-NN8),
/// <c>Synergos:Alquiler:Cancellation</c>, con los composers de verdad: una palabra que no conoce no arranca, y cada una
/// que conoce lleva la cancelación del INQUILINO a su camino —el flujo posterior o la liquidación propia—; la del
/// personal va siempre a la propia.
/// </summary>
/// <remarks>
/// No es un <c>…:Mode</c> de despliegue (<c>ModosDelComposeTests</c>): no elige qué se registra sino adónde va una
/// operación del mismo cliente, así que la huella de la composición es la misma con las dos palabras. Por eso aquí se
/// resuelve el cliente compuesto y se mira la ruta que pide.
/// </remarks>
[Collection(ComposeExclusivo.Nombre)]
public sealed class InterruptorDeLaCancelacionTests
{
    private const string Clave = "Synergos:Alquiler:Cancellation";

    private sealed class Grabadora : HttpMessageHandler
    {
        public List<string> Rutas { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
        {
            Rutas.Add(req.RequestUri!.AbsolutePath);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    private sealed class Fabrica(HttpMessageHandler h) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
            => new(h, disposeHandler: false) { BaseAddress = new Uri("http://bff-alquiler.local/") };
    }

    private static RentalAgreement Contrato() => new(
        "pta-1", "u-1", "andamio-6m", "Andamio", 1, new DateOnly(2026, 12, 10), new DateOnly(2026, 12, 17),
        210_000m, 300_000m, "COP", new DateTimeOffset(2026, 10, 17, 12, 0, 0, TimeSpan.Zero), "sello", CaminoDeCompra.Puerta);

    /// <summary>Compone el CMS con la palabra, resuelve el cliente y devuelve la ruta que pide al cancelar.</summary>
    private static async Task<string> RutaAlCancelar(string? palabra, RentalCanceller quien)
    {
        var services = ComposicionDelCms.Componer(new Dictionary<string, string?>
        {
            ["Synergos:Alquiler:BaseUrl"] = "http://bff-alquiler.local/",
            [Clave] = palabra,
        });
        var grabadora = new Grabadora();
        services.AddLogging();
        services.AddSingleton<IHttpClientFactory>(new Fabrica(grabadora));
        using var proveedor = services.BuildServiceProvider();

        await proveedor.GetRequiredService<IAlquilerDeLaPuerta>().CancelAsync(Contrato(), quien, 0m, "staff-1", "llave");
        return Assert.Single(grabadora.Rutas);
    }

    [Fact]
    public void Una_palabra_que_no_conoce_no_arranca_y_nombra_las_dos()
    {
        var ex = Assert.Throws<ModoDesconocidoException>(() => ComposicionDelCms.Componer(
            new Dictionary<string, string?> { [Clave] = "Liquidacion" }));

        Assert.Equal(Clave, ex.Clave);
        Assert.Equal([AlquilerSettings.CancelacionPosterior, AlquilerSettings.CancelacionPropia], ex.Validos);
    }

    [Theory]
    [InlineData(null, "/v1/flujos/alquiler.cancelacion")]
    [InlineData("Posterior", "/v1/flujos/alquiler.cancelacion")]
    [InlineData(" posterior ", "/v1/flujos/alquiler.cancelacion")]
    [InlineData("Propia", "/v1/alquileres/pta-1/cancelar-confirmado")]
    [InlineData("PROPIA", "/v1/alquileres/pta-1/cancelar-confirmado")]
    public async Task El_inquilino_cancela_por_el_camino_que_dice_la_palabra(string? palabra, string ruta)
        => Assert.Equal(ruta, await RutaAlCancelar(palabra, RentalCanceller.Renter));

    [Theory]
    [InlineData(null)]
    [InlineData("Propia")]
    public async Task El_personal_cancela_siempre_por_la_liquidacion_propia(string? palabra)
        => Assert.Equal("/v1/alquileres/pta-1/cancelar-confirmado", await RutaAlCancelar(palabra, RentalCanceller.Staff));
}

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Web.Services.Catalog;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.PublishedCache;
using Umbraco.Cms.Core.Web;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// La pregunta de la oferta al publicar (línea 005): si el árbol publicado ya muestra la raíz del sitio de alquiler. La
/// responde la fuente del catálogo con su misma regla, y sin escribir el ERR con que el catálogo avisa que no la halla.
/// </summary>
/// <remarks>
/// El caso con la raíz presente recorre el árbol con las extensiones de Umbraco, que piden el contenedor de la
/// aplicación: lo cubren el gate de reconstrucción y la copia piloto. Aquí van los tres en que no se ve.
/// </remarks>
public sealed class RaizDelAlquilerTests
{
    private sealed class LogQueGuarda : ILogger<UmbracoEquipmentCatalogSource>
    {
        public List<(LogLevel Nivel, string Texto)> Lineas { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Lineas.Add((logLevel, formatter(state, exception)));
    }

    private readonly LogQueGuarda _log = new();

    private UmbracoEquipmentCatalogSource Fuente(IUmbracoContextAccessor accesor, string? alcance)
    {
        var catalogo = Substitute.For<IOptionsMonitor<CatalogSettings>>();
        catalogo.CurrentValue.Returns(new CatalogSettings
        {
            Scopes = alcance is null ? new() : new Dictionary<string, string> { [UmbracoEquipmentCatalogSource.Vertical] = alcance },
        });
        var alquiler = Substitute.For<IOptionsMonitor<AlquilerSettings>>();
        alquiler.CurrentValue.Returns(new AlquilerSettings());
        return new UmbracoEquipmentCatalogSource(accesor, catalogo, alquiler, _log);
    }

    private static IUmbracoContextAccessor ConElArbol(params IPublishedContent[] raices)
    {
        var cache = Substitute.For<IPublishedContentCache>();
        cache.GetAtRoot(Arg.Any<string?>()).Returns(raices);
        var contexto = Substitute.For<IUmbracoContext>();
        contexto.Content.Returns(cache);
        var accesor = Substitute.For<IUmbracoContextAccessor>();
        accesor.TryGetUmbracoContext(out Arg.Any<IUmbracoContext?>())
            .Returns(llamada =>
            {
                llamada[0] = contexto;
                return true;
            });
        return accesor;
    }

    [Fact]
    public void Con_el_arbol_publicado_todavia_sin_raices_no_se_ve_y_no_queda_ningun_error()
    {
        // Lo del import que trae el siteRoot y sus equipos juntos: la caché publicada aún no lo muestra.
        Assert.False(Fuente(ConElArbol(), "alquiler").SeVe());
        Assert.Empty(_log.Lineas);
    }

    [Fact]
    public void Sin_contexto_de_Umbraco_no_se_ve_y_no_queda_ningun_error()
    {
        var sinUmbraco = Substitute.For<IUmbracoContextAccessor>();
        sinUmbraco.TryGetUmbracoContext(out Arg.Any<IUmbracoContext?>()).Returns(false);

        Assert.False(Fuente(sinUmbraco, "alquiler").SeVe());
        Assert.Empty(_log.Lineas);
    }

    [Fact]
    public async Task Sin_el_alcance_no_se_ve_y_la_pregunta_calla_lo_que_el_catalogo_si_dice()
    {
        var fuente = Fuente(ConElArbol(), alcance: null);

        Assert.False(fuente.SeVe());
        Assert.Empty(_log.Lineas);

        // El catálogo, en cambio, sigue fallando cerrado y diciéndolo: la regla es la misma, el registro no.
        Assert.Empty(await fuente.GetAllAsync());
        Assert.Contains(_log.Lineas, l => l.Nivel == LogLevel.Error && l.Texto.Contains("Synergos:Catalog:Scopes:Alquiler", StringComparison.Ordinal));
    }
}

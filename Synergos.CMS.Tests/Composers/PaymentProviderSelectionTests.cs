using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Composers;

namespace Synergos.CMS.Tests.Composers;

/// <summary>
/// Qué proveedor de pago sirve cuando el router está apagado (auditoría, backlog Medio #6).
/// </summary>
/// <remarks>
/// <para><b>El defecto.</b> La rama sin router era un <c>switch</c> con un solo brazo vivo
/// —<c>default</c> → stub— y el caso <c>"wompi"</c> comentado. Es decir:
/// <c>Provider="Wompi"</c> servía el <b>stub en silencio</b>, contra lo que promete la
/// documentación del propio ajuste. Un operador podía creerse en producción cobrando de verdad
/// mientras el checkout devolvía pagos de mentira. La boleta lo anotó como olor de OCP;
/// medido, era una config que miente.</para>
///
/// <para><b>Por qué estos tests y no un test del composer.</b> Componer exige un
/// <c>IUmbracoBuilder</c> entero. Lo que importa aquí no es el registro sino <b>la decisión</b>:
/// qué proveedor termina cobrando para cada combinación de config. Por eso la decisión es un
/// método con nombre.</para>
/// </remarks>
public sealed class PaymentProviderSelectionTests
{
    private static IServiceProvider Container(PaymentsSettings settings)
    {
        var services = new ServiceCollection();
        services.AddSingleton(Options.Create(settings));
        services.AddSingleton(Substitute.For<IJsonEntityStore>());
        services.AddSingleton(HttpClients());
        services.AddLogging();
        return services.BuildServiceProvider();
    }

    /// <summary>
    /// Fábrica que entrega un <see cref="HttpClient"/> de verdad. Un doble que devuelve null
    /// haría fallar el ctor de Wompi y el test diría "cae al stub" por la razón equivocada.
    /// </summary>
    private static IHttpClientFactory HttpClients()
    {
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(Arg.Any<string>())
            .Returns(_ => new HttpClient { BaseAddress = new Uri("https://sandbox.wompi.co/v1/") });
        return factory;
    }

    private static PaymentsSettings WithWompiKeys(string provider) => new()
    {
        Provider = provider,
        WompiPublicKey = "pub_test_abc",
        WompiIntegritySecret = "integrity_abc",
    };

    [Fact]
    public void Provider_Wompi_con_llaves_sirve_WOMPI_y_no_el_stub()
    {
        // El caso que estaba roto: antes esto devolvía el stub sin decir nada.
        var provider = SeamComposer.SelectSingleProvider(Container(WithWompiKeys("Wompi")));

        Assert.Equal("wompi", provider.ProviderKey);
        Assert.IsType<WompiPaymentProvider>(provider);
    }

    [Theory]
    [InlineData("wompi")]
    [InlineData("WOMPI")]
    [InlineData("  Wompi  ")]
    public void La_clave_del_proveedor_se_lee_sin_importar_caja_ni_espacios(string declared)
    {
        // El ajuste está documentado como case-insensitive, y un espacio de más en un JSON de
        // configuración no debería decidir si los cobros son reales.
        var provider = SeamComposer.SelectSingleProvider(Container(WithWompiKeys(declared)));

        Assert.Equal("wompi", provider.ProviderKey);
    }

    [Fact]
    public void Provider_Wompi_SIN_llaves_cae_al_stub()
    {
        // Degradar es correcto: romper el arranque dejaría la demo sin checkout por una llave
        // que falta. Lo que no puede es hacerlo callado — de eso se encarga el test de abajo.
        var settings = new PaymentsSettings { Provider = "Wompi" };

        Assert.IsType<StubPaymentProvider>(SeamComposer.SelectSingleProvider(Container(settings)));
    }

    [Fact]
    public void Caer_al_stub_por_falta_de_llaves_deja_una_ADVERTENCIA()
    {
        // El silencio era el defecto, no el fallback. Sin este aviso, la única señal de que los
        // pagos son de mentira es que nunca llega la plata.
        var log = new CollectingLoggerProvider();
        var services = new ServiceCollection();
        services.AddSingleton(Options.Create(new PaymentsSettings { Provider = "Wompi" }));
        services.AddSingleton(Substitute.For<IJsonEntityStore>());
        services.AddSingleton(HttpClients());
        services.AddLogging(b => b.AddProvider(log).SetMinimumLevel(LogLevel.Trace));

        SeamComposer.SelectSingleProvider(services.BuildServiceProvider());

        var warning = Assert.Single(log.Entries, e => e.Level == LogLevel.Warning);
        Assert.Contains("stub", warning.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("NO son reales", warning.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Stub")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("un-psp-que-no-existe")]
    public void Cualquier_otro_valor_sirve_el_stub_durable(string? provider)
    {
        // Incluido un proveedor desconocido: el default del producto es la demo durable, y no
        // hay adapter al que caer.
        var settings = new PaymentsSettings { Provider = provider! };

        Assert.IsType<StubPaymentProvider>(SeamComposer.SelectSingleProvider(Container(settings)));
    }

    [Fact]
    public void Tener_llaves_NO_alcanza_para_cobrar_de_verdad_si_no_se_pidio_Wompi()
    {
        // Las llaves pueden quedar puestas de una prueba anterior. Quien manda es Provider.
        var provider = SeamComposer.SelectSingleProvider(Container(WithWompiKeys("Stub")));

        Assert.IsType<StubPaymentProvider>(provider);
    }

    /// <summary>Logger provider mínimo que retiene lo emitido, para poder afirmar sobre ello.</summary>
    private sealed class CollectingLoggerProvider : ILoggerProvider
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = new();

        public ILogger CreateLogger(string categoryName) => new Collecting(Entries);
        public void Dispose() { }

        private sealed class Collecting : ILogger
        {
            private readonly List<(LogLevel, string)> _entries;
            public Collecting(List<(LogLevel, string)> entries) => _entries = entries;

            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                lock (_entries)
                {
                    _entries.Add((logLevel, formatter(state, exception)));
                }
            }
        }
    }
}

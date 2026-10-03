using System.Globalization;
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Services;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// Los avisos de carrito abandonado escriben el subtotal en es-CO y con SU moneda, sea cual sea la
/// cultura del hilo del scanner.
/// </summary>
/// <remarks>
/// <para>Los cuatro canales —Slack, Discord, Teams y el correo, asunto y plantilla— escribían
/// <c>{Subtotal:N2} {Currency}</c>, o sea con la cultura del HILO. El scanner es un
/// <c>BackgroundService</c>: no tiene petición ni sitio, así que en un servidor en en-US un carrito de
/// 123.500 pesos llegaba al equipo como «123,500.00 COP» —la coma es de miles para en-US y decimal
/// para quien lo lee en Colombia—.</para>
///
/// <para><b>El hilo se fija en en-US</b>, como en el test del saldo de salud: con la cultura de la
/// máquina del arquitecto (es-CO) el defecto daría «123.500,00 COP», y un test que buscara «123.500»
/// pasaría con él puesto. Por eso se compara el texto y se exige que NO esté la forma vieja.</para>
/// </remarks>
public sealed class AvisosDeCarritoAbandonadoTests
{
    private static readonly AbandonedCart EnPesos = new("cart-1", 3, 123_500m, "COP", DateTime.UtcNow.AddMinutes(-90));

    private static readonly IPriceFormatter Formato = new EsCoPriceFormatter(new CartSettings());

    private sealed class Captura : HttpMessageHandler
    {
        public List<string> Cuerpos { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Cuerpos.Add(await request.Content!.ReadAsStringAsync(ct));
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    private sealed class Fabrica(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private static IOptionsMonitor<CartAbandonmentSettings> Ajustes()
    {
        var monitor = Substitute.For<IOptionsMonitor<CartAbandonmentSettings>>();
        monitor.CurrentValue.Returns(new CartAbandonmentSettings
        {
            SlackWebhookUrl = "https://hooks.example/slack",
            DiscordWebhookUrl = "https://hooks.example/discord",
            TeamsWebhookUrl = "https://hooks.example/teams",
            NotifyEmailAddress = "ventas@example.co",
        });
        return monitor;
    }

    private static IBrandingProvider Marca()
    {
        var marca = Substitute.For<IBrandingProvider>();
        marca.GetCurrent().Returns(new BrandIdentity("synergos", "SynergosLabs"));
        return marca;
    }

    private static ICartAbandonmentNotifierChannel Canal(string canal, HttpMessageHandler http) => canal switch
    {
        "slack" => new SlackCartAbandonmentNotifier(new Fabrica(http), Ajustes(), Marca(), Formato,
            NullLogger<SlackCartAbandonmentNotifier>.Instance),
        "discord" => new DiscordCartAbandonmentNotifier(new Fabrica(http), Ajustes(), Marca(), Formato,
            NullLogger<DiscordCartAbandonmentNotifier>.Instance),
        "teams" => new TeamsCartAbandonmentNotifier(new Fabrica(http), Ajustes(), Marca(), Formato,
            NullLogger<TeamsCartAbandonmentNotifier>.Instance),
        _ => throw new ArgumentOutOfRangeException(nameof(canal), canal, null),
    };

    /// <summary>Todos los textos del cuerpo JSON, ya sin escapar.</summary>
    private static List<string> Textos(string json)
    {
        var textos = new List<string>();
        void Recorrer(JsonElement e)
        {
            switch (e.ValueKind)
            {
                case JsonValueKind.String: textos.Add(e.GetString()!); break;
                case JsonValueKind.Object: foreach (var p in e.EnumerateObject()) Recorrer(p.Value); break;
                case JsonValueKind.Array: foreach (var i in e.EnumerateArray()) Recorrer(i); break;
            }
        }

        using var doc = JsonDocument.Parse(json);
        Recorrer(doc.RootElement);
        return textos;
    }

    private static async Task EnEnUs(Func<Task> accion)
    {
        var antes = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
        try
        {
            await accion();
        }
        finally
        {
            CultureInfo.CurrentCulture = antes;
        }
    }

    [Theory]
    [InlineData("slack")]
    [InlineData("discord")]
    [InlineData("teams")]
    public async Task El_webhook_escribe_el_subtotal_en_es_CO_y_no_en_la_cultura_del_hilo(string canal)
    {
        var http = new Captura();

        await EnEnUs(() => Canal(canal, http).NotifyAbandonedAsync(EnPesos, default));

        var textos = Textos(Assert.Single(http.Cuerpos));
        Assert.Contains(textos, t => t.Contains("$ 123.500", StringComparison.Ordinal));
        Assert.DoesNotContain(textos, t => t.Contains("123,500", StringComparison.Ordinal));
    }

    [Fact] // la moneda viaja con el importe: un carrito en dólares no sale con el símbolo del peso.
    public async Task Un_carrito_en_otra_moneda_lleva_su_codigo()
    {
        var http = new Captura();

        await EnEnUs(() => Canal("slack", http).NotifyAbandonedAsync(EnPesos with { Subtotal = 99m, Currency = "USD" }, default));

        Assert.Contains(Textos(Assert.Single(http.Cuerpos)), t => t.Contains("USD 99", StringComparison.Ordinal));
    }

    [Fact] // el correo: el asunto y lo que recibe la plantilla, que ya no formatea.
    public async Task El_correo_lleva_el_subtotal_en_es_CO_en_el_asunto_y_en_la_plantilla()
    {
        var correo = Substitute.For<IEmailService>();
        var plantilla = Substitute.For<IEmailTemplateRenderer>();
        CartAbandonmentEmailModel? modelo = null;
        plantilla.RenderAsync(Arg.Any<string>(), Arg.Any<CartAbandonmentEmailModel>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                modelo = ci.ArgAt<CartAbandonmentEmailModel>(1);
                return Task.FromResult("<p>cuerpo</p>");
            });
        var canal = new EmailCartAbandonmentNotifier(correo, plantilla, Marca(), Formato, Ajustes(),
            NullLogger<EmailCartAbandonmentNotifier>.Instance);

        await EnEnUs(() => canal.NotifyAbandonedAsync(EnPesos, default));

        Assert.Equal("$ 123.500", modelo!.SubtotalFormatted);
        await correo.Received(1).SendAsync(
            Arg.Is<EmailMessage>(m => m.Subject == "SynergosLabs · Carrito abandonado: $ 123.500"), Arg.Any<CancellationToken>());
    }

    [Fact] // la plantilla escribe lo que recibe y no vuelve a formatear con la cultura del hilo.
    public void La_plantilla_del_correo_no_formatea_el_importe()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Synergos.CMS.sln")))
        {
            dir = dir.Parent;
        }
        Assert.NotNull(dir);

        var vista = File.ReadAllText(Path.Combine(dir!.FullName, "Synergos.CMS.Web", "Views", "Emails", "CartAbandonment.cshtml"));

        Assert.Contains("@Model.SubtotalFormatted", vista, StringComparison.Ordinal);
        Assert.DoesNotContain("Subtotal.ToString(", vista, StringComparison.Ordinal);
    }
}

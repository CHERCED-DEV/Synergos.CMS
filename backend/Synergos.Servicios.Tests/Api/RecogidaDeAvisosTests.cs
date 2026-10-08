using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Synergos.Api.Notifications.Contracts;
using Synergos.Api.Notifications.Domain;
using Synergos.Api.Notifications.Storage;
using Synergos.Api.Notifications.Transport;

namespace Synergos.CMS.Tests.Api;

/// <summary>
/// La carpeta de recogida de <c>Api.Notifications</c>: el aviso se verifica ABRIENDO el <c>.eml</c>,
/// y sólo existe en Development (ADR 0140 F3).
/// </summary>
/// <remarks>
/// <para><b>Se abre el fichero, no se mira el log</b>: sin transporte, el log dice lo que quería
/// mandar y no lo que salió. El <c>.eml</c> se decodifica acá —cabeceras plegadas, asunto en
/// RFC 2047, cuerpo en base64— con lo justo y sin paquetes, porque lo que importa es que lo que
/// quedó en disco diga lo que el destinatario leería.</para>
/// </remarks>
public sealed class RecogidaDeAvisosTests : IDisposable
{
    private const string Llave = "llave-de-la-recogida";

    private readonly string _raiz = Path.Combine(Path.GetTempPath(), "recogida-" + Guid.NewGuid().ToString("N"));

    private string Buzon => Path.Combine(_raiz, "buzon");

    public void Dispose()
    {
        try { if (Directory.Exists(_raiz)) Directory.Delete(_raiz, recursive: true); }
        catch (IOException) { /* el host puede tener un fichero aún abierto; es un temporal */ }
    }

    private WebApplicationFactory<SaveTemplateRequest> Avisos(string entorno = "Development")
        => new WebApplicationFactory<SaveTemplateRequest>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment(entorno);
            b.UseSetting("Notifications:Storage:Root", Path.Combine(_raiz, "almacen"));
            b.UseSetting("Notifications:ApiKey", Llave);
            b.UseSetting("Notifications:Pickup:Directory", Buzon);
        });

    private static async Task<HttpResponseMessage> Post(HttpClient http, string ruta, object cuerpo, string llave)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, ruta) { Content = JsonContent.Create(cuerpo) };
        req.Headers.Add("Idempotency-Key", llave);
        return await http.SendAsync(req);
    }

    [Fact]
    public async Task Un_envio_deja_UN_eml_con_su_destinatario_su_asunto_en_UTF8_y_los_valores_codificados()
    {
        using var fabrica = Avisos();
        using var http = fabrica.CreateClient();
        http.DefaultRequestHeaders.Add("X-Synergos-Key", Llave);

        using var plantilla = await Post(http, "/v1/templates", new SaveTemplateRequest(
            "prueba.recogida", "Email", "Tus entradas están listas, {nombre}", "<p>Hola {nombre}: {detalle}</p>"), "plantilla-1");
        Assert.Equal(HttpStatusCode.Created, plantilla.StatusCode);

        var envio = new SendRequest("prueba.persona", "p1", "ana@ejemplo.co", "prueba.recogida",
            new Dictionary<string, string> { ["nombre"] = "Ana María", ["detalle"] = "<b>2 & más</b>" });
        using var primera = await Post(http, "/v1/deliveries", envio, "envio-1");
        using var repetida = await Post(http, "/v1/deliveries", envio, "envio-1");

        Assert.Equal(HttpStatusCode.Created, primera.StatusCode);
        var entrega = (await primera.Content.ReadFromJsonAsync<DeliveryResponse>())!;
        Assert.Equal("Accepted", entrega.Status);

        // UNO aunque se repita con la misma llave: la idempotencia del servicio va antes que el transporte.
        var eml = Assert.Single(Directory.GetFiles(Buzon, "*.eml"));
        var (cabeceras, cuerpo) = Abrir(eml);

        Assert.Contains("ana@ejemplo.co", cabeceras["To"], StringComparison.Ordinal);
        Assert.Equal("Tus entradas están listas, Ana María", Decodificar(cabeceras["Subject"]));
        Assert.Contains("Hola Ana María: &lt;b&gt;2 &amp; más&lt;/b&gt;", cuerpo, StringComparison.Ordinal);
        Assert.False(string.IsNullOrWhiteSpace(cabeceras[PickupNotificationSender.CabeceraDelEnvio]));
    }

    [Fact]
    public void Fuera_de_Development_la_recogida_no_arranca()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Elegir("Production", ("Notifications:Pickup:Directory", Buzon)));

        Assert.Contains("sólo vale en Development", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Fuera_de_Development_el_host_de_verdad_no_arranca_con_recogida()
    {
        using var fabrica = Avisos("Production");

        var ex = Record.Exception(() => fabrica.CreateClient());

        Assert.NotNull(ex);
        Assert.Contains("sólo vale en Development", ex!.ToString(), StringComparison.Ordinal);
        Assert.False(Directory.Exists(Buzon));
    }

    [Fact]
    public void Recogida_y_Resend_a_la_vez_no_arrancan()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Elegir("Development",
            ("Notifications:Pickup:Directory", Buzon), ("Notifications:Resend:ApiKey", "re_x")));

        Assert.Contains("uno o el otro", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Sin_recogida_ni_Resend_sigue_el_que_rechaza_y_cada_uno_elige_el_suyo()
    {
        Assert.IsType<LoggingNotificationSender>(Elegir("Development"));
        Assert.IsType<LoggingNotificationSender>(Elegir("Production"));
        Assert.IsType<ResendNotificationSender>(Elegir("Production", ("Notifications:Resend:ApiKey", "re_x")));
        Assert.IsType<PickupNotificationSender>(Elegir("Development", ("Notifications:Pickup:Directory", Buzon)));
    }

    // ── Lo justo para elegir sin host y para abrir un .eml ──────────────────

    private sealed class Entorno(string nombre) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = nombre;
        public string ApplicationName { get; set; } = "Synergos.Api.Notifications";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private static INotificationSender Elegir(string entorno, params (string Clave, string Valor)[] config)
    {
        var configuracion = new ConfigurationBuilder()
            .AddInMemoryCollection(config.Select(c => new KeyValuePair<string, string?>(c.Clave, c.Valor)))
            .Build();
        var servicios = new ServiceCollection().AddLogging();
        EleccionDelTransporte.Registrar(servicios, configuracion, new Entorno(entorno));
        return servicios.BuildServiceProvider().GetRequiredService<INotificationSender>();
    }

    /// <summary>Las cabeceras (desplegadas) y el cuerpo decodificado de un <c>.eml</c> de una sola parte.</summary>
    private static (Dictionary<string, string> Cabeceras, string Cuerpo) Abrir(string ruta)
    {
        var texto = File.ReadAllText(ruta, Encoding.ASCII);
        var corte = texto.IndexOf("\r\n\r\n", StringComparison.Ordinal);
        Assert.True(corte > 0, "El .eml no separa cabeceras y cuerpo.");

        var cabeceras = Regex.Replace(texto[..corte], "\r\n[ \t]+", " ")
            .Split("\r\n")
            .Select(l => l.Split(':', 2))
            .Where(p => p.Length == 2)
            .GroupBy(p => p[0].Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First()[1].Trim(), StringComparer.OrdinalIgnoreCase);

        Assert.Equal("base64", cabeceras["Content-Transfer-Encoding"]);
        var cuerpo = Encoding.UTF8.GetString(Convert.FromBase64String(texto[(corte + 4)..].Replace("\r\n", string.Empty)));
        return (cabeceras, cuerpo);
    }

    /// <summary>Un valor de cabecera con palabras codificadas (RFC 2047), en B o en Q.</summary>
    private static string Decodificar(string valor)
    {
        var palabras = Regex.Matches(valor, @"=\?([^?]+)\?([BbQq])\?([^?]*)\?=");
        if (palabras.Count == 0) return valor;

        var texto = new StringBuilder();
        foreach (Match p in palabras)
        {
            var bytes = p.Groups[2].Value is "B" or "b"
                ? Convert.FromBase64String(p.Groups[3].Value)
                : Regex.Replace(p.Groups[3].Value.Replace('_', ' '), "=([0-9A-Fa-f]{2})",
                        m => ((char)Convert.ToByte(m.Groups[1].Value, 16)).ToString())
                    .Select(c => (byte)c).ToArray();
            texto.Append(Encoding.GetEncoding(p.Groups[1].Value).GetString(bytes));
        }
        return texto.ToString();
    }
}

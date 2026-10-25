using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Synergos.Bff.Core;
using Synergos.Bff.Core.Flow;
using Synergos.Bff.Pasos;
using Synergos.CMS.Tests.Architecture;
using Synergos.CMS.Tests.Bff.FlujoDeMuestra;
using Synergos.Shared;

namespace Synergos.CMS.Tests.Bff;

/// <summary>
/// La oferta declarada del núcleo (P12; NM-1 de 010, 001-P11 supuesto A con N1): un orquestador declarado la monta con
/// <c>{Origen}:Oferta:Clase</c>, publica el precio de la pieza y el de cada variante contra <c>Api.Pricing</c> real, y su
/// flujo cotiza con <c>forma.una-linea-de-oferta</c> el MISMO nombre que publicó.
/// </summary>
public sealed class OfertaDeclaradaTests
{
    private const string Llave = "llave-de-la-muestra";
    private const string Clase = "muestra.curso";

    private static object Oferta(
        string pieza = "mate-1", decimal precio = 900_000m, string moneda = "COP", params (string Codigo, decimal Total)[] variantes)
        => new { pieza, precio, moneda, variantes = variantes.Select(v => new { codigo = v.Codigo, total = v.Total }).ToList() };

    private static readonly (string, decimal)[] Planes = [("contado", 850_000m), ("premium", 1_200_000m), ("beca", 0m)];

    [Fact]
    public async Task Publicar_fija_el_precio_de_la_pieza_y_el_de_cada_variante_y_el_flujo_cotiza_ese_mismo_nombre()
    {
        await using var a = await Academia.Arrancar();

        var (estado, publicada) = await a.Publicar(Oferta(variantes: Planes));

        Assert.True(estado == HttpStatusCode.OK, publicada.ToString());
        Assert.Equal(4, publicada.GetProperty("precios").GetInt32());
        Assert.Equal(new[] { "contado", "premium", "beca" }, publicada.GetProperty("variantes").EnumerateArray().Select(v => v.GetString()!));
        Assert.Equal(900_000m, await a.Total(null));
        Assert.Equal(1_200_000m, await a.Total("premium"));
        Assert.Equal(850_000m, await a.Total("contado"));
        Assert.Equal(0m, await a.Total("beca"));
    }

    [Fact]
    public async Task Republicar_sin_una_variante_la_deja_de_cotizar_y_las_demas_siguen()
    {
        await using var a = await Academia.Arrancar();
        Assert.Equal(HttpStatusCode.OK, (await a.Publicar(Oferta(variantes: Planes))).Estado);

        var (estado, cuerpo) = await a.Publicar(Oferta(precio: 950_000m, variantes: [("contado", 800_000m), ("beca", 0m)]), "oferta-2");

        Assert.True(estado == HttpStatusCode.OK, cuerpo.ToString());
        Assert.Equal(3, cuerpo.GetProperty("precios").GetInt32());
        Assert.Equal("pricing.price_not_in_effect", (await a.Abrir("premium", "m-premium")).Cuerpo.GetProperty("code").GetString());
        Assert.Equal(950_000m, await a.Total(null));
        Assert.Equal(800_000m, await a.Total("contado"));
    }

    [Fact]
    public async Task Retirar_vence_la_pieza_y_sus_variantes_juntas_y_repetir_da_lo_mismo()
    {
        await using var a = await Academia.Arrancar();
        Assert.Equal(HttpStatusCode.OK, (await a.Publicar(Oferta(variantes: Planes))).Estado);

        var (estado, retirada) = await a.Retirar("mate-1");
        var (otra, repetida) = await a.Retirar("mate-1", "retiro-2");

        Assert.True(estado == HttpStatusCode.OK, retirada.ToString());
        Assert.True(retirada.GetProperty("retirada").GetBoolean());
        Assert.Equal(0, retirada.GetProperty("precios").GetInt32());
        Assert.Equal(HttpStatusCode.OK, otra);
        Assert.True(repetida.GetProperty("retirada").GetBoolean());
        foreach (var plan in new string?[] { null, "contado", "premium", "beca" })
        {
            Assert.Equal("pricing.price_not_in_effect", (await a.Abrir(plan, $"m-{plan ?? "pieza"}")).Cuerpo.GetProperty("code").GetString());
        }

        Assert.Equal(HttpStatusCode.OK, (await a.Publicar(Oferta(variantes: [("contado", 700_000m)]), "oferta-3")).Estado);
        Assert.Equal(700_000m, await a.Total("contado"));
    }

    [Fact]
    public async Task Repetir_la_publicacion_con_su_llave_da_la_misma_oferta()
    {
        await using var a = await Academia.Arrancar();

        var (_, una) = await a.Publicar(Oferta(variantes: Planes));
        var (estado, otra) = await a.Publicar(Oferta(variantes: Planes));

        Assert.Equal(HttpStatusCode.OK, estado);
        Assert.Equal(una.ToString(), otra.ToString());
        Assert.Equal(1_200_000m, await a.Total("premium"));
    }

    [Fact]
    public async Task Retirar_lo_que_nunca_se_publico_es_que_no_existe()
    {
        await using var a = await Academia.Arrancar();

        foreach (var pieza in new[] { "mate-1", "..%2Fotra" })
        {
            var (estado, cuerpo) = await a.Retirar(pieza);

            Assert.Equal(HttpStatusCode.NotFound, estado);
            Assert.Equal("muestra.oferta_no_existe", cuerpo.GetProperty("code").GetString());
        }
    }

    public static TheoryData<string, object> Invalidas() => new()
    {
        { "pieza", Oferta(pieza: "mate/1") },
        { "precio", Oferta(precio: -1m) },
        { "moneda", Oferta(moneda: "PESOS") },
        { "variantes.codigo", Oferta(variantes: [("pre/mium", 1m)]) },
        { "variantes.total", Oferta(variantes: [("premium", -1m)]) },
        { "variantes.codigo", Oferta(variantes: [("premium", 1m), ("premium", 2m)]) },
    };

    [Theory]
    [MemberData(nameof(Invalidas))]
    public async Task Una_oferta_invalida_nombra_el_campo_y_no_publica_nada(string campo, object oferta)
    {
        await using var a = await Academia.Arrancar();

        var (estado, cuerpo) = await a.Publicar(oferta);

        Assert.Equal(HttpStatusCode.BadRequest, estado);
        Assert.Equal("muestra.oferta_invalida", cuerpo.GetProperty("code").GetString());
        Assert.Contains($"«{campo}»", cuerpo.GetProperty("detail").GetString(), StringComparison.Ordinal);
        Assert.Equal("pricing.price_not_found", (await a.Abrir(null, "m-nada")).Cuerpo.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Sin_llave_no_se_publica()
    {
        await using var a = await Academia.Arrancar();

        var (estado, _) = await a.Publicar(Oferta(), llave: null);

        Assert.Equal(HttpStatusCode.BadRequest, estado);
        Assert.Equal("pricing.price_not_found", (await a.Abrir(null, "m-nada")).Cuerpo.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Montar_la_oferta_sin_su_clase_no_arranca_y_nombra_la_clave()
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Academia.Arrancar(clase: null));

        Assert.Contains("«muestra:Oferta:Clase»", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Una_clase_que_no_es_una_clase_no_arranca()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Academia.Arrancar(clase: "Muestra Curso"));
    }

    // ── El orquestador de la muestra con la oferta montada ──────────────────

    private sealed class Academia : IAsyncDisposable
    {
        private readonly string _raiz = Path.Combine(Path.GetTempPath(), "oferta-declarada-" + Guid.NewGuid().ToString("N"));
        private WebApplication? _app;

        public ArnesDeCapacidades Capacidades { get; } = new ArnesDeCapacidades()
            .Levanta<Synergos.Api.Pricing.Contracts.PriceResponse>("pricing")
            .Levanta<Synergos.Api.Payments.Contracts.PaymentResponse>("payments");

        public HttpClient Cliente { get; private set; } = null!;

        public static async Task<Academia> Arrancar(string? clase = Clase)
        {
            var a = new Academia();
            try
            {
                var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
                builder.WebHost.UseTestServer();
                builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["muestra:Flujos:Carpeta"] = Path.Combine(Proyectos.Dir("Synergos.Servicios.Tests"), "Bff", "OfertaDeclarada", "flujos"),
                    ["muestra:Storage:Root"] = a._raiz,
                    ["muestra:Oferta:Clase"] = clase,
                    ["Muestra:ApiKey"] = Llave,
                });
                builder.Services.AddSingleton<IHttpClientFactory>(a.Capacidades);
                builder.AddCorrelation();
                builder.AddOrquestadorDeclarado(ArranqueDeLaMuestra.Vocabulario, typeof(OfertaDeclaradaTests).Assembly);

                a._app = builder.Build();
                a._app.UseCorrelation();
                a._app.UseSharedKeyAuth(a._app.Configuration["Muestra:ApiKey"]);
                a._app.MapFlujosDeclarados();
                a._app.MapOfertaDeclarada();
                await a._app.StartAsync();
                a.Cliente = a._app.GetTestClient();
                a.Cliente.DefaultRequestHeaders.Add("X-Synergos-Key", Llave);
                return a;
            }
            catch
            {
                await a.DisposeAsync();
                throw;
            }
        }

        public async ValueTask DisposeAsync()
        {
            Cliente?.Dispose();
            if (_app is not null) await _app.DisposeAsync();
            Capacidades.Dispose();
            try { if (Directory.Exists(_raiz)) Directory.Delete(_raiz, recursive: true); }
            catch (IOException) { /* un temporal */ }
        }

        public Task<(HttpStatusCode Estado, JsonElement Cuerpo)> Publicar(object oferta, string? llave = "oferta-1")
        {
            var peticion = new HttpRequestMessage(HttpMethod.Post, "v1/ofertas") { Content = JsonContent.Create(oferta) };
            if (llave is not null) peticion.Headers.Add("Idempotency-Key", llave);
            return Enviar(peticion);
        }

        public Task<(HttpStatusCode Estado, JsonElement Cuerpo)> Retirar(string pieza, string llave = "retiro-1")
        {
            var peticion = new HttpRequestMessage(HttpMethod.Post, $"v1/ofertas/{pieza}/retirar");
            peticion.Headers.Add("Idempotency-Key", llave);
            return Enviar(peticion);
        }

        /// <summary>Abre la matrícula de la muestra por la puerta: el curso como referencia de su clase y el plan, anulable.</summary>
        public Task<(HttpStatusCode Estado, JsonElement Cuerpo)> Abrir(string? plan, string llave, string curso = "mate-1")
        {
            var peticion = new HttpRequestMessage(HttpMethod.Post, "v1/flujos/muestra.matricula")
            {
                Content = JsonContent.Create(new { curso = new { kind = Clase, id = curso }, plan }),
            };
            peticion.Headers.Add("Idempotency-Key", llave);
            // Un alumno por matrícula: lo que se mide es el precio, no el tope de sagas de un dueño.
            peticion.Headers.TryAddWithoutValidation(CabecerasDeLaPuerta.Sujeto, $"muestra.alumno:{llave}");
            return Enviar(peticion);
        }

        /// <summary>El total con que abre la matrícula del plan: lo que cotizó el flujo con el nombre de la oferta.</summary>
        public async Task<decimal> Total(string? plan)
        {
            var (estado, cuerpo) = await Abrir(plan, $"m-{plan ?? "pieza"}-{Guid.NewGuid():N}");
            Assert.True(estado == HttpStatusCode.Created, $"La matrícula del plan «{plan}» no abrió: {(int)estado} {cuerpo}");
            return cuerpo.GetProperty("datos").GetProperty("total").GetProperty("amount").GetDecimal();
        }

        private async Task<(HttpStatusCode, JsonElement)> Enviar(HttpRequestMessage peticion)
        {
            using (peticion)
            {
                using var r = await Cliente.SendAsync(peticion);
                var texto = await r.Content.ReadAsStringAsync();
                return (r.StatusCode, string.IsNullOrWhiteSpace(texto) ? default : JsonDocument.Parse(texto).RootElement.Clone());
            }
        }
    }
}

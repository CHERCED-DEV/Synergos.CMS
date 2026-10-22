using System.Buffers.Text;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Synergos.Bff.Core;
using Synergos.Bff.Core.Flow;
using Synergos.Bff.Tienda.Contracts;
using Synergos.Shared;

namespace Synergos.CMS.Tests.Bff;

/// <summary>
/// La compra de la tienda DE VERDAD por el flujo declarado (004-tienda-compra): el <c>Program</c> de
/// <c>Bff.Tienda</c> sirviendo <c>tienda.compra</c> contra Pricing, Inventory, Payments, Orders, Fulfillment y
/// Notifications reales en el arnés, con lo que pone la puerta en cabeceras.
/// </summary>
/// <remarks>
/// Lo que ninguna capacidad sabe hacer a pedido —caerse o decir que no— va guionado en el cable; lo demás es la
/// capacidad real, y lo que quedó se mira donde quedó (el cobro en Payments, las existencias en Inventory, el
/// pedido en Orders, el envío en Fulfillment, el correo en Notifications).
/// </remarks>
internal sealed class CompraDeTiendaReal : IDisposable
{
    public const string Llave = "llave-de-tienda";
    public const string Producto = "tienda.producto";

    private readonly string _raiz = Path.Combine(Path.GetTempPath(), "compra-tienda-" + Guid.NewGuid().ToString("N"));
    private readonly WebApplicationFactory<PurchaseResponse> _fabrica;

    /// <param name="existencias">Las unidades de cada producto (p-1, p-2:azul, p-3).</param>
    /// <param name="precios">El precio de cada producto; uno que falta no tiene precio publicado.</param>
    /// <param name="cae">Las rutas (<c>METODO /ruta</c>, «*» un segmento) que contestan 503, como una capacidad caída.</param>
    /// <param name="rechaza">Las que contestan 409 con un rechazo definitivo (no transitorio).</param>
    /// <param name="conRecogida">Notifications deja cada correo como <c>.eml</c> en <see cref="Buzon"/>.</param>
    /// <param name="pierdeLaPrimera">La primera petición cuya ruta termina así LLEGA a la capacidad y su respuesta se
    /// pierde: el orquestador ve una caída.</param>
    /// <param name="ajustes">Configuración del orquestador, además de la del arnés (<c>Tienda:Cupon:…</c>).</param>
    /// <param name="reloj">El reloj del orquestador (el de sus sagas y sus barridos); sin él, el del sistema.</param>
    /// <param name="envolver">Una envoltura sobre el cable hacia las capacidades (la compuerta de los turnos).</param>
    public CompraDeTiendaReal(
        IReadOnlyDictionary<string, int>? existencias = null, IReadOnlyDictionary<string, decimal>? precios = null,
        IReadOnlyCollection<string>? cae = null, IReadOnlyCollection<string>? rechaza = null, bool conRecogida = false,
        string? pierdeLaPrimera = null, IReadOnlyDictionary<string, string>? ajustes = null, TimeProvider? reloj = null,
        Func<IHttpClientFactory, IHttpClientFactory>? envolver = null)
    {
        Capacidades = new ArnesDeCapacidades()
            .Levanta<Synergos.Api.Pricing.Contracts.PriceResponse>("pricing")
            .Levanta<Synergos.Api.Inventory.Contracts.StockItemResponse>("inventory")
            .Levanta<Synergos.Api.Payments.Contracts.PaymentResponse>("payments")
            .Levanta<Synergos.Api.Orders.Contracts.OrderResponse>("orders")
            .Levanta<Synergos.Api.Fulfillment.Contracts.ShipmentResponse>("fulfillment")
            .Levanta<Synergos.Api.Notifications.Contracts.SaveTemplateRequest>(
                "notifications", conRecogida ? [("Notifications:Pickup:Directory", Buzon)] : []);
        Aprovisionar(
            existencias ?? new Dictionary<string, int> { ["p-1"] = 10, ["p-2:azul"] = 10, ["p-3"] = 10 },
            precios ?? new Dictionary<string, decimal> { ["p-1"] = 20_000m, ["p-2:azul"] = 35_000m, ["p-3"] = 5_000m })
            .GetAwaiter().GetResult();

        var cable = new Cable(Capacidades, cae ?? [], rechaza ?? [], pierdeLaPrimera);
        _cable = cable;
        var fabrica = envolver?.Invoke(cable) ?? cable;
        _fabrica = new WebApplicationFactory<PurchaseResponse>().WithWebHostBuilder(b =>
        {
            b.UseSetting("tienda:Storage:Root", Raiz);
            b.UseSetting("Tienda:ApiKey", Llave);
            foreach (var (clave, valor) in ajustes ?? new Dictionary<string, string>()) b.UseSetting(clave, valor);
            b.ConfigureTestServices(s =>
            {
                s.AddSingleton(fabrica);
                if (reloj is not null) s.AddSingleton(reloj);
            });
        });
        Orquestador = _fabrica.CreateClient();
        Orquestador.DefaultRequestHeaders.Add(SharedKeyAuth.HeaderName, Llave);
        _llamadas = cable.Llamadas;
    }

    public ArnesDeCapacidades Capacidades { get; }

    public HttpClient Orquestador { get; }

    public IServiceProvider Servicios => _fabrica.Services;

    /// <summary>
    /// Lo que la compra pidió a las capacidades, <c>METODO /ruta</c>, sin la vuelta del barrido de avisos
    /// (<c>GET /v1/deliveries/queued</c>), que corre sola desde que el orquestador arranca.
    /// </summary>
    public IReadOnlyList<string> Llamadas => _llamadas.Where(l => l != "GET /v1/deliveries/queued").ToList();

    private readonly IReadOnlyList<string> _llamadas;

    private readonly Cable _cable;

    /// <summary>La capacidad caída vuelve: la ruta deja de contestar 503.</summary>
    public void Encender(string ruta) => _cable.Encender(ruta);

    /// <summary>El ítem de existencias de cada producto.</summary>
    public Dictionary<string, string> Items { get; } = new(StringComparer.Ordinal);

    public string Raiz => Path.Combine(_raiz, "tienda");

    public string Buzon => Path.Combine(_raiz, "buzon");

    public SagaEngine<SagaDeFlujo> Sagas => Servicios.GetRequiredService<SagaEngine<SagaDeFlujo>>();

    public void Dispose()
    {
        Orquestador.Dispose();
        _fabrica.Dispose();
        Capacidades.Dispose();
        try { if (Directory.Exists(_raiz)) Directory.Delete(_raiz, recursive: true); }
        catch (IOException) { /* un temporal */ }
    }

    private async Task Aprovisionar(IReadOnlyDictionary<string, int> existencias, IReadOnlyDictionary<string, decimal> precios)
    {
        foreach (var (producto, unidades) in existencias)
        {
            var item = await Post("inventory", "v1/items", new { subjectKind = Producto, subjectId = producto, onHand = unidades }, "item-" + producto);
            Items[producto] = item.GetProperty("id").GetString()!;
        }
        foreach (var (producto, precio) in precios)
        {
            await Post("pricing", "v1/prices", new
            {
                subjectKind = Producto, subjectId = producto, amount = new { amount = precio, currency = "COP" }, taxRateBasisPoints = 0,
            }, "precio-" + producto);
        }

        // La plantilla de VERDAD, la que publica provisionar.sh: el correo que se abre es el que recibiría quien compra.
        using var plantillas = JsonDocument.Parse(File.ReadAllText(Path.Combine(
            Synergos.CMS.Tests.Architecture.Proyectos.Raiz(), "tools", "provisionar.plantillas.json")));
        // Y la de la guardia (la de una compensación que se rindió), la misma que publica provisionar.sh.
        foreach (var clave in new[] { "tienda.compra.confirmada", "bff.compensacion.colgada" })
        {
            var aviso = plantillas.RootElement.EnumerateArray()
                .Single(e => e.TryGetProperty("key", out var k) && k.GetString() == clave);
            await Post("notifications", "v1/templates", new
            {
                key = aviso.GetProperty("key").GetString(), channel = aviso.GetProperty("channel").GetString(),
                subject = aviso.GetProperty("subject").GetString(), body = aviso.GetProperty("body").GetString(),
            }, "plantilla-" + clave);
        }
    }

    private async Task<JsonElement> Post(string capacidad, string ruta, object cuerpo, string llave)
    {
        using var http = Capacidades.CreateClient(capacidad);
        using var peticion = new HttpRequestMessage(HttpMethod.Post, ruta) { Content = JsonContent.Create(cuerpo) };
        peticion.Headers.Add("Idempotency-Key", llave);
        using var r = await http.SendAsync(peticion);
        var texto = await r.Content.ReadAsStringAsync();
        Assert.True(r.StatusCode == HttpStatusCode.Created, $"{capacidad} {ruta}: {(int)r.StatusCode} {texto}");
        return JsonDocument.Parse(texto).RootElement.Clone();
    }

    private static string Base64(object valor) => Base64Url.EncodeToString(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(valor)));

    private static HttpRequestMessage DeLaPuerta(HttpMethod metodo, string ruta, string sujeto, object? cuerpo = null)
    {
        var peticion = new HttpRequestMessage(metodo, ruta) { Content = cuerpo is null ? null : JsonContent.Create(cuerpo) };
        peticion.Headers.TryAddWithoutValidation(CabecerasDeLaPuerta.Sujeto, $"tienda.comprador:{sujeto}");
        return peticion;
    }

    private static HttpRequestMessage ConNegocio(HttpRequestMessage peticion)
    {
        peticion.Headers.TryAddWithoutValidation(CabecerasDeLaPuerta.Negocio, Base64(new { carrier = "servientrega" }));
        return peticion;
    }

    /// <summary>Una línea del cuerpo: el producto con su clase y la cantidad.</summary>
    public static object Linea(string producto, int cantidad, string kind = Producto)
        => new { producto = new { kind, id = producto }, cantidad };

    /// <summary>La canasta de siempre: p-1 ×2, p-2 azul ×1, p-3 ×4 = 40.000 + 35.000 + 20.000 = 95.000.</summary>
    public static object[] Canasta() => [Linea("p-1", 2), Linea("p-2:azul", 1), Linea("p-3", 4)];

    public static object Direccion() => new { linea1 = "Calle 1 # 2-3", ciudad = "Bogotá", pais = "CO" };

    public Task<(HttpStatusCode Estado, JsonElement Cuerpo)> Cotizar(object[]? lineas = null, string? promocion = null, string sujeto = "u-1")
        => Enviar(ConNegocio(DeLaPuerta(HttpMethod.Post, "v1/flujos/tienda.compra/cotizar", sujeto,
            new { lineas = lineas ?? Canasta(), promocion })));

    public Task<(HttpStatusCode Estado, JsonElement Cuerpo)> Abrir(
        object[]? lineas = null, string llave = "compra-1", string sujeto = "u-1", string? promocion = null)
    {
        var peticion = ConNegocio(DeLaPuerta(HttpMethod.Post, "v1/flujos/tienda.compra", sujeto, new { lineas = lineas ?? Canasta(), promocion }));
        peticion.Headers.Add("Idempotency-Key", llave);
        return Enviar(peticion);
    }

    public Task<(HttpStatusCode Estado, JsonElement Cuerpo)> Cerrar(string id, string sujeto = "u-1", object? direccion = null, bool sinCuerpo = false)
    {
        var peticion = DeLaPuerta(HttpMethod.Post, $"v1/flujos/tienda.compra/{id}/cerrar", sujeto,
            sinCuerpo ? null : new { direccion = direccion ?? Direccion() });
        peticion.Headers.TryAddWithoutValidation(CabecerasDeLaPuerta.Contacto, Base64(new
        {
            correo = "comprador@ejemplo.co", nombre = "Ana", enlace = "https://sitio.ejemplo/tienda/?compra=" + id, sitio = "Tienda de prueba",
        }));
        return Enviar(peticion);
    }

    public Task<(HttpStatusCode Estado, JsonElement Cuerpo)> Consultar(string id, string sujeto = "u-1")
        => Enviar(DeLaPuerta(HttpMethod.Get, $"v1/flujos/tienda.compra/{id}", sujeto));

    public Task<(HttpStatusCode Estado, JsonElement Cuerpo)> Cancelar(string id, string sujeto = "u-1")
        => Enviar(DeLaPuerta(HttpMethod.Post, $"v1/flujos/tienda.compra/{id}/cancelar", sujeto));

    /// <summary>
    /// Devolver parte de una compra (US 7): el flujo posterior <c>tienda.devolucion</c>, del personal —el sujeto es
    /// quien devuelve, no quien compró—, como lo abre la puerta del CMS al llegar la RMA a «reembolsada».
    /// </summary>
    public Task<(HttpStatusCode Estado, JsonElement Cuerpo)> Devolver(string compra, object monto, string llave, string personal = "staff-1")
    {
        var peticion = new HttpRequestMessage(HttpMethod.Post, "v1/flujos/tienda.devolucion") { Content = JsonContent.Create(new { compra, monto }) };
        peticion.Headers.TryAddWithoutValidation(CabecerasDeLaPuerta.Sujeto, $"tienda.personal:{personal}");
        peticion.Headers.Add("Idempotency-Key", llave);
        return Enviar(peticion);
    }

    /// <summary>Un monto en el cuerpo, como lo lee el tipo <c>dinero</c>.</summary>
    public static object Pesos(decimal monto, string moneda = "COP") => new { amount = monto, currency = moneda };

    /// <summary>
    /// Una promoción publicada en Pricing, como la crearía quien opera el sitio: <c>Percentage</c> en puntos básicos
    /// (1000 = 10 %) o <c>Fixed</c> en pesos.
    /// </summary>
    public Task Promocion(string codigo, string tipo, decimal valor, DateTimeOffset desde, DateTimeOffset hasta)
        => Post("pricing", "v1/promotions", new
        {
            code = codigo, kind = tipo, value = valor, currency = "COP", validFrom = desde, validTo = hasta,
        }, "promocion-" + codigo);

    /// <summary>Publica la oferta de un producto, como lo hace el CMS: sin cabeceras de la puerta, con llave.</summary>
    public Task<(HttpStatusCode Estado, JsonElement Cuerpo)> Publicar(object oferta, string llave)
    {
        var peticion = new HttpRequestMessage(HttpMethod.Post, "v1/ofertas") { Content = JsonContent.Create(oferta) };
        peticion.Headers.Add("Idempotency-Key", llave);
        return Enviar(peticion);
    }

    /// <summary>Retira la oferta entera de un producto, como lo hace el CMS al despublicarlo.</summary>
    public Task<(HttpStatusCode Estado, JsonElement Cuerpo)> Retirar(string producto, string llave)
    {
        var peticion = new HttpRequestMessage(HttpMethod.Post, $"v1/ofertas/{Uri.EscapeDataString(producto)}/retirar");
        peticion.Headers.Add("Idempotency-Key", llave);
        return Enviar(peticion);
    }

    /// <summary>El pozo de existencias de un sujeto de la tienda (<c>p-1</c>, <c>p-2:azul</c>), o nada si no existe.</summary>
    public async Task<JsonElement?> Pozo(string sujeto)
    {
        using var http = Capacidades.CreateClient("inventory");
        using var r = await http.GetAsync($"v1/items?subjectKind={Producto}&subjectId={Uri.EscapeDataString(sujeto)}");
        if (r.StatusCode == HttpStatusCode.NotFound) return null;
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        return await r.Content.ReadFromJsonAsync<JsonElement>();
    }

    public string Dato(string id, string nombre) => Sagas.Find(id)!.Datos[nombre].Valor!.Value.GetString()!;

    public int Veces(string llamada) => Llamadas.Count(l => l == llamada);

    public int Posicion(Func<string, bool> llamada) => Llamadas.ToList().FindIndex(l => llamada(l));

    public async Task<JsonElement> Leer(string capacidad, string ruta)
    {
        using var http = Capacidades.CreateClient(capacidad);
        return await http.GetFromJsonAsync<JsonElement>(ruta);
    }

    public async Task<int> Disponibles(string producto)
        => (await Leer("inventory", $"v1/items/{Items[producto]}")).GetProperty("available").GetInt32();

    public async Task<int> Existentes(string producto)
        => (await Leer("inventory", $"v1/items/{Items[producto]}")).GetProperty("onHand").GetInt32();

    private async Task<(HttpStatusCode, JsonElement)> Enviar(HttpRequestMessage peticion)
    {
        using (peticion)
        {
            using var r = await Orquestador.SendAsync(peticion);
            var texto = await r.Content.ReadAsStringAsync();
            // Un 400 del framework (un cuerpo que falta) no trae Rechazo ni JSON: se mira su estado y nada más.
            var esJson = r.Content.Headers.ContentType?.MediaType?.Contains("json", StringComparison.Ordinal) == true;
            return (r.StatusCode, !esJson || string.IsNullOrWhiteSpace(texto) ? default : JsonDocument.Parse(texto).RootElement.Clone());
        }
    }

    /// <summary>Las capacidades reales, con las caídas y los rechazos pedidos.</summary>
    private sealed class Cable(
        IHttpClientFactory real, IReadOnlyCollection<string> cae, IReadOnlyCollection<string> rechaza, string? pierdeLaPrimera)
        : IHttpClientFactory
    {
        private int _perdidas;

        private readonly HashSet<string> _caidas = new(cae, StringComparer.Ordinal);

        public List<string> Llamadas { get; } = [];

        public void Encender(string ruta)
        {
            lock (_caidas) _caidas.Remove(ruta);
        }

        private string? PierdeLaPrimera { get; } = pierdeLaPrimera;

        private bool Cae(string ruta)
        {
            lock (_caidas) return _caidas.Any(c => Coincide(c, ruta));
        }

        private IReadOnlyCollection<string> Rechaza { get; } = rechaza;

        private static bool Coincide(string patron, string ruta)
            => System.Text.RegularExpressions.Regex.IsMatch(ruta,
                "^" + System.Text.RegularExpressions.Regex.Escape(patron).Replace("\\*", "[^/]+", StringComparison.Ordinal) + "$");

        public HttpClient CreateClient(string name)
        {
            var interno = real.CreateClient(name);
            return new HttpClient(new Paso(this, interno)) { BaseAddress = interno.BaseAddress };
        }

        private sealed class Paso(Cable cable, HttpClient interno) : HttpMessageHandler
        {
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
            {
                var ruta = $"{r.Method.Method} {r.RequestUri!.AbsolutePath}";
                lock (cable.Llamadas) cable.Llamadas.Add(ruta);

                if (cable.Cae(ruta)) return Problema(HttpStatusCode.ServiceUnavailable, "guion.caida");
                if (cable.Rechaza.Any(c => Coincide(c, ruta))) return Problema(HttpStatusCode.Conflict, "guion.rechazo");

                using var copia = new HttpRequestMessage(r.Method, r.RequestUri) { Content = r.Content };
                foreach (var h in r.Headers) copia.Headers.TryAddWithoutValidation(h.Key, h.Value);
                var respuesta = await interno.SendAsync(copia, ct);
                if (cable.PierdeLaPrimera is { } sufijo && r.RequestUri.AbsolutePath.EndsWith(sufijo, StringComparison.Ordinal)
                    && Interlocked.Increment(ref cable._perdidas) == 1)
                {
                    respuesta.Dispose();
                    throw new HttpRequestException("La respuesta se perdió por el camino.");
                }
                return respuesta;
            }

            private static HttpResponseMessage Problema(HttpStatusCode estado, string codigo) => new(estado)
            {
                Content = new StringContent($$"""{"code":"{{codigo}}","detail":"guionado en el cable"}""", Encoding.UTF8, "application/problem+json"),
            };
        }
    }
}

/// <summary>Las historias 1 y 2 de la compra de la tienda por el flujo declarado, contra las capacidades reales.</summary>
public sealed class CompraDeTiendaTests
{
    [Fact]
    public async Task Abre_cotiza_aparta_registra_y_autoriza_y_al_cerrar_captura_consume_despacha_cumple_y_avisa()
    {
        using var t = new CompraDeTiendaReal();

        var (estado, abierta) = await t.Abrir();
        Assert.True(estado == HttpStatusCode.Created, abierta.ToString());
        Assert.Equal("Running", abierta.GetProperty("estado").GetString());
        var datos = abierta.GetProperty("datos");
        Assert.Equal(95_000m, datos.GetProperty("total").GetProperty("amount").GetDecimal());
        var lineas = datos.GetProperty("lineasDelPedido").EnumerateArray().ToList();
        Assert.Equal(3, lineas.Count);
        Assert.Equal(35_000m, lineas.Single(l => l.GetProperty("sujeto").GetProperty("id").GetString() == "p-2:azul")
            .GetProperty("precioUnitario").GetProperty("amount").GetDecimal());
        Assert.Equal(8, await t.Disponibles("p-1"));
        var id = abierta.GetProperty("id").GetString()!;
        var cobro = t.Dato(id, "cobroId");
        Assert.Equal("Authorized", (await t.Leer("payments", $"v1/payments/{cobro}")).GetProperty("status").GetString());

        var (cerro, cerrada) = await t.Cerrar(id);
        Assert.True(cerro == HttpStatusCode.OK, cerrada.ToString());
        Assert.Equal("Completed", cerrada.GetProperty("estado").GetString());

        Assert.Equal("Captured", (await t.Leer("payments", $"v1/payments/{cobro}")).GetProperty("status").GetString());
        Assert.Equal(8, await t.Existentes("p-1"));
        Assert.Equal(6, await t.Existentes("p-3"));
        var pedido = cerrada.GetProperty("datos").GetProperty("pedido").GetString()!;
        Assert.Equal("Fulfilled", (await t.Leer("orders", $"v1/orders/{pedido}")).GetProperty("status").GetString());
        var envio = cerrada.GetProperty("datos").GetProperty("envio").GetString()!;
        Assert.Equal("servientrega", (await t.Leer("fulfillment", $"v1/shipments/{envio}")).GetProperty("carrier").GetString());
        Assert.Equal(1, (await t.Leer("notifications", "v1/deliveries?toKind=tienda.comprador&toId=u-1")).GetProperty("items").GetArrayLength());

        // El orden del cierre lo fija el oráculo: capturar → consumir → despachar → cumplir.
        var captura = t.Posicion(l => l.EndsWith("/capture", StringComparison.Ordinal));
        var consumo = t.Posicion(l => l.EndsWith("/consume", StringComparison.Ordinal));
        var despacho = t.Posicion(l => l == "POST /v1/shipments");
        var cumple = t.Posicion(l => l.EndsWith("/fulfill", StringComparison.Ordinal));
        Assert.True(captura < consumo && consumo < despacho && despacho < cumple, string.Join(" | ", t.Llamadas));
    }

    /// <summary>
    /// El aviso al comprador es UN correo de verdad (el <c>.eml</c> que deja Notifications, no una fila en un
    /// log), con el número de la compra y el total cobrado; cerrar otra vez —el reintento de una respuesta
    /// perdida— no manda otro (004-tienda-compra, T029).
    /// </summary>
    [Fact]
    public async Task El_correo_sale_UNA_vez_con_el_numero_y_el_total_y_reconfirmar_no_manda_otro()
    {
        using var t = new CompraDeTiendaReal(conRecogida: true);
        var (_, abierta) = await t.Abrir();
        var id = abierta.GetProperty("id").GetString()!;

        Assert.Equal(HttpStatusCode.OK, (await t.Cerrar(id)).Estado);
        Assert.Equal(HttpStatusCode.OK, (await t.Cerrar(id)).Estado);

        var correo = File.ReadAllText(Assert.Single(Directory.GetFiles(t.Buzon, "*.eml")));
        Assert.Contains("comprador@ejemplo.co", correo, StringComparison.Ordinal);
        // El cuerpo va en base64 (PickupNotificationSender): se lee lo que leería quien lo abre.
        var cuerpo = CuerpoDelCorreo(correo);
        Assert.Contains(abierta.GetProperty("numero").GetString()!, cuerpo, StringComparison.Ordinal);
        Assert.Contains("95.000 COP", cuerpo, StringComparison.Ordinal);
    }

    /// <summary>El cuerpo de un <c>.eml</c> de la carpeta de recogida, que lo escribe en base64.</summary>
    private static string CuerpoDelCorreo(string eml)
    {
        var inicio = eml.IndexOf("\r\n\r\n", StringComparison.Ordinal);
        Assert.True(inicio > 0, "El .eml no separa las cabeceras del cuerpo.");
        var base64 = string.Concat(eml[(inicio + 4)..].Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0));
        return System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(base64));
    }

    [Fact]
    public async Task Cotizar_dice_el_total_y_las_lineas_sin_apartar_ni_cobrar_nada()
    {
        using var t = new CompraDeTiendaReal();

        var (estado, cotizada) = await t.Cotizar();

        Assert.True(estado == HttpStatusCode.OK, cotizada.ToString());
        Assert.Equal(95_000m, cotizada.GetProperty("datos").GetProperty("total").GetProperty("amount").GetDecimal());
        Assert.DoesNotContain(t.Llamadas, l => l.EndsWith("/holds", StringComparison.Ordinal) || l == "POST /v1/payments" || l == "POST /v1/orders");
    }

    [Fact]
    public async Task Repetir_abrir_con_la_misma_llave_no_aparta_registra_ni_autoriza_dos_veces()
    {
        using var t = new CompraDeTiendaReal();

        var (_, primera) = await t.Abrir();
        var (estado, segunda) = await t.Abrir();

        Assert.True(estado is HttpStatusCode.Created or HttpStatusCode.OK, segunda.ToString());
        Assert.Equal(primera.GetProperty("id").GetString(), segunda.GetProperty("id").GetString());
        Assert.Equal(1, t.Veces("POST /v1/payments"));
        Assert.Equal(1, t.Veces("POST /v1/orders"));
        Assert.Equal(8, await t.Disponibles("p-1"));
    }

    [Fact]
    public async Task Confirmar_dos_veces_no_captura_consume_ni_avisa_dos_veces()
    {
        using var t = new CompraDeTiendaReal();
        var (_, abierta) = await t.Abrir();
        var id = abierta.GetProperty("id").GetString()!;

        await t.Cerrar(id);
        var (estado, otra) = await t.Cerrar(id);

        Assert.True(estado == HttpStatusCode.OK, otra.ToString());
        Assert.Equal("Completed", otra.GetProperty("estado").GetString());
        Assert.Equal(1, t.Llamadas.Count(l => l.EndsWith("/capture", StringComparison.Ordinal)));
        Assert.Equal(8, await t.Existentes("p-1"));
        Assert.Equal(1, (await t.Leer("notifications", "v1/deliveries?toKind=tienda.comprador&toId=u-1")).GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task La_compra_de_otro_miembro_es_como_si_no_existiera()
    {
        using var t = new CompraDeTiendaReal();
        var (_, abierta) = await t.Abrir();
        var id = abierta.GetProperty("id").GetString()!;

        var (consulta, _) = await t.Consultar(id, sujeto: "intruso");
        var (cierre, _) = await t.Cerrar(id, sujeto: "intruso");
        var (cancela, _) = await t.Cancelar(id, sujeto: "intruso");
        var (inexistente, _) = await t.Consultar("no-existe");

        Assert.Equal(inexistente, consulta);
        Assert.Equal(HttpStatusCode.NotFound, consulta);
        Assert.Equal(HttpStatusCode.NotFound, cierre);
        Assert.Equal(HttpStatusCode.NotFound, cancela);
        Assert.DoesNotContain(t.Llamadas, l => l.EndsWith("/capture", StringComparison.Ordinal) || l.EndsWith("/void", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Sin_direccion_completa_se_rechaza_ANTES_de_capturar()
    {
        using var t = new CompraDeTiendaReal();
        var (_, abierta) = await t.Abrir();
        var id = abierta.GetProperty("id").GetString()!;

        var (sinCuerpo, _) = await t.Cerrar(id, sinCuerpo: true);
        var (sinCiudad, _) = await t.Cerrar(id, direccion: new { linea1 = "Calle 1", pais = "CO" });

        Assert.Equal(HttpStatusCode.BadRequest, sinCuerpo);
        Assert.Equal(HttpStatusCode.BadRequest, sinCiudad);
        Assert.DoesNotContain(t.Llamadas, l => l.EndsWith("/capture", StringComparison.Ordinal));
        Assert.Equal("Authorized", (await t.Leer("payments", $"v1/payments/{t.Dato(id, "cobroId")}")).GetProperty("status").GetString());
    }

    [Fact]
    public async Task La_saga_no_guarda_ni_la_direccion_ni_el_contacto()
    {
        using var t = new CompraDeTiendaReal();
        var (_, abierta) = await t.Abrir();
        await t.Cerrar(abierta.GetProperty("id").GetString()!);

        var guardado = string.Concat(Directory.EnumerateFiles(t.Raiz, "*", SearchOption.AllDirectories).Select(File.ReadAllText));
        Assert.NotEmpty(guardado);
        Assert.DoesNotContain("Calle 1", guardado, StringComparison.Ordinal);
        Assert.DoesNotContain("comprador@ejemplo.co", guardado, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Un_producto_de_otra_clase_no_se_lee_del_cuerpo()
    {
        // NR-1: la definición fija la clase de lo que llega del cuerpo; un aforo de Eventos no es un producto.
        using var t = new CompraDeTiendaReal();

        var (estado, _) = await t.Abrir([CompraDeTiendaReal.Linea("evt-1/GEN", 1, kind: "eventos.aforo")]);

        Assert.Equal(HttpStatusCode.BadRequest, estado);
        Assert.True(t.Llamadas.Count == 0, string.Join(" | ", t.Llamadas));
    }

    [Theory]
    [InlineData(0, "tienda.cart_empty")]
    [InlineData(51, "tienda.cart_too_large")]
    public async Task Una_compra_vacia_o_de_mas_de_50_lineas_se_rechaza_sin_tocar_nada(int cuantas, string codigo)
    {
        using var t = new CompraDeTiendaReal();

        var (estado, cuerpo) = await t.Abrir(Enumerable.Range(0, cuantas).Select(i => CompraDeTiendaReal.Linea($"x-{i}", 1)).ToArray());

        Assert.NotEqual(HttpStatusCode.Created, estado);
        Assert.Equal(codigo, cuerpo.GetProperty("code").GetString());
        Assert.Empty(t.Llamadas);
    }

    [Fact]
    public async Task Una_cantidad_cero_o_un_producto_repetido_se_rechazan_sin_tocar_nada()
    {
        using var t = new CompraDeTiendaReal();

        var (_, cero) = await t.Abrir([CompraDeTiendaReal.Linea("p-1", 0)], llave: "a");
        var (_, repetido) = await t.Abrir([CompraDeTiendaReal.Linea("p-1", 1), CompraDeTiendaReal.Linea("p-1", 2)], llave: "b");

        Assert.Equal("tienda.bad_line", cero.GetProperty("code").GetString());
        Assert.Equal("tienda.bad_line", repetido.GetProperty("code").GetString());
        Assert.Empty(t.Llamadas);
    }

    [Fact]
    public async Task Cancelar_antes_de_confirmar_suelta_anula_y_libera_sin_cobrar()
    {
        using var t = new CompraDeTiendaReal();
        var (_, abierta) = await t.Abrir();
        var id = abierta.GetProperty("id").GetString()!;
        var cobro = t.Dato(id, "cobroId");
        var pedido = t.Dato(id, "pedido");

        var (estado, cancelada) = await t.Cancelar(id);

        Assert.True(estado == HttpStatusCode.OK, cancelada.ToString());
        Assert.Equal("Compensated", cancelada.GetProperty("estado").GetString());
        Assert.Equal(10, await t.Disponibles("p-1"));
        Assert.Equal("Voided", (await t.Leer("payments", $"v1/payments/{cobro}")).GetProperty("status").GetString());
        Assert.Equal("Cancelled", (await t.Leer("orders", $"v1/orders/{pedido}")).GetProperty("status").GetString());
    }
}

/// <summary>
/// La historia 2: si algo falla a mitad, no queda plata cobrada sin mercancía. Los mismos puntos de fallo que fija
/// el oráculo del motor imperativo (<c>PurchaseCompensationTests</c>), contra el flujo declarado y las capacidades reales.
/// </summary>
public sealed class CompraDeTiendaFallosTests
{
    [Fact]
    public async Task Sin_existencias_en_la_tercera_linea_se_sueltan_las_dos_primeras_sin_pedido_ni_cobro()
    {
        using var t = new CompraDeTiendaReal(existencias: new Dictionary<string, int> { ["p-1"] = 10, ["p-2:azul"] = 10, ["p-3"] = 2 });

        var (estado, cuerpo) = await t.Abrir();

        Assert.NotEqual(HttpStatusCode.Created, estado);
        Assert.Equal("inventory.insufficient_stock", cuerpo.GetProperty("code").GetString());
        Assert.Equal(10, await t.Disponibles("p-1"));
        Assert.Equal(10, await t.Disponibles("p-2:azul"));
        Assert.DoesNotContain(t.Llamadas, l => l == "POST /v1/orders" || l == "POST /v1/payments");
    }

    [Fact]
    public async Task Un_pago_rechazado_suelta_los_apartados_y_anula_el_pedido()
    {
        using var t = new CompraDeTiendaReal(rechaza: ["POST /v1/payments"]);

        var (estado, cuerpo) = await t.Abrir();

        Assert.NotEqual(HttpStatusCode.Created, estado);
        Assert.Equal("guion.rechazo", cuerpo.GetProperty("code").GetString());
        Assert.Equal(10, await t.Disponibles("p-1"));
        Assert.Equal(1, t.Llamadas.Count(l => l.StartsWith("POST /v1/orders/", StringComparison.Ordinal) && l.EndsWith("/cancel", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Si_el_despacho_falla_DESPUES_de_capturar_se_devuelve_se_repone_y_se_anula_el_pedido()
    {
        using var t = new CompraDeTiendaReal(rechaza: ["POST /v1/shipments"]);
        var (_, abierta) = await t.Abrir();
        var id = abierta.GetProperty("id").GetString()!;
        var cobro = t.Dato(id, "cobroId");
        var pedido = t.Dato(id, "pedido");

        var (estado, cuerpo) = await t.Cerrar(id);

        Assert.NotEqual(HttpStatusCode.OK, estado);
        Assert.Equal("guion.rechazo", cuerpo.GetProperty("code").GetString());
        var (_, consultada) = await t.Consultar(id);
        Assert.Equal("Compensated", consultada.GetProperty("estado").GetString());
        // Un cobro devuelto sigue «Captured» en Api.Payments: lo devuelto va aparte, y lo devolvible queda en cero.
        var pago = await t.Leer("payments", $"v1/payments/{cobro}");
        Assert.Equal(95_000m, pago.GetProperty("refunded").GetProperty("amount").GetDecimal());
        Assert.Equal(0m, pago.GetProperty("refundable").GetProperty("amount").GetDecimal());
        Assert.Equal(10, await t.Existentes("p-1"));
        Assert.Equal(10, await t.Existentes("p-3"));
        Assert.Equal("Cancelled", (await t.Leer("orders", $"v1/orders/{pedido}")).GetProperty("status").GetString());
    }

    [Fact]
    public async Task Si_el_pedido_no_cumple_tras_despachar_la_compra_se_completa_y_el_pedido_queda_para_conciliar()
    {
        using var t = new CompraDeTiendaReal(rechaza: ["POST /v1/orders/*/fulfill"]);
        var (_, abierta) = await t.Abrir();
        var id = abierta.GetProperty("id").GetString()!;

        var (estado, cerrada) = await t.Cerrar(id);

        Assert.True(estado == HttpStatusCode.OK, cerrada.ToString());
        Assert.Equal("Completed", cerrada.GetProperty("estado").GetString());
        Assert.Equal("Captured", (await t.Leer("payments", $"v1/payments/{t.Dato(id, "cobroId")}")).GetProperty("status").GetString());
        Assert.NotEqual("Cancelled", (await t.Leer("orders", $"v1/orders/{t.Dato(id, "pedido")}")).GetProperty("status").GetString());
        Assert.DoesNotContain(t.Llamadas, l => l.EndsWith("/cancel", StringComparison.Ordinal) || l.EndsWith("/refund", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Un_aviso_que_no_sale_no_deshace_la_compra()
    {
        using var t = new CompraDeTiendaReal(rechaza: ["POST /v1/deliveries"]);
        var (_, abierta) = await t.Abrir();
        var id = abierta.GetProperty("id").GetString()!;

        var (estado, cerrada) = await t.Cerrar(id);

        Assert.True(estado == HttpStatusCode.OK, cerrada.ToString());
        Assert.Equal("Completed", cerrada.GetProperty("estado").GetString());
        Assert.Equal("Captured", (await t.Leer("payments", $"v1/payments/{t.Dato(id, "cobroId")}")).GetProperty("status").GetString());
    }

    [Fact]
    public async Task Con_total_cero_se_cumple_sin_autorizar_ni_capturar()
    {
        using var t = new CompraDeTiendaReal(precios: new Dictionary<string, decimal> { ["p-1"] = 0m });

        var (_, abierta) = await t.Abrir([CompraDeTiendaReal.Linea("p-1", 1)]);
        var (estado, cerrada) = await t.Cerrar(abierta.GetProperty("id").GetString()!);

        Assert.True(estado == HttpStatusCode.OK, cerrada.ToString());
        Assert.Equal("Completed", cerrada.GetProperty("estado").GetString());
        Assert.DoesNotContain(t.Llamadas, l => l.StartsWith("POST /v1/payments", StringComparison.Ordinal));
        Assert.Equal(9, await t.Existentes("p-1"));
    }

    [Fact]
    public async Task Un_producto_sin_precio_publicado_se_rechaza_antes_de_apartar()
    {
        using var t = new CompraDeTiendaReal(precios: new Dictionary<string, decimal> { ["p-1"] = 20_000m });

        var (estado, cuerpo) = await t.Abrir();

        Assert.NotEqual(HttpStatusCode.Created, estado);
        Assert.StartsWith("pricing.", cuerpo.GetProperty("code").GetString(), StringComparison.Ordinal);
        Assert.DoesNotContain(t.Llamadas, l => l.EndsWith("/holds", StringComparison.Ordinal));
    }
}

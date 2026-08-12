using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Services;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// La tienda comprando de verdad contra <c>Bff.Tienda</c> (HU #24).
/// </summary>
/// <remarks>
/// <para>Lo que se prueba acá no es «compra». Es lo que puede salir mal cuando la compra cruza
/// seis servicios y el CMS no es dueño de ninguno:</para>
///
/// <list type="bullet">
///   <item>que un timeout <b>no se trague la excepción</b> y muestre «compra exitosa» — es el
///   peor resultado posible de esta HU;</item>
///   <item>que reintentar <b>no cree dos pedidos</b>;</item>
///   <item>que un 401 sea un <b>defecto de despliegue</b> y no un mensaje para el comprador;</item>
///   <item>que un rechazo del negocio <b>llegue con su motivo</b> — «se agotó mientras
///   comprabas» es accionable y «error» no lo es.</item>
/// </list>
/// </remarks>
public sealed class HttpShopOrderServiceTests
{
    /// <summary>Un árbol de servicios de mentira que se puede tirar a mitad de una compra.</summary>
    private sealed class ServiciosFalsos : HttpMessageHandler
    {
        private readonly Dictionary<string, Func<HttpResponseMessage>> _rutas = new(StringComparer.OrdinalIgnoreCase);

        public List<(string Method, string Path, string? Key)> Llamadas { get; } = new();

        /// <summary>Lo que se manda, para poder mirar por dónde viajaba un dato personal (#47).</summary>
        public List<string> Cuerpos { get; } = new();

        public HashSet<string> Caidas { get; } = new(StringComparer.OrdinalIgnoreCase);

        public ServiciosFalsos Ok(string ruta, string json)
        {
            _rutas[ruta] = () => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
            return this;
        }

        public ServiciosFalsos Falla(string ruta, HttpStatusCode codigo, string code, string detail)
        {
            _rutas[ruta] = () => new HttpResponseMessage(codigo)
            {
                Content = new StringContent(
                    $$"""{"title":"x","status":{{(int)codigo}},"detail":"{{detail}}","code":"{{code}}"}""",
                    Encoding.UTF8, "application/problem+json"),
            };
            return this;
        }

        public ServiciosFalsos Caida(string ruta) { Caidas.Add(ruta); return this; }

        public int Veces(string method, string sufijo)
            => Llamadas.Count(l => l.Method == method && l.Path.EndsWith(sufijo, StringComparison.Ordinal));

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
        {
            var path = req.RequestUri!.AbsolutePath;
            var clave = $"{req.Method.Method} {path}";
            req.Headers.TryGetValues("Idempotency-Key", out var k);
            Llamadas.Add((req.Method.Method, path, k?.FirstOrDefault()));

            if (req.Content is not null)
            {
                Cuerpos.Add(req.Content.ReadAsStringAsync(ct).GetAwaiter().GetResult());
            }

            if (Caidas.Contains(clave)) throw new HttpRequestException("guionado: caída");

            return Task.FromResult(_rutas.TryGetValue(clave, out var f)
                ? f()
                : new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    private sealed class FabricaFalsa : IHttpClientFactory
    {
        private readonly ServiciosFalsos _handler;
        public FabricaFalsa(ServiciosFalsos h) => _handler = h;
        public HttpClient CreateClient(string name)
            => new(_handler, disposeHandler: false) { BaseAddress = new Uri("http://tienda.local/") };
    }

    private sealed class Monitor<T> : IOptionsMonitor<T>
    {
        public Monitor(T v) => CurrentValue = v;
        public T CurrentValue { get; }
        public T Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<T, string?> l) => null;
    }

    private const string CanastaOk = """{"id":"c-1"}""";
    private const string CompraOk = """
        {"id":"p-1","buyerKind":"tienda.comprador","buyerId":"ana@ejemplo.co","cartId":"c-1",
         "status":"Running","total":{"amount":119000,"currency":"COP"},"orderId":"o-1",
         "shipmentId":null,"heldLines":1,"pendingCompensations":0,"lastError":null}
        """;

    private static ServiciosFalsos Feliz() => new ServiciosFalsos()
        .Ok("POST /v1/carts", CanastaOk)
        .Ok("POST /v1/carts/c-1/lines", CanastaOk)
        .Ok("POST /v1/purchases", CompraOk)
        .Ok("GET /v1/purchases/p-1", CompraOk)
        .Ok("POST /v1/purchases/p-1/confirm", CompraOk.Replace("\"Running\"", "\"Completed\""));

    private static HttpShopOrderService Nuevo(ServiciosFalsos svc)
        => new(new FabricaFalsa(svc),
               new Monitor<TiendaSettings>(new TiendaSettings { Mode = "Bff", Carrier = "servientrega" }),
               NullLogger<HttpShopOrderService>.Instance);

    private static readonly IReadOnlyList<ShopCartItem> UnItem = new[] { new ShopCartItem("sku-1", null, 2) };
    private static readonly ShopCustomer Ana = new("Ana", "ana@ejemplo.co");
    private static readonly ShopShippingAddress Casa =
        new("Calle 1 #2-3", null, "Bogotá", "Cundinamarca", "110111", "CO", "Ana");

    // ── Comprar ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Comprar_abre_la_canasta_le_pone_las_lineas_y_recien_ahi_compra()
    {
        // La costura que la HU daba por resuelta: POST /v1/purchases recibe un cartId, no una
        // lista. La canasta es la única fuente de qué se está comprando.
        var svc = Feliz();

        var r = await Nuevo(svc).CheckoutAsync(UnItem, Ana);

        Assert.Equal("p-1", r.OrderRef);
        Assert.Equal(119000m, r.Amount);
        Assert.Equal(1, svc.Veces("POST", "/v1/carts"));
        Assert.Equal(1, svc.Veces("POST", "/lines"));
        Assert.Equal(1, svc.Veces("POST", "/v1/purchases"));
    }

    [Fact]
    public async Task La_llave_de_idempotencia_es_la_MISMA_en_dos_intentos_iguales()
    {
        // Es lo que hace que reintentar tras un timeout no cree dos pedidos. Con un Guid nuevo
        // por intento, el segundo compraría otra vez lo mismo.
        var a = Feliz();
        var b = Feliz();

        await Nuevo(a).CheckoutAsync(UnItem, Ana);
        await Nuevo(b).CheckoutAsync(UnItem, Ana);

        var llaveA = a.Llamadas.Single(l => l.Path == "/v1/purchases").Key;
        var llaveB = b.Llamadas.Single(l => l.Path == "/v1/purchases").Key;

        Assert.False(string.IsNullOrWhiteSpace(llaveA));
        Assert.Equal(llaveA, llaveB);
    }

    [Fact]
    public async Task Reordenar_la_canasta_NO_cambia_la_llave()
    {
        // Si cambiara, el comprador que mueve una línea en pantalla y reintenta compraría dos
        // veces. Por eso las líneas entran ordenadas al hash.
        var a = Feliz();
        var b = Feliz();
        var dos = new[] { new ShopCartItem("sku-1", null, 2), new ShopCartItem("sku-2", "v1", 1) };
        var alReves = new[] { new ShopCartItem("sku-2", "v1", 1), new ShopCartItem("sku-1", null, 2) };

        await Nuevo(a).CheckoutAsync(dos, Ana);
        await Nuevo(b).CheckoutAsync(alReves, Ana);

        Assert.Equal(
            a.Llamadas.Single(l => l.Path == "/v1/purchases").Key,
            b.Llamadas.Single(l => l.Path == "/v1/purchases").Key);
    }

    [Fact]
    public async Task Dos_compradores_distintos_NO_comparten_llave()
    {
        var a = Feliz();
        var b = Feliz();

        await Nuevo(a).CheckoutAsync(UnItem, Ana);
        await Nuevo(b).CheckoutAsync(UnItem, new ShopCustomer("Beto", "beto@ejemplo.co"));

        Assert.NotEqual(
            a.Llamadas.Single(l => l.Path == "/v1/purchases").Key,
            b.Llamadas.Single(l => l.Path == "/v1/purchases").Key);
    }

    [Fact]
    public async Task Con_sesion_la_llave_sale_del_member_y_no_del_correo()
    {
        // El memberKey es la identidad de confianza-servidor. Dos correos distintos del MISMO
        // member —cambió el suyo entre dos intentos— no pueden ser dos compras.
        var a = Feliz();
        var b = Feliz();
        var key = Guid.NewGuid();

        await Nuevo(a).CheckoutAsync(UnItem, new ShopCustomer("Ana", "ana@ejemplo.co", key));
        await Nuevo(b).CheckoutAsync(UnItem, new ShopCustomer("Ana", "otro@ejemplo.co", key));

        Assert.Equal(
            a.Llamadas.Single(l => l.Path == "/v1/purchases").Key,
            b.Llamadas.Single(l => l.Path == "/v1/purchases").Key);
    }

    // ── Lo que puede salir mal ──────────────────────────────────────────────

    [Fact]
    public async Task Un_timeout_NO_se_traga_la_excepcion_ni_dice_compra_exitosa()
    {
        // EL PEOR RESULTADO POSIBLE de esta HU es un cobro sin pedido, y el segundo peor es
        // decirle a alguien que compró cuando no. Tiene que reventar, y con un mensaje que sea
        // CIERTO.
        var svc = Feliz().Caida("POST /v1/purchases");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Nuevo(svc).CheckoutAsync(UnItem, Ana));

        Assert.Contains("No se te cobró", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Ante_un_timeout_se_PREGUNTA_si_la_compra_existio_antes_de_rendirse()
    {
        // Un timeout no dice «no se cobró»: dice «no sé». Y como la llave ES el identificador de
        // la saga, se puede consultar. Sin esto, un tiempo de espera agotado con la compra ya
        // creada dejaría al comprador viendo un error sobre una compra que sí ocurrió — y
        // reintentando a ciegas.
        var svc = Feliz().Caida("POST /v1/purchases");
        var llave = HttpShopOrderService.IdempotencyKeyFor(
            HttpShopOrderService.BuyerId(Ana), UnItem);
        svc.Ok($"GET /v1/purchases/{llave}", CompraOk);

        var r = await Nuevo(svc).CheckoutAsync(UnItem, Ana);

        Assert.Equal("p-1", r.OrderRef);
    }

    /// <summary>
    /// Quien compra sin registrarse viaja SEUDONIMIZADO: el orquestador no tiene por qué guardar
    /// un correo (defecto #47).
    /// </summary>
    /// <remarks>
    /// La saga persiste el <c>buyerId</c>. Con el correo en crudo, <c>Bff.Tienda</c> acababa con
    /// un fichero lleno de direcciones de gente que compró como invitada, en un servicio que no
    /// tiene ninguna razón para saber quién es nadie. Eventos y la visita al inmueble (#33a) ya
    /// lo hacían así; Tienda era el que faltaba.
    /// </remarks>
    [Fact]
    public async Task El_comprador_invitado_viaja_seudonimizado()
    {
        var id = HttpShopOrderService.BuyerId(Ana);

        Assert.DoesNotContain("@", id, StringComparison.Ordinal);
        Assert.DoesNotContain("ana", id, StringComparison.OrdinalIgnoreCase);

        // Estable —el mismo comprador es el mismo aunque escriba el correo distinto— y distinto
        // por persona. Sin lo primero, cada compra sería de alguien nuevo y la idempotencia se
        // caería con él.
        Assert.Equal(id, HttpShopOrderService.BuyerId(new ShopCustomer("Otra", "  ANA@ejemplo.co ")));
        Assert.NotEqual(id, HttpShopOrderService.BuyerId(new ShopCustomer("Ana", "otra@ejemplo.co")));

        // Y lo que de verdad importa: no sale por el cable. Se mira el CUERPO, que es por donde
        // viajaba.
        var orq = Feliz();
        await Nuevo(orq).CheckoutAsync(UnItem, Ana);

        Assert.NotEmpty(orq.Cuerpos);
        Assert.All(orq.Cuerpos, c => Assert.DoesNotContain("ana@ejemplo.co", c, StringComparison.OrdinalIgnoreCase));
        Assert.Contains(orq.Cuerpos, c => c.Contains(id, StringComparison.Ordinal));
    }

    [Fact]
    public void Con_sesion_viaja_el_memberKey_y_no_se_vuelve_a_hashear()
    {
        // El memberKey ya es opaco. Hashearlo otra vez no agregaría nada y rompería la
        // correspondencia con el resto del CMS, que lo usa tal cual.
        var k = Guid.NewGuid();

        Assert.Equal(k.ToString("n"), HttpShopOrderService.BuyerId(new ShopCustomer("Ana", "ana@ejemplo.co", k)));
    }

    /// <summary>
    /// El correo que sale de una compra leída NO es el que devuelve el orquestador (defecto #47).
    /// </summary>
    /// <remarks>
    /// El orquestador no sabe quién compró: devuelve el mismo identificador opaco que el CMS le
    /// mandó. Copiarlo a <c>CustomerEmail</c> coincidía con el correo por accidente mientras el
    /// <c>buyerId</c> ERA el correo, y ya mentía para quien tiene sesión — devolvía el
    /// <c>memberKey</c> en hexadecimal donde la vista espera un correo.
    /// </remarks>
    [Fact]
    public async Task Una_compra_leida_NO_devuelve_el_buyerId_como_si_fuera_el_correo()
    {
        // El guion de `CompraOk` devuelve a propósito un `buyerId` CON FORMA DE CORREO
        // ("ana@ejemplo.co") aunque el CMS ya no mande ninguno: es el caso adversario. Lo que se
        // afirma no es «no hay correos por ahí», sino que este camino no lo copia venga como
        // venga.
        var svc = Feliz();

        var orden = await Nuevo(svc).GetOrderAsync("p-1");

        Assert.NotNull(orden);
        Assert.Equal(string.Empty, orden!.CustomerEmail);
        Assert.DoesNotContain("@", orden.CustomerEmail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Un_401_es_un_defecto_de_DESPLIEGUE_y_no_un_mensaje_para_el_comprador()
    {
        // La llave compartida está mal o no está. El comprador no puede hacer nada con eso, así
        // que afuera sale genérico — y adentro se grita.
        var svc = Feliz().Falla("POST /v1/purchases", HttpStatusCode.Unauthorized, "unauthorized", "llave invalida");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Nuevo(svc).CheckoutAsync(UnItem, Ana));

        Assert.DoesNotContain("llave", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("No se te cobró", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Un_403_SI_llega_con_su_motivo_porque_NO_es_un_fallo_de_llave()
    {
        // `SharedKeyAuth` responde 401 cuando la llave falla, NUNCA 403. Un 403 del árbol de
        // servicios es un rechazo de negocio —`consent.not_granted` es el que aparece de verdad—
        // y tragarse su motivo deja al comprador sin saber qué hacer.
        //
        // Este test existe porque el defecto apareció agendando contra los procesos vivos de
        // Salud (HU #25), no acá: el doble solo devolvía 403 para el caso de la llave, así que
        // confirmaba la misma suposición equivocada que el código.
        var svc = Feliz().Falla("POST /v1/purchases", HttpStatusCode.Forbidden,
            "consent.not_granted", "No hay consentimiento para 'tienda.compra'.");

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => Nuevo(svc).CheckoutAsync(UnItem, Ana));

        Assert.Contains("consentimiento", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Un_rechazo_del_negocio_llega_CON_SU_MOTIVO()
    {
        // «Se agotó mientras comprabas» es accionable; «error» no lo es. El motivo del rechazo
        // sí es del comprador.
        var svc = Feliz().Falla("POST /v1/purchases", HttpStatusCode.Conflict,
            "inventory.insufficient_stock", "Se pidieron 2 y queda 1 de sku-1.");

        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => Nuevo(svc).CheckoutAsync(UnItem, Ana));

        Assert.Contains("queda 1", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Con_el_BFF_apagado_las_LECTURAS_degradan_en_vez_de_reventar()
    {
        // La tienda tiene que seguir sirviendo: no se puede comprar, y lo dice, pero el catálogo
        // y las fichas no dependen de esto.
        var svc = Feliz().Caida("GET /v1/purchases/p-1");
        var cliente = Nuevo(svc);

        Assert.Null(await cliente.GetOrderAsync("p-1"));
        Assert.Empty(await cliente.GetOrdersByMemberAsync(Guid.NewGuid()));
    }

    // ── Confirmar ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Confirmar_sin_direccion_se_rechaza_ANTES_de_mover_plata()
    {
        // Descubrir que falta la dirección después de capturar costaría una devolución por un
        // campo de formulario. Y el motor en proceso la ignora, así que el contrato la deja
        // pasar: comprobarla es trabajo de este adaptador.
        var svc = Feliz();

        await Assert.ThrowsAsync<ArgumentException>(() => Nuevo(svc).ConfirmAsync("p-1"));

        Assert.Equal(0, svc.Veces("POST", "/confirm"));
    }

    [Fact]
    public async Task Confirmar_con_direccion_captura_y_despacha()
    {
        var svc = Feliz();

        var r = await Nuevo(svc).ConfirmAsync("p-1", Casa);

        Assert.Equal("Paid", r.Status);
        Assert.Equal(1, svc.Veces("POST", "/confirm"));
    }

    [Fact]
    public async Task El_estado_de_la_saga_se_traduce_a_lo_que_la_tienda_entiende()
    {
        // Compensated significa que se deshizo todo: para el comprador eso es «cancelada», no
        // «pendiente». Dejarlo en pendiente mostraría una compra viva que ya no existe.
        var svc = Feliz().Ok("GET /v1/purchases/p-1", CompraOk.Replace("\"Running\"", "\"Compensated\""));

        var orden = await Nuevo(svc).GetOrderAsync("p-1");

        Assert.Equal(OrderStatus.Cancelled, orden!.Status);
    }
}

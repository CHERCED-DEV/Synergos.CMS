using System.Net;
using System.Reflection;
using System.Text;
using Synergos.Bff.Tienda.Clients;
using Synergos.CMS.Tests.Contratos;
using Synergos.Core;

namespace Synergos.CMS.Tests.Bff;

/// <summary>
/// Lo que <c>Bff.Tienda</c> manda y lee de la capacidad de envíos cabe en el contrato que la capacidad publica
/// (013-seller-operaciones, T009 y T014; principio III): el gate consumidor → capacidad de
/// <see cref="ContratoConsumidorEventosTests"/>, para el despacho.
/// </summary>
/// <remarks>
/// <para><b>Nace con el despacho, y su alcance está escrito.</b> Recorre los métodos de <see cref="TiendaCapabilities"/>
/// que hablan con Fulfillment, que es lo que el personal empieza a leer y a mover. Los demás quedan censados en
/// <see cref="FueraDeEsteGate"/>, con su razón. Un método nuevo tiene que estar en uno de los dos: si no, hablaría con
/// su capacidad sin que nadie cruce su forma.</para>
///
/// <para>Cada recorrido llama al método con una grabadora en lugar del <see cref="IHttpClientFactory"/>, y la petición
/// que habría salido se valida con <see cref="SubconjuntoOpenApi"/>, igual que el <c>T</c> que lee.</para>
/// </remarks>
public sealed class ContratoConsumidorTiendaTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;
    private static readonly IdempotencyKey Llave = IdempotencyKey.Of("s-1:paso");

    /// <summary>Cómo llamar a cada método con argumentos plausibles; el valor no importa, la forma sí.</summary>
    private static readonly Dictionary<string, Func<TiendaCapabilities, Task>> Recorrido = new(StringComparer.Ordinal)
    {
        ["ShipAsync"] = c => c.ShipAsync(
            Ref.Create("orders.pedido", "o-1"), new { line1 = "Calle 1 # 2-3", city = "Bogotá", country = "CO" }, "servientrega", Llave, Ct),
        ["GetShipmentAsync"] = c => c.GetShipmentAsync("shp-1", Ct),
        ["TrackShipmentAsync"] = c => c.TrackShipmentAsync("shp-1", "InTransit", "GUIA-123", Ct),
    };

    private const string LaCompraEntera =
        "la compra entera lo cruza contra las capacidades REALES en el arnés (CompraDeTiendaTests y CompraDeTiendaFallosTests); " +
        "este gate nació con el despacho (013-seller-operaciones) y cubre Fulfillment";

    /// <summary>Los métodos que este gate no recorre, y quién cruza entonces su forma.</summary>
    private static readonly Dictionary<string, string> FueraDeEsteGate = new[]
    {
        "GetCartAsync", "CheckoutCartAsync", "QuoteAsync", "SetPriceAsync", "FindStockAsync", "DeclareStockAsync",
        "GetStockAsync", "AdjustStockAsync", "RestockAsync", "HoldStockAsync", "ReleaseStockAsync", "ConsumeStockAsync",
        "PlaceOrderAsync", "FulfillOrderAsync", "CancelOrderAsync", "GetOrderAsync", "AuthorizeAsync", "CaptureAsync",
        "VoidAsync", "RefundAsync", "GetPaymentAsync",
    }.ToDictionary(m => m, _ => LaCompraEntera, StringComparer.Ordinal);

    public static TheoryData<string> Metodos()
    {
        var datos = new TheoryData<string>();
        foreach (var m in Recorrido.Keys.Order(StringComparer.Ordinal)) datos.Add(m);
        return datos;
    }

    /// <summary>Los métodos de <see cref="TiendaCapabilities"/> que hablan con una capacidad.</summary>
    private static IReadOnlyList<MethodInfo> DelCliente()
        => typeof(TiendaCapabilities)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.ReturnType.IsGenericType && m.ReturnType.GetGenericTypeDefinition() == typeof(Task<>)
                     && m.ReturnType.GetGenericArguments()[0] is { IsGenericType: true } r
                     && r.GetGenericTypeDefinition() == typeof(Result<>))
            .ToList();

    [Fact]
    public void Cada_metodo_del_cliente_esta_recorrido_o_fuera_con_su_razon()
    {
        var declarados = DelCliente().Select(m => m.Name).Order(StringComparer.Ordinal).ToList();
        var clasificados = Recorrido.Keys.Concat(FueraDeEsteGate.Keys).Order(StringComparer.Ordinal).ToList();

        Assert.True(declarados.Count >= 24,
            $"Se descubrieron {declarados.Count} métodos Task<Result<T>> en TiendaCapabilities y son 24: el censo dejó de ver.");
        Assert.Empty(Recorrido.Keys.Intersect(FueraDeEsteGate.Keys));
        Assert.True(declarados.SequenceEqual(clasificados, StringComparer.Ordinal),
            "TiendaCapabilities y el censo de este gate no tienen los mismos métodos." +
            $"{Environment.NewLine}  sin clasificar: {string.Join(", ", declarados.Except(clasificados))}" +
            $"{Environment.NewLine}  clasificado sin método: {string.Join(", ", clasificados.Except(declarados))}");
    }

    [Theory]
    [MemberData(nameof(Metodos))]
    public async Task Lo_que_manda_y_lee_cabe_en_el_contrato_de_su_capacidad(string metodo)
    {
        var grabadora = new Grabadora();
        await Recorrido[metodo](new TiendaCapabilities(grabadora));
        Assert.True(grabadora.Peticiones.Count == 1, $"{metodo} hizo {grabadora.Peticiones.Count} llamadas y este gate espera una.");

        var p = grabadora.Peticiones[0];
        Assert.Equal(TiendaCapabilities.Fulfillment, p.Cliente);
        const string ensamblado = "Synergos.Api.Fulfillment";
        var doc = ContratoOpenApi.Comiteado(ensamblado);
        var quien = $"{metodo} → {p.Metodo} /{p.Ruta}";

        var op = SubconjuntoOpenApi.Operacion(doc, p.Metodo, "/" + p.Ruta);
        Assert.True(op is not null, $"{quien}: {ensamblado} no publica esa ruta con ese método.");

        var leido = DelCliente().Single(m => m.Name == metodo).ReturnType.GetGenericArguments()[0].GetGenericArguments()[0];
        var malas = SubconjuntoOpenApi.Peticion(doc, op!, p)
            .Concat(SubconjuntoOpenApi.Respuesta(doc, op!, leido))
            .ToList();

        Assert.True(malas.Count == 0,
            $"{quien} no cabe en el contrato de {ensamblado} ({op}):{Environment.NewLine}" +
            string.Join(Environment.NewLine, malas.Select(m => "  " + m)));
    }

    /// <summary>
    /// Un <see cref="IHttpClientFactory"/> que graba la petición y contesta <c>200 {}</c>: lo que se juzga es lo que
    /// SALE; lo que se lee se juzga por el tipo, no por esta respuesta.
    /// </summary>
    private sealed class Grabadora : IHttpClientFactory
    {
        public List<PeticionGrabada> Peticiones { get; } = [];

        public HttpClient CreateClient(string name)
            => new(new Cable(this, name)) { BaseAddress = new Uri("http://capacidad.local/") };

        private sealed class Cable(Grabadora grabadora, string cliente) : HttpMessageHandler
        {
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
            {
                var query = r.RequestUri!.Query.TrimStart('?')
                    .Split('&', StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => Uri.UnescapeDataString(x.Split('=')[0]))
                    .ToList();
                var cabeceras = r.Headers.Select(h => h.Key).ToList();
                var cuerpo = r.Content is null ? null : await r.Content.ReadAsStringAsync(ct);

                grabadora.Peticiones.Add(new PeticionGrabada(
                    cliente, r.Method.Method, r.RequestUri.AbsolutePath.TrimStart('/'), query, cabeceras, cuerpo));
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{}", Encoding.UTF8, "application/json"),
                };
            }
        }
    }
}

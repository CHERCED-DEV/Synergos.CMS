using System.Net;
using System.Reflection;
using System.Text;
using Synergos.Bff.Eventos.Clients;
using Synergos.CMS.Tests.Contratos;
using Synergos.Core;

namespace Synergos.CMS.Tests.Bff;

/// <summary>
/// Lo que <c>Bff.Eventos</c> manda y lee de cada capacidad cabe en el contrato que la capacidad
/// publica (ADR 0140, F2): el gate de compatibilidad consumidor → capacidad.
/// </summary>
/// <remarks>
/// <para><b>Por qué hace falta, con el oráculo de la F1 en verde.</b> Los tests de compra corren el
/// flujo contra dobles que contestan lo que el test les dice, así que un campo renombrado en una
/// capacidad —o en el cuerpo anónimo que arma <see cref="EventosCapabilities"/>— los deja en verde:
/// medido, con <c>forKind→forType</c>, <c>Available→AvailableUnits</c> o <c>currency→moneda</c> el
/// oráculo sigue 44/44 y en producción la capacidad tiraría el campo en silencio. Esto los cruza
/// contra el JSON comiteado de cada capacidad, que la deriva mantiene fiel a su código: consumidor →
/// documento → productor, cerrado por transitividad.</para>
///
/// <para><b>No se generan clientes</b> (NSwag, Kiota): lanzan excepción ante un no-2xx, que no encaja
/// con <c>Result</c>/<c>Rejection</c>, y generan DTO completos contra el «sólo los campos que usa» de
/// <see cref="EventosCapabilities"/>. Se reabre cuando los puertos de deshacer vivan en
/// <c>Bff.Pasos</c> con un segundo consumidor.</para>
///
/// <para><b>Un caso por método, y un censo.</b> Cada método público que devuelve
/// <c>Task&lt;Result&lt;T&gt;&gt;</c> tiene su recorrido: se le llama con una grabadora en lugar del
/// <see cref="IHttpClientFactory"/>, y la petición que habría salido —método, ruta, query, cabeceras,
/// cuerpo— se valida con <see cref="SubconjuntoOpenApi"/>, igual que el <c>T</c> que lee. Un método
/// nuevo sin recorrido es rojo en el censo: si no, quedaría fuera sin que nadie lo note.</para>
/// </remarks>
public sealed class ContratoConsumidorEventosTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;
    private static readonly Ref Algo = Ref.Create("eventos.compra", "s-1");
    private static readonly IdempotencyKey Llave = IdempotencyKey.Of("s-1:paso");
    private static readonly Money Monto = Money.Of(1000m, "COP");

    /// <summary>Cómo llamar a cada método con argumentos plausibles; el valor no importa, la forma sí.</summary>
    private static readonly Dictionary<string, Func<EventosCapabilities, Task>> Recorrido = new(StringComparer.Ordinal)
    {
        ["QuoteAsync"] = c => c.QuoteAsync([(Ref.Create("eventos.localidad", "e1/vip"), 2)], Ct),
        ["FindAforoAsync"] = c => c.FindAforoAsync(Ref.Create("eventos.aforo", "e1/vip/A-14"), Ct),
        ["HoldAforoAsync"] = c => c.HoldAforoAsync("po-1", 1, Algo, Llave, Ct),
        ["ReleaseAforoAsync"] = c => c.ReleaseAforoAsync("ah-1", Ct),
        ["ConsumeAforoAsync"] = c => c.ConsumeAforoAsync("ah-1", Ct),
        ["RestockAforoAsync"] = c => c.RestockAforoAsync("po-1", 1, Llave, Ct),
        ["AuthorizeAsync"] = c => c.AuthorizeAsync(Algo, Ref.Create("eventos.comprador", "u-1"), Monto, Llave, Ct),
        ["CaptureAsync"] = c => c.CaptureAsync("pg-1", Llave, Ct),
        ["VoidAsync"] = c => c.VoidAsync("pg-1", Ct),
        ["RefundAsync"] = c => c.RefundAsync("pg-1", Monto, "motivo", Llave, Ct),
        ["GetPaymentAsync"] = c => c.GetPaymentAsync("pg-1", Ct),
    };

    public static TheoryData<string> Metodos()
    {
        var datos = new TheoryData<string>();
        foreach (var m in Recorrido.Keys.Order(StringComparer.Ordinal)) datos.Add(m);
        return datos;
    }

    /// <summary>Los métodos de <see cref="EventosCapabilities"/> que hablan con una capacidad.</summary>
    private static IReadOnlyList<MethodInfo> DelCliente()
        => typeof(EventosCapabilities)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.ReturnType.IsGenericType && m.ReturnType.GetGenericTypeDefinition() == typeof(Task<>)
                     && m.ReturnType.GetGenericArguments()[0] is { IsGenericType: true } r
                     && r.GetGenericTypeDefinition() == typeof(Result<>))
            .ToList();

    [Fact]
    public void Cada_metodo_del_cliente_tiene_su_recorrido()
    {
        var declarados = DelCliente().Select(m => m.Name).Order(StringComparer.Ordinal).ToList();

        Assert.True(declarados.Count >= 11,
            $"Se descubrieron {declarados.Count} métodos Task<Result<T>> en EventosCapabilities y son 11: el censo dejó de ver.");
        Assert.True(declarados.SequenceEqual(Recorrido.Keys.Order(StringComparer.Ordinal), StringComparer.Ordinal),
            "EventosCapabilities y el recorrido de este gate no tienen los mismos métodos. Sin recorrido, " +
            "un método nuevo habla con su capacidad sin que nadie cruce su forma con el contrato." +
            $"{Environment.NewLine}  sin recorrido: {string.Join(", ", declarados.Except(Recorrido.Keys))}" +
            $"{Environment.NewLine}  recorrido sin método: {string.Join(", ", Recorrido.Keys.Except(declarados))}");
    }

    [Theory]
    [MemberData(nameof(Metodos))]
    public async Task Lo_que_manda_y_lee_cabe_en_el_contrato_de_su_capacidad(string metodo)
    {
        var grabadora = new Grabadora();
        await Recorrido[metodo](new EventosCapabilities(grabadora));
        Assert.True(grabadora.Peticiones.Count == 1, $"{metodo} hizo {grabadora.Peticiones.Count} llamadas y este gate espera una.");

        var p = grabadora.Peticiones[0];
        var ensamblado = DocumentoDe(p.Cliente);
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
    /// El documento de la capacidad a la que habla un cliente: el nombre del cliente es el de la
    /// capacidad (<c>pricing</c> → <c>Synergos.Api.Pricing</c>), y si no lo es, el fichero no existe y
    /// la lectura falla a gritos.
    /// </summary>
    private static string DocumentoDe(string cliente)
        => "Synergos.Api." + char.ToUpperInvariant(cliente[0]) + cliente[1..];

    /// <summary>
    /// Un <see cref="IHttpClientFactory"/> que graba la petición y contesta <c>200 {}</c>: lo que se
    /// juzga es lo que SALE; lo que se lee se juzga por el tipo, no por esta respuesta.
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

using System.Net;
using System.Reflection;
using System.Text;
using Synergos.Bff.Viajes.Clients;
using Synergos.CMS.Tests.Contratos;
using Synergos.Core;

namespace Synergos.CMS.Tests.Bff.Viajes;

/// <summary>
/// Lo que <c>/v1/trips</c> (<see cref="ViajesCapabilities"/>) manda y lee de cada capacidad cabe en el contrato que la
/// capacidad publica: el gate consumidor → capacidad (ADR 0140 F2), con el patrón de <c>ContratoConsumidorEventosTests</c>
/// (línea 006, T005).
/// </summary>
/// <remarks>
/// <para><b>Por qué hace falta con el oráculo en verde.</b> <c>OraculoDelViaje</c> corre contra capacidades falsas que
/// contestan lo que el test les dice: un campo renombrado en el cuerpo anónimo de <see cref="ViajesCapabilities"/> lo deja
/// en verde y la capacidad de verdad lo tiraría en silencio. Esto lo cruza contra el JSON comiteado de cada capacidad.</para>
///
/// <para><b>La reserva por la puerta no pasa por aquí</b>: usa los adaptadores comunes del núcleo, que censa
/// <c>ContratoConsumidorPasosTests</c>. Esto cubre sólo el código del vertical que sigue sirviendo <c>/v1/trips</c>.</para>
/// </remarks>
public sealed class ContratoConsumidorViajesTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;
    private static readonly Ref Viaje = Ref.Create("viajes.viaje", "viaje-1");
    private static readonly Ref Viajero = Ref.Create("viajes.viajero", "u-1");
    private static readonly IdempotencyKey Llave = IdempotencyKey.Of("viaje-1:paso");
    private static readonly Money Monto = Money.Of(1_200_000m, "COP");
    private static readonly DateTimeOffset Dia = new(2026, 9, 1, 8, 0, 0, TimeSpan.Zero);

    /// <summary>Cómo llamar a cada método con argumentos plausibles; el valor no importa, la forma sí.</summary>
    private static readonly Dictionary<string, Func<ViajesCapabilities, Task>> Recorrido = new(StringComparer.Ordinal)
    {
        ["QuoteAsync"] = c => c.QuoteAsync([Ref.Create("viajes.tarifa", "vuelo/AV-8020")], Ct),
        ["FindResourceAsync"] = c => c.FindResourceAsync(Ref.Create("viajes.producto", "vuelo/AV-8020"), Ct),
        ["HoldAsync"] = c => c.HoldAsync("rec-1", TimeWindow.Of(Dia, Dia.AddHours(2)), Viajero, Llave, Ct),
        ["ReleaseHoldAsync"] = c => c.ReleaseHoldAsync("h-1", Ct),
        ["ConfirmHoldAsync"] = c => c.ConfirmHoldAsync("h-1", Llave, Ct),
        ["CancelReservationAsync"] = c => c.CancelReservationAsync("res-1", Ct),
        ["AuthorizeAsync"] = c => c.AuthorizeAsync(Viaje, Viajero, Monto, Llave, Ct),
        ["CaptureAsync"] = c => c.CaptureAsync("pg-1", Llave, Ct),
        ["VoidAsync"] = c => c.VoidAsync("pg-1", Ct),
        ["RefundAsync"] = c => c.RefundAsync("pg-1", Monto, "viaje cancelado", Llave, Ct),
        ["GetPaymentAsync"] = c => c.GetPaymentAsync("pg-1", Ct),
    };

    public static TheoryData<string> Metodos()
    {
        var datos = new TheoryData<string>();
        foreach (var m in Recorrido.Keys.Order(StringComparer.Ordinal)) datos.Add(m);
        return datos;
    }

    /// <summary>Los métodos de <see cref="ViajesCapabilities"/> que hablan con una capacidad.</summary>
    private static IReadOnlyList<MethodInfo> DelCliente()
        => typeof(ViajesCapabilities)
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
            $"Se descubrieron {declarados.Count} métodos Task<Result<T>> en ViajesCapabilities y son 11: el censo dejó de ver.");
        Assert.True(declarados.SequenceEqual(Recorrido.Keys.Order(StringComparer.Ordinal), StringComparer.Ordinal),
            "ViajesCapabilities y el recorrido de este gate no tienen los mismos métodos. Sin recorrido, " +
            "un método nuevo habla con su capacidad sin que nadie cruce su forma con el contrato." +
            $"{Environment.NewLine}  sin recorrido: {string.Join(", ", declarados.Except(Recorrido.Keys))}" +
            $"{Environment.NewLine}  recorrido sin método: {string.Join(", ", Recorrido.Keys.Except(declarados))}");
    }

    [Theory]
    [MemberData(nameof(Metodos))]
    public async Task Lo_que_manda_y_lee_cabe_en_el_contrato_de_su_capacidad(string metodo)
    {
        var grabadora = new Grabadora();
        await Recorrido[metodo](new ViajesCapabilities(grabadora));
        Assert.True(grabadora.Peticiones.Count == 1, $"{metodo} hizo {grabadora.Peticiones.Count} llamadas y este gate espera una.");

        var p = grabadora.Peticiones[0];
        var ensamblado = "Synergos.Api." + char.ToUpperInvariant(p.Cliente[0]) + p.Cliente[1..];
        var doc = ContratoOpenApi.Comiteado(ensamblado);
        var quien = $"{metodo} → {p.Metodo} /{p.Ruta}";

        var op = SubconjuntoOpenApi.Operacion(doc, p.Metodo, "/" + p.Ruta);
        Assert.True(op is not null, $"{quien}: {ensamblado} no publica esa ruta con ese método.");

        var leido = DelCliente().Single(m => m.Name == metodo).ReturnType.GetGenericArguments()[0].GetGenericArguments()[0];
        var malas = SubconjuntoOpenApi.Peticion(doc, op!, p).Concat(SubconjuntoOpenApi.Respuesta(doc, op!, leido)).ToList();

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

        public HttpClient CreateClient(string name) => new(new Cable(this, name)) { BaseAddress = new Uri("http://capacidad.local/") };

        private sealed class Cable(Grabadora grabadora, string cliente) : HttpMessageHandler
        {
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
            {
                var query = r.RequestUri!.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => Uri.UnescapeDataString(x.Split('=')[0])).ToList();
                var cuerpo = r.Content is null ? null : await r.Content.ReadAsStringAsync(ct);
                grabadora.Peticiones.Add(new PeticionGrabada(
                    cliente, r.Method.Method, r.RequestUri.AbsolutePath.TrimStart('/'), query, r.Headers.Select(h => h.Key).ToList(), cuerpo));
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}", Encoding.UTF8, "application/json") };
            }
        }
    }
}

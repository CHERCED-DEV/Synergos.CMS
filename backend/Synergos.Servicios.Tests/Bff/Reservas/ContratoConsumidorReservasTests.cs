using System.Net;
using System.Reflection;
using System.Text;
using Synergos.Bff.Reservas.Clients;
using Synergos.Bff.Reservas.Contracts;
using Synergos.CMS.Tests.Contratos;
using Synergos.Core;

namespace Synergos.CMS.Tests.Bff.Reservas;

/// <summary>
/// Lo que la OFERTA de <c>Bff.Reservas</c> manda y lee de la agenda y de precios cabe en el contrato que cada
/// capacidad publica: el gate consumidor → capacidad (ADR 0140 F2), con el patrón de <c>ContratoConsumidorEventosTests</c>.
/// </summary>
/// <remarks>
/// La reserva no pasa por aquí: usa los adaptadores comunes del núcleo, que censa <c>ContratoConsumidorPasosTests</c>.
/// Esto cubre sólo <see cref="CapacidadesDeLaOferta"/>, el código del vertical que P12 retirará.
/// </remarks>
public sealed class ContratoConsumidorReservasTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;
    private static readonly Ref Servicio = Ref.Create("reservas.servicio", "masaje-60");
    private static readonly IdempotencyKey Llave = IdempotencyKey.Of("oferta|abc");

    private static readonly Dictionary<string, Func<CapacidadesDeLaOferta, Task>> Recorrido = new(StringComparer.Ordinal)
    {
        ["BuscarRecursoAsync"] = c => c.BuscarRecursoAsync(Servicio, Ct),
        ["RegistrarRecursoAsync"] = c => c.RegistrarRecursoAsync(Servicio, 2, "America/Bogota",
            [new BloqueDeHorarioDto("Monday", "09:00", "13:00")], 1440, 60, Llave, Ct),
        ["RevisarRecursoAsync"] = c => c.RevisarRecursoAsync("res-1", 3,
            [new BloqueDeHorarioDto("Tuesday", "08:00", "12:00")], 720, 30, Llave, Ct),
        ["FijarPrecioAsync"] = c => c.FijarPrecioAsync(Servicio, Money.Of(140_000m, "COP"), DateTimeOffset.UnixEpoch, Llave, Ct),
    };

    public static TheoryData<string> Metodos()
    {
        var datos = new TheoryData<string>();
        foreach (var m in Recorrido.Keys.Order(StringComparer.Ordinal)) datos.Add(m);
        return datos;
    }

    private static IReadOnlyList<MethodInfo> DelCliente()
        => typeof(CapacidadesDeLaOferta)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.ReturnType.IsGenericType && m.ReturnType.GetGenericTypeDefinition() == typeof(Task<>)
                     && m.ReturnType.GetGenericArguments()[0] is { IsGenericType: true } r
                     && r.GetGenericTypeDefinition() == typeof(Result<>))
            .ToList();

    [Fact]
    public void Cada_metodo_del_cliente_tiene_su_recorrido()
    {
        var declarados = DelCliente().Select(m => m.Name).Order(StringComparer.Ordinal).ToList();

        Assert.True(declarados.Count >= 4, $"Se descubrieron {declarados.Count} métodos y son 4: el censo dejó de ver.");
        Assert.True(declarados.SequenceEqual(Recorrido.Keys.Order(StringComparer.Ordinal), StringComparer.Ordinal),
            $"CapacidadesDeLaOferta y el recorrido no coinciden. Sin recorrido: {string.Join(", ", declarados.Except(Recorrido.Keys))}; " +
            $"recorrido sin método: {string.Join(", ", Recorrido.Keys.Except(declarados))}");
    }

    [Theory]
    [MemberData(nameof(Metodos))]
    public async Task Lo_que_manda_y_lee_cabe_en_el_contrato_de_su_capacidad(string metodo)
    {
        var grabadora = new Grabadora();
        await Recorrido[metodo](new CapacidadesDeLaOferta(grabadora));
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
            $"{quien} no cabe en el contrato de {ensamblado} ({op}):{Environment.NewLine}" + string.Join(Environment.NewLine, malas.Select(m => "  " + m)));
    }

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

using System.Net;
using System.Reflection;
using System.Text;
using Synergos.Bff.Pasos.Http;
using Synergos.CMS.Tests.Contratos;
using Synergos.Core;

namespace Synergos.CMS.Tests.Bff;

/// <summary>
/// Lo que los adaptadores comunes (<see cref="PuertosHttp"/>) mandan y leen de cada capacidad cabe en el
/// contrato que la capacidad publica (núcleo N1): el gate de compatibilidad consumidor → capacidad, como el
/// de Eventos (<see cref="ContratoConsumidorEventosTests"/>), para los puertos que usa cualquier flujo
/// declarado.
/// </summary>
/// <remarks>
/// <para><b>Un caso por método, y un censo.</b> Cada método público que devuelve <c>Task&lt;Result&lt;T&gt;&gt;</c>
/// tiene su recorrido: se le llama con una grabadora en lugar del <see cref="IHttpClientFactory"/>, y la
/// petición que habría salido se valida con <see cref="SubconjuntoOpenApi"/> contra el documento comiteado,
/// igual que el <c>T</c> que lee. Los puertos van como implementación explícita sobre estos métodos: no
/// hablan con nadie más.</para>
///
/// <para>Un método nuevo sin recorrido es rojo en el censo: si no, hablaría con su capacidad sin que nadie
/// cruzara su forma.</para>
/// </remarks>
public sealed class ContratoConsumidorPasosTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;
    private static readonly Ref Algo = Ref.Create("muestra.cita", "s-1");
    private static readonly Ref Alguien = Ref.Create("muestra.paciente", "u-1");
    private static readonly IdempotencyKey Llave = IdempotencyKey.Of("s-1:paso");
    private static readonly Money Monto = Money.Of(1000m, "COP");
    private static readonly TimeWindow Ventana = TimeWindow.Of(
        new DateTimeOffset(2026, 10, 20, 15, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 10, 20, 15, 30, 0, TimeSpan.Zero));

    /// <summary>Cómo llamar a cada método con argumentos plausibles; el valor no importa, la forma sí.</summary>
    private static readonly Dictionary<string, Func<PuertosHttp, Task>> Recorrido = new(StringComparer.Ordinal)
    {
        ["CotizarHttpAsync"] = p => p.CotizarHttpAsync([(Ref.Create("muestra.servicio", "consulta"), 1)], "BIENVENIDA10", Ct),
        ["HallarExistenciaAsync"] = p => p.HallarExistenciaAsync(Ref.Create("muestra.pozo", "p-1"), Ct),
        ["ApartarExistenciaAsync"] = p => p.ApartarExistenciaAsync("po-1", 2, Algo, Llave, Ct),
        ["ConsumirExistenciaAsync"] = p => p.ConsumirExistenciaAsync("ah-1", Ct),
        ["SoltarExistenciaAsync"] = p => p.SoltarExistenciaAsync("ah-1", Ct),
        ["ReponerExistenciaAsync"] = p => p.ReponerExistenciaAsync("po-1", 2, Llave, Ct),
        ["AutorizarCobroAsync"] = p => p.AutorizarCobroAsync(Algo, Alguien, Monto, Llave, Ct),
        ["CapturarCobroAsync"] = p => p.CapturarCobroAsync("pg-1", Llave, Ct),
        ["AnularCobroAsync"] = p => p.AnularCobroAsync("pg-1", Ct),
        ["LeerCobroAsync"] = p => p.LeerCobroAsync("pg-1", Ct),
        ["DevolverCobroAsync"] = p => p.DevolverCobroAsync("pg-1", Monto, "motivo", Llave, Ct),
        ["HallarRecursoAsync"] = p => p.HallarRecursoAsync(Ref.Create("muestra.profesional", "dra-1"), Ct),
        ["ApartarVentanaAsync"] = p => p.ApartarVentanaAsync("rs-1", Ventana, Alguien, Llave, Ct),
        ["ConfirmarApartadoAsync"] = p => p.ConfirmarApartadoAsync("bh-1", Llave, Ct),
        ["FranjasDelRecursoAsync"] = p => p.FranjasDelRecursoAsync("rs-1", Ventana.Start, Ventana.End.AddDays(1), Ct),
        ["LiberarApartadoAsync"] = p => p.LiberarApartadoAsync("bh-1", Ct),
        ["CancelarReservaAsync"] = p => p.CancelarReservaAsync("rv-1", Ct),
        ["CancelarReservaConLlaveAsync"] = p => p.CancelarReservaConLlaveAsync("rv-1", Llave, Ct),
        ["PedirAvisoAsync"] = p => p.PedirAvisoAsync(
            Alguien, "ana@ejemplo.co", "muestra.cita.confirmada", new Dictionary<string, string> { ["nombre"] = "Ana" }, Llave, Ct),
        ["VerificarPermisoAsync"] = p => p.VerificarPermisoAsync(Alguien, "muestra.agenda", Ct),
        ["LeerCanastaAsync"] = p => p.LeerCanastaAsync("ca-1", Ct),
        ["CerrarCanastaAsync"] = p => p.CerrarCanastaAsync("ca-1", Ct),
        ["CrearPedidoAsync"] = p => p.CrearPedidoAsync(
            Alguien, [new Synergos.Bff.Pasos.LineaDePedido(Ref.Create("muestra.producto", "pr-1"), 2, Monto)], Monto, Llave, Ct),
        ["CumplirPedidoAsync"] = p => p.CumplirPedidoAsync("pd-1", Ct),
        ["CancelarPedidoAsync"] = p => p.CancelarPedidoAsync("pd-1", Ct),
        ["DespacharEnvioAsync"] = p => p.DespacharEnvioAsync(
            Ref.Create("orders.pedido", "pd-1"),
            new Synergos.Bff.Pasos.DireccionDeEnvio("Calle 1 # 2-3", null, "Bogotá", null, null, "CO", "Ana"), "servientrega", Llave, Ct),
    };

    /// <summary>
    /// Las capacidades sin contrato publicado, con quién cruza entonces su forma. Caduca sola: si la
    /// capacidad publica documento, es rojo.
    /// </summary>
    private static readonly Dictionary<string, string> SinContrato = new(StringComparer.Ordinal)
    {
        [PuertosHttp.Notifications] =
            "Api.Notifications no publica documento: la forma del aviso la cruza TicketingAvisoTests contra la capacidad " +
            "REAL (el mismo cuerpo que EventosCapabilities.NotifyAsync).",
    };

    public static TheoryData<string> Metodos()
    {
        var datos = new TheoryData<string>();
        foreach (var m in Recorrido.Keys.Order(StringComparer.Ordinal)) datos.Add(m);
        return datos;
    }

    private static IReadOnlyList<MethodInfo> DelAdaptador()
        => typeof(PuertosHttp)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.ReturnType.IsGenericType && m.ReturnType.GetGenericTypeDefinition() == typeof(Task<>)
                     && m.ReturnType.GetGenericArguments()[0] is { IsGenericType: true } r
                     && r.GetGenericTypeDefinition() == typeof(Result<>))
            .ToList();

    [Fact]
    public void Cada_metodo_del_adaptador_tiene_su_recorrido()
    {
        var declarados = DelAdaptador().Select(m => m.Name).Order(StringComparer.Ordinal).ToList();

        Assert.True(declarados.Count >= 24,
            $"Se descubrieron {declarados.Count} métodos Task<Result<T>> en PuertosHttp y son al menos 24: el censo dejó de ver.");
        Assert.True(declarados.SequenceEqual(Recorrido.Keys.Order(StringComparer.Ordinal), StringComparer.Ordinal),
            "PuertosHttp y el recorrido de este gate no tienen los mismos métodos." +
            $"{Environment.NewLine}  sin recorrido: {string.Join(", ", declarados.Except(Recorrido.Keys))}" +
            $"{Environment.NewLine}  recorrido sin método: {string.Join(", ", Recorrido.Keys.Except(declarados))}");
    }

    [Theory]
    [MemberData(nameof(Metodos))]
    public async Task Lo_que_manda_y_lee_cabe_en_el_contrato_de_su_capacidad(string metodo)
    {
        var grabadora = new Grabadora();
        await Recorrido[metodo](new PuertosHttp(grabadora));
        Assert.True(grabadora.Peticiones.Count == 1, $"{metodo} hizo {grabadora.Peticiones.Count} llamadas y este gate espera una.");

        var p = grabadora.Peticiones[0];
        var ensamblado = "Synergos.Api." + char.ToUpperInvariant(p.Cliente[0]) + p.Cliente[1..];
        if (SinContrato.ContainsKey(p.Cliente))
        {
            Assert.False(File.Exists(ContratoOpenApi.Ruta(ensamblado)),
                $"{ensamblado} ya publica contrato: quitá «{p.Cliente}» de {nameof(SinContrato)} para que este gate lo cruce.");
            return;
        }

        var doc = ContratoOpenApi.Comiteado(ensamblado);
        var quien = $"{metodo} → {p.Metodo} /{p.Ruta}";
        var op = SubconjuntoOpenApi.Operacion(doc, p.Metodo, "/" + p.Ruta);
        Assert.True(op is not null, $"{quien}: {ensamblado} no publica esa ruta con ese método.");

        var leido = DelAdaptador().Single(m => m.Name == metodo).ReturnType.GetGenericArguments()[0].GetGenericArguments()[0];
        var malas = SubconjuntoOpenApi.Peticion(doc, op!, p).Concat(SubconjuntoOpenApi.Respuesta(doc, op!, leido)).ToList();

        Assert.True(malas.Count == 0,
            $"{quien} no cabe en el contrato de {ensamblado} ({op}):{Environment.NewLine}" +
            string.Join(Environment.NewLine, malas.Select(m => "  " + m)));
    }

    /// <summary>Un <see cref="IHttpClientFactory"/> que graba la petición y contesta <c>200 {}</c>.</summary>
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
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}", Encoding.UTF8, "application/json") };
            }
        }
    }
}

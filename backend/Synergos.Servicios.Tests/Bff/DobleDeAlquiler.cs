using System.Net;
using System.Text;
using Synergos.Core;

namespace Synergos.CMS.Tests.Bff;

/// <summary>
/// El doble de las capacidades que compone Alquiler y su guion feliz, compartido por los dos conductores del
/// oráculo: el imperativo (<c>RentalFlow</c>) y el declarado (<c>alquiler.renta</c>).
/// </summary>
/// <remarks>
/// <para><b>Vivía dentro de <c>RentalCompensationTests</c></b> y salió de ahí sin cambiar una respuesta: el
/// mismo doble para los dos motores es lo que hace que «el mismo oráculo» no sea una promesa (research R12 de
/// la línea 005). Las dos rutas que suma —la agenda por sujeto y la cotización— sólo las llama el declarado.</para>
///
/// <para><b>El doble de <c>Api.Booking</c> es estricto</b>, como el de <c>TripCompensationTests</c> y por la
/// misma razón: soltar un apartado ya confirmado se RECHAZA. Un doble más permisivo que la cosa real dejaría
/// pasar en verde un flujo que en producción no cancelaría nunca una reserva.</para>
///
/// <para><b>Es la SÉPTIMA copia del doble de capacidades</b>, con cinco formas distintas ya en el repo (dos
/// idénticas, tres que perdieron o ganaron <c>Caida</c>, <c>Secuencia</c>, <c>Veces</c> o el comodín de
/// <c>Coincide</c>). Medido, no contado. Queda anotado como hallazgo de la fábrica: S14 no tiene sub-spec de
/// arnés, así que el séptimo copia el que tenga más cerca y hereda o pierde una variación sin saberlo
/// (<c>the_same_algorithm_is_not_the_same_thing</c>).</para>
/// </remarks>
public static class DobleDeAlquiler
{
    /// <summary>Un reloj que sólo avanza cuando el test lo pide.</summary>
    public sealed class RelojFalso : TimeProvider
    {
        private DateTimeOffset _now;
        public RelojFalso(DateTimeOffset inicio) => _now = inicio;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Avanzar(TimeSpan d) => _now += d;
    }

    /// <summary>Las capacidades guionadas: qué contesta cada ruta y qué se les pidió.</summary>
    public sealed class CapacidadesFalsas : HttpMessageHandler
    {
        private readonly Dictionary<string, Func<HttpRequestMessage, HttpResponseMessage>> _rutas = new(StringComparer.Ordinal);
        private readonly Dictionary<string, Queue<string>> _secuencias = new(StringComparer.Ordinal);

        public List<(string Method, string Path, string? Query, string? Key, string? Body)> Llamadas { get; } = new();

        public CapacidadesFalsas Cuando(string patron, Func<HttpRequestMessage, HttpResponseMessage> responde)
        {
            _rutas[patron] = responde;
            return this;
        }

        public CapacidadesFalsas Ok(string patron, string json)
            => Cuando(patron, _ => Json(HttpStatusCode.OK, json));

        /// <summary>Respuestas distintas para llamadas sucesivas a la misma ruta.</summary>
        public CapacidadesFalsas Secuencia(string patron, params string[] jsons)
        {
            _secuencias[patron] = new Queue<string>(jsons);
            return Cuando(patron, _ =>
            {
                var cola = _secuencias[patron];
                return Json(HttpStatusCode.OK, cola.Count > 1 ? cola.Dequeue() : cola.Peek());
            });
        }

        public CapacidadesFalsas Falla(string patron, HttpStatusCode codigo, string code)
            => Cuando(patron, _ => new HttpResponseMessage(codigo)
            {
                Content = new StringContent($$"""{"code":"{{code}}","detail":"guionado"}""",
                    Encoding.UTF8, "application/problem+json"),
            });

        public int Veces(string method, string pathContiene)
            => Llamadas.Count(l => l.Method == method && l.Path.Contains(pathContiene, StringComparison.Ordinal));

        /// <summary>
        /// Las veces que se llamó a ESA ruta y no a una que la contenga.
        /// </summary>
        /// <remarks>
        /// <b>Hace falta acá y en los otros seis dobles no</b>, y la razón dice algo del vertical: en Viajes
        /// apartar y confirmar son dos peticiones del CMS, así que un test que cuente <c>/v1/holds</c> sólo ve
        /// apartados. Acá reservar aparta, cobra Y confirma, así que <c>Contains("/v1/payments")</c> cuenta
        /// también los <c>/v1/payments/{id}/capture</c> — medido: daba 3 donde hay 2 autorizaciones. Un
        /// contador que cuenta de más no falla en el caso feliz: falla en el que prueba la regla.
        /// </remarks>
        public int VecesEn(string method, string pathExacto)
            => Llamadas.Count(l => l.Method == method
                && l.Path.EndsWith(pathExacto, StringComparison.Ordinal));

        private static HttpResponseMessage Json(HttpStatusCode c, string body)
            => new(c) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            var clave = $"{request.Method.Method} {path}";
            var cuerpo = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);

            lock (Llamadas)
            {
                Llamadas.Add((request.Method.Method, path, request.RequestUri.Query,
                    request.Headers.TryGetValues("Idempotency-Key", out var v) ? v.FirstOrDefault() : null,
                    cuerpo));
            }

            foreach (var (patron, responde) in _rutas)
            {
                if (Coincide(patron, clave)) return responde(request);
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent("""{"code":"stub.no_route","detail":"sin guion"}""",
                    Encoding.UTF8, "application/problem+json"),
            };
        }

        private static bool Coincide(string patron, string clave)
            => patron.EndsWith('*')
                ? clave.StartsWith(patron[..^1], StringComparison.Ordinal)
                : string.Equals(patron, clave, StringComparison.Ordinal);
    }

    /// <summary>Una fábrica que entrega el doble a cada cliente nombrado.</summary>
    public sealed class FabricaFalsa : IHttpClientFactory
    {
        private readonly CapacidadesFalsas _handler;
        public FabricaFalsa(CapacidadesFalsas h) => _handler = h;
        public HttpClient CreateClient(string name)
            => new(_handler, disposeHandler: false) { BaseAddress = new Uri("http://capacidad.local/") };
    }

    public static readonly DateTimeOffset Ahora = new(2026, 9, 1, 10, 0, 0, TimeSpan.Zero);
    public static readonly Ref Arrendatario = Ref.Create("alquiler.arrendatario", "u-1");
    public static readonly TimeWindow Semana = TimeWindow.Of(
        new DateTimeOffset(2026, 9, 5, 8, 0, 0, TimeSpan.Zero),
        new DateTimeOffset(2026, 9, 12, 18, 0, 0, TimeSpan.Zero));

    public static Money Cop(decimal m) => Money.Of(m, Money.Cop);

    /// <summary>
    /// El doble se comporta como <c>Api.Booking</c>: soltar un apartado ya confirmado se RECHAZA.
    /// </summary>
    public static CapacidadesFalsas ComoBookingDeVerdad(
        CapacidadesFalsas caps, params (string Hold, string Reserva)[] items)
    {
        var confirmados = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (hold, reserva) in items)
        {
            var h = hold;
            var r = reserva;
            caps.Cuando($"POST /v1/holds/{h}/confirm", _ =>
            {
                confirmados.Add(h);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent($$"""{"id":"{{r}}","status":"Confirmed"}""",
                        Encoding.UTF8, "application/json"),
                };
            });
            caps.Cuando($"POST /v1/holds/{h}/release", _ => confirmados.Contains(h)
                ? new HttpResponseMessage(HttpStatusCode.Conflict)
                {
                    Content = new StringContent(
                        """{"code":"booking.hold_already_confirmed","detail":"el apartado ya es una reserva"}""",
                        Encoding.UTF8, "application/problem+json"),
                }
                : new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        $$"""{"id":"{{h}}","resourceId":"rec-andamio","expiresAt":"2026-09-01T10:10:00+00:00"}""",
                        Encoding.UTF8, "application/json"),
                });
            caps.Cuando($"POST /v1/reservations/{r}/cancel", _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent($$"""{"id":"{{r}}","status":"Cancelled"}""",
                    Encoding.UTF8, "application/json"),
            });
        }
        return caps;
    }

    /// <summary>
    /// El guion feliz. La garantía y el alquiler son DOS cobros con identificadores distintos, y
    /// eso no es decoración del fixture: con un solo <c>pg</c> para los dos, «se capturó el
    /// alquiler y no la garantía» y «se capturó todo» darían el mismo resultado.
    /// </summary>
    public static CapacidadesFalsas Feliz() => new CapacidadesFalsas()
        .Ok("GET /v1/resources",
            """{"id":"rec-andamio","subjectKind":"alquiler.equipo","subjectId":"andamio-6m","capacity":8}""")
        .Ok("GET /v1/resources/por-sujeto",
            """{"id":"rec-andamio","subjectKind":"alquiler.equipo","subjectId":"andamio-6m","capacity":8}""")
        .Secuencia("POST /v1/holds",
            """{"id":"h-1","resourceId":"rec-andamio","expiresAt":"2026-09-01T10:10:00+00:00"}""",
            """{"id":"h-2","resourceId":"rec-andamio","expiresAt":"2026-09-01T10:10:00+00:00"}""")
        // El primer POST /v1/payments es la GARANTÍA y el segundo el ALQUILER: ése es el orden
        // del flujo, y probarlo al revés no distinguiría cuál de los dos se capturó.
        .Secuencia("POST /v1/payments",
            """{"id":"pg-dep","status":"Authorized","amount":{"amount":400000,"currency":"COP"},"refundable":{"amount":0,"currency":"COP"}}""",
            """{"id":"pg-ren","status":"Authorized","amount":{"amount":350000,"currency":"COP"},"refundable":{"amount":0,"currency":"COP"}}""")
        .Ok("POST /v1/payments/pg-ren/capture",
            """{"id":"pg-ren","status":"Captured","amount":{"amount":350000,"currency":"COP"},"refundable":{"amount":350000,"currency":"COP"}}""")
        .Ok("POST /v1/payments/pg-ren/void",
            """{"id":"pg-ren","status":"Voided","amount":{"amount":350000,"currency":"COP"},"refundable":{"amount":0,"currency":"COP"}}""")
        .Ok("POST /v1/payments/pg-ren/refund",
            """{"id":"pg-ren","status":"Captured","amount":{"amount":350000,"currency":"COP"},"refundable":{"amount":0,"currency":"COP"}}""")
        .Ok("GET /v1/payments/pg-ren",
            """{"id":"pg-ren","status":"Captured","amount":{"amount":350000,"currency":"COP"},"refundable":{"amount":350000,"currency":"COP"}}""")
        .Ok("POST /v1/payments/pg-dep/capture",
            """{"id":"pg-dep","status":"Captured","amount":{"amount":400000,"currency":"COP"},"refundable":{"amount":400000,"currency":"COP"}}""")
        .Ok("POST /v1/payments/pg-dep/void",
            """{"id":"pg-dep","status":"Voided","amount":{"amount":400000,"currency":"COP"},"refundable":{"amount":0,"currency":"COP"}}""")
        .Ok("POST /v1/payments/pg-dep/refund",
            """{"id":"pg-dep","status":"Captured","amount":{"amount":400000,"currency":"COP"},"refundable":{"amount":0,"currency":"COP"}}""")
        .Ok("GET /v1/payments/pg-dep",
            """{"id":"pg-dep","status":"Captured","amount":{"amount":400000,"currency":"COP"},"refundable":{"amount":400000,"currency":"COP"}}""")
        .Ok("POST /v1/deliveries", """{"id":"d1","status":"Sent"}""");

    public static CapacidadesFalsas FelizEstricto()
        => ComoBookingDeVerdad(Feliz(), ("h-1", "res-1"), ("h-2", "res-2"));
}

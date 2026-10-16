using System.Collections.Concurrent;
using System.Net;
using System.Text.RegularExpressions;

namespace Synergos.CMS.Tests.Bff.Reservas;

/// <summary>
/// Cancelar dos veces la misma reserva no confirmada la deshace una sola vez (011-booking-reserva, T046; SC-003): una
/// anulación del cobro y una liberación de la franja, y la segunda respuesta dice lo mismo que la primera.
/// </summary>
/// <remarks>
/// Lo que se cuenta es lo que el orquestador pidió a las capacidades, visto en el cable del arnés (<c>falla</c>, que acá
/// no rechaza nada, sólo anota). Lo que el arnés publica antes —la oferta— queda fuera.
/// </remarks>
public sealed class CancelacionRepetidaTests
{
    [Fact]
    public async Task Cancelar_dos_veces_la_misma_reserva_la_deshace_una_sola_vez()
    {
        var anotando = false;
        var vistas = new ConcurrentQueue<string>();
        using var r = await new ReservaDeServicioReal(falla: p =>
        {
            if (Volatile.Read(ref anotando)) vistas.Enqueue($"{p.Method.Method} {p.RequestUri!.AbsolutePath}");
            return null;
        }).ConOferta();
        Volatile.Write(ref anotando, true);
        var (_, abierta) = await r.Abrir();
        var id = abierta.GetProperty("id").GetString()!;

        var (primero, primera) = await r.Cancelar(id);
        var (segundo, segunda) = await r.Cancelar(id);

        Assert.Equal(HttpStatusCode.OK, primero);
        Assert.Equal(HttpStatusCode.OK, segundo);
        Assert.Equal(primera.GetProperty("estado").GetString(), segunda.GetProperty("estado").GetString());
        Assert.Single(vistas, v => Regex.IsMatch(v, "^POST /v1/payments/[^/]+/void$"));
        Assert.Single(vistas, v => Regex.IsMatch(v, "^POST /v1/holds/[^/]+/release$"));
    }
}

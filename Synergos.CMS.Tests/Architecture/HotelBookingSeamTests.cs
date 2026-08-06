namespace Synergos.CMS.Tests.Architecture;

/// <summary>
/// El borde de reservas NO orquesta el cobro, como invariante ejecutable (HU #36).
/// </summary>
/// <remarks>
/// <para><b>El defecto que impide no es de estilo.</b> Apartar, cobrar y confirmar vivían dentro
/// de <c>BookingController</c>: unas doscientas líneas de ASP.NET que decidían en qué orden se
/// abre la caja. Ahí no se podían probar sin levantar el pipeline —y de hecho los dos defectos
/// que ese código lleva corregidos, el apartado vencido que se cobraba igual y la cancelación
/// que devolvía dos veces, vivieron ahí sin test justamente por eso—.</para>
///
/// <para><b>Y lo que la costura hace posible:</b> llevar la misma reserva contra
/// <c>Synergos.Bff.Viajes</c> sin reescribir el borde. Mientras el orden viviera en el
/// controller, cablearlo era reescribirlo.</para>
///
/// <para><b>Tosco a propósito</b>, como los demás gates del repo: mira si el borde vuelve a
/// nombrar las piezas que mueve plata y cupo. No atrapa a un adversario, atrapa el atajo de un
/// martes.</para>
/// </remarks>
public sealed class HotelBookingSeamTests
{
    /// <summary>Lo que el borde no puede volver a tocar, y por qué.</summary>
    private static readonly (string Pieza, string PorQue)[] Prohibidas =
    {
        ("IPaymentProvider", "abrir la caja es del flujo, no del borde"),
        ("IReservationService", "apartar y confirmar el cupo también"),
        ("IAuditTrailWriter", "el rastro se deja donde ocurre el hecho, no donde se responde"),
    };

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Synergos.CMS.sln")))
        {
            dir = dir.Parent;
        }
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    /// <summary>El fichero sin comentarios: la prosa explica el código, no lo es.</summary>
    private static string SinComentarios(string ruta)
    {
        Assert.True(File.Exists(ruta), $"No existe {ruta}: revisar este gate.");
        return string.Join('\n', File.ReadAllLines(ruta).Select(l =>
        {
            var t = l.TrimStart();
            if (t.StartsWith("//", StringComparison.Ordinal) || t.StartsWith("*", StringComparison.Ordinal))
            {
                return string.Empty;
            }
            var i = l.IndexOf("//", StringComparison.Ordinal);
            return i >= 0 ? l[..i] : l;
        }));
    }

    private static string Controller()
        => SinComentarios(Path.Combine(RepoRoot(), "Synergos.CMS.Web", "Controllers", "BookingController.cs"));

    [Fact]
    public void El_borde_de_reservas_NO_abre_la_caja()
    {
        var codigo = Controller();

        // Si el fichero se quedara vacío o cambiara de sitio, lo de abajo pasaría vigilando la
        // nada. Ya se cometió ese error dos veces en este repo.
        Assert.True(codigo.Length > 2000, "El controller de reservas es sospechosamente corto: revisar este gate.");

        foreach (var (pieza, porQue) in Prohibidas)
        {
            Assert.False(codigo.Contains(pieza, StringComparison.Ordinal),
                $"BookingController volvió a nombrar {pieza}, y {porQue}. El orden en que se abre "
                + "la caja vive en IHotelBookingService: ahí se puede probar, y ahí se puede "
                + "cambiar por dónde se reserva sin tocar el borde.");
        }

        // Y sí habla con el flujo: sin esto, lo de arriba pasaría con un controller que ya no
        // reserva nada.
        Assert.Contains("IHotelBookingService", codigo, StringComparison.Ordinal);
        Assert.Contains("_booking.", codigo, StringComparison.Ordinal);
    }

    /// <summary>
    /// El flujo se quedó con el ORDEN, y el borde con el formato y el código de estado.
    /// </summary>
    /// <remarks>
    /// Es la otra mitad: una extracción que se llevara también el formateo de precios o la
    /// elección del código HTTP habría metido presentación en <c>Application</c>, que es la capa
    /// que ADR 0002 mantiene libre de ASP.NET.
    /// </remarks>
    [Fact]
    public void El_flujo_no_se_llevo_lo_que_es_del_borde()
    {
        var flujo = SinComentarios(Path.Combine(
            RepoRoot(), "Synergos.CMS.Application", "Services", "Impl", "StubHotelBookingService.cs"));

        foreach (var deElBorde in new[] { "IPriceFormatter", "IActionResult", "StatusCode", "BadRequest", "Ok(" })
        {
            Assert.False(flujo.Contains(deElBorde, StringComparison.Ordinal),
                $"El flujo nombra '{deElBorde}', que es del borde. Application no sabe de ASP.NET (ADR 0002).");
        }

        // Y sí se llevó el orden: capturar va ANTES de confirmar el cupo.
        var captura = flujo.IndexOf("CaptureAsync", StringComparison.Ordinal);
        var confirma = flujo.IndexOf("_reservations.ConfirmAsync", StringComparison.Ordinal);
        Assert.True(captura > 0 && confirma > captura,
            "El flujo confirma el cupo antes de capturar: un fallo del cobro dejaría una reserva sin pagar.");
    }
}

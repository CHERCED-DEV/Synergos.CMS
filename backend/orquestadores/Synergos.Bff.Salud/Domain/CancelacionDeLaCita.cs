using Synergos.Bff.Core.Flow;
using Synergos.Core;

namespace Synergos.Bff.Salud.Domain;

/// <summary>
/// Cancelar una cita YA confirmada (003-salud-cita, I6, la ficha F4): el flujo posterior <c>flujos/salud.cancelacion.json</c>
/// y su único paso propio, la devolución del copago según la antelación.
/// </summary>
/// <remarks>
/// <para><b>Lo demás lo hace el núcleo</b>: <c>flow.leer-original</c> lee de la cita lo que deja para lo posterior —el cobro,
/// el copago, la franja y la reserva—, sólo si es del paciente de la sesión y está confirmada; <c>booking.cancelar</c> la
/// cancela en la agenda con su llave por reserva, y <c>payments.devolver</c> devuelve lo que diga la política.</para>
///
/// <para><b>Primero la agenda, después la plata</b>, como Viajes y al revés que Alquiler: la agenda puede negarse (la cita ya
/// empezó), y una devolución hecha no se deshace. Con la cancelación primero, un «no» de la agenda no mueve plata; una
/// devolución caída se repite con la misma llave, y cancelar otra vez con ella es la misma cancelación (T052 del núcleo).</para>
///
/// <para><b>La política es del sitio</b> (<c>Synergos:Features:Ehr</c>): la puerta la manda como negocio, el navegador no la
/// elige, y el elemento la muestra con los mismos números antes de cancelar.</para>
/// </remarks>
public static class CancelacionDeLaCita
{
    /// <summary>El flujo posterior que cancela una cita confirmada por la puerta.</summary>
    public const string Flujo = "salud.cancelacion";

    /// <summary>Los pasos propios, para sumarlos a los comunes en el registro del orquestador.</summary>
    public static IPaso[] Pasos(TimeProvider reloj) => [new PasoDevolucionPorAntelacion(reloj)];
}

/// <summary>
/// Cuánto del copago se devuelve según con cuánta antelación se cancela: todo desde <paramref name="TotalDesdeHoras"/>
/// antes del inicio, el <paramref name="ParcialPorcentaje"/> % desde <paramref name="ParcialDesdeHoras"/>, y nada con menos.
/// </summary>
public sealed record PoliticaDeDevolucion(int TotalDesdeHoras, int ParcialDesdeHoras, int ParcialPorcentaje)
{
    /// <summary>Por qué no es una política, o nulo si lo es.</summary>
    public string? Problema()
        => TotalDesdeHoras < 0 || ParcialDesdeHoras < 0 ? "las horas no pueden ser negativas"
            : ParcialDesdeHoras > TotalDesdeHoras ? "la devolución parcial no puede empezar antes que la total"
            : ParcialPorcentaje is < 0 or > 100 ? "el porcentaje va de 0 a 100"
            : null;

    /// <summary>
    /// Lo que se devuelve de <paramref name="copago"/> si se cancela en <paramref name="ahora"/> una cita que empieza en
    /// <paramref name="inicio"/>; nulo si no se devuelve nada. Redondea hacia abajo, al centavo: nunca devuelve de más.
    /// </summary>
    /// <remarks>Nulo y no cero: <c>Api.Payments</c> rechaza una devolución de cero, y el paso que devuelve se salta con
    /// <c>omitir_si_falta</c>.</remarks>
    public Money? Devolucion(Money copago, DateTimeOffset inicio, DateTimeOffset ahora)
    {
        var antelacion = inicio - ahora;
        var parte = antelacion >= TimeSpan.FromHours(TotalDesdeHoras) ? copago.Amount
            : antelacion >= TimeSpan.FromHours(ParcialDesdeHoras)
                ? decimal.Round(copago.Amount * ParcialPorcentaje / 100m, 2, MidpointRounding.ToZero)
                : 0m;
        return parte > 0m ? Money.Of(parte, copago.Currency) : null;
    }
}

/// <summary>
/// <c>salud.devolucion-por-antelacion</c>: lee el copago, la franja y el cobro de la cita, y la política del sitio; escribe
/// lo que se devuelve (nulo si nada). Rechaza, antes de mover nada, una cita que ya empezó.
/// </summary>
/// <remarks>
/// <para><b>Va antes de la agenda</b>: si la cita ya empezó no hay nada que cancelar, y lo dice con su nombre en vez de dejar
/// que la agenda conteste por ella. La agenda de Salud se publica sin aviso previo (<c>avisoMinutos = 0</c>), así que su
/// límite y éste son el mismo: el inicio de la cita.</para>
///
/// <para><b>Sin cobro no se devuelve</b>: una cita con copago y sin el cobro que lo autorizó es una original rota; se rechaza
/// con su nombre en vez de cancelar la cita y no devolver nada.</para>
/// </remarks>
public sealed class PasoDevolucionPorAntelacion(TimeProvider reloj) : IPaso
{
    /// <summary>La cita ya empezó (o pasó): no se cancela.</summary>
    public const string YaEmpezo = "salud.cita_ya_empezo";

    /// <summary>La política que mandó el sitio no es una política.</summary>
    public const string PoliticaInvalida = "salud.politica_de_devolucion_invalida";

    /// <summary>Hay algo que devolver y la cita no tiene el cobro.</summary>
    public const string SinCobro = "salud.cita_sin_cobro";

    public string Tipo => "salud.devolucion-por-antelacion";
    public int Lecturas => 6;
    public int Escrituras => 1;
    public LlaveRequerida Llave => LlaveRequerida.Ninguna;
    public IReadOnlyList<Type> TiposDeEscritura => [typeof(Money)];

    public Task<SalidaDePaso> EjecutarAsync(EntradaDePaso entrada, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        var copago = entrada.Lee<Money>(0);
        var ventana = entrada.Lee<TimeWindow>(1);
        var cobro = entrada.Lee<string?>(2);
        var politica = new PoliticaDeDevolucion(entrada.Lee<int>(3), entrada.Lee<int>(4), entrada.Lee<int>(5));

        if (politica.Problema() is { } problema)
        {
            return Rechaza(Rejection.Invalid(PoliticaInvalida, $"La política de devolución del sitio no sirve: {problema}."));
        }
        var ahora = reloj.GetUtcNow();
        if (ahora >= ventana.Start)
        {
            return Rechaza(Rejection.Conflict(YaEmpezo, $"La cita empezó el {ventana.Start:O}: ya no se puede cancelar."));
        }

        var devolucion = politica.Devolucion(copago, ventana.Start, ahora);
        if (devolucion is not null && string.IsNullOrWhiteSpace(cobro))
        {
            return Rechaza(Rejection.Conflict(SinCobro, "La cita tiene copago y no el cobro que lo autorizó: la cancela el consultorio."));
        }
        return Task.FromResult(SalidaDePaso.Sigue(devolucion is { } d ? (object)d : null));
    }

    private static Task<SalidaDePaso> Rechaza(Rejection rechazo) => Task.FromResult(SalidaDePaso.Rechaza(rechazo));
}

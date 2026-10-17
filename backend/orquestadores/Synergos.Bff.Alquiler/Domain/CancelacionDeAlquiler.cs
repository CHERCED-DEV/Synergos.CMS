using Synergos.Bff.Core;
using Synergos.Bff.Core.Flow;
using Synergos.Bff.Pasos;
using Synergos.Core;

namespace Synergos.Bff.Alquiler.Domain;

/// <summary>
/// Los pasos propios de <c>flujos/alquiler.cancelacion.json</c>, la cancelación de lo confirmado como operación posterior
/// al cierre (NN-8; aclaración del director D-005-NN8): la regla del día de inicio, y los dos que el núcleo todavía no
/// cubre.
/// </summary>
/// <remarks>
/// <para><b>Dos son pegamento con nombre</b> y se retiran cuando el núcleo cubra su necesidad:
/// <see cref="PasoCancelarUnidades"/> (NN-18, recorrer en el flujo posterior lo que la original reservó por ítem) y
/// <see cref="PasoAnularGarantiaSiHay"/> (NN-19, omitir un paso sobre un valor nulo fuera de los que reservan). La regla
/// (<see cref="PasoAntesDelInicio"/>) es del dominio y se queda.</para>
///
/// <para><b>Todo va en la fase que abre, y ningún paso reserva</b>: la saga posterior sólo se escribe si la fase sale
/// entera. Si algo falla a medias no queda nada escrito, y repetir con la misma llave vuelve a pasar por las mismas
/// llaves derivadas: la devolución no se duplica, una reserva ya cancelada cuenta como hecha y anular lo anulado es
/// idempotente en <c>Api.Payments</c>.</para>
/// </remarks>
public static class CancelacionDeAlquiler
{
    /// <summary>El flujo posterior que cancela lo confirmado por la puerta.</summary>
    public const string Flujo = "alquiler.cancelacion";

    /// <summary>Los pasos propios de la cancelación, para sumarlos a los comunes en el registro del orquestador.</summary>
    public static IPaso[] Pasos(LiquidacionDeAlquiler liquidacion, ISagaStore<SagaDeFlujo> almacen, IDeshacerAgenda agenda, IDeshacerCobros cobros)
        => [new PasoAntesDelInicio(liquidacion), new PasoCancelarUnidades(almacen, agenda), new PasoAnularGarantiaSiHay(cobros)];
}

/// <summary>
/// <c>alquiler.antes-del-inicio</c>: lee el alquiler y su ventana y rechaza si el inquilino ya no puede cancelarlo —ya
/// empezó en el huso del despliegue, tiene la devolución declarada, o ya se devolvió o se canceló—.
/// </summary>
/// <remarks>
/// La regla es UNA, la de la liquidación propia (research R7, 005-P3): con los dos caminos detrás del interruptor del CMS,
/// cancelar por uno o por el otro dice lo mismo. Va antes de mover nada, con el turno de la liquidación.
/// </remarks>
public sealed class PasoAntesDelInicio(LiquidacionDeAlquiler liquidacion) : IPaso
{
    public string Tipo => "alquiler.antes-del-inicio";
    public int Lecturas => 2;
    public int Escrituras => 0;
    public LlaveRequerida Llave => LlaveRequerida.Ninguna;
    public IReadOnlyList<Type> TiposDeEscritura => [];

    public Task<SalidaDePaso> EjecutarAsync(EntradaDePaso entrada, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        var no = liquidacion.AntesDeCancelarPorElFlujo(entrada.Lee<string>(0), entrada.Lee<TimeWindow>(1));
        return Task.FromResult(no is null ? SalidaDePaso.Sigue() : SalidaDePaso.Rechaza(no));
    }
}

/// <summary>
/// <c>alquiler.cancelar-unidades</c>: cancela en la agenda las reservas que confirmó la renta original. Pegamento
/// atribuido a NN-18.
/// </summary>
/// <remarks>
/// <para><b>Por qué no es un <c>para_cada</c> con <c>booking.cancelar</c></b>: las unidades se confirman dentro de un
/// bloque de la original y su salida no puede nombrar lo que escribe cada ítem; un bloque del flujo posterior sólo
/// recorre una entrada o <c>reservas:&lt;paso&gt;</c> propio. Y en la original, <c>reservas:apartar</c> guarda el
/// apartado (su <c>closeTargetId</c> es el mismo apartado): la reserva confirmada sólo consta en la compensación
/// <c>booking.cancelar</c> que dejó el cierre, ya dada por hecha (medido en una saga de la copia piloto). De ahí la lee
/// este paso, como la liquidación propia (<see cref="LiquidacionDeAlquiler.ReservasConfirmadas"/>).</para>
///
/// <para><b>Una reserva ya cancelada cuenta como hecha</b> (<c>booking.already_cancelled</c>): es el reintento de una
/// cancelación que falló después de soltar las unidades. Una renta completada sin ninguna reserva no se da por cancelada
/// en silencio: se rechaza con su nombre.</para>
/// </remarks>
public sealed class PasoCancelarUnidades(ISagaStore<SagaDeFlujo> almacen, IDeshacerAgenda agenda) : IPaso
{
    /// <summary>La renta original no deja ver ninguna reserva que cancelar.</summary>
    public const string SinReservas = "alquiler.units_not_found";

    /// <summary>Lo que contesta la agenda a cancelar una reserva ya cancelada.</summary>
    public const string YaCancelada = "booking.already_cancelled";

    public string Tipo => "alquiler.cancelar-unidades";
    public int Lecturas => 1;
    public int Escrituras => 0;
    public LlaveRequerida Llave => LlaveRequerida.Ninguna;
    public IReadOnlyList<Type> TiposDeEscritura => [];

    public async Task<SalidaDePaso> EjecutarAsync(EntradaDePaso entrada, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        var id = entrada.Lee<string>(0);
        var original = almacen.Find(id)
            ?? throw new InvalidOperationException($"«{Tipo}» corre después de flow.leer-original, y la renta {id} ya no está.");

        var reservas = LiquidacionDeAlquiler.ReservasConfirmadas(original);
        if (reservas.Count == 0)
        {
            return SalidaDePaso.Rechaza(Rejection.Conflict(SinReservas,
                $"La renta {id} no deja ver ninguna reserva confirmada: no se da por cancelada sin soltar las unidades."));
        }

        foreach (var reserva in reservas)
        {
            var cancelada = await agenda.CancelarAsync(reserva, ct);
            if (!cancelada.IsOk && cancelada.Rejection!.Code != YaCancelada) return SalidaDePaso.Rechaza(cancelada.Rejection);
        }
        return SalidaDePaso.Sigue();
    }
}

/// <summary>
/// <c>alquiler.anular-garantia</c>: anula la garantía retenida, si la renta la tiene. Pegamento atribuido a NN-19.
/// </summary>
/// <remarks>
/// Un equipo sin garantía deja <c>garantiaId</c> nulo (la retención se omitió por <c>omitir_si_cero</c>), y
/// <c>payments.anular</c> lee un texto obligatorio: con el nulo pediría anular nada. <c>omitir_si_cero</c> sólo vale en
/// un paso que reserva. Éste es <c>payments.anular</c> con la omisión sobre el nulo; anular lo anulado es idempotente.
/// </remarks>
public sealed class PasoAnularGarantiaSiHay(IDeshacerCobros cobros) : IPaso
{
    public string Tipo => "alquiler.anular-garantia";
    public int Lecturas => 1;
    public int Escrituras => 0;
    public LlaveRequerida Llave => LlaveRequerida.Ninguna;
    public IReadOnlyList<Type> TiposDeEscritura => [];

    public async Task<SalidaDePaso> EjecutarAsync(EntradaDePaso entrada, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        if (entrada.Lee<string?>(0) is not { Length: > 0 } garantia) return SalidaDePaso.Sigue();

        var anulada = await cobros.AnularAsync(garantia, ct);
        return anulada.IsOk ? SalidaDePaso.Sigue() : SalidaDePaso.Rechaza(anulada.Rejection!);
    }
}

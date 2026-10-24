using Synergos.Bff.Core.Flow;
using Synergos.Core;

namespace Synergos.Bff.Alquiler.Domain;

/// <summary>
/// El paso propio de <c>flujos/alquiler.cancelacion.json</c>, la cancelación de lo confirmado como operación posterior
/// al cierre (NN-8; aclaración del director D-005-NN8): la regla del día de inicio.
/// </summary>
/// <remarks>
/// <para><b>Lo demás lo hace el núcleo</b> (bloque 7c bis): la renta deja para lo posterior su cobro, su garantía y lo
/// que apartó por unidad (<c>para_lo_posterior</c>), la cancelación recorre las unidades con <c>booking.cancelar</c>
/// sobre la reserva que produjo cada cierre (NN-18) y anula la garantía con <c>payments.anular</c> sólo si la hay
/// (<c>omitir_si_falta</c>, NN-19). Los dos pasos provisionales de T044 se retiraron con eso.</para>
///
/// <para><b>Primero la plata, al final las unidades.</b> La fase sólo se escribe si sale entera, y repetir con la misma
/// llave vuelve a pasar por las mismas llaves derivadas: devolver y anular otra vez no duplican nada, y desde el núcleo
/// T052 tampoco cancelar en la agenda la reserva que ya canceló ESTA operación (<c>booking.cancelar</c> manda su llave
/// por reserva). El orden se queda: con la plata primero, una devolución caída no suelta ninguna unidad.</para>
/// </remarks>
public static class CancelacionDeAlquiler
{
    /// <summary>El flujo posterior que cancela lo confirmado por la puerta.</summary>
    public const string Flujo = "alquiler.cancelacion";

    /// <summary>Los pasos propios de la cancelación, para sumarlos a los comunes en el registro del orquestador.</summary>
    public static IPaso[] Pasos(LiquidacionDeAlquiler liquidacion) => [new PasoAntesDelInicio(liquidacion)];
}

/// <summary>
/// <c>alquiler.antes-del-inicio</c>: lee el alquiler, su ventana y sus unidades, y rechaza si el inquilino ya no puede
/// cancelarlo —ya empezó en el huso del despliegue, tiene la devolución declarada, o ya se devolvió o se canceló— o si la
/// renta no deja ver ninguna unidad que soltar.
/// </summary>
/// <remarks>
/// <para>La regla es UNA, la de la liquidación propia (research R7, 005-P3): con los dos caminos detrás del interruptor
/// del CMS, cancelar por uno o por el otro dice lo mismo. Va antes de mover nada, con el turno de la liquidación.</para>
///
/// <para><b>Sin unidades no se cancela.</b> Una renta completada siempre cerró sus unidades, pero una guardada antes de
/// que el núcleo anotara el cierre de cada reserva las deja sin nada que recorrer: se devolvería la plata y las unidades
/// seguirían tomadas. Se rechaza con su nombre; esa renta la cancela el personal, por la liquidación propia.</para>
/// </remarks>
public sealed class PasoAntesDelInicio(LiquidacionDeAlquiler liquidacion) : IPaso
{
    /// <summary>La renta original no deja ver ninguna unidad que soltar.</summary>
    public const string SinUnidades = "alquiler.units_not_found";

    public string Tipo => "alquiler.antes-del-inicio";
    public int Lecturas => 3;
    public int Escrituras => 0;
    public LlaveRequerida Llave => LlaveRequerida.Ninguna;
    public IReadOnlyList<Type> TiposDeEscritura => [];

    public Task<SalidaDePaso> EjecutarAsync(EntradaDePaso entrada, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        var id = entrada.Lee<string>(0);
        if (liquidacion.AntesDeCancelarPorElFlujo(id, entrada.Lee<TimeWindow>(1)) is { } no)
        {
            return Task.FromResult(SalidaDePaso.Rechaza(no));
        }
        if (entrada.Lee<IReadOnlyList<FlowContext>>(2) is not { Count: > 0 })
        {
            return Task.FromResult(SalidaDePaso.Rechaza(Rejection.Conflict(SinUnidades,
                $"La renta {id} no deja ver ninguna unidad que soltar: la cancela el personal.")));
        }
        return Task.FromResult(SalidaDePaso.Sigue());
    }
}

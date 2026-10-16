using Synergos.Bff.Core.Flow;
using Synergos.Core;

namespace Synergos.Bff.Reservas.Domain;

// El paso de dominio de la reserva. NO es del caso común: es interino y espera la pieza del núcleo que lo
// retira (011-booking-reserva, research R4). Se cuenta aparte de la meta del flujo (≤ 60 líneas) y se atribuye
// a su necesidad. El de la clase fija del servicio (NR-1) ya se retiró: la clase la declara la definición.

/// <summary>
/// <c>reservas.duracion</c> (interino de NB-1): una franja dura lo que dura el servicio que se publicó.
/// </summary>
/// <remarks>
/// <para><b>El largo lo fija el servidor</b> (FR-007): la ventana llega del navegador y el precio es por servicio,
/// así que sin esta regla una franja de tres horas se cobraría como una de treinta minutos. La duración sale
/// del rastro de la oferta que el propio orquestador publicó (<see cref="RastroDeOfertas"/>). Un servicio sin
/// oferta, o retirado, no se reserva. Lo retira la duración de franja en la agenda (NB-1 del núcleo).</para>
/// </remarks>
public sealed class PasoDuracion(RastroDeOfertas ofertas) : IPaso
{
    /// <summary>El código cuando el servicio no tiene una oferta vigente.</summary>
    public const string NoReservable = "reservas.servicio_no_reservable";

    /// <summary>El código cuando la franja no dura lo que dura el servicio.</summary>
    public const string FranjaInvalida = "reservas.franja_invalida";

    public string Tipo => "reservas.duracion";
    public int Lecturas => 2;
    public int Escrituras => 0;
    public LlaveRequerida Llave => LlaveRequerida.Ninguna;
    public IReadOnlyList<Type> TiposDeEscritura => [];

    public Task<SalidaDePaso> EjecutarAsync(EntradaDePaso entrada, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        var servicio = entrada.Lee<Ref>(0);
        var ventana = entrada.Lee<TimeWindow>(1);

        if (ofertas.Vigente(servicio.Id) is not { } oferta)
        {
            return Task.FromResult(SalidaDePaso.Rechaza(Rejection.NotFound(NoReservable,
                "Ese servicio no se puede reservar en línea ahora.")));
        }

        var minutos = (ventana.End - ventana.Start).TotalMinutes;
        return Task.FromResult(minutos == oferta.DuracionMinutos
            ? SalidaDePaso.Sigue()
            : SalidaDePaso.Rechaza(Rejection.Invalid(FranjaInvalida,
                $"La franja tiene que durar {oferta.DuracionMinutos} minutos, lo que dura el servicio.")));
    }
}

using System.Text.RegularExpressions;
using Synergos.Bff.Core.Flow;
using Synergos.Core;

namespace Synergos.Bff.Reservas.Domain;

// Los dos pasos de dominio de la reserva. NO son del caso común: son interinos y cada uno espera una pieza
// del núcleo que lo retira (011-booking-reserva, research R4 y la regla NR-1 del tablero). Se cuentan aparte
// de la meta del flujo (≤ 60 líneas) y se atribuyen a su necesidad.

/// <summary>
/// <c>reservas.servicio</c> (interino de NR-1): del identificador que manda el navegador a la referencia del
/// servicio, con su CLASE FIJA (<see cref="Clase"/>).
/// </summary>
/// <remarks>
/// <para><b>El navegador nunca elige la clase</b>: con un <c>ref</c> libre del cuerpo podría apartar la agenda de
/// otro vertical (un profesional de Salud, un inmueble) con el precio de un servicio. Sólo aporta el
/// identificador, y la misma referencia sirve al recurso de la agenda y a la tarifa. Lo retira la clase
/// declarada en la definición (NR-1 del núcleo).</para>
/// </remarks>
public sealed partial class PasoServicio : IPaso
{
    /// <summary>La clase del sujeto de un servicio reservable, en la agenda y en precios.</summary>
    public const string Clase = "reservas.servicio";

    /// <summary>El código cuando el identificador no es uno que la oferta pudo publicar.</summary>
    public const string Invalido = "reservas.servicio_invalido";

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex Identificador();

    /// <summary>Si <paramref name="id"/> tiene la forma de la clave de una ficha (la misma regla que la oferta).</summary>
    public static bool EsIdentificador(string id) => Identificador().IsMatch(id);

    public string Tipo => "reservas.servicio";
    public int Lecturas => 1;
    public int Escrituras => 1;
    public LlaveRequerida Llave => LlaveRequerida.Ninguna;
    public IReadOnlyList<Type> TiposDeEscritura => [typeof(Ref)];

    public Task<SalidaDePaso> EjecutarAsync(EntradaDePaso entrada, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        var id = entrada.Lee<string>(0).Trim();
        return Task.FromResult(EsIdentificador(id)
            ? SalidaDePaso.Sigue(Ref.Create(Clase, id))
            : SalidaDePaso.Rechaza(Rejection.Invalid(Invalido, "Ese servicio no existe en este sitio.")));
    }
}

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

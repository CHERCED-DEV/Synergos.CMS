using System.Text.Json;
using Synergos.Core;

namespace Synergos.Bff.Core.Flow;

/// <summary>Un valor que la saga genérica guarda, con el tipo que declaró su flujo.</summary>
/// <param name="Tipo">El tipo declarado, con su <c>?</c> si es anulable (<c>dinero</c>, <c>texto?</c>).</param>
/// <param name="Valor">El valor como lo escribe su tipo; <c>null</c> para el nulo.</param>
/// <remarks>
/// El tipo viaja con el valor a propósito: leerlo después no depende de que la definición siga diciendo
/// lo mismo. Si una versión nueva cambia el tipo de un nombre, las sagas en vuelo se leen con el que
/// tenían y el binding lo compara (un tipo que cambió es un error que se nombra, no un default).
/// </remarks>
public sealed record ValorDeFlujo(string Tipo, JsonElement? Valor);

/// <summary>Lo que reservó un paso: sobre qué se deshace antes y después de consumirlo, y cuánto.</summary>
/// <param name="HoldId">Lo reservado en la capacidad.</param>
/// <param name="CloseTargetId">Sobre qué se deshace una vez consumido.</param>
/// <param name="Cantidad">Cuántas unidades, si el paso lo dijo: reponer existencias consumidas lo necesita.</param>
/// <param name="Cerrada">Sobre qué quedó al cerrarla (núcleo, NN-18 de 005): lo que produjo el cierre —la reserva
/// confirmada de la agenda— o, si no produjo nada, <paramref name="CloseTargetId"/>. Nulo mientras no se cierre, y en una
/// saga guardada antes de que existiera.</param>
/// <remarks>
/// Es lo que necesita una operación posterior que actúa sobre cada ítem: cancelar la reserva confirmada de cada unidad.
/// Antes sólo constaba en la compensación que dejó el cierre, ya dada por hecha.
/// </remarks>
public sealed record ReservaDeFlujo(string HoldId, string CloseTargetId, int? Cantidad = null, string? Cerrada = null);

/// <summary>
/// La saga de un flujo que no tiene saga propia (núcleo N1): guarda lo que su definición necesita y nada
/// más.
/// </summary>
/// <param name="Id">La llave de saga. Semilla de todas las llaves de paso.</param>
/// <param name="Flujo">La clave del flujo: un orquestador con varios flujos genéricos los guarda en el
/// mismo almacén, y una llave reutilizada para otro flujo no devuelve ésta.</param>
/// <param name="Dueno">Quién la abrió: el sujeto que pone la puerta.</param>
/// <param name="Status">En qué punto está.</param>
/// <param name="Datos">Lo que leen las fases que continúan y lo que el flujo declara como salida, por
/// nombre. Nunca lo efímero: ahí van los datos personales.</param>
/// <param name="Reservas">Lo reservado, por el paso que lo reservó. Tiene una entrada (quizá vacía) por
/// cada paso de la definición que reserva: un paso sin entrada no es «nada reservado», es un error.</param>
/// <param name="Compensations">Lo que hay que deshacer.</param>
/// <param name="LastError">Qué falló.</param>
/// <param name="StartedAtUtc">Cuándo arrancó.</param>
/// <param name="AlertedAtUtc">Cuándo se avisó a una persona de que algo quedó colgado.</param>
/// <param name="AlertsSent">Cuántos avisos salieron.</param>
/// <remarks>
/// <para><b>Por qué una saga genérica y no un record por flujo.</b> El record de cada dominio y su
/// binding eran ~150 de las ~316 líneas de pegamento de Eventos (informe 81), y casi todo era copiar
/// nombres del contexto a campos y de vuelta. Aquí los nombres son los de la definición y el binding se
/// deriva de ella (<c>BindingDeFlujo</c>).</para>
///
/// <para><b>Su formato en disco está fijado</b> (<c>SagaDeFlujoPersistidaTests</c>), como el de
/// <c>TicketingSaga</c>: un cambio aquí deja de leer las sagas en vuelo.</para>
/// </remarks>
public sealed record SagaDeFlujo(
    string Id,
    string Flujo,
    Ref Dueno,
    SagaStatus Status,
    IReadOnlyDictionary<string, ValorDeFlujo> Datos,
    IReadOnlyDictionary<string, IReadOnlyList<ReservaDeFlujo>> Reservas,
    IReadOnlyList<Compensation> Compensations,
    string? LastError,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? AlertedAtUtc = null,
    int AlertsSent = 0) : ISaga<SagaDeFlujo>, IHoldLedger, ISagaConDueno
{
    public SagaDeFlujo WithStatus(SagaStatus status) => this with { Status = status };

    public SagaDeFlujo WithCompensations(IReadOnlyList<Compensation> compensations) => this with { Compensations = compensations };

    public SagaDeFlujo WithAlert(DateTimeOffset? alertedAtUtc, int alertsSent)
        => this with { AlertedAtUtc = alertedAtUtc, AlertsSent = alertsSent };

    IReadOnlyList<HoldLeg> IHoldLedger.Legs(string paso)
        => Reservas.TryGetValue(paso, out var reservas)
            ? reservas.Select(r => new HoldLeg(r.HoldId, r.CloseTargetId, r.Cantidad)).ToList()
            : throw new InvalidOperationException($"La saga de «{Flujo}» no guarda reservas de «{paso}».");

    /// <summary>La saga con una reserva más del paso <paramref name="paso"/>.</summary>
    /// <exception cref="InvalidOperationException">Si la saga no guarda reservas de ese paso.</exception>
    public SagaDeFlujo ConReserva(string paso, ReservaDeFlujo reserva)
    {
        ArgumentNullException.ThrowIfNull(reserva);
        if (!Reservas.TryGetValue(paso, out var reservas))
        {
            throw new InvalidOperationException($"La saga de «{Flujo}» no guarda reservas de «{paso}».");
        }

        return this with
        {
            Reservas = new Dictionary<string, IReadOnlyList<ReservaDeFlujo>>(Reservas, StringComparer.Ordinal)
            {
                [paso] = reservas.Append(reserva).ToList(),
            },
        };
    }

    /// <summary>La saga con estos valores guardados (los que ya tenía se reemplazan por nombre).</summary>
    public SagaDeFlujo ConDatos(IReadOnlyDictionary<string, ValorDeFlujo> datos)
    {
        ArgumentNullException.ThrowIfNull(datos);
        var nuevos = new Dictionary<string, ValorDeFlujo>(Datos, StringComparer.Ordinal);
        foreach (var (nombre, valor) in datos) nuevos[nombre] = valor;
        return this with { Datos = nuevos };
    }

    /// <summary>
    /// El número que se le muestra a una persona: los últimos ocho alfanuméricos del identificador, en
    /// mayúscula. La misma regla que el aviso (<c>PasoAvisar.Valores</c>).
    /// </summary>
    /// <remarks>Un método y no una propiedad: una propiedad saldría en el fichero de la saga.</remarks>
    public static string NumeroDe(string sagaId)
    {
        ArgumentNullException.ThrowIfNull(sagaId);
        var alfanumerico = new string(sagaId.Where(char.IsAsciiLetterOrDigit).ToArray()).ToUpperInvariant();
        return alfanumerico.Length <= 8 ? alfanumerico : alfanumerico[^8..];
    }
}

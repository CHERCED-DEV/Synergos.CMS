using Synergos.Bff.Core;
using Synergos.Bff.Core.Flow;
using Synergos.Bff.Pasos;
using Synergos.Core;

namespace Synergos.Bff.Viajes.Domain;

/// <summary>
/// La confirmación parcial de <c>viajes.reserva</c> por la puerta (historia 3, T029; NV-13 del núcleo): la extensión del
/// flujo genérico que contesta si un ítem cuyo cierre falló se suelta, y qué se hace con él.
/// </summary>
/// <remarks>
/// <para><b>La política es del sitio</b>, no del navegador: la pone la puerta en la cabecera de negocio al abrir
/// (<c>Synergos:Features:TravelShell:ConfirmationPolicy</c>) y la saga la guarda. Sólo <see cref="Parcial"/> suelta; todo
/// lo demás —«todo-o-nada», un valor que no se entiende— deshace el viaje entero, como siempre.</para>
///
/// <para><b>Al soltar</b>, el ítem queda marcado «no cumplido» en el itinerario que guarda la saga, con la parte de lo
/// cobrado que le corresponde: <c>total × precio del ítem / subtotal</c>, redondeado hacia abajo a la unidad de la moneda.
/// Repartir sobre el total reparte igual descuento e impuesto; redondear hacia abajo hace que la suma de las partes no pase
/// nunca de lo cobrado (SC-004). Y arma su devolución (<see cref="DevolverParteDelItem"/>), que el intérprete ejecuta en el
/// acto; si no sale, queda pendiente para el barrido.</para>
///
/// <para><b>Si no se cumple ninguno</b> no es una parcial: el intérprete aborta (<c>flow.nada_cumplido</c>) y se deshace
/// entero; la devolución común (<c>payments.devolver</c>) devuelve lo devolvible, así que lo ya devuelto por partes no se
/// devuelve dos veces.</para>
/// </remarks>
public sealed class ExtensionDeViajes : IExtensionDelFlujo
{
    /// <summary>La política que admite confirmar lo que salió y soltar lo que no.</summary>
    public const string Parcial = "parcial";

    /// <summary>El flujo que extiende.</summary>
    public const string ElFlujo = "viajes.reserva";

    /// <summary>El paso que reserva cada ítem: sus reservas van en el orden de los ítems.</summary>
    public const string Apartar = "apartar";

    public string Flujo => ElFlujo;
    public IReadOnlyCollection<string> Lee => ["politica", "total", "subtotal"];
    public IReadOnlyCollection<string> Escribe => ["itinerario"];
    public IReadOnlyCollection<string> Arma => [DevolverParteDelItem.Clase];

    public bool SueltaSiFalla(SagaDeFlujo saga, FlowContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        return ctx.Has("politica") && string.Equals(ctx.Get<string?>("politica")?.Trim(), Parcial, StringComparison.OrdinalIgnoreCase);
    }

    public SagaDeFlujo ConItemSoltado(SagaDeFlujo saga, FlowContext ctx, string reservante, HoldLeg soltada)
    {
        ArgumentNullException.ThrowIfNull(saga);
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(soltada);

        var indice = Indice(saga, soltada.HoldId);
        var itinerario = ctx.Get<IReadOnlyList<ParadaDelViaje>>("itinerario").ToList();
        if (indice < 0 || indice >= itinerario.Count)
        {
            throw new InvalidOperationException($"La saga {saga.Id} no tiene un ítem con el apartado {soltada.HoldId}.");
        }

        var parte = Parte(ctx.Get<Money>("total"), ctx.Get<Money>("subtotal"), itinerario[indice].Precio);
        itinerario[indice] = itinerario[indice] with { NoCumplido = true, Devuelto = parte };
        ctx.Set("itinerario", (IReadOnlyList<ParadaDelViaje>)itinerario);

        // Sin nada que devolver (un ítem gratis, un viaje sin cobro) no se arma nada: Api.Payments rechaza un cero.
        return parte is { Amount: > 0m }
            ? saga.WithCompensations([.. saga.Compensations,
                Compensation.For(DevolverParteDelItem.Clase, soltada.HoldId, $"no se pudo confirmar «{itinerario[indice].Etiqueta}»")])
            : saga;
    }

    /// <summary>
    /// La parte de lo cobrado que le toca a un ítem: <c>total × precio / subtotal</c>, redondeada hacia abajo a la unidad;
    /// nula si no hay con qué calcularla.
    /// </summary>
    public static Money? Parte(Money total, Money subtotal, Money? precio)
    {
        if (precio is not { } p || subtotal.Amount <= 0m || total.Amount <= 0m) return null;
        if (!string.Equals(p.Currency, total.Currency, StringComparison.Ordinal)) return null;
        var parte = Math.Floor(total.Amount * p.Amount / subtotal.Amount);
        return parte > 0m ? Money.Of(Math.Min(parte, total.Amount), total.Currency) : null;
    }

    /// <summary>La posición del ítem cuyo apartado es <paramref name="holdId"/>: el orden de las reservas es el de los ítems.</summary>
    public static int Indice(SagaDeFlujo saga, string holdId)
    {
        ArgumentNullException.ThrowIfNull(saga);
        var reservas = saga.Reservas.GetValueOrDefault(Apartar) ?? [];
        for (var i = 0; i < reservas.Count; i++)
        {
            if (string.Equals(reservas[i].HoldId, holdId, StringComparison.Ordinal)) return i;
        }
        return -1;
    }
}

/// <summary>
/// <c>viajes.devolver-parte</c>: devuelve la parte de lo cobrado de un ítem que no se cumplió, la que la extensión escribió
/// en el itinerario de la saga.
/// </summary>
/// <remarks>
/// <para><b>Lee de la saga, no del contexto</b>: el barrido reintenta sin fase, y lo que vale es lo que quedó guardado.</para>
///
/// <para><b>Nunca más de lo devolvible</b>: si quien vende ya devolvió algo, se devuelve como mucho lo que queda; si no queda
/// nada, se da por hecha (el reintento tras un timeout). La llave sale de la saga y de la compensación, así que reintentar
/// no devuelve dos veces.</para>
/// </remarks>
public sealed class DevolverParteDelItem(IDeshacerCobros cobros, TiposDeValor tipos) : IDeshaceUnTipo
{
    /// <summary>La clase de compensación.</summary>
    public const string Clase = "viajes.devolver-parte";

    public string Tipo => Clase;

    public async Task<Rejection?> DeshacerAsync(SagaDeFlujo saga, Compensation pendiente, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(saga);
        ArgumentNullException.ThrowIfNull(pendiente);

        var indice = ExtensionDeViajes.Indice(saga, pendiente.TargetId);
        var itinerario = Leer(saga, "itinerario") as IReadOnlyList<ParadaDelViaje>;
        if (indice < 0 || itinerario is null || indice >= itinerario.Count || itinerario[indice] is not { NoCumplido: true } caido)
        {
            // Lo armó la extensión con el itinerario ya escrito: si no está, es un defecto que hay que ver, no reintentar.
            return Rejection.Invalid("viajes.parte_sin_item",
                $"La saga {saga.Id} no dice qué ítem no se cumplió con el apartado {pendiente.TargetId}.");
        }
        if (caido.Devuelto is not { Amount: > 0m } parte) return null;

        var cobro = (string)Leer(saga, "cobroId")!;
        var devolvible = await cobros.DevolvibleAsync(cobro, ct);
        if (!devolvible.IsOk) return devolvible.Rejection;
        if (devolvible.Value.Amount <= 0m) return null;

        var monto = Money.Of(Math.Min(parte.Amount, devolvible.Value.Amount), parte.Currency);
        var devuelto = await cobros.DevolverAsync(cobro, monto, pendiente.Reason, saga.KeyFor($"parte:{pendiente.Id}"), ct);
        return devuelto.IsOk ? null : devuelto.Rejection;
    }

    private object? Leer(SagaDeFlujo saga, string nombre)
        => saga.Datos.TryGetValue(nombre, out var guardado) && tipos.Resolver(guardado.Tipo) is { } tipo
            ? tipo.Leer(guardado.Valor, nombre)
            : null;
}

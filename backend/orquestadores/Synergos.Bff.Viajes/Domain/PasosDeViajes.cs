using Synergos.Bff.Core.Flow;
using Synergos.Core;

namespace Synergos.Bff.Viajes.Domain;

/// <summary>Un ítem del viaje como lo guarda y lo devuelve el flujo de la puerta: qué, cómo se llama y su periodo.</summary>
/// <remarks>
/// Sin identificadores de ninguna capacidad y sin precio: el viaje se cotiza entero (lo que costaría un ítem suelto no lo
/// sabe nadie), y el apartado o la reserva de cada ítem son de <c>Api.Booking</c>.
/// </remarks>
public sealed record ParadaDelViaje(string Producto, string Etiqueta, DateTimeOffset Inicio, DateTimeOffset Fin);

/// <summary>
/// Los pasos propios de la reserva por la puerta (<c>flujos/viajes.reserva.json</c>, línea 006): la regla del itinerario
/// y los sujetos de Viajes, lo que no es de ninguna capacidad.
/// </summary>
public static class PasosDeViajes
{
    /// <summary>Los pasos propios, para sumarlos a los comunes en el registro del orquestador.</summary>
    public static IPaso[] Propios() => [new PasoRevisarItinerario(), new PasoSujetoDelProducto(), new PasoVence(), new PasoMenosRetencion()];
}

/// <summary>
/// <c>viajes.revisar-itinerario</c>: lee los ítems; escribe lo que se cotiza (una línea por ítem, con la tarifa de su
/// producto) y el itinerario que guarda y devuelve el viaje.
/// </summary>
/// <remarks>
/// <para><b>Es la regla de <see cref="TripFlow"/> antes de tocar ninguna capacidad</b>, con sus mismos códigos, para que la
/// pantalla diga lo mismo por los dos caminos: sin ítems, demasiados, un producto en blanco, un periodo que no avanza y
/// el mismo producto dos veces EN EL MISMO PERIODO. El mismo producto en otro periodo sí vale (la ida y la vuelta).</para>
///
/// <para><b>La ventana invertida ya la rechaza el tipo <c>ventana</c></b> al leer el cuerpo (<c>viajes.entrada_invalida</c>);
/// se comprueba igual aquí porque la regla es del dominio y no del cable.</para>
///
/// <para><b>El alojamiento se cotiza por noche</b> (lote 10, 006-H2): un ítem con <c>porNoche</c> va con su tarifa POR NOCHE
/// (<see cref="TravelSubject.NightOf"/>) y las noches de su ventana como cantidad; vuelo y auto, con la tarifa del producto
/// y cantidad uno. La marca la pone quien reserva, pero no decide el precio: cada clase de tarifa es otro sujeto en
/// <c>Api.Pricing</c>, así que un alojamiento marcado «por producto» (o un vuelo marcado «por noche») no encuentra tarifa y
/// la cotización se rechaza. Nadie paga una noche por tres.</para>
/// </remarks>
public sealed class PasoRevisarItinerario : IPaso
{
    public string Tipo => "viajes.revisar-itinerario";
    public int Lecturas => 1;
    public int Escrituras => 2;
    public LlaveRequerida Llave => LlaveRequerida.Ninguna;
    public IReadOnlyList<Type> TiposDeEscritura => [typeof(IReadOnlyList<(Ref Sujeto, int Cantidad)>), typeof(IReadOnlyList<ParadaDelViaje>)];

    public Task<SalidaDePaso> EjecutarAsync(EntradaDePaso entrada, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        var items = entrada.Lee<IReadOnlyList<FlowContext>>(0);

        if (items.Count == 0) return Rechaza(Rejection.Invalid("viajes.no_items", "Un viaje necesita al menos un ítem."));
        if (items.Count > TripFlow.MaxItems)
        {
            return Rechaza(Rejection.Invalid("viajes.too_many_items", $"Un viaje admite hasta {TripFlow.MaxItems} ítems."));
        }

        var aCotizar = new List<(Ref Sujeto, int Cantidad)>(items.Count);
        var itinerario = new List<ParadaDelViaje>(items.Count);
        var vistos = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            var producto = item.Get<string>("producto").Trim();
            var ventana = item.Get<TimeWindow>("ventana");
            if (producto.Length == 0) return Rechaza(Rejection.Invalid("viajes.bad_product", "Cada ítem necesita su producto."));
            if (ventana.End <= ventana.Start)
            {
                return Rechaza(Rejection.Invalid("viajes.bad_window",
                    $"El periodo de '{producto}' no avanza: {ventana.Start:O} → {ventana.End:O}."));
            }
            if (!vistos.Add($"{producto}|{ventana.Start:O}|{ventana.End:O}"))
            {
                return Rechaza(Rejection.Invalid("viajes.duplicate_item", $"'{producto}' viene dos veces con el mismo periodo."));
            }

            var etiqueta = item.Has("etiqueta") ? item.Get<string?>("etiqueta")?.Trim() : null;
            var porNoche = item.Has("porNoche") && item.Get<bool?>("porNoche") is true;
            aCotizar.Add(porNoche ? (TravelSubject.NightOf(producto), Noches(ventana)) : (TravelSubject.PriceOf(producto), 1));
            itinerario.Add(new ParadaDelViaje(producto, string.IsNullOrEmpty(etiqueta) ? producto : etiqueta, ventana.Start, ventana.End));
        }

        return Task.FromResult(SalidaDePaso.Sigue(
            (IReadOnlyList<(Ref Sujeto, int Cantidad)>)aCotizar, (IReadOnlyList<ParadaDelViaje>)itinerario));
    }

    private static Task<SalidaDePaso> Rechaza(Rejection motivo) => Task.FromResult(SalidaDePaso.Rechaza(motivo));

    /// <summary>Las noches de una estadía: los días que cubre su ventana, al menos una.</summary>
    public static int Noches(TimeWindow ventana) => Math.Max(1, (int)Math.Ceiling((ventana.End - ventana.Start).TotalDays));
}

/// <summary>
/// <c>viajes.sujeto-del-producto</c>: lee el producto de un ítem y escribe el sujeto de su recurso en la agenda.
/// </summary>
/// <remarks>
/// El recurso se resuelve DESDE el producto (<see cref="TravelSubject.For"/>), como en <see cref="TripFlow"/>: exigirle al
/// navegador el identificador interno de <c>Api.Booking</c> haría que ese identificador viajara hasta el CMS, la fuga que
/// costó una vuelta en la HU #25.
/// </remarks>
public sealed class PasoSujetoDelProducto : IPaso
{
    public string Tipo => "viajes.sujeto-del-producto";
    public int Lecturas => 1;
    public int Escrituras => 1;
    public LlaveRequerida Llave => LlaveRequerida.Ninguna;
    public IReadOnlyList<Type> TiposDeEscritura => [typeof(Ref)];

    public Task<SalidaDePaso> EjecutarAsync(EntradaDePaso entrada, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        return Task.FromResult(SalidaDePaso.Sigue(TravelSubject.For(entrada.Lee<string>(0).Trim())));
    }
}

/// <summary>
/// <c>viajes.vence</c>: lee los ítems ya apartados y escribe cuándo vence el PRIMERO (o nulo si ninguno lo dice).
/// </summary>
/// <remarks>
/// Es lo que la pantalla puede prometer: el viaje sólo se confirma entero, así que el plazo es el del apartado que vence
/// antes. Lo dice la agenda (<c>booking.apartar-por-indice</c> escribe <c>vence</c>); sin ese dato no se inventa uno.
/// </remarks>
public sealed class PasoVence : IPaso
{
    public string Tipo => "viajes.vence";
    public int Lecturas => 1;
    public int Escrituras => 1;
    public LlaveRequerida Llave => LlaveRequerida.Ninguna;
    public IReadOnlyList<Type> TiposDeEscritura => [typeof(DateTimeOffset)];

    public Task<SalidaDePaso> EjecutarAsync(EntradaDePaso entrada, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        var vencimientos = entrada.Lee<IReadOnlyList<FlowContext>>(0)
            .Where(i => i.Has("vence"))
            .Select(i => i.Get<DateTimeOffset?>("vence"))
            .Where(v => v is not null)
            .Select(v => v!.Value)
            .ToList();

        return Task.FromResult(SalidaDePaso.Sigue(vencimientos.Count == 0 ? null : (object)vencimientos.Min()));
    }
}

/// <summary>
/// <c>viajes.menos-retencion</c>: lee el total del viaje y la penalidad que retiene quien vende; escribe lo que se
/// devuelve (total − penalidad), o nulo si no queda nada por devolver.
/// </summary>
/// <remarks>
/// <para><b>La penalidad llega calculada</b> (006-P3, como en <c>/v1/trips</c>): depende de la tarifa y de cuántos días
/// falten, y eso lo sabe quien vendió. Aquí sólo se comprueba que quepa: negativa, de otra moneda o mayor que el total
/// es <c>viajes.bad_penalty</c>, ANTES de cancelar nada.</para>
///
/// <para><b>Nulo y no cero</b> cuando no queda nada (un viaje gratis, o la penalidad entera): <c>Api.Payments</c>
/// rechaza una devolución de cero, y el paso que devuelve se salta con <c>omitir_si_falta</c>. Resta sobre el total
/// cotizado, no sobre lo devolvible del cobro: con una devolución parcial anterior, se pasaría y Payments lo rechazaría
/// con su código (NV-14).</para>
/// </remarks>
public sealed class PasoMenosRetencion : IPaso
{
    /// <summary>El código de una penalidad que no cabe en el total.</summary>
    public const string PenalidadMala = "viajes.bad_penalty";

    public string Tipo => "viajes.menos-retencion";
    public int Lecturas => 2;
    public int Escrituras => 1;
    public LlaveRequerida Llave => LlaveRequerida.Ninguna;
    public IReadOnlyList<Type> TiposDeEscritura => [typeof(Money)];

    public Task<SalidaDePaso> EjecutarAsync(EntradaDePaso entrada, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        var total = entrada.Lee<Money>(0);
        var retener = entrada.Lee<Money>(1);

        if (retener.Amount < 0m)
        {
            return Rechaza($"La penalidad no puede ser negativa: {retener.Amount}.");
        }
        if (!string.Equals(retener.Currency, total.Currency, StringComparison.Ordinal))
        {
            return Rechaza($"La penalidad va en {retener.Currency} y el viaje se cobró en {total.Currency}.");
        }
        if (retener.Amount > total.Amount)
        {
            return Rechaza($"La penalidad ({retener.Amount}) es mayor que el total del viaje ({total.Amount}).");
        }

        var resto = total.Amount - retener.Amount;
        return Task.FromResult(SalidaDePaso.Sigue(resto == 0m ? null : (object)Money.Of(resto, total.Currency)));
    }

    private static Task<SalidaDePaso> Rechaza(string detalle)
        => Task.FromResult(SalidaDePaso.Rechaza(Rejection.Invalid(PenalidadMala, detalle)));
}

using Synergos.Bff.Core.Flow;
using Synergos.Bff.Pasos;
using Synergos.Core;

namespace Synergos.Bff.Viajes.Domain;

/// <summary>Un ítem del viaje como lo guarda y lo devuelve el flujo de la puerta: qué, cómo se llama y su periodo.</summary>
/// <remarks>
/// <para>Sin identificadores de ninguna capacidad: el apartado o la reserva de cada ítem son de <c>Api.Booking</c>.</para>
///
/// <para><b>Lo de la confirmación parcial</b> (historia 3, NV-13): <paramref name="Precio"/> es lo que cotizó el servidor
/// por ese ítem (su tarifa por su cantidad), y sólo sirve para repartir el total si el ítem no se cumple;
/// <paramref name="NoCumplido"/> y <paramref name="Devuelto"/> los escribe la extensión del flujo al soltarlo: qué no se
/// pudo confirmar y cuánto de lo cobrado le corresponde. Lo dice el servidor, no la pantalla.</para>
///
/// <para><b>Cómo se cobra</b> (lote 13, 006-P9): <paramref name="Unidad"/> dice con qué tarifa se cotiza el ítem —por
/// producto (nula), por noche, por día o por trayecto— y, en un vuelo, <paramref name="Tramos"/> (1 la ida, 2 ida y vuelta),
/// <paramref name="Adultos"/> y <paramref name="Ninos"/> (los infantes no pagan). De ahí salen sus <see cref="Lineas"/>, las
/// mismas al cotizar que al repartir el precio por ítem, y quien viaja ve en el itinerario por qué cuesta lo que cuesta.</para>
/// </remarks>
public sealed record ParadaDelViaje(
    string Producto, string Etiqueta, DateTimeOffset Inicio, DateTimeOffset Fin,
    Money? Precio = null, bool NoCumplido = false, Money? Devuelto = null,
    string? Unidad = null, int? Tramos = null, int? Adultos = null, int? Ninos = null)
{
    public const string PorNoche = "noche";
    public const string PorDia = "dia";
    public const string PorTrayecto = "trayecto";

    /// <summary>
    /// Lo que se cotiza por este ítem, una línea por tarifa: la del producto por uno; la de la noche o la del día por los
    /// días de su periodo; la del trayecto por los tramos y los adultos, y la del niño por los tramos y los niños.
    /// </summary>
    public IReadOnlyList<(Ref Sujeto, int Cantidad)> Lineas() => Unidad switch
    {
        PorNoche => [(TravelSubject.NightOf(Producto), Dias(Inicio, Fin))],
        PorDia => [(TravelSubject.DayOf(Producto), Dias(Inicio, Fin))],
        PorTrayecto when Ninos is > 0 =>
        [
            (TravelSubject.LegOf(Producto), (Tramos ?? 1) * (Adultos ?? 1)),
            (TravelSubject.ChildLegOf(Producto), (Tramos ?? 1) * Ninos.Value),
        ],
        PorTrayecto => [(TravelSubject.LegOf(Producto), (Tramos ?? 1) * (Adultos ?? 1))],
        _ => [(TravelSubject.PriceOf(Producto), 1)],
    };

    /// <summary>Los días (o noches) que cubre un periodo, al menos uno.</summary>
    public static int Dias(DateTimeOffset inicio, DateTimeOffset fin) => Math.Max(1, (int)Math.Ceiling((fin - inicio).TotalDays));
}

/// <summary>
/// Los pasos propios de la reserva por la puerta (<c>flujos/viajes.reserva.json</c>, línea 006): la regla del itinerario
/// y los sujetos de Viajes, lo que no es de ninguna capacidad.
/// </summary>
public static class PasosDeViajes
{
    /// <summary>Los pasos propios, para sumarlos a los comunes en el registro del orquestador.</summary>
    public static IPaso[] Propios() =>
    [
        new PasoRevisarItinerario(), new PasoPrecioDelItem(), new PasoSujetoDelProducto(), new PasoVence(),
        new PasoMenosRetencion(), new PasoNotaDelCierre(),
    ];
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
///
/// <para><b>Y el auto por día y el vuelo por trayecto y pasajero</b> (lote 13, 006-P9), como los cobra el catálogo: un ítem
/// con <c>porDia</c> va con la tarifa del día por los días de su ventana; uno con <c>tramos</c> (1 o 2) va con la del
/// trayecto por los tramos y los <c>adultos</c>, y con la del niño por los tramos y los <c>ninos</c>. Un ítem se cobra de
/// una sola manera, y los pasajeros sólo cuentan en un vuelo.</para>
/// </remarks>
public sealed class PasoRevisarItinerario : IPaso
{
    /// <summary>Los pasajeros que pagan en un vuelo, como mucho: los de una reserva de aerolínea.</summary>
    public const int MaxPasajeros = 9;

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
            var (parada, motivo) = Parada(item, producto, string.IsNullOrEmpty(etiqueta) ? producto : etiqueta, ventana);
            if (motivo is not null) return Rechaza(motivo);
            aCotizar.AddRange(parada!.Lineas());
            itinerario.Add(parada);
        }

        return Task.FromResult(SalidaDePaso.Sigue(
            (IReadOnlyList<(Ref Sujeto, int Cantidad)>)aCotizar, (IReadOnlyList<ParadaDelViaje>)itinerario));
    }

    /// <summary>El ítem con su forma de cobro, o por qué no se puede cobrar así.</summary>
    private static (ParadaDelViaje? Parada, Rejection? Motivo) Parada(FlowContext item, string producto, string etiqueta, TimeWindow ventana)
    {
        var porNoche = item.Has("porNoche") && item.Get<bool?>("porNoche") is true;
        var porDia = item.Has("porDia") && item.Get<bool?>("porDia") is true;
        var tramos = item.Has("tramos") ? item.Get<int?>("tramos") : null;
        var adultos = item.Has("adultos") ? item.Get<int?>("adultos") : null;
        var ninos = item.Has("ninos") ? item.Get<int?>("ninos") : null;
        var parada = new ParadaDelViaje(producto, etiqueta, ventana.Start, ventana.End);

        if ((porNoche ? 1 : 0) + (porDia ? 1 : 0) + (tramos is null ? 0 : 1) > 1)
        {
            return (null, Rejection.Invalid("viajes.bad_fare_mark",
                $"'{producto}' se cobra de una sola manera: por noche, por día o por trayecto."));
        }
        if (tramos is null)
        {
            if (adultos is not null || ninos is not null)
            {
                return (null, Rejection.Invalid("viajes.bad_fare_mark", $"Los pasajeros de '{producto}' sólo cuentan en un vuelo, con sus tramos."));
            }
            return (porNoche ? parada with { Unidad = ParadaDelViaje.PorNoche }
                : porDia ? parada with { Unidad = ParadaDelViaje.PorDia }
                : parada, null);
        }
        if (tramos is not (1 or 2))
        {
            return (null, Rejection.Invalid("viajes.bad_fare_mark", $"Un vuelo va de un tramo (ida) o de dos (ida y vuelta); '{producto}' trae {tramos}."));
        }
        var cuantosAdultos = adultos ?? 1;
        var cuantosNinos = ninos ?? 0;
        if (cuantosAdultos < 1 || cuantosNinos < 0 || cuantosAdultos + cuantosNinos > MaxPasajeros)
        {
            return (null, Rejection.Invalid("viajes.bad_passengers",
                $"Un vuelo lleva de 1 a {MaxPasajeros} pasajeros que pagan, con al menos un adulto; '{producto}' trae {cuantosAdultos} y {cuantosNinos}."));
        }
        return (parada with
        {
            Unidad = ParadaDelViaje.PorTrayecto, Tramos = tramos, Adultos = cuantosAdultos, Ninos = cuantosNinos > 0 ? cuantosNinos : null,
        }, null);
    }

    private static Task<SalidaDePaso> Rechaza(Rejection motivo) => Task.FromResult(SalidaDePaso.Rechaza(motivo));

    /// <summary>Las noches de una estadía: los días que cubre su ventana, al menos una.</summary>
    public static int Noches(TimeWindow ventana) => ParadaDelViaje.Dias(ventana.Start, ventana.End);
}

/// <summary>
/// <c>viajes.precio-del-item</c>: lee el itinerario, lo que se cotizó (una línea por ítem, en su orden) y la cotización;
/// escribe el itinerario con el precio de cada ítem: cada tarifa de sus líneas por su cantidad, sumadas, como las cotizó
/// <c>Api.Pricing</c>.
/// </summary>
/// <remarks>
/// <para><b>Para qué</b> (historia 3): si un ítem no se puede confirmar y el sitio admite la parcial, la parte de lo cobrado
/// que le toca sale del precio que cotizó el SERVIDOR, no de la pantalla ni de un reparto a partes iguales.</para>
///
/// <para><b>Se cruza por sujeto</b>, como <see cref="PasoPreciosPorLinea"/>: la respuesta no promete el orden. Una línea sin
/// precio se rechaza con su mismo código, antes de apartar nada: sin el precio de un ítem no se sabría qué devolver.</para>
/// </remarks>
public sealed class PasoPrecioDelItem : IPaso
{
    public string Tipo => "viajes.precio-del-item";
    public int Lecturas => 3;
    public int Escrituras => 1;
    public LlaveRequerida Llave => LlaveRequerida.Ninguna;
    public IReadOnlyList<Type> TiposDeEscritura => [typeof(IReadOnlyList<ParadaDelViaje>)];

    public Task<SalidaDePaso> EjecutarAsync(EntradaDePaso entrada, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        var itinerario = entrada.Lee<IReadOnlyList<ParadaDelViaje>>(0);
        var lineas = entrada.Lee<IReadOnlyList<(Ref Sujeto, int Cantidad)>>(1);
        var precios = new Dictionary<Ref, Money>();
        foreach (var (sujeto, unitario) in entrada.Lee<QuoteView>(2).Lineas) precios.TryAdd(sujeto, unitario);

        // Cada ítem tiene sus líneas (una, o dos en un vuelo con niños): las mismas que se cotizaron, en su orden.
        if (!itinerario.SelectMany(p => p.Lineas()).SequenceEqual(lineas))
        {
            throw new InvalidOperationException($"El itinerario de {itinerario.Count} ítems no es el que se cotizó ({lineas.Count} líneas).");
        }

        var conPrecio = new List<ParadaDelViaje>(itinerario.Count);
        foreach (var parada in itinerario)
        {
            var suma = 0m;
            string? moneda = null;
            foreach (var (sujeto, cantidad) in parada.Lineas())
            {
                if (!precios.TryGetValue(sujeto, out var unitario))
                {
                    return Task.FromResult(SalidaDePaso.Rechaza(Rejection.Invalid(PasoPreciosPorLinea.SinPrecioDeLinea,
                        $"La cotización no trae precio para {sujeto}: no se sabría qué parte del viaje es «{parada.Etiqueta}».")));
                }
                suma += unitario.Amount * cantidad;
                moneda = unitario.Currency;
            }
            conPrecio.Add(parada with { Precio = Money.Of(suma, moneda!) });
        }

        return Task.FromResult(SalidaDePaso.Sigue((IReadOnlyList<ParadaDelViaje>)conPrecio));
    }
}

/// <summary>
/// <c>viajes.nota-del-cierre</c>: lee el itinerario al cerrar y escribe la nota del aviso: vacía si todo se confirmó; si no,
/// qué no se pudo confirmar y cuánto se devuelve, como lo escribió la extensión del flujo.
/// </summary>
/// <remarks>
/// El correo de un viaje parcial no puede decir que «cada parte quedó reservada». La plantilla lleva <c>{nota}</c> y el
/// texto sale del itinerario que guarda la saga, el mismo que ve quien viaja en «Mis viajes».
/// </remarks>
public sealed class PasoNotaDelCierre : IPaso
{
    public string Tipo => "viajes.nota-del-cierre";
    public int Lecturas => 1;
    public int Escrituras => 1;
    public LlaveRequerida Llave => LlaveRequerida.Ninguna;
    public IReadOnlyList<Type> TiposDeEscritura => [typeof(string)];

    public Task<SalidaDePaso> EjecutarAsync(EntradaDePaso entrada, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        return Task.FromResult(SalidaDePaso.Sigue(Nota(entrada.Lee<IReadOnlyList<ParadaDelViaje>>(0))));
    }

    /// <summary>La nota de un itinerario: vacía sin ítems no cumplidos.</summary>
    public static string Nota(IReadOnlyList<ParadaDelViaje> itinerario)
    {
        ArgumentNullException.ThrowIfNull(itinerario);
        var caidos = itinerario.Where(p => p.NoCumplido).ToList();
        if (caidos.Count == 0) return string.Empty;

        var nombres = string.Join(", ", caidos.Select(p => p.Etiqueta));
        var devuelto = caidos.Where(p => p.Devuelto is { Amount: > 0m }).Select(p => p.Devuelto!.Value).ToList();
        var texto = $" No se pudo confirmar: {nombres}.";
        if (devuelto.Count == 0) return texto;

        var suma = Money.Of(devuelto.Sum(m => m.Amount), devuelto[0].Currency);
        return $"{texto} Te devolvemos su parte: {PasoAvisar.Escribir(suma, TimeZoneInfo.Utc)}.";
    }
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

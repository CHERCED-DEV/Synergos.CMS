using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Synergos.Bff.Core;
using Synergos.Bff.Eventos.Clients;
using Synergos.Core;
using Synergos.Shared;

namespace Synergos.Bff.Eventos.Domain;

/// <summary>Lo que el CMS publica de un evento para que se pueda vender, tal como llega.</summary>
/// <param name="EventId">De qué evento.</param>
/// <param name="Currency">En qué moneda están los precios de sus localidades.</param>
/// <param name="StartsAtUtc">Cuándo empieza: desde ahí no se vende, tenga la localidad ventana o no.</param>
/// <param name="Tiers">Sus localidades.</param>
/// <remarks>
/// Todo anulable porque así llega del cable, y las reglas de abajo dicen qué falta con su código. Es el
/// mismo reparto que la compra: el contrato publica la forma, el dominio lo que exige.
/// </remarks>
public sealed record OfertaDeEvento(
    string? EventId, string? Currency, DateTimeOffset? StartsAtUtc, IReadOnlyList<LocalidadOfertada>? Tiers);

/// <summary>Una localidad de la oferta.</summary>
/// <param name="Code">El código de la localidad, el mismo con que la nombra una línea de la compra.</param>
/// <param name="Price">Cuánto vale una entrada.</param>
/// <param name="MaxPerOrder">Cuántas admite una compra, sumando sus líneas. Nulo: sin tope.</param>
/// <param name="Capacity">El aforo de cupo general. Con butacas no se mira: cada una es un pozo de 1.</param>
/// <param name="Seats">Las butacas nominadas, si la localidad las tiene.</param>
/// <param name="SaleOpensUtc">Desde cuándo se vende, incluido. Nulo: desde ya.</param>
/// <param name="SaleClosesUtc">Hasta cuándo, excluido. Nulo: hasta que empiece el evento.</param>
public sealed record LocalidadOfertada(
    string? Code, decimal? Price, int? MaxPerOrder, int? Capacity, IReadOnlyList<string>? Seats,
    DateTimeOffset? SaleOpensUtc, DateTimeOffset? SaleClosesUtc);

/// <summary>Lo que quedó publicado de una localidad: lo que dijo <c>Api.Pricing</c> y el aforo declarado.</summary>
/// <param name="Pools">Cuántos pozos de aforo tiene: uno en cupo general, uno por butaca nominada.</param>
public sealed record LocalidadPublicada(
    string Code, Money Price, DateTimeOffset? ValidFrom, DateTimeOffset? ValidTo, int? MaxPerOrder, int Capacity, int Pools);

/// <summary>La oferta de un evento, publicada.</summary>
public sealed record OfertaPublicada(string EventId, IReadOnlyList<LocalidadPublicada> Tiers);

/// <summary>Lo que este orquestador declaró en un pozo de aforo, y el ajuste que dejó a medias si lo hay.</summary>
/// <param name="Pozo">El sujeto del pozo, como texto (<see cref="AforoSubject"/>).</param>
/// <param name="Declarado">El aforo que declaró la última vez que terminó. Nulo: nunca terminó de declararlo.</param>
/// <param name="Hacia">El aforo hacia el que iba un ajuste que puede no haber llegado. Nulo: nada a medias.</param>
/// <param name="Llave">La llave con que salió ese ajuste: repetirlo con ella no lo aplica dos veces.</param>
public sealed record AforoPublicado(string Pozo, int? Declarado, int? Hacia = null, string? Llave = null);

/// <summary>Un pozo que este orquestador publicó: de qué localidad, y de qué butaca si la tiene.</summary>
public sealed record PozoRegistrado(string Localidad, string? Butaca);

/// <summary>
/// Lo que este orquestador publicó de un evento la última vez: sus localidades y sus pozos (ADR 0140 F3).
/// </summary>
/// <remarks>
/// Es el rastro de sus propios pasos, como <see cref="AforoPublicado"/>: sin él no sabría qué dejar de
/// vender cuando una localidad o una butaca sale de la oferta, o cuando el evento se retira entero.
/// </remarks>
public sealed record OfertaRegistrada(
    string EventId, string Currency, IReadOnlyList<string> Localidades, IReadOnlyList<PozoRegistrado> Pozos);

/// <summary>
/// Publica la oferta de un evento en las capacidades: el precio de cada localidad en <c>Api.Pricing</c> y
/// sus pozos de aforo en <c>Api.Inventory</c> (ADR 0140 F3).
/// </summary>
/// <remarks>
/// <para><b>Por qué existe.</b> El modo Bff no podía vender el catálogo del CMS: nadie publicaba
/// <c>eventos.localidad</c> ni <c>eventos.aforo</c>, y toda compra salía con <c>price_not_found</c> o «esa
/// localidad no tiene aforo declarado» (medido por dos informes del plan de la F3). Los sujetos son los de
/// la compra (<see cref="AforoSubject"/>), y por eso se publica desde acá y no desde el CMS: escribir
/// directo en las capacidades le habría copiado al CMS el vocabulario de sujetos.</para>
///
/// <para><b>El calendario de venta va en la vigencia del precio</b>: <c>[abre, min(cierra, empieza))</c>.
/// Un evento que empezó no se vende aunque su localidad diga que cierra al otro día, y el rechazo llega al
/// cotizar, que va primero en el flujo: sin apartar nada. El tope por compra va en el tope por cotización.
/// Y el impuesto es CERO (<see cref="ImpuestoEnPuntosBasicos"/>).</para>
///
/// <para><b>El aforo se ajusta con un RELATIVO y no fijando el total</b>, porque <c>Api.Inventory</c> lleva
/// lo que QUEDA, no lo declarado: cada venta lo baja. Fijar el total en el aforo nuevo devolvería al pozo
/// lo ya vendido. Ese relativo es «el aforo pasó de 100 a 120», y para saber el 100 hace falta recordar
/// lo que se declaró — la capacidad no lo sabe, y el CMS sólo tiene el valor nuevo. Por eso este
/// orquestador guarda, por pozo, <see cref="AforoPublicado"/>: no es una copia del cupo —lo que queda
/// sigue siendo de <c>Api.Inventory</c>, la única verdad— sino el rastro de los pasos que él mismo dio,
/// que es lo único que un orquestador guarda (<see cref="ISagaStore{TSaga}"/>).</para>
///
/// <para><b>Un ajuste se anota ANTES de salir y se cierra al volver.</b> Si el proceso muere entre las dos
/// cosas, la publicación siguiente encuentra el ajuste a medias y lo repite con SU llave: si había llegado,
/// la capacidad lo reconoce y no lo suma dos veces; si no, lo aplica. Sin la anotación, una publicación
/// con otro aforo calcularía su relativo contra un declarado viejo y el pozo quedaría corrido para
/// siempre, sin error.</para>
///
/// <para><b>Una publicación a la vez</b>, aunque haya réplicas (<see cref="StoreWriteGate"/>): el publicar
/// del editor y el republicar del administrador pueden coincidir, y dos que leen el mismo declarado suman
/// el mismo relativo dos veces.</para>
///
/// <para><b>La oferta es el estado ENTERO del evento</b> (ADR 0140 F3): lo que una publicación no trae deja
/// de venderse. Una localidad que sale de la oferta —borrada, con aforo cero, o que el contenido omite
/// por mal formada— se RETIRA: su precio deja de valer (vigencia hasta ahora), así que cotizar, que va
/// primero, la rechaza sin apartar nada. Una butaca que sale, o un pozo de cupo general que pasa a
/// butacas, se AGOTA: su aforo baja a lo vendido y apartado. Y un evento despublicado o borrado se
/// retira entero (<see cref="RetirarAsync"/>). No se borra nada: el precio y el pozo siguen ahí, con su
/// historia, y republicar los vuelve a poner a la venta.</para>
///
/// <para><b>El aforo nunca baja de lo vendido y apartado.</b> Bajarlo por debajo lo rechazaba
/// <c>Api.Inventory</c> y la publicación quedaba a medias. Ahora se recorta a lo comprometido, se
/// anota como declarado lo que de verdad quedó y el log lo dice: un aforo de 2 con 7 vendidas queda en
/// 7, y subirlo después a 12 deja 5 libres.</para>
///
/// <para><b>Cada localidad es independiente</b>: una que falla —una capacidad caída— no impide publicar
/// las demás. La publicación sigue, y al final contesta el rechazo de la primera que falló con la lista
/// de las que no se publicaron; republicar termina el resto sin duplicar nada.</para>
/// </remarks>
public sealed class OfertaDeEventos : IDisposable
{
    /// <summary>El impuesto con que se publica el precio de una localidad, en puntos básicos.</summary>
    /// <remarks>
    /// Cero porque el precio de la ficha ya es lo que paga el comprador: con impuesto, el orquestador
    /// cobraría más de lo que pinta el contenido, y el modo en proceso cobra el precio tal cual.
    /// </remarks>
    public const int ImpuestoEnPuntosBasicos = 0;

    // El prefijo de los códigos del BFF, el de sus endpoints: no se referencia la clase de los endpoints
    // para que el dominio no dependa del borde.
    private const string Prefijo = "eventos";

    // Sólo ESTE código es «no hay pozo». Un 404 de una ruta que no existe trae otro y es un fallo de verdad:
    // tomarlo por «no hay» declararía encima.
    private const string NoExiste = $"{EventosCapabilities.Inventory}.item_not_found";

    private readonly EventosCapabilities _capacidades;
    private readonly JsonCollectionStore<AforoPublicado> _publicados;
    private readonly JsonCollectionStore<OfertaRegistrada> _ofertas;
    private readonly StoreWriteGate _turno;
    private readonly TimeProvider _reloj;
    private readonly ILogger<OfertaDeEventos> _log;

    public OfertaDeEventos(
        EventosCapabilities capacidades, IOptions<SagaStorageOptions> almacen, TimeProvider reloj, ILogger<OfertaDeEventos> log)
    {
        _capacidades = capacidades;
        var raiz = Path.Combine(almacen.Value.Root, "ofertas");
        _publicados = new JsonCollectionStore<AforoPublicado>(raiz, "aforos", a => a.Pozo);
        _ofertas = new JsonCollectionStore<OfertaRegistrada>(raiz, "eventos", o => o.EventId);
        _turno = new StoreWriteGate(raiz, Prefijo);
        _reloj = reloj;
        _log = log;
    }

    /// <inheritdoc />
    public void Dispose() => _turno.Dispose();

    /// <summary>Publica la oferta. Repetirla —con la misma llave o con otra— no duplica nada.</summary>
    /// <param name="oferta">Lo que llegó.</param>
    /// <param name="llave">La llave de la publicación: de ella salen las del precio y las de cada ajuste.</param>
    /// <param name="ct">Cancelación.</param>
    /// <returns>Lo publicado; o, si alguna localidad no se pudo publicar, el rechazo de la primera que
    /// falló con la lista de todas las que faltan. Las demás quedan publicadas igual.</returns>
    public async Task<Result<OfertaPublicada>> PublicarAsync(OfertaDeEvento oferta, IdempotencyKey llave, CancellationToken ct)
    {
        // La FORMA se comprueba antes de escribir nada: una oferta con la tercera localidad mal no deja
        // las dos primeras publicadas y el resto a medias. Lo que la capacidad rechace después ya no es
        // de forma, y cada localidad sigue por su lado.
        var motivo = Revisar(oferta);
        if (motivo is not null) return Result.Rejected<OfertaPublicada>(motivo);

        using var turno = await _turno.TryEnterAsync(ct);
        if (turno is null) return Result.Rejected<OfertaPublicada>(_turno.Ocupado);

        var evento = oferta.EventId!;
        var moneda = oferta.Currency!;
        var inicio = oferta.StartsAtUtc!.Value;
        var publicadas = new List<LocalidadPublicada>(oferta.Tiers!.Count);
        var fallos = new List<(string Que, Rejection Motivo)>();
        var pozosVivos = new List<PozoRegistrado>();

        foreach (var l in oferta.Tiers!)
        {
            var (desde, hasta) = Vigencia(l, inicio);
            var monto = Money.Of(l.Price!.Value, moneda);
            var sujeto = AforoSubject.PriceOf(evento, l.Code!);
            var pozos = Pozos(l);
            pozosVivos.AddRange(pozos.Select(p => p.Pozo));

            var precio = await _capacidades.SetPriceAsync(sujeto, monto, ImpuestoEnPuntosBasicos, desde, hasta, l.MaxPerOrder,
                Derivada(llave.Value, "precio", sujeto.ToString(), monto.ToString(), desde?.ToString("O"), hasta.ToString("O"),
                    l.MaxPerOrder?.ToString(CultureInfo.InvariantCulture)), ct);
            if (!precio.IsOk)
            {
                fallos.Add((l.Code!, precio.Rejection!));
                continue;
            }

            var declarado = 0;
            Rejection? fallo = null;
            foreach (var (pozo, aforo) in pozos)
            {
                var asegurado = await AsegurarAsync(evento, pozo, aforo, llave, crear: true, ct);
                if (asegurado.IsOk) declarado += asegurado.Value;
                else fallo ??= asegurado.Rejection;
            }
            if (fallo is not null)
            {
                fallos.Add((l.Code!, fallo));
                continue;
            }

            var p = precio.Value;
            publicadas.Add(new LocalidadPublicada(
                l.Code!, Money.Of(p.Amount.Amount, p.Amount.Currency), p.ValidFrom, p.ValidTo, p.MaxPerQuote,
                declarado, pozos.Count));
        }

        // Lo que la publicación anterior traía y ésta no, deja de venderse.
        var actuales = oferta.Tiers!.Select(l => l.Code!).ToList();
        var pendientes = await RetirarLoQueFaltaAsync(evento, actuales, pozosVivos, llave, fallos, ct);
        _ofertas.Put(new OfertaRegistrada(evento, moneda,
            actuales.Concat(pendientes.Localidades).Distinct(StringComparer.Ordinal).ToList(),
            pozosVivos.Concat(pendientes.Pozos).Distinct().ToList()));

        return fallos.Count == 0
            ? Result.Ok(new OfertaPublicada(evento, publicadas))
            : Result.Rejected<OfertaPublicada>(Juntar(evento, fallos));
    }

    /// <summary>
    /// Retira la oferta entera de un evento: ninguna de sus localidades se vende más (el evento se despublicó
    /// o se borró). Repetirlo no hace daño; un evento que nunca se publicó no tiene nada que retirar.
    /// </summary>
    public async Task<Result<OfertaPublicada>> RetirarAsync(string? eventId, IdempotencyKey llave, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(eventId)) return Result.Rejected<OfertaPublicada>(Invalido("bad_event", "Hace falta eventId."));

        using var turno = await _turno.TryEnterAsync(ct);
        if (turno is null) return Result.Rejected<OfertaPublicada>(_turno.Ocupado);

        var fallos = new List<(string Que, Rejection Motivo)>();
        var pendientes = await RetirarLoQueFaltaAsync(eventId, [], [], llave, fallos, ct);
        if (_ofertas.Find(eventId) is { } previa)
        {
            _ofertas.Put(previa with { Localidades = pendientes.Localidades, Pozos = pendientes.Pozos });
        }

        return fallos.Count == 0
            ? Result.Ok(new OfertaPublicada(eventId, []))
            : Result.Rejected<OfertaPublicada>(Juntar(eventId, fallos));
    }

    /// <summary>
    /// Deja de vender lo que la oferta registrada del evento traía y la nueva no: retira el precio de cada
    /// localidad que falta y agota cada pozo que falta. Devuelve lo que no se pudo retirar, que se queda
    /// registrado para que la próxima publicación lo vuelva a intentar.
    /// </summary>
    private async Task<(List<string> Localidades, List<PozoRegistrado> Pozos)> RetirarLoQueFaltaAsync(
        string evento, IReadOnlyCollection<string> localidades, IReadOnlyCollection<PozoRegistrado> pozos,
        IdempotencyKey llave, List<(string Que, Rejection Motivo)> fallos, CancellationToken ct)
    {
        var quedan = (Localidades: new List<string>(), Pozos: new List<PozoRegistrado>());
        if (_ofertas.Find(evento) is not { } previa) return quedan;

        var ahora = _reloj.GetUtcNow();
        foreach (var codigo in previa.Localidades.Where(c => !localidades.Contains(c, StringComparer.Ordinal)))
        {
            var sujeto = AforoSubject.PriceOf(evento, codigo);
            var retirado = await _capacidades.SetPriceAsync(sujeto, Money.Of(0m, previa.Currency), ImpuestoEnPuntosBasicos,
                null, ahora, null, Derivada(llave.Value, "retirar", sujeto.ToString(), ahora.ToString("O")), ct);
            if (retirado.IsOk)
            {
                _log.LogInformation("La localidad {Sujeto} salió de la oferta: su precio deja de valer.", sujeto);
                continue;
            }
            fallos.Add((codigo, retirado.Rejection!));
            quedan.Localidades.Add(codigo);
        }

        foreach (var pozo in previa.Pozos.Where(p => !pozos.Contains(p)))
        {
            var agotado = await AsegurarAsync(evento, pozo, 0, llave, crear: false, ct);
            if (agotado.IsOk) continue;
            fallos.Add((pozo.Butaca is null ? pozo.Localidad : $"{pozo.Localidad}/{pozo.Butaca}", agotado.Rejection!));
            quedan.Pozos.Add(pozo);
        }

        return quedan;
    }

    /// <summary>El rechazo de la primera parte que falló, con la lista de todas las que faltan.</summary>
    private static Rejection Juntar(string evento, List<(string Que, Rejection Motivo)> fallos)
    {
        var primero = fallos[0].Motivo;
        return primero with
        {
            Message = $"{primero.Message} De la oferta de {evento} no quedó al día: "
                      + string.Join(", ", fallos.Select(f => $"{f.Que} ({f.Motivo.Code})").Distinct(StringComparer.Ordinal))
                      + ". Lo demás sí; republicar termina lo que falta sin duplicar nada.",
        };
    }

    /// <summary>La vigencia del precio: desde que abre, hasta lo PRIMERO entre el cierre y el inicio.</summary>
    /// <remarks>
    /// Es la regla del CMS (<c>CalendarioDeVenta</c>): un evento que empezó no se vende nunca, y esa regla es
    /// del evento, no de la localidad. Una localidad que «cierra» al otro día del inicio cierra al inicio.
    /// </remarks>
    private static (DateTimeOffset? Desde, DateTimeOffset Hasta) Vigencia(LocalidadOfertada l, DateTimeOffset inicio)
        => (l.SaleOpensUtc, l.SaleClosesUtc is { } cierra && cierra < inicio ? cierra : inicio);

    /// <summary>Los pozos de una localidad con su aforo: uno de cupo general, o uno de 1 por butaca.</summary>
    private static List<(PozoRegistrado Pozo, int Aforo)> Pozos(LocalidadOfertada l)
        => l.Seats is { Count: > 0 } butacas
            ? butacas.Select(b => (new PozoRegistrado(l.Code!, b), 1)).ToList()
            : [(new PozoRegistrado(l.Code!, null), l.Capacity!.Value)];

    /// <summary>
    /// Deja el pozo con <paramref name="aforo"/> declarado —o con lo vendido y apartado, si es más—: lo
    /// declara, lo ajusta o no hace nada. Devuelve el aforo que quedó declarado.
    /// </summary>
    /// <param name="crear">Si el pozo se declara cuando no existe. Agotar uno que no existe no crea nada.</param>
    private async Task<Result<int>> AsegurarAsync(
        string evento, PozoRegistrado registrado, int aforo, IdempotencyKey publicacion, bool crear, CancellationToken ct)
    {
        var pozo = AforoSubject.For(evento, registrado.Localidad, registrado.Butaca);
        var clave = pozo.ToString();
        var registro = _publicados.Find(clave);

        // Primero lo que quedó a medias, con SU llave: si llegó, la capacidad lo reconoce y no lo repite.
        if (registro is { Hacia: { } pendiente })
        {
            var terminado = await AplicarAsync(pozo, registro.Declarado, pendiente, IdempotencyKey.Of(registro.Llave!), ct);
            if (!terminado.IsOk && terminado.Rejection!.IsTransient) return Result.Rejected<int>(terminado.Rejection);

            // Un rechazo que no es transitorio dice que no se aplicó: lo declarado sigue siendo lo de antes.
            registro = terminado.IsOk ? new AforoPublicado(clave, pendiente) : new AforoPublicado(clave, registro.Declarado);
            _publicados.Put(registro);
        }

        var item = await _capacidades.FindAforoAsync(pozo, ct);
        if (!item.IsOk && item.Rejection!.Code != NoExiste) return Result.Rejected<int>(item.Rejection);
        if (!item.IsOk && !crear) return Result.Ok(0);

        int? desde;
        var hacia = aforo;
        if (!item.IsOk)
        {
            desde = null;
        }
        else if (registro?.Declarado is not { } declarado)
        {
            // Un pozo que este orquestador no declaró: no sabe sobre qué aforo se declaró, así que no
            // ajusta a ciegas. Lo toma como declarado en lo que dice la oferta y lo deja escrito.
            _log.LogWarning(
                "El pozo {Pozo} ya existía y no lo declaró esta publicación: se toma {Aforo} como su aforo declarado, sin ajustar.",
                clave, aforo);
            _publicados.Put(new AforoPublicado(clave, aforo));
            return Result.Ok(aforo);
        }
        else
        {
            // Nunca por debajo de lo comprometido: lo declarado menos lo que queda libre es lo vendido más lo
            // apartado, y Api.Inventory rechaza bajar de ahí. Se recorta y se dice.
            var comprometido = declarado - item.Value.Available;
            if (aforo < comprometido)
            {
                _log.LogWarning(
                    "El pozo {Pozo} no baja a {Aforo}: tiene {Comprometido} vendidas o apartadas, y queda declarado en {Comprometido}.",
                    clave, aforo, comprometido, comprometido);
                hacia = comprometido;
            }
            if (declarado == hacia) return Result.Ok(hacia);
            desde = declarado;
        }

        var llave = desde is null
            ? Derivada("declarar", clave, hacia.ToString(CultureInfo.InvariantCulture))
            : Derivada(publicacion.Value, "ajustar", clave, desde.Value.ToString(CultureInfo.InvariantCulture),
                hacia.ToString(CultureInfo.InvariantCulture));

        _publicados.Put(new AforoPublicado(clave, desde, hacia, llave.Value));
        var r = await AplicarAsync(pozo, desde, hacia, llave, ct);
        if (r.IsOk)
        {
            _publicados.Put(new AforoPublicado(clave, hacia));
            return Result.Ok(hacia);
        }

        if (!r.Rejection!.IsTransient) _publicados.Put(new AforoPublicado(clave, desde));
        return Result.Rejected<int>(r.Rejection);
    }

    /// <summary>Declara el pozo si no hay declarado, o le suma la diferencia si lo hay.</summary>
    private async Task<Result<bool>> AplicarAsync(Ref pozo, int? desde, int hacia, IdempotencyKey llave, CancellationToken ct)
    {
        if (desde is not { } declarado)
        {
            return (await _capacidades.DeclareAforoAsync(pozo, hacia, llave, ct)).Map(_ => true);
        }

        var item = await _capacidades.FindAforoAsync(pozo, ct);
        if (!item.IsOk) return Result.Rejected<bool>(item.Rejection!);

        _log.LogInformation("El aforo de {Pozo} pasa de {Desde} a {Hacia}.", pozo, declarado, hacia);
        return (await _capacidades.RestockAforoAsync(item.Value.Id, hacia - declarado, llave, ct)).Map(_ => true);
    }

    /// <summary>
    /// Una llave derivada de sus partes, con una huella para que quepa: las partes las escribe quien
    /// publica y pueden traer barras o pasar de 128 juntas.
    /// </summary>
    private static IdempotencyKey Derivada(params string?[] partes)
        => IdempotencyKey.Of("oferta|" + Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('|', partes.Select(p => p ?? "-"))))).ToLowerInvariant()[..40]);

    /// <summary>Lo que la oferta tiene que traer, con el código de cada falta.</summary>
    private static Rejection? Revisar(OfertaDeEvento o)
    {
        if (string.IsNullOrWhiteSpace(o.EventId)) return Invalido("bad_event", "Hace falta eventId.");
        if (o.Currency is not { Length: 3 } || !o.Currency.All(char.IsAsciiLetter))
        {
            return Invalido("bad_offer", "Hace falta currency, un código ISO 4217 de tres letras.");
        }
        if (o.StartsAtUtc is null) return Invalido("bad_offer", "Hace falta startsAtUtc: desde ahí el evento no se vende.");
        if (o.Tiers is not { Count: > 0 }) return Invalido("bad_offer", "Hace falta al menos una localidad en tiers.");

        var codigos = new HashSet<string>(StringComparer.Ordinal);
        foreach (var l in o.Tiers)
        {
            if (string.IsNullOrWhiteSpace(l.Code) || !codigos.Add(l.Code))
            {
                return Invalido("bad_tier", "Cada localidad necesita un code, y distinto del de las demás.");
            }
            if (l.Price is not >= 0m) return Invalido("bad_price", $"La localidad {l.Code} necesita un price que no sea negativo.");
            if (l.MaxPerOrder is <= 0) return Invalido("bad_max_per_order", $"El tope por compra de {l.Code} tiene que ser mayor que cero.");

            if (l.Seats is { Count: > 0 } butacas)
            {
                if (butacas.Any(string.IsNullOrWhiteSpace) || butacas.Distinct(StringComparer.Ordinal).Count() != butacas.Count)
                {
                    return Invalido("bad_capacity", $"Las butacas de {l.Code} tienen que tener nombre y no repetirse.");
                }
            }
            else if (l.Capacity is not >= 0)
            {
                return Invalido("bad_capacity", $"La localidad {l.Code} necesita un capacity que no sea negativo, o sus butacas.");
            }

            var (desde, hasta) = Vigencia(l, o.StartsAtUtc.Value);
            if (desde is { } abre && abre >= hasta)
            {
                return Invalido("bad_sale_window",
                    $"La venta de {l.Code} abre {abre:O} y tiene que abrir antes de cerrar o de que empiece el evento ({hasta:O}).");
            }
        }
        return null;
    }

    private static Rejection Invalido(string code, string message) => Rejection.Invalid($"{Prefijo}.{code}", message);
}

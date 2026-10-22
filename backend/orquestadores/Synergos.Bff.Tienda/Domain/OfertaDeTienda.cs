using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Synergos.Bff.Core;
using Synergos.Bff.Tienda.Clients;
using Synergos.Core;
using Synergos.Shared;

namespace Synergos.Bff.Tienda.Domain;

/// <summary>Lo que el CMS publica de un producto para que se pueda vender, tal como llega.</summary>
/// <param name="ProductId">De qué producto: el id con que lo nombra una línea de la compra.</param>
/// <param name="Currency">En qué moneda están los precios de sus variantes.</param>
/// <param name="Variants">Lo que se vende de él: una sin <c>VariantId</c> si no tiene variantes.</param>
/// <remarks>
/// Todo anulable porque así llega del cable, y las reglas de abajo dicen qué falta con su código (el
/// reparto de <c>OfertaDeEventos</c>: el contrato publica la forma, el dominio lo que exige).
/// </remarks>
public sealed record OfertaDeProducto(string? ProductId, string? Currency, IReadOnlyList<VarianteOfertada>? Variants);

/// <summary>Una variante de la oferta de un producto.</summary>
/// <param name="VariantId">Cuál; nulo para el producto sin variantes.</param>
/// <param name="Price">Cuánto vale una unidad.</param>
/// <param name="Stock">Cuántas declara el editor que hay.</param>
public sealed record VarianteOfertada(string? VariantId, decimal? Price, int? Stock);

/// <summary>Lo que quedó publicado de una variante: lo que dijo <c>Api.Pricing</c> y las existencias declaradas.</summary>
public sealed record VariantePublicada(string? VariantId, Money Price, int Stock);

/// <summary>La oferta de un producto, publicada.</summary>
public sealed record OfertaPublicada(string ProductId, IReadOnlyList<VariantePublicada> Variants);

/// <summary>Lo que este orquestador declaró en el pozo de existencias de una variante, y el ajuste a medias.</summary>
/// <param name="Pozo">El sujeto del pozo, como texto.</param>
/// <param name="Declarado">Las existencias que declaró la última vez que terminó. Nulo: nunca terminó.</param>
/// <param name="Hacia">Hacia dónde iba un ajuste que puede no haber llegado. Nulo: nada a medias.</param>
/// <param name="Llave">La llave con que salió ese ajuste: repetirlo con ella no lo aplica dos veces.</param>
public sealed record ExistenciasPublicadas(string Pozo, int? Declarado, int? Hacia = null, string? Llave = null);

/// <summary>Lo que este orquestador publicó de un producto la última vez: el id de cada variante a la venta.</summary>
public sealed record ProductoRegistrado(string ProductId, string Currency, IReadOnlyList<string> Sujetos);

/// <summary>
/// Publica la oferta de un producto del catálogo en las capacidades: el precio de cada variante en
/// <c>Api.Pricing</c> y sus existencias en <c>Api.Inventory</c> (004-tienda-compra, R-9, FR-023).
/// </summary>
/// <remarks>
/// <para><b>Por qué existe.</b> Nadie publicaba los precios ni las existencias de la tienda: la compra por
/// la puerta (y la del motor imperativo) cotiza y aparta contra <c>tienda.producto</c>, y sin oferta todo
/// salía con «no hay precio». Lo publica el CMS al publicar o retirar un producto, y se publica desde acá
/// —no escribiendo él en las capacidades— para que el vocabulario de sujetos sea de un solo lado. Es el
/// molde de <c>OfertaDeEventos</c> sin lo que es de eventos (localidades, butacas, calendario de venta).</para>
///
/// <para><b>El sujeto de una variante es el de la línea de la compra</b>: <c>tienda.producto:{producto}</c>
/// o <c>tienda.producto:{producto}:{variante}</c>, la forma que ya usa la canasta del CMS. Por eso ni el
/// producto ni la variante pueden llevar «:».</para>
///
/// <para><b>Las existencias se ajustan con un RELATIVO</b>, porque <c>Api.Inventory</c> lleva lo que QUEDA:
/// fijar el total devolvería al pozo lo ya vendido. Para el relativo hace falta recordar lo declarado, y por
/// eso se guarda <see cref="ExistenciasPublicadas"/> por pozo: el rastro de los pasos propios, anotado
/// ANTES de salir y cerrado al volver, para que un ajuste cuya respuesta se perdió se termine con su llave
/// y no se sume dos veces. <b>Nunca bajan de lo comprometido</b> (vendido más apartado): se recorta y se
/// dice en el log.</para>
///
/// <para><b>La oferta es el estado ENTERO del producto</b>: la variante que una publicación no trae deja de
/// venderse —su precio vence ahora y su pozo se agota hasta lo comprometido—, y retirar el producto deja de
/// vender todas. No se borra nada: republicar las vuelve a poner a la venta.</para>
///
/// <para><b>Cada variante es independiente</b>: una que falla no impide publicar las demás; la respuesta es
/// el rechazo de la primera con la lista de las que faltan, y republicar termina el resto sin duplicar.
/// <b>Una publicación a la vez</b> (<see cref="StoreWriteGate"/>), aunque haya réplicas.</para>
/// </remarks>
public sealed class OfertaDeTienda : IDisposable
{
    /// <summary>La clase del sujeto de lo que se vende: la de las líneas de la compra.</summary>
    public const string KindDelProducto = "tienda.producto";

    /// <summary>El impuesto con que se publica el precio, en puntos básicos.</summary>
    /// <remarks>
    /// Cero porque el precio de la ficha ya es lo que paga el comprador: con impuesto, el orquestador cobraría
    /// más de lo que pinta el catálogo, y el motor en proceso cobra el precio tal cual.
    /// </remarks>
    public const int ImpuestoEnPuntosBasicos = 0;

    private const string Prefijo = "tienda";

    // Sólo ESTE código es «no hay pozo»: un 404 de una ruta que no existe trae otro y es un fallo de verdad.
    private const string NoExiste = $"{TiendaCapabilities.Inventory}.item_not_found";

    private readonly TiendaCapabilities _capacidades;
    private readonly JsonCollectionStore<ExistenciasPublicadas> _publicadas;
    private readonly JsonCollectionStore<ProductoRegistrado> _productos;
    private readonly StoreWriteGate _turno;
    private readonly TimeProvider _reloj;
    private readonly ILogger<OfertaDeTienda> _log;

    public OfertaDeTienda(
        TiendaCapabilities capacidades, IOptions<SagaStorageOptions> almacen, TimeProvider reloj, ILogger<OfertaDeTienda> log)
    {
        _capacidades = capacidades;
        var raiz = Path.Combine(almacen.Value.Root, "ofertas");
        _publicadas = new JsonCollectionStore<ExistenciasPublicadas>(raiz, "existencias", e => e.Pozo);
        _productos = new JsonCollectionStore<ProductoRegistrado>(raiz, "productos", p => p.ProductId);
        _turno = new StoreWriteGate(raiz, Prefijo);
        _reloj = reloj;
        _log = log;
    }

    /// <inheritdoc />
    public void Dispose() => _turno.Dispose();

    /// <summary>El sujeto con que la compra nombra una variante.</summary>
    public static Ref SujetoDe(string producto, string? variante)
        => Ref.Create(KindDelProducto, string.IsNullOrWhiteSpace(variante) ? producto : $"{producto}:{variante}");

    /// <summary>Publica la oferta. Repetirla —con la misma llave o con otra— no duplica nada.</summary>
    /// <returns>Lo publicado; o, si alguna variante no se pudo publicar, el rechazo de la primera que falló
    /// con la lista de las que faltan. Las demás quedan publicadas igual.</returns>
    public async Task<Result<OfertaPublicada>> PublicarAsync(OfertaDeProducto oferta, IdempotencyKey llave, CancellationToken ct)
    {
        // La FORMA se mira antes de escribir nada: una oferta con la tercera variante mal no deja las dos
        // primeras publicadas.
        var motivo = Revisar(oferta);
        if (motivo is not null) return Result.Rejected<OfertaPublicada>(motivo);

        using var turno = await _turno.TryEnterAsync(ct);
        if (turno is null) return Result.Rejected<OfertaPublicada>(_turno.Ocupado);

        var producto = oferta.ProductId!;
        var moneda = oferta.Currency!.ToUpperInvariant();
        var publicadas = new List<VariantePublicada>(oferta.Variants!.Count);
        var fallos = new List<(string Que, Rejection Motivo)>();
        var vivos = new List<string>(oferta.Variants.Count);

        foreach (var v in oferta.Variants)
        {
            var sujeto = SujetoDe(producto, v.VariantId);
            vivos.Add(sujeto.Id);
            var monto = Money.Of(v.Price!.Value, moneda);

            var precio = await _capacidades.SetPriceAsync(sujeto, monto, ImpuestoEnPuntosBasicos, null, null,
                Derivada(llave.Value, "precio", sujeto.ToString(), monto.ToString()), ct);
            if (!precio.IsOk)
            {
                fallos.Add((sujeto.Id, precio.Rejection!));
                continue;
            }

            var existencias = await AsegurarAsync(sujeto, v.Stock!.Value, llave, crear: true, ct);
            if (!existencias.IsOk)
            {
                fallos.Add((sujeto.Id, existencias.Rejection!));
                continue;
            }

            publicadas.Add(new VariantePublicada(
                v.VariantId, Money.Of(precio.Value.Amount.Amount, precio.Value.Amount.Currency), existencias.Value));
        }

        // Lo que la publicación anterior traía y ésta no, deja de venderse.
        var pendientes = await RetirarLoQueFaltaAsync(producto, vivos, llave, fallos, ct);
        _productos.Put(new ProductoRegistrado(producto, moneda, vivos.Concat(pendientes).Distinct(StringComparer.Ordinal).ToList()));

        return fallos.Count == 0
            ? Result.Ok(new OfertaPublicada(producto, publicadas))
            : Result.Rejected<OfertaPublicada>(Juntar(producto, fallos));
    }

    /// <summary>
    /// Retira la oferta entera de un producto (se despublicó, fue a la papelera o se borró): ninguna de sus
    /// variantes se vende más. Repetirlo no hace daño; uno que nunca se publicó no tiene nada que retirar.
    /// </summary>
    public async Task<Result<OfertaPublicada>> RetirarAsync(string? productId, IdempotencyKey llave, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(productId)) return Result.Rejected<OfertaPublicada>(Invalido("bad_product", "Hace falta productId."));

        using var turno = await _turno.TryEnterAsync(ct);
        if (turno is null) return Result.Rejected<OfertaPublicada>(_turno.Ocupado);

        var fallos = new List<(string Que, Rejection Motivo)>();
        var pendientes = await RetirarLoQueFaltaAsync(productId, [], llave, fallos, ct);
        if (_productos.Find(productId) is { } previo) _productos.Put(previo with { Sujetos = pendientes });

        return fallos.Count == 0
            ? Result.Ok(new OfertaPublicada(productId, []))
            : Result.Rejected<OfertaPublicada>(Juntar(productId, fallos));
    }

    /// <summary>
    /// Deja de vender lo que el registro del producto traía y la oferta nueva no: vence el precio de cada
    /// variante que falta y agota su pozo. Devuelve lo que no se pudo retirar, que queda registrado para que
    /// la próxima publicación lo vuelva a intentar.
    /// </summary>
    private async Task<List<string>> RetirarLoQueFaltaAsync(
        string producto, IReadOnlyCollection<string> vivos, IdempotencyKey llave, List<(string Que, Rejection Motivo)> fallos,
        CancellationToken ct)
    {
        var quedan = new List<string>();
        if (_productos.Find(producto) is not { } previo) return quedan;

        var ahora = _reloj.GetUtcNow();
        foreach (var id in previo.Sujetos.Where(s => !vivos.Contains(s, StringComparer.Ordinal)))
        {
            var sujeto = Ref.Create(KindDelProducto, id);
            var vencido = await _capacidades.SetPriceAsync(sujeto, Money.Of(0m, previo.Currency), ImpuestoEnPuntosBasicos, null, ahora,
                Derivada(llave.Value, "retirar", sujeto.ToString(), ahora.ToString("O")), ct);
            var agotado = vencido.IsOk ? await AsegurarAsync(sujeto, 0, llave, crear: false, ct) : Result.Ok(0);
            if (vencido.IsOk && agotado.IsOk)
            {
                _log.LogInformation("La variante {Sujeto} salió de la oferta: su precio deja de valer y su pozo se agota.", sujeto);
                continue;
            }
            fallos.Add((id, (vencido.IsOk ? agotado.Rejection : vencido.Rejection)!));
            quedan.Add(id);
        }
        return quedan;
    }

    /// <summary>El rechazo de la primera variante que falló, con la lista de todas las que faltan.</summary>
    private static Rejection Juntar(string producto, List<(string Que, Rejection Motivo)> fallos)
    {
        var primero = fallos[0].Motivo;
        return primero with
        {
            Message = $"{primero.Message} De la oferta de {producto} no quedó al día: "
                      + string.Join(", ", fallos.Select(f => $"{f.Que} ({f.Motivo.Code})").Distinct(StringComparer.Ordinal))
                      + ". Lo demás sí; republicar termina lo que falta sin duplicar nada.",
        };
    }

    /// <summary>
    /// Deja el pozo con <paramref name="existencias"/> declaradas —o con lo vendido y apartado, si es más—: lo
    /// declara, lo ajusta o no hace nada. Devuelve lo que quedó declarado.
    /// </summary>
    /// <param name="crear">Si el pozo se declara cuando no existe. Agotar uno que no existe no crea nada.</param>
    private async Task<Result<int>> AsegurarAsync(Ref pozo, int existencias, IdempotencyKey publicacion, bool crear, CancellationToken ct)
    {
        var clave = pozo.ToString();
        var registro = _publicadas.Find(clave);

        // Primero lo que quedó a medias, con SU llave: si llegó, la capacidad lo reconoce y no lo repite.
        if (registro is { Hacia: { } pendiente })
        {
            var terminado = await AplicarAsync(pozo, registro.Declarado, pendiente, IdempotencyKey.Of(registro.Llave!), ct);
            if (!terminado.IsOk && terminado.Rejection!.IsTransient) return Result.Rejected<int>(terminado.Rejection);

            // Un rechazo que no es transitorio dice que no se aplicó: lo declarado sigue siendo lo de antes.
            registro = terminado.IsOk ? new ExistenciasPublicadas(clave, pendiente) : new ExistenciasPublicadas(clave, registro.Declarado);
            _publicadas.Put(registro);
        }

        var item = await _capacidades.FindStockAsync(pozo, ct);
        if (!item.IsOk && item.Rejection!.Code != NoExiste) return Result.Rejected<int>(item.Rejection);
        if (!item.IsOk && !crear) return Result.Ok(0);

        int? desde;
        var hacia = existencias;
        if (!item.IsOk)
        {
            desde = null;
        }
        else if (registro?.Declarado is not { } declarado)
        {
            // Un pozo que este orquestador no declaró (provisionado a mano, por ejemplo): no sabe sobre qué
            // se declaró, así que no ajusta a ciegas. Lo toma como declarado en lo que dice la oferta.
            _log.LogWarning(
                "El pozo {Pozo} ya existía y no lo declaró esta oferta: se toma {Existencias} como lo declarado, sin ajustar.",
                clave, existencias);
            _publicadas.Put(new ExistenciasPublicadas(clave, existencias));
            return Result.Ok(existencias);
        }
        else
        {
            // Nunca por debajo de lo comprometido: lo declarado menos lo libre es lo vendido más lo apartado.
            var comprometido = declarado - item.Value.Available;
            if (existencias < comprometido)
            {
                _log.LogWarning(
                    "El pozo {Pozo} no baja a {Existencias}: tiene {Comprometido} vendidas o apartadas, y queda declarado en {Comprometido}.",
                    clave, existencias, comprometido, comprometido);
                hacia = comprometido;
            }
            if (declarado == hacia) return Result.Ok(hacia);
            desde = declarado;
        }

        var llave = desde is null
            ? Derivada("declarar", clave, hacia.ToString(CultureInfo.InvariantCulture))
            : Derivada(publicacion.Value, "ajustar", clave, desde.Value.ToString(CultureInfo.InvariantCulture),
                hacia.ToString(CultureInfo.InvariantCulture));

        _publicadas.Put(new ExistenciasPublicadas(clave, desde, hacia, llave.Value));
        var r = await AplicarAsync(pozo, desde, hacia, llave, ct);
        if (r.IsOk)
        {
            _publicadas.Put(new ExistenciasPublicadas(clave, hacia));
            return Result.Ok(hacia);
        }

        if (!r.Rejection!.IsTransient) _publicadas.Put(new ExistenciasPublicadas(clave, desde));
        return Result.Rejected<int>(r.Rejection);
    }

    /// <summary>Declara el pozo si no hay declarado, o le suma la diferencia si lo hay.</summary>
    private async Task<Result<bool>> AplicarAsync(Ref pozo, int? desde, int hacia, IdempotencyKey llave, CancellationToken ct)
    {
        if (desde is not { } declarado)
        {
            return (await _capacidades.DeclareStockAsync(pozo, hacia, llave, ct)).Map(_ => true);
        }

        var item = await _capacidades.FindStockAsync(pozo, ct);
        if (!item.IsOk) return Result.Rejected<bool>(item.Rejection!);

        _log.LogInformation("Las existencias de {Pozo} pasan de {Desde} a {Hacia}.", pozo, declarado, hacia);
        return (await _capacidades.RestockAsync(item.Value.Id, hacia - declarado, llave, ct)).Map(_ => true);
    }

    /// <summary>Una llave derivada de sus partes, con una huella para que quepa.</summary>
    private static IdempotencyKey Derivada(params string?[] partes)
        => IdempotencyKey.Of("oferta|" + Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('|', partes.Select(p => p ?? "-"))))).ToLowerInvariant()[..40]);

    /// <summary>Lo que la oferta tiene que traer, con el código de cada falta.</summary>
    private static Rejection? Revisar(OfertaDeProducto o)
    {
        if (string.IsNullOrWhiteSpace(o.ProductId) || o.ProductId.Contains(':', StringComparison.Ordinal))
        {
            return Invalido("bad_product", "Hace falta productId, sin «:».");
        }
        if (o.Currency is not { Length: 3 } || !o.Currency.All(char.IsAsciiLetter))
        {
            return Invalido("bad_offer", "Hace falta currency, un código ISO 4217 de tres letras.");
        }
        if (o.Variants is not { Count: > 0 }) return Invalido("bad_offer", "Hace falta al menos una variante en variants.");

        var vistas = new HashSet<string>(StringComparer.Ordinal);
        foreach (var v in o.Variants)
        {
            var nombre = string.IsNullOrWhiteSpace(v.VariantId) ? string.Empty : v.VariantId;
            if (nombre.Contains(':', StringComparison.Ordinal) || !vistas.Add(nombre))
            {
                return Invalido("bad_variant", "Cada variante necesita un variantId sin «:» y distinto del de las demás (una sola sin él).");
            }
            var cual = nombre.Length == 0 ? o.ProductId : $"{o.ProductId}:{nombre}";
            if (v.Price is not >= 0m) return Invalido("bad_price", $"{cual} necesita un price que no sea negativo.");
            if (v.Stock is not >= 0) return Invalido("bad_stock", $"{cual} necesita un stock que no sea negativo.");
        }
        return null;
    }

    private static Rejection Invalido(string code, string message) => Rejection.Invalid($"{Prefijo}.{code}", message);
}

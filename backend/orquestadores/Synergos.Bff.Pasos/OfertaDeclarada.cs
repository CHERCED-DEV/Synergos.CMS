using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Synergos.Bff.Core;
using Synergos.Core;
using Synergos.Shared;

namespace Synergos.Bff.Pasos;

// ── La oferta declarada (P12; NM-1 de 010, 001-P11 supuesto A con N1) ───────
// Publicar desde el contenido el precio de una pieza y el de cada variante, y retirarlos juntos, sin código del
// vertical: el orquestador declara la clase de la pieza en {Origen}:Oferta:Clase y monta las rutas con
// `app.MapOfertaDeclarada()`. Hoy sólo precios; el pozo y el recurso de la agenda entran aquí cuando se pidan.

/// <summary>
/// El nombre con que se publica y se cotiza el precio de una pieza y el de cada variante: una sola función para la
/// oferta y para el paso <c>forma.una-linea-de-oferta</c> (001-P11, N1).
/// </summary>
/// <remarks>
/// La pieza es <c>{clase}:{pieza}</c>; su variante, <c>{clase}.variante:{pieza}/{codigo}</c>. La variante tiene una clase
/// propia: quien compra una pieza (la clase la fija la definición, NR-1) no puede nombrar el precio de una variante
/// como si fuera otra pieza. La pieza y el código son claves sin barra, así que el nombre no es ambiguo.
/// </remarks>
public static partial class SujetoDeLaOferta
{
    public const string SufijoDeLaVariante = ".variante";
    public const char Separador = '/';

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex Clave();

    /// <summary>Si <paramref name="texto"/> es la clave de una pieza o el código de una variante.</summary>
    public static bool EsClave(string? texto) => texto is not null && Clave().IsMatch(texto);

    /// <summary>El precio de <paramref name="pieza"/>, o el de su <paramref name="variante"/> si no es nula ni está en blanco.</summary>
    public static Ref De(Ref pieza, string? variante)
    {
        ArgumentNullException.ThrowIfNull(pieza);
        if (!EsClave(pieza.Id)) throw new ArgumentException($"«{pieza.Id}» no es la clave de una pieza.", nameof(pieza));
        if (string.IsNullOrWhiteSpace(variante)) return pieza;

        var codigo = variante.Trim();
        if (!EsClave(codigo)) throw new ArgumentException($"«{codigo}» no es el código de una variante.", nameof(variante));
        return Ref.Create(pieza.Kind + SufijoDeLaVariante, $"{pieza.Id}{Separador}{codigo}");
    }
}

/// <summary>Una variante de la pieza, como llega: todo anulable (molde F2), el dominio dice qué falta.</summary>
public sealed record VarianteDeLaOfertaDto(string? Codigo = null, decimal? Total = null);

/// <summary>Lo que publica el CMS al publicar la ficha: la pieza, su precio, la moneda y sus variantes.</summary>
public sealed record PublicarOfertaDeclaradaRequest(
    string? Pieza = null,
    decimal? Precio = null,
    string? Moneda = null,
    IReadOnlyList<VarianteDeLaOfertaDto>? Variantes = null);

/// <summary>La oferta como quedó: <paramref name="Precios"/> cuenta los vigentes (cero si se retiró).</summary>
public sealed record OfertaDeclaradaResponse(
    string Pieza, decimal Precio, string Moneda, IReadOnlyList<string> Variantes, int Precios, bool Retirada);

/// <summary>Una variante publicada, en el rastro.</summary>
public sealed record VarianteOfertada(string Codigo, decimal Total);

/// <summary>El rastro de una pieza publicada: lo que se retira junto y la variante que sale al republicar.</summary>
public sealed record PiezaOfertada(string Pieza, decimal Precio, string Moneda, IReadOnlyList<VarianteOfertada> Variantes, bool Retirada);

/// <summary>
/// Publica y retira la oferta de una pieza en <c>Api.Pricing</c>: su precio y el de cada variante, cero incluido.
/// </summary>
/// <remarks>
/// <para><b>Republicar reemplaza</b>: la variante que ya no está deja de valer (su precio vence ahora), y las que están
/// se fijan otra vez. Retirar vence todos juntos; retirar lo retirado es la misma respuesta, y lo que nunca se
/// publicó es <c>{origen}.oferta_no_existe</c>. Un precio retirado sigue en <c>Api.Pricing</c> con su historia, y la
/// cotización lo rechaza (<c>pricing.price_not_in_effect</c>).</para>
///
/// <para><b>Cada llamada a la capacidad lleva una llave derivada</b> de la de la publicación y de lo que fija: repetir la
/// publicación con su llave no fija otra versión, y una a medias se completa al repetirla. El rastro se escribe al
/// final, con el turno del almacén tomado, para que dos publicaciones de la misma pieza no se crucen.</para>
/// </remarks>
public sealed partial class OfertaDeclarada : IDisposable
{
    /// <summary>La clave, bajo el origen del orquestador, que dice la clase de la pieza.</summary>
    public const string ClaveDeLaClase = "Oferta:Clase";

    private readonly IOfertaPort _precios;
    private readonly TimeProvider _reloj;
    private readonly JsonCollectionStore<PiezaOfertada> _piezas;
    private readonly StoreWriteGate _turno;

    public OfertaDeclarada(IOfertaPort precios, string clase, string origen, string raiz, TimeProvider reloj)
    {
        ArgumentNullException.ThrowIfNull(precios);
        ArgumentNullException.ThrowIfNull(reloj);
        ArgumentException.ThrowIfNullOrWhiteSpace(origen);
        ArgumentException.ThrowIfNullOrWhiteSpace(raiz);
        if (clase is null || !EsClase(clase))
        {
            throw new ArgumentException(
                $"«{clase}» no es la clase de una pieza: van palabras en minúscula separadas por puntos, como «{origen}.pieza».", nameof(clase));
        }

        _precios = precios;
        _reloj = reloj;
        Clase = clase;
        CodePrefix = origen;
        Invalida = $"{origen}.oferta_invalida";
        NoExiste = $"{origen}.oferta_no_existe";
        _piezas = new JsonCollectionStore<PiezaOfertada>(raiz, "piezas", p => p.Pieza);
        _turno = new StoreWriteGate(raiz, origen);
    }

    /// <summary>La clase de la pieza que publica este orquestador.</summary>
    public string Clase { get; }

    /// <summary>El prefijo de sus códigos: el origen del orquestador.</summary>
    public string CodePrefix { get; }

    public string Invalida { get; }

    public string NoExiste { get; }

    /// <summary>
    /// La oferta de un orquestador declarado: la clase sale de <c>{Origen}:Oferta:Clase</c> y el rastro va junto a sus
    /// sagas (<c>{Origen}:Storage:Root</c>/ofertas). Sin la clase no arranca: montar la oferta sin decir qué publica es
    /// un error de despliegue, no algo que se descubre en la primera publicación.
    /// </summary>
    public static OfertaDeclarada De(IConfiguration configuracion, SagaVocabulary vocabulario, IOfertaPort precios, TimeProvider reloj)
    {
        ArgumentNullException.ThrowIfNull(configuracion);
        ArgumentNullException.ThrowIfNull(vocabulario);
        var clave = $"{vocabulario.Origin}:{ClaveDeLaClase}";
        if (configuracion[clave] is not { Length: > 0 } clase || string.IsNullOrWhiteSpace(clase))
        {
            throw new InvalidOperationException(
                $"La oferta declarada está montada y «{clave}» no dice la clase de la pieza que publica (por ejemplo, «{vocabulario.Origin}.pieza»).");
        }

        var raiz = Path.Combine(configuracion[$"{vocabulario.Origin}:Storage:Root"] ?? new SagaStorageOptions().Root, "ofertas");
        return new OfertaDeclarada(precios, clase.Trim(), vocabulario.Origin, raiz, reloj);
    }

    public void Dispose() => _turno.Dispose();

    /// <summary>Publica (o republica) la oferta de una pieza.</summary>
    public async Task<Result<PiezaOfertada>> PublicarAsync(PublicarOfertaDeclaradaRequest o, IdempotencyKey llave, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(o);
        if (Revisar(o) is { } motivo) return Result.Rejected<PiezaOfertada>(motivo);
        using var turno = await _turno.TryEnterAsync(ct);
        if (turno is null) return Result.Rejected<PiezaOfertada>(_turno.Ocupado);

        var pieza = Ref.Create(Clase, o.Pieza!.Trim());
        var moneda = o.Moneda!.Trim().ToUpperInvariant();
        var variantes = (o.Variantes ?? []).Select(v => new VarianteOfertada(v.Codigo!.Trim(), v.Total!.Value)).ToList();

        var precio = Money.Of(o.Precio!.Value, moneda);
        var fijado = await _precios.FijarPrecioAsync(SujetoDeLaOferta.De(pieza, null), precio, null,
            Derivada(llave.Value, "precio", pieza.Id, precio.ToString()), ct);
        if (!fijado.IsOk) return Result.Rejected<PiezaOfertada>(fijado.Rejection!);

        foreach (var variante in variantes)
        {
            var total = Money.Of(variante.Total, moneda);
            var hecho = await _precios.FijarPrecioAsync(SujetoDeLaOferta.De(pieza, variante.Codigo), total, null,
                Derivada(llave.Value, "variante", pieza.Id, variante.Codigo, total.ToString()), ct);
            if (!hecho.IsOk) return Result.Rejected<PiezaOfertada>(hecho.Rejection!);
        }

        if (_piezas.Find(pieza.Id) is { Retirada: false } previa)
        {
            var ahora = _reloj.GetUtcNow();
            foreach (var salio in previa.Variantes.Where(p => variantes.All(n => n.Codigo != p.Codigo)))
            {
                var vencido = await _precios.FijarPrecioAsync(SujetoDeLaOferta.De(pieza, salio.Codigo),
                    Money.Of(salio.Total, previa.Moneda), ahora, Derivada(llave.Value, "salio", pieza.Id, salio.Codigo), ct);
                if (!vencido.IsOk) return Result.Rejected<PiezaOfertada>(vencido.Rejection!);
            }
        }

        var publicada = new PiezaOfertada(pieza.Id, precio.Amount, moneda, variantes, Retirada: false);
        _piezas.Put(publicada);
        return Result.Ok(publicada);
    }

    /// <summary>Retira la oferta de una pieza: su precio y el de cada variante vencen ahora.</summary>
    public async Task<Result<PiezaOfertada>> RetirarAsync(string pieza, IdempotencyKey llave, CancellationToken ct)
    {
        using var turno = await _turno.TryEnterAsync(ct);
        if (turno is null) return Result.Rejected<PiezaOfertada>(_turno.Ocupado);

        if (!SujetoDeLaOferta.EsClave(pieza) || _piezas.Find(pieza) is not { } previa)
        {
            return Result.Rejected<PiezaOfertada>(Rejection.NotFound(NoExiste, $"La pieza {pieza} nunca se publicó."));
        }
        if (previa.Retirada) return Result.Ok(previa);

        var ahora = _reloj.GetUtcNow();
        var sujeto = Ref.Create(Clase, pieza);
        var vencen = new List<(string? Variante, decimal Total)> { (null, previa.Precio) };
        vencen.AddRange(previa.Variantes.Select(v => ((string?)v.Codigo, v.Total)));
        foreach (var (variante, total) in vencen)
        {
            var hecho = await _precios.FijarPrecioAsync(SujetoDeLaOferta.De(sujeto, variante), Money.Of(total, previa.Moneda), ahora,
                Derivada(llave.Value, "retirar", pieza, variante), ct);
            if (!hecho.IsOk) return Result.Rejected<PiezaOfertada>(hecho.Rejection!);
        }

        var retirada = previa with { Retirada = true };
        _piezas.Put(retirada);
        return Result.Ok(retirada);
    }

    internal Rejection? Revisar(PublicarOfertaDeclaradaRequest o)
    {
        if (!SujetoDeLaOferta.EsClave(o.Pieza?.Trim())) return Mal("pieza", "hace falta la clave de la pieza");
        if (o.Precio is not >= 0m) return Mal("precio", "tiene que ser mayor o igual que 0");
        if (o.Moneda?.Trim() is not { Length: 3 } moneda || !moneda.All(char.IsAsciiLetter)) return Mal("moneda", "tiene que ser un código ISO 4217");

        var codigos = new HashSet<string>(StringComparer.Ordinal);
        foreach (var v in o.Variantes ?? [])
        {
            if (!SujetoDeLaOferta.EsClave(v.Codigo?.Trim())) return Mal("variantes.codigo", $"«{v.Codigo}» no es el código de una variante");
            if (v.Total is not >= 0m) return Mal("variantes.total", $"el de la variante «{v.Codigo}» tiene que ser mayor o igual que 0");
            if (!codigos.Add(v.Codigo!.Trim())) return Mal("variantes.codigo", $"«{v.Codigo}» está dos veces");
        }

        return null;

        Rejection Mal(string campo, string motivo) => Rejection.Invalid(Invalida, $"La oferta no se publica: «{campo}» {motivo}.");
    }

    [GeneratedRegex("^[a-z][a-z0-9-]*(\\.[a-z][a-z0-9-]*)+$", RegexOptions.CultureInvariant)]
    private static partial Regex Clases();

    private static bool EsClase(string clase) => Clases().IsMatch(clase);

    private static IdempotencyKey Derivada(params string?[] partes)
        => IdempotencyKey.Of("oferta|" + Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('|', partes.Select(p => p ?? "-"))))).ToLowerInvariant()[..40]);
}

/// <summary>Las rutas de la oferta declarada: las que llama el CMS al publicar y al despublicar la ficha.</summary>
public static class OfertaDeclaradaEndpoints
{
    /// <summary>
    /// Monta <c>POST /v1/ofertas</c> y <c>POST /v1/ofertas/{pieza}/retirar</c>, las dos con llave. La oferta se resuelve
    /// aquí, al arrancar: sin <c>{Origen}:Oferta:Clase</c> el orquestador no arranca.
    /// </summary>
    public static WebApplication MapOfertaDeclarada(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);
        var oferta = app.Services.GetRequiredService<OfertaDeclarada>();

        app.MapPost("/v1/ofertas", async Task<Results<Ok<OfertaDeclaradaResponse>, ProblemHttpResult>> (
            PublicarOfertaDeclaradaRequest req, HttpRequest http, CancellationToken ct) =>
        {
            if (!IdempotencyHeader.TryRead(http, oferta.CodePrefix, out var llave, out var falta)) return falta!;
            return (await oferta.PublicarAsync(req, llave, ct)).Map(Respuesta).ToHttp();
        }).WithName("PublishDeclaredOffer").ConLlaveDeIdempotencia();

        app.MapPost("/v1/ofertas/{pieza}/retirar", async Task<Results<Ok<OfertaDeclaradaResponse>, ProblemHttpResult>> (
            string pieza, HttpRequest http, CancellationToken ct) =>
        {
            if (!IdempotencyHeader.TryRead(http, oferta.CodePrefix, out var llave, out var falta)) return falta!;
            return (await oferta.RetirarAsync(pieza, llave, ct)).Map(Respuesta).ToHttp();
        }).WithName("RetireDeclaredOffer").ConLlaveDeIdempotencia();

        return app;
    }

    private static OfertaDeclaradaResponse Respuesta(PiezaOfertada o)
        => new(o.Pieza, o.Precio, o.Moneda, o.Variantes.Select(v => v.Codigo).ToList(), o.Retirada ? 0 : 1 + o.Variantes.Count, o.Retirada);
}

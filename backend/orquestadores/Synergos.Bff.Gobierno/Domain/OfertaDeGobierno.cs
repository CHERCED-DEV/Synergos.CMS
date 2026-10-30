using System.Security.Cryptography;
using System.Text;
using Synergos.Bff.Core;
using Synergos.Bff.Gobierno.Clients;
using Synergos.Bff.Gobierno.Contracts;
using Synergos.Core;
using Synergos.Shared;

namespace Synergos.Bff.Gobierno.Domain;

/// <summary>Lo que este orquestador publicó de un trámite: el rastro de sus propios pasos.</summary>
/// <param name="Retirada">Si la oferta se retiró: su tasa dejó de valer.</param>
public sealed record TramiteOfertado(string Tramite, decimal Tasa, string Moneda, bool Retirada);

/// <summary>El rastro de las ofertas publicadas, en el almacén del orquestador (ADR 0105).</summary>
/// <remarks>
/// No es una copia de los precios —esos son de la capacidad de precios—: es lo que el propio orquestador publicó, para
/// saber qué retirar cuando el CMS despublica el trámite.
/// </remarks>
public sealed class RastroDeTramites(string raiz)
{
    private readonly JsonCollectionStore<TramiteOfertado> _tramites = new(raiz, "tramites", t => t.Tramite);

    /// <summary>La carpeta del rastro dentro del almacén del orquestador.</summary>
    public string Raiz { get; } = raiz;

    /// <summary>El rastro en la carpeta <c>ofertas/</c> del almacén de <c>gobierno</c> (el de sus sagas).</summary>
    public static RastroDeTramites De(IConfiguration configuracion)
        => new(Path.Combine(configuracion["gobierno:Storage:Root"] ?? new SagaStorageOptions().Root, "ofertas"));

    public TramiteOfertado? Buscar(string tramite) => _tramites.Find(tramite);

    public void Guardar(TramiteOfertado oferta) => _tramites.Put(oferta);
}

/// <summary>
/// Publica la oferta de un trámite: su tasa en la capacidad de precios, con el sujeto
/// <c>gobierno.tramite:{clave del nodo}</c> que la radicación cotiza (009-gobierno-tramite, data-model §7).
/// </summary>
/// <remarks>
/// <para><b>Es código del vertical</b> y no del caso común: el molde de <c>OfertaDeReservas</c>, sin agenda ni planes.
/// La oferta genérica del núcleo (P12, NB-8) la dejará lista para retirar. Se mide aparte del flujo.</para>
///
/// <para><b>Un trámite gratuito publica su cero</b>: así la AUSENCIA de precio sigue diciendo «no se radica en línea»,
/// y la radicación, que cotiza sin caer a cero, nunca deja gratis un trámite con tasa cuya oferta no llegó. Qué se
/// ofrece lo decide el CMS (las reglas del catálogo, ADR 0123 §4); aquí sólo se exige una tasa no negativa.</para>
///
/// <para><b>Publicar reemplaza</b> la tasa. <b>Retirar no borra</b>: la tasa deja de valer desde ahora, y cotizar —que
/// va primero en el flujo— rechaza sin autorizar nada.</para>
/// </remarks>
public sealed class OfertaDeGobierno(CapacidadesDeLaOferta capacidades, RastroDeTramites rastro, TimeProvider reloj) : IDisposable
{
    /// <summary>La clase del trámite: la que declara la definición de la radicación y con la que se cotiza.</summary>
    public const string ClaseDelTramite = "gobierno.tramite";

    public const string Invalida = "gobierno.oferta_invalida";
    public const string NoExiste = "gobierno.oferta_no_existe";

    private readonly StoreWriteGate _turno = new(rastro.Raiz, "gobierno");

    public void Dispose() => _turno.Dispose();

    /// <summary>El sujeto del precio de un trámite: el mismo que lleva la radicación en su entrada.</summary>
    public static Ref Sujeto(string tramite) => Ref.Create(ClaseDelTramite, tramite);

    /// <summary>Publica la oferta. Repetirla con la misma llave no duplica nada.</summary>
    public async Task<Result<TramiteOfertado>> PublicarAsync(PublicarOfertaDeTramiteRequest o, IdempotencyKey llave, CancellationToken ct)
    {
        if (Revisar(o) is { } motivo) return Result.Rejected<TramiteOfertado>(motivo);
        using var turno = await _turno.TryEnterAsync(ct);
        if (turno is null) return Result.Rejected<TramiteOfertado>(_turno.Ocupado);

        var tramite = o.Tramite!.Trim();
        var tasa = Money.Of(o.Tasa!.Value, o.Moneda!.Trim().ToUpperInvariant());
        var fijada = await capacidades.FijarPrecioAsync(Sujeto(tramite), tasa, null,
            Derivada(llave.Value, "tasa", tramite, tasa.ToString()), ct);
        if (!fijada.IsOk) return Result.Rejected<TramiteOfertado>(fijada.Rejection!);

        var oferta = new TramiteOfertado(tramite, tasa.Amount, tasa.Currency, Retirada: false);
        rastro.Guardar(oferta);
        return Result.Ok(oferta);
    }

    /// <summary>Retira la oferta: la tasa del trámite deja de valer desde ahora. Repetirlo no hace daño.</summary>
    public async Task<Result<TramiteOfertado>> RetirarAsync(string tramite, IdempotencyKey llave, CancellationToken ct)
    {
        using var turno = await _turno.TryEnterAsync(ct);
        if (turno is null) return Result.Rejected<TramiteOfertado>(_turno.Ocupado);

        if (rastro.Buscar(tramite) is not { } previa)
        {
            return Result.Rejected<TramiteOfertado>(Rejection.NotFound(NoExiste, $"El trámite {tramite} nunca se publicó."));
        }
        if (previa.Retirada) return Result.Ok(previa);

        var hecho = await capacidades.FijarPrecioAsync(Sujeto(tramite), Money.Of(previa.Tasa, previa.Moneda), reloj.GetUtcNow(),
            Derivada(llave.Value, "retirar", tramite), ct);
        if (!hecho.IsOk) return Result.Rejected<TramiteOfertado>(hecho.Rejection!);

        var hecha = previa with { Retirada = true };
        rastro.Guardar(hecha);
        return Result.Ok(hecha);
    }

    /// <summary>La clave del nodo de un trámite: letras, dígitos, guiones y guiones bajos, hasta 64 (una GUID cabe).</summary>
    public static bool EsClave(string? clave)
        => clave is { Length: > 0 and <= 64 } && clave.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

    internal static Rejection? Revisar(PublicarOfertaDeTramiteRequest o)
    {
        if (!EsClave(o.Tramite?.Trim())) return Mal("tramite", "hace falta la clave del trámite");
        if (o.Tasa is not >= 0m) return Mal("tasa", "tiene que ser mayor o igual que 0");
        if (o.Moneda?.Trim() is not { Length: 3 } moneda || !moneda.All(char.IsAsciiLetter)) return Mal("moneda", "tiene que ser un código ISO 4217");
        return null;

        static Rejection Mal(string campo, string motivo) => Rejection.Invalid(Invalida, $"La oferta no se publica: «{campo}» {motivo}.");
    }

    private static IdempotencyKey Derivada(params string?[] partes)
        => IdempotencyKey.Of("oferta|" + Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('|', partes.Select(p => p ?? "-"))))).ToLowerInvariant()[..40]);
}

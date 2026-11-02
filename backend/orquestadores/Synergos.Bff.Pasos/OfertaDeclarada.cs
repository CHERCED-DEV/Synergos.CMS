using System.Globalization;
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
// `app.MapOfertaDeclarada()`. Con la agenda (NN-22 de 003), la pieza publica además su recurso en la agenda: el
// profesional, el servicio o la sala que el flujo halla por la misma clase. El pozo entra aquí cuando se pida.

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

/// <summary>Un bloque del horario semanal, como llega: el día en inglés y las horas <c>HH:mm</c>.</summary>
public sealed record BloqueDeLaOfertaDto(string? Dia = null, string? Desde = null, string? Hasta = null);

/// <summary>La agenda de la pieza, como llega (NN-22): todo anulable, el dominio dice qué falta.</summary>
/// <param name="Cupos">Cuántas reservas admite a la vez cada franja.</param>
/// <param name="FranjaMinutos">Lo que dura cada franja.</param>
/// <param name="Horario">El horario semanal, en la zona.</param>
/// <param name="Zona">La zona horaria IANA del horario.</param>
/// <param name="AvisoMinutos">Con cuánta anticipación se puede cancelar; sin él, cero.</param>
public sealed record AgendaDeLaOfertaDto(
    int? Cupos = null,
    int? FranjaMinutos = null,
    IReadOnlyList<BloqueDeLaOfertaDto>? Horario = null,
    string? Zona = null,
    int? AvisoMinutos = null);

/// <summary>Lo que publica el CMS al publicar la ficha: la pieza, su precio, la moneda, sus variantes y su agenda.</summary>
/// <param name="Agenda">El recurso de la pieza en la agenda, o nulo si la pieza no se agenda (o si no cambia).</param>
public sealed record PublicarOfertaDeclaradaRequest(
    string? Pieza = null,
    decimal? Precio = null,
    string? Moneda = null,
    IReadOnlyList<VarianteDeLaOfertaDto>? Variantes = null,
    AgendaDeLaOfertaDto? Agenda = null);

/// <summary>La oferta como quedó: <paramref name="Precios"/> cuenta los vigentes (cero si se retiró).</summary>
/// <param name="RecursoId">El recurso de la pieza en la agenda, si tiene.</param>
/// <param name="ReservasFuera">Cuántas reservas vigentes dejó fuera la última revisión de la agenda: ninguna se canceló,
/// y decidir qué hacer con ellas es del negocio.</param>
public sealed record OfertaDeclaradaResponse(
    string Pieza, decimal Precio, string Moneda, IReadOnlyList<string> Variantes, int Precios, bool Retirada,
    string? RecursoId, int ReservasFuera);

/// <summary>Una variante publicada, en el rastro.</summary>
public sealed record VarianteOfertada(string Codigo, decimal Total);

/// <summary>La agenda publicada de una pieza, en el rastro: su recurso y lo que dejó fuera la última revisión.</summary>
public sealed record AgendaOfertada(string RecursoId, AgendaDeLaPieza Agenda, int ReservasFuera);

/// <summary>El rastro de una pieza publicada: lo que se retira junto y la variante que sale al republicar.</summary>
public sealed record PiezaOfertada(
    string Pieza, decimal Precio, string Moneda, IReadOnlyList<VarianteOfertada> Variantes, bool Retirada, AgendaOfertada? Agenda = null);

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
///
/// <para><b>La agenda va antes que el precio</b> (NN-22 de 003, el molde de la oferta de Reservas): el recurso se
/// registra con la MISMA clase e id que el precio, que es como el flujo lo halla (<c>booking.hallar</c>).</para>
/// <list type="bullet">
///   <item>Registrarlo lleva una llave de la pieza y de su agenda, no de la publicación: dos publicaciones con los mismos
///   valores no registran dos recursos.</item>
///   <item>Republicar con otra agenda REVISA el recurso: la agenda no cancela ninguna reserva, y la respuesta cuenta las
///   vigentes que ya no cabrían (<c>reservasFuera</c>).</item>
///   <item>Otra zona se rechaza (<c>{origen}.oferta_no_actualizable</c>) sin tocar nada, porque la agenda no cambia la de
///   un recurso registrado.</item>
///   <item>Sin agenda en la petición, la agenda no se toca.</item>
///   <item>Retirar no cierra la agenda (la capacidad no tiene cómo, y un horario vacío la dejaría abierta a todo): vence
///   el precio. El flujo que no deba agendar una pieza retirada la cotiza antes de apartar, con precio cero si no cobra.</item>
/// </list>
/// </remarks>
public sealed partial class OfertaDeclarada : IDisposable
{
    /// <summary>La clave, bajo el origen del orquestador, que dice la clase de la pieza.</summary>
    public const string ClaveDeLaClase = "Oferta:Clase";

    private const string SinRecurso = "booking.resource_not_found";

    private readonly IOfertaPort _capacidades;
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

        _capacidades = precios;
        _reloj = reloj;
        Clase = clase;
        CodePrefix = origen;
        Invalida = $"{origen}.oferta_invalida";
        NoExiste = $"{origen}.oferta_no_existe";
        NoActualizable = $"{origen}.oferta_no_actualizable";
        _piezas = new JsonCollectionStore<PiezaOfertada>(raiz, "piezas", p => p.Pieza);
        _turno = new StoreWriteGate(raiz, origen);
    }

    /// <summary>La clase de la pieza que publica este orquestador.</summary>
    public string Clase { get; }

    /// <summary>El prefijo de sus códigos: el origen del orquestador.</summary>
    public string CodePrefix { get; }

    public string Invalida { get; }

    public string NoExiste { get; }

    /// <summary>El rechazo de una agenda en otra zona que la de su recurso registrado.</summary>
    public string NoActualizable { get; }

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
        var previa = _piezas.Find(pieza.Id);

        // Sin agenda en la petición, la de antes sigue como estaba (y no hay reservas que contar de nuevo).
        var agenda = previa?.Agenda is { } antes ? antes with { ReservasFuera = 0 } : null;
        if (o.Agenda is { } pedida)
        {
            var publicada = await PublicarAgendaAsync(pieza, Normalizar(pedida), llave, ct);
            if (!publicada.IsOk) return Result.Rejected<PiezaOfertada>(publicada.Rejection!);
            agenda = publicada.Value;
        }

        var precio = Money.Of(o.Precio!.Value, moneda);
        var fijado = await _capacidades.FijarPrecioAsync(SujetoDeLaOferta.De(pieza, null), precio, null,
            Derivada(llave.Value, "precio", pieza.Id, precio.ToString()), ct);
        if (!fijado.IsOk) return Result.Rejected<PiezaOfertada>(fijado.Rejection!);

        foreach (var variante in variantes)
        {
            var total = Money.Of(variante.Total, moneda);
            var hecho = await _capacidades.FijarPrecioAsync(SujetoDeLaOferta.De(pieza, variante.Codigo), total, null,
                Derivada(llave.Value, "variante", pieza.Id, variante.Codigo, total.ToString()), ct);
            if (!hecho.IsOk) return Result.Rejected<PiezaOfertada>(hecho.Rejection!);
        }

        if (previa is { Retirada: false })
        {
            var ahora = _reloj.GetUtcNow();
            foreach (var salio in previa.Variantes.Where(p => variantes.All(n => n.Codigo != p.Codigo)))
            {
                var vencido = await _capacidades.FijarPrecioAsync(SujetoDeLaOferta.De(pieza, salio.Codigo),
                    Money.Of(salio.Total, previa.Moneda), ahora, Derivada(llave.Value, "salio", pieza.Id, salio.Codigo), ct);
                if (!vencido.IsOk) return Result.Rejected<PiezaOfertada>(vencido.Rejection!);
            }
        }

        var oferta = new PiezaOfertada(pieza.Id, precio.Amount, moneda, variantes, Retirada: false, agenda);
        _piezas.Put(oferta);
        return Result.Ok(oferta);
    }

    /// <summary>
    /// Registra el recurso de la pieza, o lo revisa si su agenda cambió; otra zona se rechaza sin tocar nada.
    /// </summary>
    private async Task<Result<AgendaOfertada>> PublicarAgendaAsync(Ref pieza, AgendaDeLaPieza agenda, IdempotencyKey llave, CancellationToken ct)
    {
        var valores = Valores(agenda);
        var hallado = await _capacidades.HallarRecursoAsync(pieza, ct);
        if (!hallado.IsOk && hallado.Rejection!.Code != SinRecurso) return Result.Rejected<AgendaOfertada>(hallado.Rejection);

        if (!hallado.IsOk)
        {
            var registrado = await _capacidades.RegistrarRecursoAsync(pieza, agenda, Derivada("recurso", pieza.Id, valores), ct);
            return registrado.Map(r => new AgendaOfertada(r.Id, agenda, 0));
        }

        var recurso = hallado.Value;
        if (!string.Equals(recurso.Agenda.Zona, agenda.Zona, StringComparison.Ordinal))
        {
            return Result.Rejected<AgendaOfertada>(Rejection.Conflict(NoActualizable,
                $"La pieza {pieza.Id} ya está en la agenda en la zona {recurso.Agenda.Zona}, y la agenda no cambia la zona de un "
                + "recurso registrado. No se publicó nada."));
        }
        if (Valores(recurso.Agenda) == valores) return Result.Ok(new AgendaOfertada(recurso.Id, agenda, 0));

        // La llave es la de ESTA publicación: volver a una agenda que ya tuvo es otra revisión, no la de antes.
        var revisado = await _capacidades.RevisarRecursoAsync(recurso.Id, agenda, Derivada(llave.Value, "revisar", pieza.Id, valores), ct);
        return revisado.Map(r => new AgendaOfertada(recurso.Id, agenda, r.Fuera));
    }

    /// <summary>La agenda como la escribe la agenda: el día con su nombre en inglés y las horas tal cual.</summary>
    private static AgendaDeLaPieza Normalizar(AgendaDeLaOfertaDto a) => new(
        a.Cupos!.Value,
        a.FranjaMinutos!.Value,
        a.Horario!.Select(b => new BloqueDeAgenda(Enum.Parse<DayOfWeek>(b.Dia!.Trim(), ignoreCase: true).ToString(), b.Desde!.Trim(), b.Hasta!.Trim()))
            .ToList(),
        a.Zona!.Trim(),
        a.AvisoMinutos ?? 0);

    /// <summary>La agenda en una cadena que no depende del orden del horario: con ella se sabe si cambió.</summary>
    private static string Valores(AgendaDeLaPieza a) => string.Join('|',
        a.Cupos.ToString(CultureInfo.InvariantCulture), a.FranjaMinutos.ToString(CultureInfo.InvariantCulture),
        a.AvisoMinutos.ToString(CultureInfo.InvariantCulture), a.Zona,
        string.Join(';', a.Horario.Select(b => $"{b.Dia},{b.Desde},{b.Hasta}").Order(StringComparer.Ordinal)));

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
            var hecho = await _capacidades.FijarPrecioAsync(SujetoDeLaOferta.De(sujeto, variante), Money.Of(total, previa.Moneda), ahora,
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

        return o.Agenda is { } agenda ? RevisarAgenda(agenda, Mal) : null;

        Rejection Mal(string campo, string motivo) => Rejection.Invalid(Invalida, $"La oferta no se publica: «{campo}» {motivo}.");
    }

    private static Rejection? RevisarAgenda(AgendaDeLaOfertaDto a, Func<string, string, Rejection> mal)
    {
        if (a.Cupos is not >= 1) return mal("agenda.cupos", "tiene que ser un entero mayor o igual que 1");
        if (a.FranjaMinutos is not > 0) return mal("agenda.franjaMinutos", "tiene que ser un entero mayor que cero");
        if (a.AvisoMinutos is < 0) return mal("agenda.avisoMinutos", "tiene que ser un entero mayor o igual que 0");
        if (string.IsNullOrWhiteSpace(a.Zona) || !TimeZoneInfo.TryFindSystemTimeZoneById(a.Zona.Trim(), out _))
            return mal("agenda.zona", "no es una zona horaria conocida");
        if (a.Horario is not { Count: > 0 }) return mal("agenda.horario", "hace falta al menos un bloque");

        var caben = 0;
        foreach (var b in a.Horario)
        {
            if (!Enum.TryParse<DayOfWeek>(b.Dia?.Trim(), ignoreCase: true, out _) || int.TryParse(b.Dia, out _))
                return mal("agenda.horario.dia", $"«{b.Dia}» no es un día de la semana");
            if (!TimeOnly.TryParseExact(b.Desde?.Trim(), "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var desde)
                || !TimeOnly.TryParseExact(b.Hasta?.Trim(), "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var hasta)
                || hasta <= desde)
            {
                return mal("agenda.horario", $"«{b.Desde}»–«{b.Hasta}» no es un bloque HH:mm que avanza en el mismo día");
            }
            if ((hasta - desde).TotalMinutes >= a.FranjaMinutos) caben++;
        }

        return caben == 0 ? mal("agenda.franjaMinutos", "no cabe en ningún bloque del horario") : null;
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
        => new(o.Pieza, o.Precio, o.Moneda, o.Variantes.Select(v => v.Codigo).ToList(), o.Retirada ? 0 : 1 + o.Variantes.Count, o.Retirada,
            o.Agenda?.RecursoId, o.Agenda?.ReservasFuera ?? 0);
}

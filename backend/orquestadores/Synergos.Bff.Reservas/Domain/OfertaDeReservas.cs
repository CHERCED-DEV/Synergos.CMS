using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Synergos.Bff.Core;
using Synergos.Bff.Reservas.Clients;
using Synergos.Bff.Reservas.Contracts;
using Synergos.Core;
using Synergos.Shared;

namespace Synergos.Bff.Reservas.Domain;

/// <summary>Lo que este orquestador publicó de un servicio: el rastro de sus propios pasos.</summary>
/// <param name="Servicio">La clave estable de la ficha.</param>
/// <param name="RecursoId">El recurso de la agenda que quedó registrado para él.</param>
/// <param name="Retirada">Si la oferta se retiró: su precio dejó de valer.</param>
/// <param name="ReservasFuera">Cuántas reservas vigentes dejó fuera la última revisión del recurso (la agenda no las
/// canceló: decidir qué hacer con ellas es del negocio).</param>
public sealed record ServicioOfertado(
    string Servicio, string Nombre, int DuracionMinutos, IReadOnlyList<BloqueDeHorarioDto> Horario, int Cupos,
    int PlazoCancelacionHoras, decimal Precio, string Moneda, string Zona, string RecursoId, bool Retirada,
    int ReservasFuera = 0);

/// <summary>El rastro de las ofertas publicadas, en el almacén del orquestador (ADR 0105).</summary>
/// <remarks>
/// No es una copia del cupo ni del precio —esos siguen siendo de la agenda y de precios—: es lo que el propio
/// orquestador publicó, como <c>AforoPublicado</c> de Eventos.
/// </remarks>
public sealed class RastroDeOfertas(string raiz)
{
    private readonly JsonCollectionStore<ServicioOfertado> _servicios = new(raiz, "servicios", s => s.Servicio);

    /// <summary>La carpeta del rastro dentro del almacén del orquestador.</summary>
    public string Raiz { get; } = raiz;

    /// <summary>El rastro en la carpeta <c>ofertas/</c> del almacén de <c>reservas</c> (el de sus sagas).</summary>
    public static RastroDeOfertas De(IConfiguration configuracion)
        => new(Path.Combine(configuracion["reservas:Storage:Root"] ?? new SagaStorageOptions().Root, "ofertas"));

    public ServicioOfertado? Buscar(string servicio) => _servicios.Find(servicio);

    /// <summary>La oferta del servicio si se puede reservar: publicada y no retirada.</summary>
    public ServicioOfertado? Vigente(string servicio) => Buscar(servicio) is { Retirada: false } o ? o : null;

    public void Guardar(ServicioOfertado oferta) => _servicios.Put(oferta);
}

/// <summary>
/// Publica la oferta de un servicio reservable: su recurso en la agenda (horario, cupos, plazo) y su precio
/// (011-booking-reserva, <c>contracts/oferta.md</c>).
/// </summary>
/// <remarks>
/// <para><b>Es código del vertical</b> y no del caso común: el molde de <c>OfertaDeEventos</c>, que la pieza
/// genérica de la oferta (P12, NB-8) retirará. Se mide aparte del flujo.</para>
///
/// <para><b>La franja la fija la agenda</b> (NB-1): el recurso se registra con la duración del servicio como
/// <c>slotMinutes</c>, y es la agenda la que rechaza al apartar una ventana de otro largo o fuera de la grilla.</para>
///
/// <para><b>Republicar con otro horario, cupos, plazo o duración REVISA el recurso</b> (NB-7): la agenda no cancela
/// ninguna reserva y dice cuántas vigentes ya no cabrían (<see cref="ServicioOfertado.ReservasFuera"/>), que el CMS le
/// enseña al editor. Lo único que la agenda no cambia es la zona del recurso: otra zona se RECHAZA
/// (<see cref="NoActualizable"/>) sin tocar nada, en vez de registrar un segundo recurso para el mismo servicio —la
/// agenda devolvería el primero— o publicar el precio a medias.</para>
///
/// <para><b>Retirar no borra</b>: el precio deja de valer desde ahora, así que cotizar —que va primero en el
/// flujo— lo rechaza sin apartar nada; el recurso y sus reservas siguen ahí.</para>
/// </remarks>
public sealed partial class OfertaDeReservas(CapacidadesDeLaOferta capacidades, RastroDeOfertas rastro, TimeProvider reloj) : IDisposable
{
    /// <summary>
    /// La clase del sujeto de un servicio en la agenda y en precios: la MISMA que declara la definición del flujo
    /// para la referencia que manda el navegador (NR-1), así que el flujo sólo alcanza lo que esta oferta publicó.
    /// </summary>
    public const string Clase = "reservas.servicio";

    public const string Invalida = "reservas.oferta_invalida";
    public const string NoActualizable = "reservas.oferta_no_actualizable";
    public const string NoExiste = "reservas.oferta_no_existe";
    private const string SinRecurso = "booking.resource_not_found";

    private readonly StoreWriteGate _turno = new(rastro.Raiz, "reservas");

    public void Dispose() => _turno.Dispose();

    /// <summary>Publica la oferta. Repetirla con la misma llave, o con los mismos valores, no duplica nada.</summary>
    public async Task<Result<ServicioOfertado>> PublicarAsync(PublicarOfertaRequest o, IdempotencyKey llave, CancellationToken ct)
    {
        if (Revisar(o) is { } motivo) return Result.Rejected<ServicioOfertado>(motivo);
        using var turno = await _turno.TryEnterAsync(ct);
        if (turno is null) return Result.Rejected<ServicioOfertado>(_turno.Ocupado);

        var sujeto = Ref.Create(Clase, o.Servicio!.Trim());
        var horario = o.Horario!.Select(Normalizar).ToList();
        var aviso = o.PlazoCancelacionHoras!.Value * 60;

        var cupos = o.Cupos!.Value;
        var franja = o.DuracionMinutos!.Value;
        var valores = string.Join('|', cupos.ToString(CultureInfo.InvariantCulture), o.Zona, aviso.ToString(CultureInfo.InvariantCulture),
            string.Join(';', horario.Select(b => $"{b.Dia}{b.Desde}{b.Hasta}")), franja.ToString(CultureInfo.InvariantCulture));

        var recurso = await capacidades.BuscarRecursoAsync(sujeto, ct);
        if (!recurso.IsOk && recurso.Rejection!.Code != SinRecurso) return Result.Rejected<ServicioOfertado>(recurso.Rejection);
        if (recurso.IsOk && recurso.Value.TimeZoneId != o.Zona)
        {
            return Result.Rejected<ServicioOfertado>(Rejection.Conflict(NoActualizable,
                $"El servicio {sujeto.Id} ya está en la agenda en la zona {recurso.Value.TimeZoneId}, y la agenda no cambia la "
                + "zona de un recurso registrado. No se publicó nada."));
        }

        var fuera = 0;
        if (!recurso.IsOk)
        {
            recurso = await capacidades.RegistrarRecursoAsync(sujeto, cupos, o.Zona!, horario, aviso, franja,
                Derivada("recurso", sujeto.Id, valores), ct);
            if (!recurso.IsOk) return Result.Rejected<ServicioOfertado>(recurso.Rejection!);
        }
        else if (!Igual(recurso.Value, cupos, horario, aviso, franja))
        {
            // La llave es la de ESTA publicación: volver a unos valores que ya tuvo es otra revisión, no la de antes.
            var revision = await capacidades.RevisarRecursoAsync(recurso.Value.Id, cupos, horario, aviso, franja,
                Derivada(llave.Value, "revisar", sujeto.Id, valores), ct);
            if (!revision.IsOk) return Result.Rejected<ServicioOfertado>(revision.Rejection!);
            fuera = revision.Value.Outside.Count;
        }

        var precio = Money.Of(o.Precio!.Value, o.Moneda!.Trim().ToUpperInvariant());
        var fijado = await capacidades.FijarPrecioAsync(sujeto, precio, null,
            Derivada(llave.Value, "precio", sujeto.Id, precio.ToString()), ct);
        if (!fijado.IsOk) return Result.Rejected<ServicioOfertado>(fijado.Rejection!);

        var oferta = new ServicioOfertado(sujeto.Id, o.Nombre?.Trim() ?? sujeto.Id, franja, horario,
            cupos, o.PlazoCancelacionHoras!.Value, precio.Amount, precio.Currency, o.Zona!, recurso.Value.Id, Retirada: false, fuera);
        rastro.Guardar(oferta);
        return Result.Ok(oferta);
    }

    /// <summary>Retira la oferta: su precio deja de valer desde ahora. Repetirlo no hace daño.</summary>
    public async Task<Result<ServicioOfertado>> RetirarAsync(string servicio, IdempotencyKey llave, CancellationToken ct)
    {
        using var turno = await _turno.TryEnterAsync(ct);
        if (turno is null) return Result.Rejected<ServicioOfertado>(_turno.Ocupado);

        if (rastro.Buscar(servicio) is not { } previa)
        {
            return Result.Rejected<ServicioOfertado>(Rejection.NotFound(NoExiste, $"El servicio {servicio} nunca se publicó."));
        }
        if (previa.Retirada) return Result.Ok(previa);

        var ahora = reloj.GetUtcNow();
        var retirado = await capacidades.FijarPrecioAsync(Ref.Create(Clase, servicio), Money.Of(previa.Precio, previa.Moneda),
            ahora, Derivada(llave.Value, "retirar", servicio), ct);
        if (!retirado.IsOk) return Result.Rejected<ServicioOfertado>(retirado.Rejection!);

        var hecha = previa with { Retirada = true };
        rastro.Guardar(hecha);
        return Result.Ok(hecha);
    }

    /// <summary>Lo que la oferta tiene que traer, con el campo que falta en el mensaje.</summary>
    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex Identificador();

    /// <summary>Si <paramref name="id"/> tiene la forma de la clave de una ficha: lo único que se publica como servicio.</summary>
    public static bool EsIdentificador(string id) => Identificador().IsMatch(id);

    internal static Rejection? Revisar(PublicarOfertaRequest o)
    {
        if (string.IsNullOrWhiteSpace(o.Servicio) || !EsIdentificador(o.Servicio.Trim()))
            return Mal("servicio", "hace falta la clave de la ficha del servicio");
        if (o.DuracionMinutos is not > 0) return Mal("duracionMinutos", "tiene que ser un entero mayor que cero");
        if (o.Cupos is not >= 1) return Mal("cupos", "tiene que ser un entero mayor o igual que 1");
        if (o.PlazoCancelacionHoras is not >= 0) return Mal("plazoCancelacionHoras", "tiene que ser un entero mayor o igual que 0");
        if (o.Precio is not >= 0m) return Mal("precio", "tiene que ser mayor o igual que 0");
        if (o.Moneda?.Trim() is not { Length: 3 } moneda || !moneda.All(char.IsAsciiLetter)) return Mal("moneda", "tiene que ser un código ISO 4217");
        if (string.IsNullOrWhiteSpace(o.Zona) || !TimeZoneInfo.TryFindSystemTimeZoneById(o.Zona, out _)) return Mal("zona", "no es una zona horaria conocida");
        if (o.Horario is not { Count: > 0 }) return Mal("horario", "hace falta al menos un bloque");

        var bloques = 0;
        foreach (var b in o.Horario)
        {
            if (!Enum.TryParse<DayOfWeek>(b.Dia, ignoreCase: true, out _)) return Mal("horario.dia", $"«{b.Dia}» no es un día de la semana");
            if (!TimeOnly.TryParseExact(b.Desde, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var desde)
                || !TimeOnly.TryParseExact(b.Hasta, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var hasta)
                || hasta <= desde)
            {
                return Mal("horario", $"«{b.Desde}»–«{b.Hasta}» no es un bloque HH:mm que avanza en el mismo día");
            }
            if ((hasta - desde).TotalMinutes >= o.DuracionMinutos) bloques++;
        }

        return bloques == 0 ? Mal("duracionMinutos", "no cabe en ningún bloque del horario") : null;

        static Rejection Mal(string campo, string motivo) => Rejection.Invalid(Invalida, $"La oferta no se publica: «{campo}» {motivo}.");
    }

    private static BloqueDeHorarioDto Normalizar(BloqueDeHorarioDto b)
        => new(Enum.Parse<DayOfWeek>(b.Dia!, ignoreCase: true).ToString(), b.Desde!.Trim(), b.Hasta!.Trim());

    private static bool Igual(RecursoDto r, int cupos, IReadOnlyList<BloqueDeHorarioDto> horario, int aviso, int franja)
        => r.Capacity == cupos && r.CancellationNoticeMinutes == aviso && r.SlotMinutes == franja
           && r.Opening.Select(f => $"{f.Day}|{f.From}|{f.To}").Order(StringComparer.Ordinal).SequenceEqual(
               horario.Select(b => $"{b.Dia}|{b.Desde}|{b.Hasta}").Order(StringComparer.Ordinal), StringComparer.Ordinal);

    private static IdempotencyKey Derivada(params string?[] partes)
        => IdempotencyKey.Of("oferta|" + Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('|', partes.Select(p => p ?? "-"))))).ToLowerInvariant()[..40]);
}

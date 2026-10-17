using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Synergos.Bff.Alquiler.Clients;
using Synergos.Bff.Core;
using Synergos.Core;
using Synergos.Shared;

namespace Synergos.Bff.Alquiler.Domain;

/// <summary>Un tramo de la tarifa: desde cuántos días aplica y cuánto vale el día ahí.</summary>
/// <param name="DesdeDias">Desde cuántos días (dos o más: el día uno es la tarifa base).</param>
/// <param name="ValorDia">Cuánto vale el día en ese tramo.</param>
public sealed record TramoDeTarifa(int DesdeDias, decimal ValorDia);

/// <summary>Lo que el CMS publica de un equipo para que se pueda alquilar por la puerta, tal como llega.</summary>
/// <remarks>
/// Todo anulable porque así llega del cable; las reglas de <see cref="OfertaDeAlquiler"/> dicen qué falta, con su
/// código. Los valores son los de la ficha del <c>equipmentPage</c>, leídos con las mismas reglas que el catálogo.
/// </remarks>
/// <param name="Equipo">El slug del equipo.</param>
/// <param name="Moneda">ISO 4217 de todos los importes.</param>
/// <param name="ValorDia">La tarifa base del día.</param>
/// <param name="Tramos">Los tramos por duración.</param>
/// <param name="GarantiaUnidad">La garantía de UNA unidad; cero es «sin garantía».</param>
/// <param name="MinDias">El alquiler más corto.</param>
/// <param name="MaxDias">El alquiler más largo.</param>
/// <param name="Unidades">Cuántas unidades hay.</param>
public sealed record OfertaDeEquipo(
    string? Equipo, string? Moneda, decimal? ValorDia, IReadOnlyList<TramoDeTarifa>? Tramos,
    decimal? GarantiaUnidad, int? MinDias, int? MaxDias, int? Unidades);

/// <summary>
/// Lo que este orquestador publicó de un equipo la última vez: el rastro de sus propios pasos (como
/// <c>OfertaRegistrada</c> de Eventos), y lo que lee <c>alquiler.tramo</c> para elegir el tramo y los límites.
/// </summary>
/// <remarks>
/// <b>Los importes viven en <c>Api.Pricing</c></b>, que es quien cotiza; aquí quedan como rastro de lo que se
/// publicó. El paso de la renta lee de aquí sólo los límites y desde cuántos días aplica cada tramo.
/// </remarks>
/// <param name="ReservasFuera">
/// Las reservas vigentes que la última revisión de unidades dejó por encima de los cupos (NN-6): siguen confirmadas, y
/// qué hacer con ellas lo decide el personal. Vacío si las unidades no bajaron; nulo en lo registrado antes de NN-6.
/// </param>
public sealed record OfertaDeAlquilerRegistrada(
    string Equipo, string Moneda, decimal ValorDia, IReadOnlyList<TramoDeTarifa> Tramos, decimal GarantiaUnidad,
    int MinDias, int MaxDias, int Unidades, bool Retirada, DateTimeOffset PublicadaUtc,
    IReadOnlyList<string>? ReservasFuera = null);

/// <summary>
/// Publica la oferta de un equipo en las capacidades: el precio de cada tramo y la garantía en <c>Api.Pricing</c>,
/// y el recurso con sus unidades en <c>Api.Booking</c> (lote 3, 005-P1; el patrón de la ADR 0140, F3, decisión 4).
/// </summary>
/// <remarks>
/// <para><b>Por qué existe.</b> Por la ruta directa el CMS cotiza con el catálogo y se lo manda al orquestador
/// ya calculado. Por la puerta el cuerpo lo escribe el navegador, y un total ahí sería un precio que pone quien
/// paga. Publicada la oferta, la renta cotiza con <c>Api.Pricing</c> y el navegador sólo nombra equipo, unidades
/// y fechas. De paso deja de ser un paso manual de despliegue crear el recurso del equipo en la agenda.</para>
///
/// <para><b>Repetir no duplica.</b> Cada escritura lleva una llave derivada de lo que escribe, así que republicar
/// la misma oferta devuelve lo que ya estaba; cambiar un precio lo vuelve a fijar.</para>
///
/// <para><b>Cambiar las unidades revisa el recurso</b> (NN-6, <c>revise</c> de la agenda del núcleo): la agenda toma la
/// capacidad nueva sin tocar ninguna reserva. Si bajaron y hay reservas vigentes por encima de los cupos, siguen
/// confirmadas —cancelarlas es una decisión del personal, con su política— y quedan con nombre en
/// <see cref="OfertaDeAlquilerRegistrada.ReservasFuera"/>, en la respuesta y en el registro.</para>
///
/// <para><b>Retirar no borra nada</b>: la oferta queda registrada como retirada, la renta deja de ofrecer el
/// equipo, y los alquileres vivos siguen con lo que pactaron.</para>
/// </remarks>
public sealed class OfertaDeAlquiler : IDisposable
{
    private const string Prefijo = "alquiler";
    private const string NoExiste = "booking.resource_not_found";

    private readonly AlquilerCapabilities _capacidades;
    private readonly JsonCollectionStore<OfertaDeAlquilerRegistrada> _ofertas;
    private readonly StoreWriteGate _turno;
    private readonly TimeProvider _reloj;
    private readonly ILogger<OfertaDeAlquiler> _log;

    public OfertaDeAlquiler(
        AlquilerCapabilities capacidades, IOptions<SagaStorageOptions> almacen, TimeProvider reloj, ILogger<OfertaDeAlquiler> log)
    {
        ArgumentNullException.ThrowIfNull(almacen);
        _capacidades = capacidades;
        var raiz = Path.Combine(almacen.Value.Root, "ofertas");
        _ofertas = new JsonCollectionStore<OfertaDeAlquilerRegistrada>(raiz, "equipos", o => o.Equipo);
        _turno = new StoreWriteGate(raiz, Prefijo);
        _reloj = reloj;
        _log = log;
    }

    /// <inheritdoc />
    public void Dispose() => _turno.Dispose();

    /// <summary>Lo publicado de un equipo, o null si nunca se publicó.</summary>
    public OfertaDeAlquilerRegistrada? Registrada(string equipo) => _ofertas.Find(equipo);

    /// <summary>Publica la oferta. Repetirla no duplica nada.</summary>
    /// <param name="oferta">Lo que llegó.</param>
    /// <param name="ct">Cancelación.</param>
    /// <returns>Lo publicado, o el rechazo (de forma, de las unidades, o de la capacidad).</returns>
    public async Task<Result<OfertaDeAlquilerRegistrada>> PublicarAsync(OfertaDeEquipo oferta, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(oferta);
        if (Revisar(oferta) is { } malo) return Result.Rejected<OfertaDeAlquilerRegistrada>(malo);

        using var turno = await _turno.TryEnterAsync(ct);
        if (turno is null) return Result.Rejected<OfertaDeAlquilerRegistrada>(_turno.Ocupado);

        var equipo = oferta.Equipo!.Trim();
        var moneda = oferta.Moneda!;
        var tramos = (oferta.Tramos ?? []).OrderBy(t => t.DesdeDias).ToList();

        // Primero las unidades: sin recurso no hay qué apartar, y un precio publicado sin él vendería aire.
        var recurso = await AsegurarRecursoAsync(equipo, oferta.Unidades!.Value, ct);
        if (!recurso.IsOk) return Result.Rejected<OfertaDeAlquilerRegistrada>(recurso.Rejection!);
        var fuera = recurso.Value ?? [];

        // Sin revisión, lo que dejó fuera la última sigue siendo lo último que se sabe: republicar no lo borra.
        var fueraRegistradas = recurso.Value is null
            ? _ofertas.Find(equipo)?.ReservasFuera ?? []
            : fuera.Select(r => r.ReservationId).ToList();

        var precios = new List<(Ref Sujeto, Money Monto)>
        {
            (RentalSubject.TarifaOf(equipo, 1), Money.Of(oferta.ValorDia!.Value, moneda)),
            (RentalSubject.GarantiaUnidadOf(equipo), Money.Of(oferta.GarantiaUnidad!.Value, moneda)),
        };
        precios.AddRange(tramos.Select(t => (RentalSubject.TarifaOf(equipo, t.DesdeDias), Money.Of(t.ValorDia, moneda))));

        foreach (var (sujeto, monto) in precios)
        {
            var fijado = await _capacidades.SetPriceAsync(
                sujeto, monto, Derivada("precio", sujeto.ToString(), monto.Amount.ToString(CultureInfo.InvariantCulture), monto.Currency), ct);
            if (!fijado.IsOk) return Result.Rejected<OfertaDeAlquilerRegistrada>(fijado.Rejection!);
        }

        var registrada = new OfertaDeAlquilerRegistrada(
            equipo, moneda, oferta.ValorDia!.Value, tramos, oferta.GarantiaUnidad!.Value,
            oferta.MinDias!.Value, oferta.MaxDias!.Value, oferta.Unidades!.Value, Retirada: false, _reloj.GetUtcNow(),
            fueraRegistradas);
        _ofertas.Put(registrada);
        _log.LogInformation("Oferta de {Equipo} publicada: {Unidades} unidad(es), {Tramos} tramo(s).", equipo, registrada.Unidades, tramos.Count);
        if (fuera.Count > 0)
        {
            _log.LogWarning(
                "El equipo {Equipo} quedó con {Unidades} unidad(es) y {Fuera} reserva(s) vigente(s) ya no caben ({Reservas}): "
                + "siguen confirmadas; qué hacer con ellas lo decide el personal.",
                equipo, registrada.Unidades, fuera.Count, string.Join(", ", fuera.Select(r => $"{r.ReservationId} {r.Start:yyyy-MM-dd}–{r.End:yyyy-MM-dd}")));
        }
        return Result.Ok(registrada);
    }

    /// <summary>Retira la oferta de un equipo: la renta deja de ofrecerlo. No toca los alquileres vivos.</summary>
    public async Task<Result<OfertaDeAlquilerRegistrada>> RetirarAsync(string? equipo, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(equipo)) return Result.Rejected<OfertaDeAlquilerRegistrada>(Invalido("offer_equipment_required", "Hace falta el equipo."));

        using var turno = await _turno.TryEnterAsync(ct);
        if (turno is null) return Result.Rejected<OfertaDeAlquilerRegistrada>(_turno.Ocupado);

        if (_ofertas.Find(equipo) is not { } previa)
        {
            return Result.Rejected<OfertaDeAlquilerRegistrada>(Rejection.NotFound(
                $"{Prefijo}.equipment_not_offered", $"El equipo {equipo} no tiene oferta publicada."));
        }

        var retirada = previa with { Retirada = true };
        _ofertas.Put(retirada);
        return Result.Ok(retirada);
    }

    /// <summary>
    /// Halla el recurso del equipo o lo crea con sus unidades; con otras unidades, lo revisa (NN-6).
    /// </summary>
    /// <returns>
    /// Las reservas vigentes que la revisión dejó fuera, nulo si no hubo revisión (el recurso ya tenía esas unidades o
    /// se acaba de crear), o el rechazo.
    /// </returns>
    private async Task<Result<IReadOnlyList<OutsideReservationDto>?>> AsegurarRecursoAsync(string equipo, int unidades, CancellationToken ct)
    {
        var sujeto = RentalSubject.For(equipo);
        var hallado = await _capacidades.FindResourceBySubjectAsync(sujeto, ct);
        if (hallado.IsOk)
        {
            if (hallado.Value.Capacity == unidades) return Result.Ok<IReadOnlyList<OutsideReservationDto>?>(null);

            var revisado = await _capacidades.ReviseResourceAsync(hallado.Value.Id, unidades,
                Derivada("revision", hallado.Value.Id, unidades.ToString(CultureInfo.InvariantCulture)), ct);
            return revisado.IsOk
                ? Result.Ok<IReadOnlyList<OutsideReservationDto>?>(revisado.Value.Outside)
                : Result.Rejected<IReadOnlyList<OutsideReservationDto>?>(revisado.Rejection!);
        }

        if (hallado.Rejection!.Code != NoExiste) return Result.Rejected<IReadOnlyList<OutsideReservationDto>?>(hallado.Rejection);
        var creado = await _capacidades.CreateResourceAsync(
            sujeto, unidades, Derivada("recurso", sujeto.ToString(), unidades.ToString(CultureInfo.InvariantCulture)), ct);
        return creado.IsOk
            ? Result.Ok<IReadOnlyList<OutsideReservationDto>?>(null)
            : Result.Rejected<IReadOnlyList<OutsideReservationDto>?>(creado.Rejection!);
    }

    /// <summary>Una llave derivada de sus partes, con una huella para que quepa.</summary>
    private static IdempotencyKey Derivada(params string?[] partes)
        => IdempotencyKey.Of("oferta|" + Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('|', partes.Select(p => p ?? "-"))))).ToLowerInvariant()[..40]);

    /// <summary>Lo que la oferta tiene que traer, con el código de cada falta.</summary>
    /// <remarks>
    /// El valor del día y la garantía NO pueden ser cero o menos, respectivamente: un día de cero es «gratis»
    /// sin que nadie lo haya decidido (el mismo corte que <c>alquiler.bad_total</c> del flujo imperativo), y la
    /// renta por la puerta no tiene otro sitio donde rechazarlo antes de cobrar.
    /// </remarks>
    private static Rejection? Revisar(OfertaDeEquipo o)
    {
        if (string.IsNullOrWhiteSpace(o.Equipo)) return Invalido("offer_equipment_required", "Hace falta el equipo.");
        if (o.Moneda is not { Length: 3 } || !o.Moneda.All(char.IsAsciiLetter))
        {
            return Invalido("offer_bad_currency", "Hace falta la moneda: un código ISO 4217 de tres letras.");
        }
        if (o.ValorDia is not > 0m) return Invalido("offer_bad_rate", "El valor base del día tiene que ser mayor que cero.");

        var vistos = new HashSet<int>();
        foreach (var t in o.Tramos ?? [])
        {
            if (t.DesdeDias < 2 || !vistos.Add(t.DesdeDias))
            {
                return Invalido("offer_bad_rate", "Cada tramo aplica desde dos días o más, y no se repite: el día uno es la tarifa base.");
            }
            if (t.ValorDia <= 0m) return Invalido("offer_bad_rate", $"El tramo desde {t.DesdeDias} días necesita un valor del día mayor que cero.");
        }

        if (o.GarantiaUnidad is not >= 0m) return Invalido("offer_bad_deposit", "La garantía por unidad no puede faltar ni ser negativa.");
        if (o.MinDias is not >= 1 || o.MaxDias is not { } max || max < o.MinDias)
        {
            return Invalido("offer_bad_bounds", "Hacen falta los días mínimo (uno o más) y máximo (no menor que el mínimo).");
        }
        if (o.Unidades is not >= 1) return Invalido("offer_bad_units", "Hace falta al menos una unidad.");
        return null;
    }

    private static Rejection Invalido(string code, string message) => Rejection.Invalid($"{Prefijo}.{code}", message);
}

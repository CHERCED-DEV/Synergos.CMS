using Synergos.Api.Geo.Storage;
using Synergos.Core;
using Synergos.Shared;

namespace Synergos.Api.Geo.Domain;

/// <summary>Un sitio con su distancia al centro de la búsqueda.</summary>
public sealed record NearbyPlace(Place Place, double DistanceKm);

/// <summary>Compone las reglas de <see cref="GeoRules"/> con el almacén.</summary>
public sealed class GeoService
{
    private readonly IPlaceStore _places;
    private readonly IIdempotencyLedger _idempotency;
    private readonly TimeProvider _clock;
    private readonly object _gate = new();

    public GeoService(IPlaceStore places, IIdempotencyLedger idempotency, TimeProvider clock)
    {
        _places = places;
        _idempotency = idempotency;
        _clock = clock;
    }

    /// <summary>Fija la ubicación de algo. Reescribe si ese sujeto ya la tenía.</summary>
    public Result<Place> SetPlace(Ref subject, double? lat, double? lon, string? label,
        IReadOnlyDictionary<string, string>? facets, IdempotencyKey key)
    {
        lock (_gate)
        {
            if (_idempotency.Find("place", key) is { } yaEra)
            {
                return _places.Find(yaEra) is { } previo
                    ? Result.Ok(previo)
                    : Rejection.Conflict($"{GeoRules.CodePrefix}.idempotency_orphan", "La llave ya se usó pero el sitio no está.");
            }

            var motivo = GeoRules.CheckCoordinates(lat, lon);
            if (motivo is not null) return Result.Rejected<Place>(motivo);

            var id = _places.FindBySubject(subject)?.Id ?? Guid.NewGuid().ToString("n");
            var place = new Place(id, subject, lat!.Value, lon!.Value, label?.Trim(),
                facets ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase), _clock.GetUtcNow());

            _places.Put(place);
            _idempotency.Remember("place", key, id);
            return Result.Ok(place);
        }
    }

    public Result<Place> Get(string id)
        => _places.Find(id) is { } p
            ? Result.Ok(p)
            : Rejection.NotFound($"{GeoRules.CodePrefix}.place_not_found", $"No existe el sitio {id}.");

    /// <summary>Lo que está a menos de <paramref name="radiusKm"/> del centro, lo más cerca primero.</summary>
    public Result<Page<NearbyPlace>> Nearby(
        double? lat, double? lon, double? radiusKm,
        IReadOnlyDictionary<string, string>? facets, int offset, int limit)
    {
        var motivo = GeoRules.CheckCoordinates(lat, lon) ?? GeoRules.CheckRadius(radiusKm);
        if (motivo is not null) return Result.Rejected<Page<NearbyPlace>>(motivo);

        var cerca = _places.All()
            .Where(p => facets is null || facets.All(f =>
                p.Facets.TryGetValue(f.Key, out var v) && string.Equals(v, f.Value, StringComparison.OrdinalIgnoreCase)))
            .Select(p => new NearbyPlace(p, GeoRules.DistanceKm(lat!.Value, lon!.Value, p.Latitude, p.Longitude)))
            .Where(x => x.DistanceKm <= radiusKm!.Value)
            .OrderBy(x => x.DistanceKm)
            // Desempate estable: dos sitios a la misma distancia devolverían órdenes distintos
            // entre llamadas iguales, y la lista "parpadearía" sin que nada haya cambiado.
            .ThenBy(x => x.Place.Id, StringComparer.Ordinal)
            .ToList();

        return Result.Ok(new Page<NearbyPlace>(cerca.Skip(offset).Take(limit).ToList(), cerca.Count, offset));
    }
}

using Synergos.Core;

namespace Synergos.Api.Geo.Domain;

/// <summary>Lo que la ubicación rechaza <b>sola</b>, y cómo se mide una distancia.</summary>
public static class GeoRules
{
    public const string CodePrefix = "geo";

    /// <summary>Radio máximo de una búsqueda, en kilómetros.</summary>
    public const double MaxRadiusKm = 500;

    /// <summary>Radio de la Tierra, en kilómetros.</summary>
    private const double EarthRadiusKm = 6371.0088;

    /// <summary>Si el punto está en el planeta.</summary>
    public static Rejection? CheckCoordinates(double? lat, double? lon)
    {
        if (lat is null || lon is null)
        {
            return Rejection.Invalid($"{CodePrefix}.coordinates_required", "Hacen falta latitude y longitude.");
        }
        if (lat is < -90 or > 90)
        {
            return Rejection.Invalid($"{CodePrefix}.bad_latitude", $"La latitud va entre -90 y 90, y llegó {lat}.");
        }
        return lon is < -180 or > 180
            ? Rejection.Invalid($"{CodePrefix}.bad_longitude", $"La longitud va entre -180 y 180, y llegó {lon}.")
            : null;
    }

    /// <summary>Si el radio pedido cabe.</summary>
    /// <remarks>
    /// Sin tope, un radio de veinte mil kilómetros devuelve el índice entero — un volcado
    /// disfrazado de búsqueda por cercanía.
    /// </remarks>
    public static Rejection? CheckRadius(double? km)
        => km is > 0 and <= MaxRadiusKm
            ? null
            : Rejection.Invalid($"{CodePrefix}.bad_radius", $"El radio va entre 0 y {MaxRadiusKm} km.");

    /// <summary>
    /// Distancia en kilómetros por la fórmula del semiverseno.
    /// </summary>
    /// <remarks>
    /// Semiverseno y no Pitágoras sobre grados: un grado de longitud mide 111 km en el ecuador y
    /// 78 km en Medellín. Tratar los grados como plano hace que "los 5 km más cercanos" traiga
    /// cosas a 7 km hacia el este y se pierda lo que está a 4 km al norte.
    /// </remarks>
    public static double DistanceKm(double lat1, double lon1, double lat2, double lon2)
    {
        var dLat = Rad(lat2 - lat1);
        var dLon = Rad(lon2 - lon1);
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
                + Math.Cos(Rad(lat1)) * Math.Cos(Rad(lat2)) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return EarthRadiusKm * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }

    private static double Rad(double grados) => grados * Math.PI / 180;
}

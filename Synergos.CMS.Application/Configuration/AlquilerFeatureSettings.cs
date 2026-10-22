using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Application.Configuration;

/// <summary>
/// La configuración de negocio del alquiler de equipos (ADR 0137) — sección <c>Synergos:Features:Alquiler</c>.
/// </summary>
/// <remarks>
/// El piloto 2 (#147) nació antes de la ADR 0137 y la API llegaba por un campo del editor, con
/// <c>/api/alquiler</c> escrito además en la vista Razor. Es del despliegue, por sitio, como en las
/// otras ocho funcionalidades: el editor no tiene nada que decidir sobre dónde vive una API.
/// </remarks>
public sealed class AlquilerFeatureSettings : SeccionDeNegocio<AlquilerFeatureSitio, NegocioDeAlquiler>
{
    /// <summary>La ruta de configuración de la sección.</summary>
    public const string Seccion = "Synergos:Features:Alquiler";

    /// <summary>Dónde vive la API del alquiler: relativa al sitio o absoluta.</summary>
    public string ApiBase { get; init; } = "/api/alquiler";

    /// <summary>
    /// Por dónde alquila el elemento, si el despliegue lo declara: <c>puerta</c> o <c>directa</c>
    /// (<see cref="CaminoDeCompra"/>, línea 005 research R9). Sin valor no se declara nada y decide la regla: la
    /// puerta cuando abre <c>alquiler.renta</c>, y la ruta directa en otro caso.
    /// </summary>
    /// <remarks>Un sitio con el valor vacío vuelve a la regla aunque los valores base declaren uno.</remarks>
    public string? RentalPath { get; init; }

    /// <inheritdoc />
    protected override NegocioDeAlquiler Fusionar(AlquilerFeatureSitio? propio)
        => new(
            ApiBase: propio?.ApiBase ?? ApiBase,
            RentalPath: CaminoDeCompra.Normalizar(propio?.RentalPath ?? RentalPath));

    /// <inheritdoc />
    protected override IEnumerable<string> ProblemasDe(NegocioDeAlquiler negocio)
        => new[]
        {
            ReglasDeNegocio.ApiBase("ApiBase", negocio.ApiBase),
            ReglasDeNegocio.Camino("RentalPath", negocio.RentalPath),
        }.OfType<string>();
}

/// <summary>
/// Lo que un sitio cambia de <see cref="AlquilerFeatureSettings"/>. Una clave sin valor la hereda.
/// </summary>
public sealed class AlquilerFeatureSitio
{
    /// <inheritdoc cref="AlquilerFeatureSettings.ApiBase"/>
    public string? ApiBase { get; init; }

    /// <inheritdoc cref="AlquilerFeatureSettings.RentalPath"/>
    public string? RentalPath { get; init; }
}

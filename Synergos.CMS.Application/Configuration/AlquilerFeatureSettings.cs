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

    /// <inheritdoc />
    protected override NegocioDeAlquiler Fusionar(AlquilerFeatureSitio? propio)
        => new(ApiBase: propio?.ApiBase ?? ApiBase);

    /// <inheritdoc />
    protected override IEnumerable<string> ProblemasDe(NegocioDeAlquiler negocio)
        => new[] { ReglasDeNegocio.ApiBase("ApiBase", negocio.ApiBase) }.OfType<string>();
}

/// <summary>
/// Lo que un sitio cambia de <see cref="AlquilerFeatureSettings"/>. Una clave sin valor la hereda.
/// </summary>
public sealed class AlquilerFeatureSitio
{
    /// <inheritdoc cref="AlquilerFeatureSettings.ApiBase"/>
    public string? ApiBase { get; init; }
}

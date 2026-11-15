using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Application.Configuration;

/// <summary>
/// La cabecera de las apps dentro de un sitio (ADR 0137, D-016-V2) — sección <c>Synergos:Features:Cabecera</c>.
/// </summary>
/// <remarks>
/// <para><b>Por defecto, secundaria</b>: dentro de un sitio con cabecera, la app no repite su marca y su menú queda como
/// barra secundaria. Un sitio puede pedir la completa por la Key de su siteRoot (<c>Sitios:&lt;Key&gt;:Modo</c>).
/// Fuera de un sitio la app pinta siempre la completa (<see cref="ModoDeCabecera.Decidir"/>). Una palabra que no es
/// ninguna de las dos no deja arrancar.</para>
/// </remarks>
public sealed class CabeceraFeatureSettings : SeccionDeNegocio<CabeceraFeatureSitio, NegocioDeCabecera>
{
    /// <summary>La sección de configuración.</summary>
    public const string Seccion = "Synergos:Features:Cabecera";

    /// <summary>Cómo pinta la app su cabecera dentro de un sitio: <c>secundaria</c> o <c>completa</c>.</summary>
    public string Modo { get; init; } = ModoDeCabecera.Secundaria;

    /// <inheritdoc />
    protected override NegocioDeCabecera Fusionar(CabeceraFeatureSitio? propio)
        => new(ModoDeCabecera.Normalizar(propio?.Modo ?? Modo));

    /// <inheritdoc />
    protected override IEnumerable<string> ProblemasDe(NegocioDeCabecera negocio)
        => ModoDeCabecera.EsValido(negocio.Modo)
            ? []
            : [$"Modo: «{negocio.Modo}» tiene que ser «{ModoDeCabecera.Secundaria}» o «{ModoDeCabecera.Completa}»."];
}

/// <summary>Lo que un sitio cambia de <see cref="CabeceraFeatureSettings"/>. Una clave sin valor la hereda.</summary>
public sealed class CabeceraFeatureSitio
{
    /// <inheritdoc cref="CabeceraFeatureSettings.Modo"/>
    public string? Modo { get; init; }
}

using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Application.Configuration;

/// <summary>
/// Si un sitio vende en línea (ADR 0137, 016-contenido-piezas) — sección <c>Synergos:Features:Compra</c>.
/// </summary>
/// <remarks>
/// <para><b>Por defecto, sí</b>: la Tienda y todo sitio que nadie declare siguen con el carrito, como antes.</para>
/// <para><b>Un sitio lo apaga por la Key de su siteRoot</b> (<c>Sitios:&lt;Key&gt;:EnLinea = false</c>): sin el botón
/// del carrito en sus fichas y tarjetas ni el ícono en el encabezado. Lo que el sitio ya ofrece para conseguirlo
/// («Agendar visita», «Reservar ahora») lo compone el editor y no cambia. Superset: el carrito no se borra, se
/// condiciona.</para>
/// <para><b>Lo leen las vistas por el siteRoot de la página</b> (<see cref="SeccionDeNegocio{TSitio,TNegocio}.Para"/>), no por
/// el dominio de la petición: la base no tiene dominios, y por dominio regiría siempre la base.</para>
/// </remarks>
public sealed class CompraFeatureSettings : SeccionDeNegocio<CompraFeatureSitio, NegocioDeCompra>
{
    /// <summary>La sección de configuración.</summary>
    public const string Seccion = "Synergos:Features:Compra";

    /// <summary>Si el sitio vende en línea: el carrito en fichas, tarjetas y encabezado. Default true.</summary>
    public bool EnLinea { get; init; } = true;

    /// <inheritdoc />
    protected override NegocioDeCompra Fusionar(CompraFeatureSitio? propio) => new(propio?.EnLinea ?? EnLinea);

    /// <inheritdoc />
    protected override IEnumerable<string> ProblemasDe(NegocioDeCompra negocio) => [];
}

/// <summary>Lo que un sitio cambia de <see cref="CompraFeatureSettings"/>. Una clave sin valor la hereda.</summary>
public sealed class CompraFeatureSitio
{
    /// <inheritdoc cref="CompraFeatureSettings.EnLinea"/>
    public bool? EnLinea { get; init; }
}

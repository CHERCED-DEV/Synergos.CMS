using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Web.Services;

/// <summary>
/// Enchufa una sección de negocio (ADR 0137) en una línea: enlazada, validada al arrancar, y servida
/// como <see cref="INegocioDelSitio{TNegocio}"/> para el sitio de la petición.
/// </summary>
/// <remarks>
/// <para><b>Una línea, y no cuatro por funcionalidad.</b> Cada pieza se olvida sola y en verde: sin
/// <c>ValidateOnStart</c> una clave mal escrita se descubre en la primera compra (o nunca); sin el
/// validador registrado, <c>ValidateOnStart</c> no tiene nada que correr; sin el lector por sitio, el
/// override no rige. Juntas acá, no se pueden separar.</para>
///
/// <para><b>Recibe la sección, no su nombre</b>: el que llama escribe
/// <c>builder.Config.GetSection("Synergos:Features:X")</c> con el literal, que es lo que mide
/// <c>SeccionesDeConfiguracionTests</c> para que dos secciones no queden a una letra.</para>
/// </remarks>
public static class SeccionesDeNegocio
{
    /// <summary>Registra la sección <paramref name="seccion"/> como configuración de negocio.</summary>
    public static IServiceCollection AddSeccionDeNegocio<TSeccion, TNegocio>(
        this IServiceCollection services,
        IConfigurationSection seccion)
        where TSeccion : class, ISeccionDeNegocio<TNegocio>
        where TNegocio : class
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(seccion);

        services.AddOptions<TSeccion>().Bind(seccion).ValidateOnStart();
        services.AddSingleton<IValidateOptions<TSeccion>>(new ValidadorDeSeccionDeNegocio<TSeccion>(seccion));
        services.AddSingleton<INegocioDelSitio<TNegocio>>(sp =>
        {
            var sitio = sp.GetRequiredService<SitioDeLaPeticion>();
            return new NegocioDelSitio<TSeccion, TNegocio>(
                sp.GetRequiredService<IOptionsMonitor<TSeccion>>(),
                () => sitio.Resolver().SiteRoot?.Key,
                seccion.Path,
                sp.GetRequiredService<ILoggerFactory>().CreateLogger(seccion.Path));
        });

        return services;
    }
}

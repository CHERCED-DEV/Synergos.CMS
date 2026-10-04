using System.Reflection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Synergos.CMS.Web.Services.SynHost;

/// <summary>
/// Encuentra los <see cref="IResolutorSynHost{TProps}"/> del ensamblado y los registra.
/// </summary>
/// <remarks>
/// <para><b>Por qué por descubrimiento y no una línea por elemento en el composer.</b> La vista
/// pide su resolver con <c>@inject</c>, y las vistas se compilan en caliente: un resolver escrito
/// y no registrado no falla en el build, falla con un 500 la primera vez que alguien pide la
/// página. Descubriéndolos, escribir el resolver ES enchufarlo; y hay gate
/// (<c>ContratoSynHostTests</c>) que exige exactamente uno por record.</para>
///
/// <para>Singleton: un resolver no guarda estado de la petición —recibe el bloque— y sus
/// dependencias (el fallback de valores, la fábrica del diccionario) son singleton en Umbraco.</para>
/// </remarks>
public static class ResolutoresSynHost
{
    /// <summary>Cada resolver de <paramref name="ensamblado"/>, con el servicio que implementa.</summary>
    public static IReadOnlyList<(Type Servicio, Type Implementacion)> Descubrir(Assembly ensamblado)
    {
        ArgumentNullException.ThrowIfNull(ensamblado);

        return ensamblado.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false })
            .SelectMany(t => t.GetInterfaces()
                .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IResolutorSynHost<>))
                .Select(i => (Servicio: i, Implementacion: t)))
            .OrderBy(p => p.Servicio.GenericTypeArguments[0].Name, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Registra los resolvers de este ensamblado.</summary>
    public static IServiceCollection AddResolutoresSynHost(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        foreach (var (servicio, implementacion) in Descubrir(typeof(ResolutoresSynHost).Assembly))
        {
            services.AddSingleton(servicio, implementacion);
        }

        // Lo que un resolver consulta y no es un resolver: el set de iconos del sitio (#192).
        services.TryAddSingleton<IconosDelSistema>();

        return services;
    }
}

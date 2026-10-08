using Microsoft.Extensions.DependencyInjection.Extensions;
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

        // Y por su NOMBRE, para quien no conoce el tipo: la puerta adjunta los campos que un flujo
        // declara de la sección que nombra su configuración (ADR 0140 F3). Es la misma fusión por
        // sitio —el mismo INegocioDelSitio—, no una segunda lectura de la configuración.
        services.AddSingleton(new SeccionDeNegocioRegistrada(
            seccion.Key, typeof(TNegocio), sp => sp.GetRequiredService<INegocioDelSitio<TNegocio>>().Actual()));
        services.TryAddSingleton<SeccionesDelSitio>();

        return services;
    }
}

/// <summary>Una sección de negocio registrada, con el nombre por el que se la pide.</summary>
/// <param name="Nombre">El último tramo de la sección: <c>Eventos</c> por <c>Synergos:Features:Eventos</c>.</param>
/// <param name="Tipo">El tipo de su configuración fusionada.</param>
/// <param name="Actual">La configuración que rige la petición en curso.</param>
public sealed record SeccionDeNegocioRegistrada(string Nombre, Type Tipo, Func<IServiceProvider, object> Actual);

/// <summary>
/// Las secciones de negocio registradas, por nombre: el acceso genérico que usa la puerta (ADR 0140 F3).
/// </summary>
public sealed class SeccionesDelSitio
{
    private readonly IServiceProvider _servicios;
    private readonly Dictionary<string, SeccionDeNegocioRegistrada> _porNombre;

    public SeccionesDelSitio(IEnumerable<SeccionDeNegocioRegistrada> registradas, IServiceProvider servicios)
    {
        _servicios = servicios;
        _porNombre = registradas.ToDictionary(r => r.Nombre, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>El tipo de la sección <paramref name="nombre"/>, o nulo si no está registrada.</summary>
    public Type? TipoDe(string nombre) => _porNombre.GetValueOrDefault(nombre)?.Tipo;

    /// <summary>La configuración de la sección <paramref name="nombre"/> para el sitio de la petición.</summary>
    public object? Actual(string nombre) => _porNombre.GetValueOrDefault(nombre)?.Actual(_servicios);
}

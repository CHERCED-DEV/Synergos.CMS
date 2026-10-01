using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Synergos.CMS.Web.Composers;
using Umbraco.Cms.Core.Composing;

namespace Synergos.CMS.Tests.Architecture;

/// <summary>
/// Corre DE VERDAD los composers del producto web contra una configuración y devuelve lo que
/// registraron.
/// </summary>
/// <remarks>
/// <para><b>Por qué hace falta, y no basta con leer la fuente.</b> Un registro comentado contiene
/// el mismo texto que uno vivo, y un gate que busca texto pasa en verde sobre él —medido en
/// <c>BarridoSegregationTests</c>—. Preguntarle al contenedor es lo único que no admite esa
/// lectura. Y es lo único que contesta «¿qué hace ESTE valor de configuración?» sin arrancar el
/// CMS entero: lo que el composer decide mirando <c>builder.Config</c> pasa acá tal cual.</para>
///
/// <para><b>El builder es un doble y alcanza</b>: los composers del producto sólo tocan
/// <c>Services</c> y <c>Config</c> —<c>AddNotificationHandler</c> escribe en <c>Services</c>—.
/// Si alguno empieza a pedir otra cosa del builder, esto lanza acá en vez de mentir.</para>
///
/// <para>Se promovió al SEGUNDO consumidor (§0.B.17): nació dentro de
/// <c>NadaSiembraAlArrancarTests</c> (#176) y lo necesitó <c>ModosDelComposeTests</c> (#177).
/// Desde el #182 también contesta QUÉ claves leen los composers al componer
/// (<see cref="ClavesQueLeen"/>).</para>
/// </remarks>
internal static class ComposicionDelCms
{
    /// <summary>Todos los <see cref="IComposer"/> del producto web, compuestos contra <paramref name="configuracion"/>.</summary>
    internal static ServiceCollection Componer(IReadOnlyDictionary<string, string?> configuracion)
        => Componer(configuracion, anotadas: null);

    /// <summary>
    /// Las claves que los composers le PIDEN a la configuración al componer —por indexador o por
    /// sección—, preguntadas al composer y no leídas de su fuente (#182).
    /// </summary>
    /// <remarks>
    /// Es el segundo camino para saber qué interruptores existen: el primero lee la fuente, y una
    /// clave armada con una variable o leída por un ayudante no aparece ahí escrita. Lo que se
    /// pide de una sección ya obtenida (<c>seccion["BaseUrl"]</c>) no se anota: sólo lo que pasa
    /// por <c>builder.Config</c>.
    /// </remarks>
    internal static IReadOnlySet<string> ClavesQueLeen(IReadOnlyDictionary<string, string?> configuracion)
    {
        var anotadas = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Componer(configuracion, anotadas);
        return anotadas;
    }

    private static ServiceCollection Componer(IReadOnlyDictionary<string, string?> configuracion, ISet<string>? anotadas)
    {
        var services = new ServiceCollection();
        var builder = Substitute.For<IUmbracoBuilder>();
        builder.Services.Returns(services);
        IConfiguration config = new ConfigurationBuilder().AddInMemoryCollection(configuracion).Build();
        builder.Config.Returns(anotadas is null ? config : new ConfiguracionQueAnota(config, anotadas));

        foreach (var composer in typeof(SeamComposer).Assembly.GetTypes()
                     .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(IComposer).IsAssignableFrom(t))
                     .OrderBy(t => t.Name, StringComparer.Ordinal))
        {
            ((IComposer)Activator.CreateInstance(composer)!).Compose(builder);
        }

        return services;
    }

    /// <summary>
    /// El tipo que un registro entrega: el de la implementación, el que devuelve su fábrica o el
    /// de la instancia.
    /// </summary>
    /// <remarks>
    /// Un registro por fábrica (<c>AddHostedService(sp =&gt; sp.GetRequiredService&lt;T&gt;())</c>)
    /// no trae <c>ImplementationType</c>. Y en .NET 8 leer <c>ImplementationType</c> de un registro
    /// CON CLAVE lanza, así que esos se leen por su lado.
    /// </remarks>
    internal static string Entrega(ServiceDescriptor d)
        => d.IsKeyedService
            ? d.KeyedImplementationType?.Name
              ?? d.KeyedImplementationFactory?.Method.ReturnType.Name
              ?? d.KeyedImplementationInstance?.GetType().Name
              ?? "¿?"
            : d.ImplementationType?.Name
              ?? d.ImplementationFactory?.Method.ReturnType.Name
              ?? d.ImplementationInstance?.GetType().Name
              ?? "¿?";

    /// <summary>
    /// La huella de una composición: qué se registra bajo qué contrato, ordenado. Dos
    /// configuraciones con la misma huella cablearon lo mismo.
    /// </summary>
    internal static IReadOnlyList<string> Huella(IServiceCollection services)
        => services
            .Select(d => $"{d.ServiceType.FullName}|{(d.IsKeyedService ? d.ServiceKey : null)}|{Entrega(d)}|{d.Lifetime}")
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToList();

    /// <summary>Una configuración que anota cada clave que se le pide.</summary>
    private sealed class ConfiguracionQueAnota(IConfiguration interior, ISet<string> anotadas) : IConfiguration
    {
        public string? this[string key]
        {
            get
            {
                anotadas.Add(key);
                return interior[key];
            }
            set => interior[key] = value;
        }

        public IConfigurationSection GetSection(string key)
        {
            anotadas.Add(key);
            return interior.GetSection(key);
        }

        public IEnumerable<IConfigurationSection> GetChildren() => interior.GetChildren();

        public Microsoft.Extensions.Primitives.IChangeToken GetReloadToken() => interior.GetReloadToken();
    }
}

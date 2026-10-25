using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Synergos.Bff.Core.Flow;

/// <summary>Un flujo leído, con de dónde salió (para nombrar las dos fuentes de una clave repetida).</summary>
public sealed record FlujoLeido(FlujoDef Flujo, string Fuente);

/// <summary>
/// Los flujos que trae un orquestador y su registro en el host genérico (núcleo N1, P09).
/// </summary>
/// <remarks>
/// <para><b>De dónde salen</b>: los incrustados en el ensamblado (<c>flujos/*.json</c>, que viajan con el
/// binario: un despliegue no queda con el binario de una versión y la definición de otra) y, si se
/// configura <c>{Origen}:Flujos:Carpeta</c>, los <c>*.json</c> de esa carpeta (para que la fábrica declare
/// un flujo sin recompilar).</para>
///
/// <para><b>Una clave en dos fuentes no arranca</b> (supuesto P3): elegir una en silencio es el fallo que
/// el lector estricto ya evita con las claves repetidas. El mensaje nombra las dos.</para>
///
/// <para><b>Sólo se registran como genéricos los que declaran <c>tipos</c></b>: uno sin tipos es de una saga
/// tipada (Eventos) y lo registra su orquestador con su binding, como siempre.</para>
/// </remarks>
public static class FlujosDeclarados
{
    /// <summary>El prefijo de los recursos incrustados: <c>flujos/&lt;clave&gt;.json</c>.</summary>
    public const string Prefijo = "flujos/";

    /// <summary>Lee un flujo incrustado por su nombre de recurso.</summary>
    /// <exception cref="InvalidOperationException">Si el ensamblado no lo trae.</exception>
    public static FlujoDef Incrustado(Assembly ensamblado, string recurso)
    {
        ArgumentNullException.ThrowIfNull(ensamblado);
        using var flujo = ensamblado.GetManifestResourceStream(recurso)
            ?? throw new InvalidOperationException($"Falta el recurso «{recurso}» en {ensamblado.GetName().Name}.");
        using var lector = new StreamReader(flujo);
        return FlujoDef.Leer(lector.ReadToEnd());
    }

    /// <summary>Los flujos incrustados de <paramref name="ensamblado"/> y los de <paramref name="carpeta"/>.</summary>
    /// <exception cref="InvalidOperationException">Si la carpeta configurada no existe, o si una clave sale dos veces.</exception>
    /// <exception cref="FormatException">Si una definición no se lee (con su fuente en el mensaje).</exception>
    public static IReadOnlyList<FlujoLeido> Cargar(Assembly ensamblado, string? carpeta)
    {
        ArgumentNullException.ThrowIfNull(ensamblado);
        var leidos = new List<FlujoLeido>();

        foreach (var recurso in ensamblado.GetManifestResourceNames()
                     .Where(r => r.StartsWith(Prefijo, StringComparison.Ordinal) && r.EndsWith(".json", StringComparison.Ordinal))
                     .Order(StringComparer.Ordinal))
        {
            leidos.Add(new FlujoLeido(Con(recurso, () => Incrustado(ensamblado, recurso)), $"{ensamblado.GetName().Name}:{recurso}"));
        }

        if (!string.IsNullOrWhiteSpace(carpeta))
        {
            if (!Directory.Exists(carpeta))
            {
                throw new InvalidOperationException($"La carpeta de flujos «{carpeta}» está configurada y no existe.");
            }
            foreach (var fichero in Directory.EnumerateFiles(carpeta, "*.json").Order(StringComparer.Ordinal))
            {
                leidos.Add(new FlujoLeido(Con(fichero, () => FlujoDef.Leer(File.ReadAllText(fichero))), fichero));
            }
        }

        var repetida = leidos.GroupBy(l => l.Flujo.Clave, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1);
        if (repetida is not null)
        {
            throw new InvalidOperationException(
                $"La clave de flujo «{repetida.Key}» sale de dos sitios ({string.Join(" y ", repetida.Select(r => r.Fuente))}): "
                + "ninguna gana en silencio. Quitá una de las dos.");
        }
        return leidos;
    }

    private static FlujoDef Con(string fuente, Func<FlujoDef> leer)
    {
        try
        {
            return leer();
        }
        catch (FormatException ex)
        {
            throw new FormatException($"{fuente}: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Registra los flujos genéricos (los que declaran <c>tipos</c>) de <paramref name="ensamblado"/> y de la
    /// carpeta configurada: su binding derivado, su intérprete, su fachada y su validación AL ARRANCAR.
    /// </summary>
    /// <remarks>
    /// Necesita registrados la máquina de sagas de <see cref="SagaDeFlujo"/> (con un ejecutor que
    /// <see cref="IConoceSusTipos"/>), un <see cref="IRegistroDePasos"/> y unos <see cref="TiposDeValor"/>:
    /// los pone <c>AddOrquestadorDeclarado</c> de <c>Bff.Pasos</c>. Los endpoints los mapea
    /// <see cref="MapeoDeFlujos.MapFlujosDeclarados"/>.
    /// </remarks>
    public static WebApplicationBuilder AddFlujosGenericos(this WebApplicationBuilder builder, SagaVocabulary vocabulario, Assembly ensamblado)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(vocabulario);

        var carpeta = builder.Configuration[$"{vocabulario.Origin}:Flujos:Carpeta"];
        var genericos = Cargar(ensamblado, carpeta).Where(l => l.Flujo.Tipos is not null).Select(l => l.Flujo).ToList();
        Procesos(builder, vocabulario, genericos);

        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<FlowCatalog>, FlowDefinitionValidator>());
        foreach (var flujo in genericos)
        {
            var fases = flujo.Fases.Select(f => f.Nombre).ToList();

            builder.Services.AddOptions<FlowCatalog>()
                .Configure<IServiceProvider>((catalogo, sp) => catalogo.Registrar(flujo, ContratoDelFlujo.De(Binding(sp, flujo), fases)))
                .ValidateOnStart();

            builder.Services.AddSingleton(sp =>
            {
                var motor = sp.GetRequiredService<SagaEngine<SagaDeFlujo>>();
                var interprete = new FlowRunner<SagaDeFlujo>(
                    motor, sp.GetRequiredService<IRegistroDePasos>(), flujo, Binding(sp, flujo), fases,
                    sp.GetRequiredService<TimeProvider>(), sp.GetRequiredService<ILoggerFactory>().CreateLogger($"Synergos.Flujo.{flujo.Clave}"));
                var fachada = new FachadaDeFlujo<SagaDeFlujo>(
                    motor, interprete, flujo, ReglasDeLaFachada.Para(vocabulario), s => string.Equals(s.Flujo, flujo.Clave, StringComparison.Ordinal));
                return new FlujoGenerico(flujo, Binding(sp, flujo), fachada, vocabulario);
            });
        }

        return builder;
    }

    /// <summary>
    /// El catálogo de procesos de <c>{Origen}:Procesos:Archivo</c> (núcleo, lote 7; 009-P3 = A): contra él se cruzan al
    /// arrancar los pasos de procesos. Un flujo que actúa sobre procesos sin él no se construye.
    /// </summary>
    /// <remarks>
    /// El archivo es la definición versionada que publica la provisión (<c>tools/provisionar.procesos.json</c>): el
    /// despliegue lo pone donde el orquestador lo lee (NP-1 de 009). Configurado y ausente no se ignora, como la carpeta
    /// de flujos.
    /// </remarks>
    private static void Procesos(WebApplicationBuilder builder, SagaVocabulary vocabulario, IReadOnlyList<FlujoDef> flujos)
    {
        var clave = $"{vocabulario.Origin}:Procesos:Archivo";
        var archivo = builder.Configuration[clave];
        if (string.IsNullOrWhiteSpace(archivo))
        {
            var conProcesos = flujos.Where(f => f.Pasos.Values.Any(p => p.Proceso is not null)).Select(f => f.Clave).ToList();
            if (conProcesos.Count > 0)
            {
                throw new InvalidOperationException(
                    $"{string.Join(", ", conProcesos.Select(c => $"«{c}»"))} actúa sobre procesos y falta «{clave}»: sin las "
                    + "definiciones de los procesos, la transición que el proceso no tiene se descubriría con la primera persona que actúa.");
            }
            return;
        }

        if (!File.Exists(archivo))
        {
            throw new InvalidOperationException($"El catálogo de procesos «{archivo}» está configurado en «{clave}» y no existe.");
        }
        builder.Services.AddSingleton(CatalogoDeProcesos.Leer(File.ReadAllText(archivo)));
    }

    private static BindingDeFlujo Binding(IServiceProvider sp, FlujoDef flujo)
        => new(flujo, sp.GetRequiredService<TiposDeValor>(), sp.GetRequiredService<IRegistroDePasos>(),
            (sp.GetRequiredService<ICompensationExecutor<SagaDeFlujo>>() as IConoceSusTipos)?.Tipos ?? Array.Empty<string>());
}

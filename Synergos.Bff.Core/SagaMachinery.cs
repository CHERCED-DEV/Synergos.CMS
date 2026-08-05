using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Synergos.Shared;

namespace Synergos.Bff.Core;

/// <summary>
/// El arranque de un orquestador, en una llamada.
/// </summary>
/// <remarks>
/// <b>El registro también se promueve, y no es cosmética.</b> Lo que un BFF tiene que cablear
/// —almacén, compensador, motor, barrido, un cliente HTTP por capacidad con su llave y su
/// timeout— son doce líneas fáciles de escribir <i>casi</i> igual ocho veces. La que se escribe
/// distinta es la que deja un orquestador sin barrido, y eso no falla en el arranque: falla el
/// día que hay que compensar.
/// </remarks>
public static class SagaMachinery
{
    /// <summary>
    /// Registra la máquina de sagas para un dominio.
    /// </summary>
    /// <typeparam name="TSaga">La saga del dominio.</typeparam>
    /// <typeparam name="TExecutor">Quién sabe deshacer los kinds de ese dominio.</typeparam>
    /// <param name="builder">El host.</param>
    /// <param name="vocabulary">Cómo se nombra este orquestador.</param>
    /// <param name="capabilities">Las capacidades con las que habla, por nombre de cliente.</param>
    /// <remarks>
    /// <paramref name="capabilities"/> <b>no incluye</b> la de avisos: se agrega sola, porque un
    /// orquestador sin forma de avisar que algo quedó colgado no es un orquestador terminado.
    /// Olvidarla sería el modo de fallo silencioso que este método existe para cerrar.
    /// </remarks>
    public static WebApplicationBuilder AddSagaMachinery<TSaga, TExecutor>(
        this WebApplicationBuilder builder,
        SagaVocabulary vocabulary,
        params string[] capabilities)
        where TSaga : class, ISaga<TSaga>
        where TExecutor : class, ICompensationExecutor<TSaga>
    {
        var raiz = vocabulary.Origin;

        builder.Services.Configure<SagaStorageOptions>(builder.Configuration.GetSection($"{raiz}:Storage"));
        builder.Services.Configure<AlertOptions>(builder.Configuration.GetSection($"{raiz}:Alerts"));

        // La cadencia del barrido y el plazo de abandono (HU #29). Los dos son compromisos de
        // quien despliega, no verdades del código: ver SweepOptions.
        builder.Services.Configure<SweepOptions>(builder.Configuration.GetSection($"{raiz}:Sweep"));

        builder.Services.AddSingleton(vocabulary);
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<ISagaStore<TSaga>, FileSystemSagaStore<TSaga>>();
        builder.Services.AddSingleton<ICompensationExecutor<TSaga>, TExecutor>();
        builder.Services.AddSingleton<Compensator<TSaga>>();
        builder.Services.AddSingleton<CompensationAlert>();
        builder.Services.AddSingleton<SagaEngine<TSaga>>();
        builder.Services.AddHostedService<CompensationSweeper<TSaga>>();

        // El barrido de avisos colgados (HU #29). Va en TODOS los orquestadores, no en uno
        // elegido a dedo, y eso es deliberado: elegir uno obligaría a nombrarlo en algún sitio
        // —«el de la tienda barre los avisos de salud»— y el día que ese host esté caído nadie
        // barrería. Que barran todos cuesta una consulta de más por vuelta y sobrevive a que
        // falte cualquiera. Que dos coincidan sobre el mismo envío no manda dos avisos: la
        // capacidad rechaza el reintento simultáneo (`retry_in_flight`).
        builder.Services.AddHostedService<DeliverySweeper>();

        // Un cliente nombrado POR capacidad: cada una con su URL, su llave y su timeout.
        // Mezclarlas haría que subir el timeout de Payments se lo subiera a todas.
        foreach (var cap in capabilities.Append(CompensationAlert.Capability).Distinct(StringComparer.Ordinal))
        {
            var seccion = builder.Configuration.GetSection($"{raiz}:Capabilities:{cap}");
            builder.Services.AddHttpClient(cap, http =>
            {
                http.BaseAddress = new Uri(seccion["BaseUrl"] ?? $"http://localhost/{cap}/");
                http.Timeout = TimeSpan.FromSeconds(double.TryParse(seccion["TimeoutSeconds"], out var s) ? s : 10);
                if (seccion["ApiKey"] is { Length: > 0 } llave)
                {
                    http.DefaultRequestHeaders.TryAddWithoutValidation(SharedKeyAuth.HeaderName, llave);
                }
            })
            // El hilo de la correlación cruza el salto (HU #28). Va acá y no en cada llamada
            // porque acá es donde se crean TODOS los clientes de TODOS los orquestadores: una
            // capacidad nueva lo hereda al registrarse, sin que nadie se acuerde. Y sin esto la
            // saga deja seis rastros con seis identificadores distintos, que es el problema de
            // partida con un paso más de trabajo.
            .AddHttpMessageHandler<CorrelationHandler>();
        }

        return builder;
    }
}

using Synergos.Api.Notifications.Domain;
using Synergos.Api.Notifications.Storage;

namespace Synergos.Api.Notifications.Transport;

/// <summary>
/// Qué transporte usa este despliegue: Resend, la carpeta de recogida o el que rechaza.
/// </summary>
/// <remarks>
/// <para><b>Uno solo, y la elección no se adivina.</b> Con la clave de Resend, Resend. Con la carpeta
/// de recogida, la carpeta —sólo en Development (<see cref="PickupOptions"/>)—. Sin nada, el que
/// rechaza con <c>transport_not_configured</c> y lo grita. Las dos a la vez, o la carpeta fuera de
/// Development, no arrancan: son configuraciones que un despliegue no tiene que poder tener.</para>
///
/// <para>Vive fuera de <c>Program.cs</c> para que la regla se pruebe sin levantar el host.</para>
/// </remarks>
public static class EleccionDelTransporte
{
    /// <summary>Registra el transporte, o lanza si la configuración pide dos o uno que no vale acá.</summary>
    /// <exception cref="InvalidOperationException">La carpeta fuera de Development, o junto a Resend.</exception>
    public static void Registrar(IServiceCollection services, IConfiguration config, IHostEnvironment entorno)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(entorno);

        services.Configure<ResendOptions>(config.GetSection("Notifications:Resend"));
        services.Configure<PickupOptions>(config.GetSection(PickupOptions.Seccion));

        var conResend = config.GetSection("Notifications:Resend")["ApiKey"] is { Length: > 0 };
        if (config.GetSection(PickupOptions.Seccion)["Directory"] is { Length: > 0 })
        {
            if (!entorno.IsDevelopment())
            {
                throw new InvalidOperationException(
                    $"{PickupOptions.Seccion}:Directory sólo vale en Development, y el entorno es {entorno.EnvironmentName}: "
                    + "los correos quedarían en una carpeta que no lee nadie.");
            }
            if (conResend)
            {
                throw new InvalidOperationException(
                    $"{PickupOptions.Seccion}:Directory y Notifications:Resend:ApiKey están puestos a la vez: uno o el otro.");
            }
            services.AddSingleton<INotificationSender, PickupNotificationSender>();
        }
        else if (conResend)
        {
            services.AddHttpClient<INotificationSender, ResendNotificationSender>();
        }
        else
        {
            services.AddSingleton<INotificationSender, LoggingNotificationSender>();
        }
    }
}

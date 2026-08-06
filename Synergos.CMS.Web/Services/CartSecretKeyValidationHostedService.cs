using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;

namespace Synergos.CMS.Web.Services;

/// <summary>
/// Boot-time guard que detecta cuando <see cref="CartSettings.SecretKey"/>
/// quedó en su valor default <c>"synergos-dev-secret-change-me"</c> bajo
/// un environment distinto a <c>Development</c>. Loggea
/// <see cref="LogLevel.Critical"/> para que el operador lo detecte en el
/// log de boot. No detiene la aplicación — el cart sigue operativo, pero
/// la cookie es trivial de forjar.
/// </summary>
/// <remarks>
/// TODO documentado en ADR 0028. La razón de no fallar el startup es
/// que sitios en preview/staging pueden booteear con env="Production"
/// y aún no tener `SecretKey` rotada. Critical en log es suficiente
/// señal para corregir antes del público.
/// </remarks>
public sealed class CartSecretKeyValidationHostedService : IHostedService
{
    private const string DefaultSecretKey = "synergos-dev-secret-change-me";

    private readonly IOptions<CartSettings> _options;
    private readonly IHostEnvironment _environment;
    private readonly ILogger<CartSecretKeyValidationHostedService> _logger;

    public CartSecretKeyValidationHostedService(
        IOptions<CartSettings> options,
        IHostEnvironment environment,
        ILogger<CartSecretKeyValidationHostedService> logger)
    {
        _options = options;
        _environment = environment;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var secret = _options.Value.SecretKey;
        if (string.Equals(secret, DefaultSecretKey, StringComparison.Ordinal) &&
            !_environment.IsDevelopment())
        {
            _logger.LogCritical(
                "Synergos:Cart:SecretKey está en su valor default bajo environment '{Environment}'. " +
                "El cart cookie es trivial de forjar — sobreescribe el valor en appsettings.{Environment}.json " +
                "o vía variable de entorno antes de exponer este sitio al público.",
                _environment.EnvironmentName,
                _environment.EnvironmentName);
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

namespace Synergos.CMS.Application.Configuration;

/// <summary>
/// Cuántas veces y con qué espera repite el CMS una llamada al árbol de servicios (o a la
/// pasarela de pago) que falló de forma pasajera — sección <c>Synergos:Reintento</c> (#178).
/// </summary>
/// <remarks>
/// <para><b>Es otra perilla que la de los webhooks, y no por gusto.</b>
/// <c>WebhookResilienceSettings</c> espera dos segundos de base y repite tres veces porque quien
/// espera es un avisador de fondo; acá quien espera es una persona comprando o agendando, y cada
/// milisegundo de espera es suyo. Por eso los números son chicos, y por eso el techo NO lo pone
/// esta sección: lo pone el <c>TimeoutSeconds</c> de cada capacidad, que abarca los reintentos
/// enteros — un reintento nunca alarga la espera más allá de lo que el despliegue ya aceptó.</para>
///
/// <para><b>Lo que decide SI se repite no está acá</b>, y a propósito: eso lo dice la capacidad
/// con la bandera <c>transient</c> (#129) y lo acota que la petición sea repetible —un método
/// seguro o una <c>Idempotency-Key</c>—. Esta sección sólo dice cuántas veces y cada cuánto.</para>
///
/// <para>Se relee en caliente (ADR 0064): cambiar el fichero de configuración rehace la cadena
/// de reintento sin reiniciar el proceso.</para>
/// </remarks>
public sealed class ReintentoSettings
{
    /// <summary>
    /// Reintentos después del primer intento. <c>2</c> por defecto (tres intentos en total);
    /// <c>0</c> apaga el reintento sin tocar el resto de la cadena.
    /// </summary>
    public int MaxRetryAttempts { get; init; } = 2;

    /// <summary>
    /// Espera base entre intentos, en milisegundos. <c>200</c> por defecto; crece de forma
    /// exponencial y con variación aleatoria para que dos réplicas no reintenten a la vez.
    /// </summary>
    public int RetryBaseDelayMs { get; init; } = 200;
}

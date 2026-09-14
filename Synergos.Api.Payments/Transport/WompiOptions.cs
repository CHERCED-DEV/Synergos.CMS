namespace Synergos.Api.Payments.Transport;

/// <summary>
/// Lo que hace falta para cobrar por Wompi.
/// </summary>
/// <remarks>
/// <para><b>Nada de esto tiene un valor por defecto que sirva</b>, y es a propósito: un default
/// que «casi funciona» produce un despliegue que cree que cobra. Sin credencial, la selección de
/// <c>Program.cs</c> registra el proveedor que rechaza a gritos — nunca el stub en silencio.</para>
///
/// <para><b>Por qué Wompi y no otro</b> (HU #27, y ya estaba escrito en ADR 0116 para el árbol
/// del CMS): en Colombia PSE y Nequi son la mitad de los pagos, así que una pasarela sin ellos no
/// cobra. Cambiar de proveedor cuesta reescribir <see cref="WompiPaymentProvider"/> — para eso
/// existe la costura.</para>
///
/// <para><b>Son CUATRO secretos y hacen cosas distintas</b>, que es la primera fuente de líos de
/// esta integración. La pública y la de integridad firman el checkout que ve el comprador; la
/// privada autentica las consultas y las devoluciones contra el API; la de eventos verifica lo
/// que llega de vuelta. Tener una no implica tener las otras, y por eso cada una se comprueba por
/// separado y se dice cuál falta.</para>
/// </remarks>
public sealed class WompiOptions
{
    /// <summary>El nombre con el que se pide este proveedor en <c>Payments:Provider</c>.</summary>
    public const string ProviderName = "wompi";

    /// <summary>
    /// La llave privada (<c>prv_…</c>) — el <c>Bearer</c> de las consultas y las devoluciones.
    /// </summary>
    /// <remarks>
    /// Se llama <c>ApiKey</c> y no <c>PrivateKey</c> porque es la que la selección genérica de
    /// <c>Program.cs</c> exige a cualquier proveedor con nombre puesto: <c>Payments:{nombre}:ApiKey</c>.
    /// Renombrarla acá dejaría a Wompi fuera de esa comprobación común.
    /// </remarks>
    public string? ApiKey { get; set; }

    /// <summary>La llave pública (<c>pub_test_…</c> / <c>pub_prod_…</c>). Viaja en la URL del checkout.</summary>
    public string? PublicKey { get; set; }

    /// <summary>El secreto de integridad. Firma la transacción; NUNCA sale del servidor.</summary>
    public string? IntegritySecret { get; set; }

    /// <summary>El secreto de eventos. Sin él no se acepta ningún webhook.</summary>
    public string? EventsSecret { get; set; }

    /// <summary>
    /// La base del API.
    /// </summary>
    /// <remarks>
    /// Sandbox y producción se distinguen SOLO por esto: las llaves ya vienen con su prefijo
    /// (<c>pub_test_</c> / <c>pub_prod_</c>), así que apuntar a producción con llaves de prueba
    /// falla en Wompi y no en silencio.
    /// </remarks>
    public string BaseUrl { get; set; } = "https://sandbox.wompi.co/v1/";

    /// <summary>La base del checkout hospedado. Se sobreescribe en los tests.</summary>
    public string CheckoutBaseUrl { get; set; } = "https://checkout.wompi.co/p/";

    /// <summary>A dónde vuelve el comprador tras pagar. Opcional: sin ella, Wompi se queda con él.</summary>
    public string? RedirectUrl { get; set; }

    /// <summary>Cuánto se espera al proveedor antes de darlo por no disponible.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>
    /// Qué falta para poder cobrar, o <c>null</c> si no falta nada.
    /// </summary>
    /// <remarks>
    /// Devuelve el nombre del ajuste y no un booleano porque lo que hace útil un fallo de
    /// despliegue es <b>cuál</b> falta: «no está configurado» manda a leer código, y
    /// «falta <c>Payments:wompi:IntegritySecret</c>» manda a poner una variable.
    /// </remarks>
    public string? QueFalta()
    {
        if (string.IsNullOrWhiteSpace(ApiKey)) return "Payments:wompi:ApiKey (la llave privada prv_…)";
        if (string.IsNullOrWhiteSpace(PublicKey)) return "Payments:wompi:PublicKey (la llave pública pub_…)";
        return string.IsNullOrWhiteSpace(IntegritySecret) ? "Payments:wompi:IntegritySecret" : null;
    }

    /// <summary>Si hay lo mínimo para poder cobrar.</summary>
    public bool IsConfigured => QueFalta() is null;
}

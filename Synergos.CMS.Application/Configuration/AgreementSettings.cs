namespace Synergos.CMS.Application.Configuration;

/// <summary>El secreto con que se sella un contrato de alquiler, y quién custodia la llave.</summary>
/// <remarks>
/// <para>Sección <c>Synergos:Alquiler:Agreement</c> — <b>ANIDADA</b> bajo la del vertical y en su
/// propio POCO. Ésa es la corrección del #154: Eventos tuvo el suyo en una sección HERMANA
/// (<c>Synergos:Events</c> contra <c>Synergos:Eventos</c>), a una letra de distancia, donde el
/// binder descarta en silencio. Y separado del POCO de la transacción porque el cliente del eje 2
/// no tiene por qué llevar dentro la llave de firma.</para>
///
/// <para>Vacío significa «generá una y guardala cifrada», que es lo que
/// <c>CustodiaDeLlaveDeFirma</c> hace. Ponerlo es lo que permite ROTARLO y llevarlo a otra
/// instalación — sin él, perder el volumen invalida todo comprobante ya impreso.</para>
/// </remarks>
public sealed class AgreementSettings
{
    /// <summary>El secreto de firma. Vacío genera una llave y la guarda cifrada.</summary>
    public string SigningSecret { get; init; } = string.Empty;

    /// <summary>
    /// Quién sella los contratos nuevos (research R11 de la línea 005): <c>Local</c> (default, la llave del CMS) o
    /// <c>Api</c> (<c>Synergos.Api.Signing</c>). Comprobar lo hace siempre quien selló cada contrato.
    /// </summary>
    public string Mode { get; init; } = "Local";

    /// <summary>Dónde vive <c>Api.Signing</c> y bajo qué propósito se sella: la sección anidada <c>Signing</c>.</summary>
    public AgreementSigningSettings Signing { get; init; } = new();
}

/// <summary>
/// La sección <c>Synergos:Alquiler:Agreement:Signing</c>: el destino de <c>Api.Signing</c> para los contratos.
/// </summary>
/// <remarks>
/// Con <c>Mode=Local</c> y <c>BaseUrl</c> puesto, el sellador de <c>Api.Signing</c> sigue registrado para COMPROBAR los
/// contratos que selló mientras el interruptor estuvo en <c>Api</c>: conmutar de vuelta no los deja sin comprobar.
/// </remarks>
public sealed class AgreementSigningSettings
{
    /// <summary>La raíz de <c>Api.Signing</c>.</summary>
    public string BaseUrl { get; init; } = string.Empty;

    /// <summary>La llave compartida del árbol de servicios.</summary>
    public string ApiKey { get; init; } = string.Empty;

    /// <summary>
    /// El propósito bajo el que viven las llaves de los contratos en <c>Api.Signing</c>. PROPIO, no el de Academia: las
    /// llaves viven por propósito, y compartirlo dejaría que un diploma comprobara como contrato.
    /// </summary>
    public string Purpose { get; init; } = "alquiler.contrato";

    /// <summary>Cuánto se le espera, reintentos incluidos. Lento es como caído: el contrato sale con el sello pendiente.</summary>
    public int TimeoutSeconds { get; init; } = 10;
}

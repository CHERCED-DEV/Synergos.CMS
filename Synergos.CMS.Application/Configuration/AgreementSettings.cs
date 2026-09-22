namespace Synergos.CMS.Application.Configuration;

/// <summary>El secreto con que se sella un contrato de alquiler.</summary>
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
}

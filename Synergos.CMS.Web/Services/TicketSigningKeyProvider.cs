using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Web.Services;

/// <summary>
/// Resuelve la llave con la que se firman los QR de las entradas (T9), en este orden:
/// el secreto configurado (<c>Synergos:Eventos:Ticket:SigningSecret</c>) y, si no hay, una
/// llave aleatoria generada UNA vez, cifrada con <see cref="IDataProtector"/> y guardada
/// en <see cref="IJsonEntityStore"/> bajo una clave conocida.
/// </summary>
/// <remarks>
/// <para><b>Por qué no basta con generar una llave en memoria:</b> el bug que T9 corrige
/// es exactamente ese. El QR anterior derivaba de <c>String.GetHashCode()</c>, que en
/// .NET Core está randomizado por proceso, así que cambiaba en cada reinicio. Una llave
/// efímera repetiría la historia: las entradas emitidas ayer dejarían de validar hoy.</para>
/// <para><b>Por qué NO se usa <see cref="IPrivateFileStore"/> (ADR 0109), que era la
/// opción obvia:</b> ese almacén <b>genera su propio id opaco</b> y descarta el nombre
/// que le pasa el llamador — es justo lo que lo hace seguro para documentos subidos
/// (nadie elige un id adivinable ni pisa un fichero ajeno), y justo lo que lo inhabilita
/// para un singleton que hay que <b>recuperar por nombre</b>. Se intentó, y la llave se
/// guardó bajo un GUID: al reiniciar no se encontraba y se generaba otra, reproduciendo
/// el bug que T9 venía a cerrar. Lo detectó la verificación en vivo, no los tests.
/// <see cref="IJsonEntityStore"/> sí acepta la clave del llamador, que es lo que hace
/// falta aquí.</para>
/// <para><b>Por qué no un default en el repo:</b> un secreto commiteado es un secreto
/// conocido, y firmar con él daría tokens falsificables con apariencia de seguros.</para>
/// <para>La llave se resuelve <b>perezosamente</b> y se cachea: cero I/O en el arranque
/// (ADR 0013). Va cifrada porque <see cref="IJsonEntityStore"/> guarda JSON en claro.</para>
/// </remarks>
public sealed class TicketSigningKeyProvider : IDisposable
{
    private readonly CustodiaDeLlaveDeFirma _custodia;

    /// <summary>Construye la custodia de esta llave.</summary>
    /// <param name="store">Dónde se guarda cifrada.</param>
    /// <param name="dataProtectionProvider">Con qué se cifra.</param>
    /// <param name="options">La sección del vertical.</param>
    /// <param name="logger">Dónde se avisa.</param>
    public TicketSigningKeyProvider(
        IJsonEntityStore store,
        IDataProtectionProvider dataProtectionProvider,
        IOptions<TicketSettings> options,
        ILogger<TicketSigningKeyProvider> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        _custodia = new CustodiaDeLlaveDeFirma(
            store,
            dataProtectionProvider,
            keyId: "ticket-signing-v1",
            protectorPurpose: "Synergos.Tickets.SigningKey.v1",
            secretoConfigurado: () => options.Value.SigningSecret,
            sujeto: "Tickets",
            claveDeConfiguracion: "Synergos:Eventos:Ticket:SigningSecret",
            consecuenciaDePerderla: "los QR ya emitidos dejarán de ser válidos",
            logger: logger);
    }

    /// <summary>La llave, resuelta una vez y cacheada.</summary>
    /// <param name="cancellationToken">Cancelación del request en curso.</param>
    /// <returns>Los bytes de la llave.</returns>
    public Task<byte[]> GetKeyAsync(CancellationToken cancellationToken = default)
        => _custodia.GetKeyAsync(cancellationToken);

    /// <inheritdoc />
    public void Dispose() => _custodia.Dispose();
}

/// <summary>
/// <see cref="ITicketSigner"/> que resuelve la llave perezosamente vía
/// <see cref="TicketSigningKeyProvider"/> y delega en <c>HmacTicketSigner</c>.
/// </summary>
/// <remarks>
/// Existe porque la llave es asíncrona de resolver (puede haber que leerla o crearla) y
/// <see cref="ITicketSigner"/> es síncrono a propósito: firmar y verificar son puro
/// cómputo, y hacer async la puerta de un evento por una llave que se resuelve una vez
/// contagiaría async a todo el camino del check-in.
/// </remarks>
public sealed class LazyTicketSigner : ITicketSigner
{
    private readonly TicketSigningKeyProvider _keys;
    private readonly Lazy<ITicketSigner> _inner;

    public LazyTicketSigner(TicketSigningKeyProvider keys)
    {
        _keys = keys;
        _inner = new Lazy<ITicketSigner>(
            () => new Synergos.CMS.Application.Services.Impl.HmacTicketSigner(
                _keys.GetKeyAsync().GetAwaiter().GetResult()),
            LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public string Sign(TicketToken token) => _inner.Value.Sign(token);

    public TicketToken? Verify(string? rawToken) => _inner.Value.Verify(rawToken);
}

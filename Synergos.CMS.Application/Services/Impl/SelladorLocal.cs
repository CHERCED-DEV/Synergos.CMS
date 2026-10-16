using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Application.Services.Impl;

/// <summary>
/// El sellador de la llave LOCAL del CMS: envuelve el <see cref="IAgreementSigner"/> de siempre, que se conserva.
/// </summary>
/// <remarks>
/// <para><b>Se registra en los dos modos del interruptor</b>: con <c>Mode=Api</c> ya no sella, pero sigue comprobando
/// los contratos que selló — todos los anteriores al interruptor. Sin él, cada contrato impreso dejaría de comprobar el
/// día que se conmuta.</para>
///
/// <para>Comprobar nunca contesta «no sé»: la llave está en esta máquina.</para>
/// </remarks>
public sealed class SelladorLocal : ISelladorDeContratos
{
    private readonly IAgreementSigner _firmante;

    /// <summary>Construye el sellador local.</summary>
    /// <param name="firmante">El firmante de la llave local.</param>
    public SelladorLocal(IAgreementSigner firmante)
        => _firmante = firmante ?? throw new ArgumentNullException(nameof(firmante));

    /// <inheritdoc />
    public string Nombre => RentalAgreement.SelladoLocal;

    /// <inheritdoc />
    public Task<SelloDelContrato> SellarAsync(AgreementSubject subject, CancellationToken cancellationToken = default)
        => Task.FromResult(new SelloDelContrato(_firmante.Seal(subject), Nombre));

    /// <inheritdoc />
    public Task<bool?> ComprobarAsync(
        string? sello, AgreementSubject subject, CancellationToken cancellationToken = default)
        => Task.FromResult<bool?>(_firmante.Matches(sello, subject));
}

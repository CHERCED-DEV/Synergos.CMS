using System.Text.Json;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Application.Services.Impl;

/// <summary>
/// El registro de contratos emitidos: el ÚNICO sitio que sabe qué se le entregó a quién.
/// </summary>
/// <remarks>
/// <para><b>Este registro NUNCA sale a la red</b>, que es la invariante del eje 3 (doc 12 §3.2):
/// lo que puede salir es el SELLO —la custodia de la llave— y no el índice de emitidos. Por eso
/// con <c>Synergos.Bff.Alquiler</c> caído quien alquiló sigue viendo su contrato y cuánto se le
/// retuvo; lo que se para es reservar, devolver y cancelar.</para>
///
/// <para><b>Y por eso mismo es el modelo de LECTURA del vertical</b>: el seam de la transacción
/// no tiene operación de consulta a propósito. Es la forma del timeline de pedidos (#46) y de las
/// bandejas de Gobierno (#62).</para>
///
/// <para>Durable desde el primer día: un comprobante que se pierde al reiniciar no sirve para lo
/// único que existe.</para>
/// </remarks>
public sealed class EquipmentAgreementLedger
{
    /// <summary>El namespace de este registro en el almacén.</summary>
    public const string ResourceType = "alquiler-agreements";

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    private readonly IJsonEntityStore _store;

    /// <summary>Construye el registro.</summary>
    /// <param name="store">Dónde viven los contratos, durable.</param>
    public EquipmentAgreementLedger(IJsonEntityStore store)
        => _store = store ?? throw new ArgumentNullException(nameof(store));

    /// <summary>Anota un contrato emitido. Reemplaza el de ese alquiler si ya había.</summary>
    /// <param name="agreement">El contrato.</param>
    /// <param name="cancellationToken">Cancelación del request en curso.</param>
    /// <returns>Una tarea que termina cuando quedó escrito.</returns>
    public Task RecordAsync(RentalAgreement agreement, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(agreement);
        return _store.WriteAsync(ResourceType, agreement.RentalId,
            JsonSerializer.Serialize(agreement, Json), cancellationToken);
    }

    /// <summary>El contrato de un alquiler, o null.</summary>
    /// <param name="rentalId">Cuál alquiler.</param>
    /// <param name="cancellationToken">Cancelación del request en curso.</param>
    /// <returns>El contrato, o null si no se emitió.</returns>
    public async Task<RentalAgreement?> GetAsync(
        string rentalId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(rentalId))
        {
            return null;
        }

        var json = await _store.ReadAsync(ResourceType, rentalId, cancellationToken).ConfigureAwait(false);
        return json is null ? null : Deserializar(json);
    }

    /// <summary>Los contratos de una persona, del más reciente al más viejo.</summary>
    /// <param name="renterId">El seudónimo de quien alquiló.</param>
    /// <param name="cancellationToken">Cancelación del request en curso.</param>
    /// <returns>Sus contratos. Vacío es «todavía ninguno», que es cierto: reservar los crea.</returns>
    public async Task<IReadOnlyList<RentalAgreement>> ListForAsync(
        string renterId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(renterId))
        {
            return Array.Empty<RentalAgreement>();
        }

        var todos = await _store.ListAsync(ResourceType, cancellationToken).ConfigureAwait(false);
        return todos
            .Select(Deserializar)
            .Where(a => a is not null && string.Equals(a.RenterId, renterId, StringComparison.Ordinal))
            .Select(a => a!)
            .OrderByDescending(a => a.IssuedUtc)
            .ToList();
    }

    private static RentalAgreement? Deserializar(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<RentalAgreement>(json, Json);
        }
        catch (JsonException)
        {
            // Un contrato ilegible no tumba la bandeja entera; se omite.
            return null;
        }
    }
}

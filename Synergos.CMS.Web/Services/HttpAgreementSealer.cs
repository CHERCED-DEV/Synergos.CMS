using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Web.Services;

/// <summary>
/// El <see cref="ISelladorDeContratos"/> cuya llave la custodia <c>Synergos.Api.Signing</c> (research R11 de la línea
/// 005), con <c>Synergos:Alquiler:Agreement:Mode=Api</c>.
/// </summary>
/// <remarks>
/// <para><b>Va a <c>/v1/seals</c> y no a <c>/v1/signatures</c></b>, por lo mismo que el diploma (#45): aquel token
/// vence, no es determinista y publica su payload sin llave. Un contrato no vence, se re-emite igual y lleva al
/// titular dentro de lo sellado.</para>
///
/// <para><b>Con la capacidad caída o lenta, el contrato sale con el sello PENDIENTE y NO se cae a la llave local.</b>
/// Caer ahí registraría un contrato «sellado por api» que la API no reconoce, o uno local que nadie eligió; y se
/// sella al leerlo, cuando vuelva. Comprobar sin respuesta es <c>null</c> —«no se pudo»—, nunca <c>false</c>: darlo
/// por falso diría que un contrato bueno no cuadra. Por eso este fichero no nombra al sellador local.</para>
///
/// <para><b>Los records del cable son privados</b> mientras el núcleo no publique el contrato de <c>Api.Signing</c>
/// (NN-9 en <c>specs/005-alquiler-renta/necesidades-del-nucleo.md</c>), como en <c>HttpCertificateIdSigner</c>.</para>
/// </remarks>
public sealed class HttpAgreementSealer : ISelladorDeContratos
{
    /// <summary>Cliente nombrado que registra el composer.</summary>
    public const string ClientName = "synergos-api-signing-alquiler";

    /// <summary>Lo que el contrato registra en <c>SelladoPor</c> antes de la llave.</summary>
    public const string NombreApi = "api";

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly IHttpClientFactory _factory;
    private readonly IOptions<AgreementSettings> _settings;
    private readonly ILogger<HttpAgreementSealer> _log;

    /// <summary>Construye el sellador.</summary>
    /// <param name="factory">De dónde sale el cliente HTTP.</param>
    /// <param name="settings">La sección <c>Synergos:Alquiler:Agreement</c>, con su <c>Signing</c> anidada.</param>
    /// <param name="log">Dónde se avisa que el sello quedó pendiente.</param>
    public HttpAgreementSealer(
        IHttpClientFactory factory, IOptions<AgreementSettings> settings, ILogger<HttpAgreementSealer> log)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    /// <inheritdoc />
    public string Nombre => NombreApi;

    private string Proposito => _settings.Value.Signing.Purpose;

    /// <inheritdoc />
    public async Task<SelloDelContrato> SellarAsync(
        AgreementSubject subject, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subject);
        try
        {
            using var http = _factory.CreateClient(ClientName);
            using var respuesta = await http.PostAsJsonAsync(
                "v1/seals", new SealRequest(Proposito, Contenido(subject)), Json, cancellationToken).ConfigureAwait(false);

            if (!respuesta.IsSuccessStatusCode)
            {
                return Pendiente(subject, $"contestó {(int)respuesta.StatusCode}");
            }

            var sello = await respuesta.Content
                .ReadFromJsonAsync<SealResponse>(Json, cancellationToken).ConfigureAwait(false);
            return sello is null || string.IsNullOrWhiteSpace(sello.Seal) || string.IsNullOrWhiteSpace(sello.KeyId)
                ? Pendiente(subject, "devolvió un sello vacío")
                : new SelloDelContrato(sello.Seal, $"{NombreApi}:{sello.KeyId}");
        }
        catch (HttpRequestException)
        {
            return Pendiente(subject, "no responde");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Pendiente(subject, "tardó demasiado");
        }
        catch (JsonException)
        {
            return Pendiente(subject, "devolvió algo que no es un sello");
        }
    }

    /// <inheritdoc />
    public async Task<bool?> ComprobarAsync(
        string? sello, AgreementSubject subject, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subject);
        if (string.IsNullOrWhiteSpace(sello))
        {
            return false;
        }

        try
        {
            using var http = _factory.CreateClient(ClientName);
            using var respuesta = await http.PostAsJsonAsync(
                "v1/seals/verify", new VerifySealRequest(Proposito, Contenido(subject), sello.Trim()), Json,
                cancellationToken).ConfigureAwait(false);

            if (respuesta.IsSuccessStatusCode)
            {
                return true;
            }

            // Que no cuadre es una respuesta (403 seal_mismatch, 400 malformado). Lo demás —sin llaves del propósito,
            // la llave del árbol mal puesta, un 5xx— es «no sé», y no se convierte en «no cuadra».
            if (respuesta.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.BadRequest)
            {
                return false;
            }

            _log.LogWarning("Api.Signing no pudo comprobar el contrato {RentalId} ({Estado}).",
                subject.RentalId, (int)respuesta.StatusCode);
            return null;
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    /// <summary>
    /// Lo sellado: lo que el contrato afirma, con un separador que no aparece en ningún campo y la clase delante.
    /// </summary>
    /// <remarks>
    /// La clase (<c>contrato</c>) va delante para que el sello de otra cosa del mismo alquiler —el desenlace de la
    /// devolución— nunca pueda presentarse como el del contrato. Viaja en el cuerpo, nunca en la URL: es el sujeto.
    /// </remarks>
    private static string Contenido(AgreementSubject s)
        => string.Join('\u001f', "contrato", s.RentalId, s.RenterId, s.EquipmentId,
            s.DepositHeld.ToString("0.####", CultureInfo.InvariantCulture), s.Currency);

    private SelloDelContrato Pendiente(AgreementSubject subject, string porQue)
    {
        _log.LogWarning(
            "Api.Signing {PorQue}: el contrato {RentalId} sale con el sello PENDIENTE y se sella al leerlo.",
            porQue, subject.RentalId);
        return new SelloDelContrato(string.Empty, NombreApi, Pendiente: true);
    }

    // ── Lo que viaja (NN-9: privado hasta que el núcleo publique el contrato de Api.Signing) ──

    private sealed record SealRequest(string? Purpose, string? Payload);

    private sealed record VerifySealRequest(string? Purpose, string? Payload, string? Seal);

    private sealed record SealResponse(string Seal, string KeyId);
}

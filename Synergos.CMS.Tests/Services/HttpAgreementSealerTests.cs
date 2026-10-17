using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Services;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// El contrato lo sella <c>Api.Signing</c> con <c>Mode=Api</c> (research R11 de la línea 005, T022): sella, comprueba,
/// con la capacidad caída el sello queda pendiente —nunca con la llave local—, y al volver se sella al leerlo.
/// </summary>
/// <remarks>
/// La capacidad falsa sella DE VERDAD (HMAC con su propia llave, como <c>Api.Signing</c>) y contesta 403 cuando no
/// cuadra: un doble que dijera «sí» a todo no distinguiría un contrato bueno de uno alterado.
/// </remarks>
public sealed class HttpAgreementSealerTests
{
    private sealed class CapacidadFalsa : HttpMessageHandler
    {
        private static readonly byte[] Llave = Encoding.UTF8.GetBytes("llave-de-la-capacidad-k-1");

        public List<(string Path, string? Query, string Body)> Llamadas { get; } = new();

        /// <summary>No responde: la conexión no se abre.</summary>
        public bool Caida { get; set; }

        /// <summary>Lo que contesta en vez de atender, si no es null (p. ej. 404 sin llaves del propósito).</summary>
        public HttpStatusCode? Contesta { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
        {
            var path = req.RequestUri!.AbsolutePath;
            var cuerpo = req.Content is null ? string.Empty : await req.Content.ReadAsStringAsync(ct);
            Llamadas.Add((path, req.RequestUri.Query, cuerpo));

            if (Caida)
            {
                throw new HttpRequestException("Connection refused");
            }

            if (Contesta is { } codigo)
            {
                return Responder(codigo, $$"""{"title":"x","status":{{(int)codigo}},"code":"signing.no_keys_for_purpose"}""");
            }

            using var json = JsonDocument.Parse(cuerpo);
            var payload = json.RootElement.GetProperty("payload").GetString()!;
            var sello = Sellar(json.RootElement.GetProperty("purpose").GetString()! + "|" + payload);

            if (path.EndsWith("/verify", StringComparison.Ordinal))
            {
                return string.Equals(json.RootElement.GetProperty("seal").GetString(), sello, StringComparison.Ordinal)
                    ? Responder(HttpStatusCode.OK, """{"keyId":"k-1"}""")
                    : Responder(HttpStatusCode.Forbidden, """{"title":"x","status":403,"code":"signing.seal_mismatch"}""");
            }

            return Responder(HttpStatusCode.OK, $$"""{"seal":"{{sello}}","keyId":"k-1"}""");
        }

        private static string Sellar(string contenido)
        {
            using var hmac = new HMACSHA256(Llave);
            return Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(contenido)))
                .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        private static HttpResponseMessage Responder(HttpStatusCode codigo, string cuerpo)
            => new(codigo) { Content = new StringContent(cuerpo, Encoding.UTF8, "application/json") };
    }

    private sealed class FabricaFalsa : IHttpClientFactory
    {
        private readonly HttpMessageHandler _h;
        public FabricaFalsa(HttpMessageHandler h) => _h = h;
        public HttpClient CreateClient(string name)
            => new(_h, disposeHandler: false) { BaseAddress = new Uri("http://signing.local/") };
    }

    private sealed class AlmacenEnMemoria : IJsonEntityStore
    {
        private readonly Dictionary<string, string> _data = new(StringComparer.Ordinal);

        public Task WriteAsync(string resourceType, string key, string json, CancellationToken cancellationToken = default)
        {
            _data[resourceType + "/" + key] = json;
            return Task.CompletedTask;
        }

        public Task<string?> ReadAsync(string resourceType, string key, CancellationToken cancellationToken = default)
            => Task.FromResult(_data.TryGetValue(resourceType + "/" + key, out var json) ? json : null);

        public Task<IReadOnlyList<string>> ListAsync(string resourceType, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<string>>(_data
                .Where(kv => kv.Key.StartsWith(resourceType + "/", StringComparison.Ordinal))
                .Select(kv => kv.Value)
                .ToList());

        public Task<bool> DeleteAsync(string resourceType, string key, CancellationToken cancellationToken = default)
            => Task.FromResult(_data.Remove(resourceType + "/" + key));
    }

    private sealed class RelojFijo : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 10, 16, 12, 0, 0, TimeSpan.Zero);
    }

    private static readonly AgreementSubject Sujeto = new("alq-1", "u-1", "andamio", 250_000m, "COP");

    private static HttpAgreementSealer Armar(CapacidadFalsa capacidad)
        => new(new FabricaFalsa(capacidad), Options.Create(new AgreementSettings { Mode = "Api" }),
            NullLogger<HttpAgreementSealer>.Instance);

    private static SelladorLocal Local() => new(new HmacAgreementSigner(Encoding.UTF8.GetBytes("llave-local-del-cms-32-bytes-xx!")));

    private static Rental Alquiler() => new(
        "alq-1", "andamio", 1, new DateOnly(2026, 11, 2), new DateOnly(2026, 11, 9), "u-1",
        RentalState.Reserved, new RentalQuote("andamio", 1, 7, 30_000m, 210_000m, 250_000m, "COP"),
        250_000m, 0m);

    [Fact]
    public async Task Sella_en_v1_seals_con_el_proposito_del_contrato_y_el_sujeto_en_el_cuerpo()
    {
        var capacidad = new CapacidadFalsa();

        var sello = await Armar(capacidad).SellarAsync(Sujeto);

        Assert.False(sello.Pendiente);
        Assert.NotEqual(string.Empty, sello.Valor);
        Assert.Equal("api:k-1", sello.SelladoPor);
        var llamada = Assert.Single(capacidad.Llamadas);
        Assert.Equal("/v1/seals", llamada.Path);
        Assert.Contains("\"purpose\":\"alquiler.contrato\"", llamada.Body, StringComparison.Ordinal);
        Assert.Contains("alq-1", llamada.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("alq-1", llamada.Query ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Comprueba_lo_que_sello_y_un_contrato_alterado_no_cuadra()
    {
        var capacidad = new CapacidadFalsa();
        var sellador = Armar(capacidad);
        var sello = await sellador.SellarAsync(Sujeto);

        Assert.True(await sellador.ComprobarAsync(sello.Valor, Sujeto));
        Assert.False(await sellador.ComprobarAsync(sello.Valor, Sujeto with { DepositHeld = 1m }));
        Assert.False(await sellador.ComprobarAsync(string.Empty, Sujeto));
        Assert.Equal("/v1/seals/verify", capacidad.Llamadas[^1].Path);
    }

    /// <summary>El desenlace de la devolución va por las mismas rutas con su clase delante (005-P6, T025).</summary>
    [Fact]
    public async Task El_desenlace_se_sella_con_su_clase_y_no_comprueba_como_contrato()
    {
        var capacidad = new CapacidadFalsa();
        var sellador = Armar(capacidad);
        var desenlace = new AgreementOutcomeSubject("alq-1", "u-1", "andamio", 40_000m, 210_000m, "COP", "p-1",
            new DateTimeOffset(2026, 11, 9, 9, 0, 0, TimeSpan.Zero));

        var sello = await sellador.SellarAsync(desenlace);

        Assert.Equal("api:k-1", sello.SelladoPor);
        Assert.Contains("desenlace", capacidad.Llamadas[^1].Body, StringComparison.Ordinal);
        Assert.True(await sellador.ComprobarAsync(sello.Valor, desenlace));
        Assert.False(await sellador.ComprobarAsync(sello.Valor, Sujeto));
        Assert.False(await sellador.ComprobarAsync(sello.Valor, desenlace with { Damage = 0m }));
    }

    [Fact]
    public async Task Con_la_capacidad_caida_el_sello_queda_pendiente_y_comprobar_no_sabe()
    {
        var capacidad = new CapacidadFalsa { Caida = true };
        var sellador = Armar(capacidad);

        var sello = await sellador.SellarAsync(Sujeto);

        Assert.True(sello.Pendiente);
        Assert.Equal(string.Empty, sello.Valor);
        Assert.Equal(HttpAgreementSealer.NombreApi, sello.SelladoPor);
        Assert.Null(await sellador.ComprobarAsync("un-sello", Sujeto));

        // Sin llaves del propósito es un despliegue a medio configurar: pendiente al sellar, «no sé» al comprobar.
        capacidad.Caida = false;
        capacidad.Contesta = HttpStatusCode.NotFound;
        Assert.True((await sellador.SellarAsync(Sujeto)).Pendiente);
        Assert.Null(await sellador.ComprobarAsync("un-sello", Sujeto));
    }

    [Fact]
    public async Task Caida_al_emitir_nunca_sella_con_la_llave_local_y_al_volver_se_sella_al_leer()
    {
        var capacidad = new CapacidadFalsa { Caida = true };
        var ledger = new EquipmentAgreementLedger(new AlmacenEnMemoria());
        var emisor = new EquipmentAgreementIssuer(ledger, Armar(capacidad), new RelojFijo(), [Local()]);

        var emitido = await emisor.IssueAsync(Alquiler(), "Andamio");

        Assert.True(emitido.SelloPendiente);
        Assert.Equal(HttpAgreementSealer.NombreApi, emitido.SelladoPor);
        Assert.Equal(string.Empty, emitido.Seal);
        Assert.Null((await emisor.LeerAsync(emitido)).Comprobado);

        // Mientras estaba pendiente, otra operación anotó el estado: el sello no la pisa.
        await ledger.RecordAsync(emitido with { Estado = RentalState.Declared });

        capacidad.Caida = false;
        var leido = await emisor.LeerAsync(emitido);

        Assert.True(leido.Comprobado);
        Assert.False(leido.Contrato.SelloPendiente);
        Assert.Equal("api:k-1", leido.Contrato.SelladoPor);
        var guardado = await ledger.GetAsync("alq-1");
        Assert.Equal(leido.Contrato.Seal, guardado!.Seal);
        Assert.Equal(RentalState.Declared, guardado.Estado);
    }
}

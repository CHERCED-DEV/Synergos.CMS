using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Services;
using Xunit;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// Cubre <see cref="TicketSigningKeyProvider"/> (T9 — de dónde sale la llave que firma
/// los QR).
/// </summary>
/// <remarks>
/// <b>Este fichero existe por un bug que los tests NO atraparon y sí la verificación en
/// vivo.</b> La primera versión guardaba la llave en <c>IPrivateFileStore</c>, que
/// GENERA su propio id opaco y descarta el nombre del llamador (lo que lo hace seguro
/// para documentos subidos). La llave quedaba bajo un GUID, al reiniciar no se
/// encontraba, se generaba otra… y el QR volvía a cambiar en cada arranque: exactamente
/// el bug que T9 venía a cerrar, reintroducido por el arreglo. El test que faltaba es el
/// de abajo: <b>dos proveedores distintos sobre el MISMO store devuelven la misma
/// llave</b> — que es lo que significa "sobrevive un reinicio".
/// </remarks>
public sealed class TicketSigningKeyProviderTests : IDisposable
{
    private readonly string _root;
    private readonly IDataProtectionProvider _protection;

    public TicketSigningKeyProviderTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "syn-t9-keys-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _protection = DataProtectionProvider.Create(new DirectoryInfo(Path.Combine(_root, "dp")));
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { /* best-effort */ }
    }

    private TicketSigningKeyProvider Make(IJsonEntityStore store, string configured = "") =>
        new(store,
            _protection,
            Options.Create(new EventsSettings { TicketSigningSecret = configured }),
            NullLogger<TicketSigningKeyProvider>.Instance);

    [Fact] // EL TEST QUE FALTABA: la llave sobrevive un "reinicio" (proveedor nuevo, mismo store)
    public async Task GetKey_AcrossProviderInstances_IsStable()
    {
        var store = new InMemoryJsonEntityStore();

        var before = await Make(store).GetKeyAsync();
        var after = await Make(store).GetKeyAsync();   // "reinicio": instancia nueva

        Assert.NotEmpty(before);
        Assert.Equal(before, after);
    }

    [Fact] // …y por tanto el QR firmado con ella también sobrevive
    public async Task SignedToken_SurvivesRestart()
    {
        var store = new InMemoryJsonEntityStore();
        var token = new TicketToken("evt-x", "tkt_1", 0);

        var beforeQr = new HmacTicketSigner(await Make(store).GetKeyAsync()).Sign(token);
        var afterSigner = new HmacTicketSigner(await Make(store).GetKeyAsync());

        Assert.Equal(beforeQr, afterSigner.Sign(token));
        Assert.NotNull(afterSigner.Verify(beforeQr)); // el QR impreso ayer valida hoy
    }

    [Fact] // El secreto configurado MANDA sobre el generado (es la vía de producción)
    public async Task GetKey_ConfiguredSecret_Wins()
    {
        var store = new InMemoryJsonEntityStore();

        var key = await Make(store, configured: "secreto-de-produccion").GetKeyAsync();

        Assert.Equal(Encoding.UTF8.GetBytes("secreto-de-produccion"), key);
        // No se generó ni guardó nada: la llave viene de config.
        Assert.Empty(await store.ListAsync("keys"));
    }

    [Fact] // Dos despliegues con el MISMO secreto configurado firman igual (multi-instancia)
    public async Task GetKey_SameConfiguredSecret_MatchesAcrossStores()
    {
        var a = await Make(new InMemoryJsonEntityStore(), "compartido").GetKeyAsync();
        var b = await Make(new InMemoryJsonEntityStore(), "compartido").GetKeyAsync();

        Assert.Equal(a, b);
    }

    [Fact] // La llave generada NO se guarda en claro (el store de JSON no cifra)
    public async Task GeneratedKey_IsStoredEncrypted()
    {
        var store = new InMemoryJsonEntityStore();

        var key = await Make(store).GetKeyAsync();

        var raw = await store.ReadAsync("keys", "ticket-signing-v1");
        Assert.False(string.IsNullOrWhiteSpace(raw));
        Assert.DoesNotContain(Convert.ToBase64String(key), raw!, StringComparison.Ordinal);
    }

    [Fact] // idempotent: pedir la llave dos veces a la MISMA instancia no la regenera
    public async Task GetKey_Twice_SameInstance_IsCached()
    {
        var provider = Make(new InMemoryJsonEntityStore());

        Assert.Equal(await provider.GetKeyAsync(), await provider.GetKeyAsync());
    }
}

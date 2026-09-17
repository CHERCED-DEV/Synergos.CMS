using Microsoft.Extensions.Options;
using Synergos.Api.Cart.Domain;
using Synergos.Api.Cart.Storage;
using Synergos.Core;
using Synergos.Shared;

namespace Synergos.CMS.Tests.Api;

/// <summary>
/// Con qué se afirmó la identidad del dueño de una canasta (HU #14).
/// </summary>
/// <remarks>
/// <para><b>«Esta canasta es de Ana» no dice nada sin «y así se supo que era Ana».</b> Hasta esta
/// rebanada, cualquiera con la llave compartida abría canastas a nombre de quien quisiera y el
/// registro las leía igual que las abiertas por la propia persona. Es el defecto #42 sobre el
/// dato que decide de quién es lo que se va a comprar.</para>
///
/// <para><b>Nulo es «no consta», no un default.</b> Las canastas anteriores no llevan afirmación,
/// y ésa es la verdad sobre ellas: rellenarlas con <c>CmsSession</c> inventaría una comprobación
/// que nadie hizo.</para>
/// </remarks>
public sealed class CartIdentityTests
{
    private sealed class RelojFalso(DateTimeOffset inicio) : TimeProvider
    {
        private readonly DateTimeOffset _now = inicio;

        public override DateTimeOffset GetUtcNow() => _now;
    }

    private static readonly DateTimeOffset Ahora = new(2026, 9, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly Ref Duenio = Ref.Create("tienda.comprador", "m-1");

    private static (CartService Svc, string Raiz) Nuevo()
    {
        var raiz = Path.Combine(Path.GetTempPath(), "cart-id-" + Guid.NewGuid().ToString("n"));
        var svc = new CartService(
            new FileSystemCartStore(Options.Create(new CartStorageOptions { Root = raiz })),
            new FileIdempotencyLedger(raiz),
            new RelojFalso(Ahora));
        return (svc, raiz);
    }

    // ── happy ───────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(IdentityAssertion.CmsSession)]
    [InlineData(IdentityAssertion.IdentityToken)]
    [InlineData(IdentityAssertion.GovFederation)]
    public void Abrir_guarda_con_que_se_afirmo(IdentityAssertion afirmacion)
    {
        var (svc, _) = Nuevo();

        var abierta = svc.Open(Duenio, null, IdempotencyKey.Of("k1"), afirmacion);

        Assert.Equal(afirmacion, abierta.Value!.OpenedWith);
    }

    /// <summary>
    /// Y se puede LEER después: sin eso, guardarla no serviría de nada.
    /// </summary>
    /// <remarks>
    /// Se abre un almacén NUEVO sobre el mismo fichero, no se relee del mismo: el caché de
    /// <c>JsonCollectionStore</c> hace que leer del mismo proceso nunca deserialice, y ahí se
    /// escondió el defecto #82 durante toda una HU.
    /// </remarks>
    [Fact]
    public void La_afirmacion_sobrevive_al_disco()
    {
        var (svc, raiz) = Nuevo();
        var id = svc.Open(Duenio, null, IdempotencyKey.Of("k1"), IdentityAssertion.IdentityToken).Value!.Id;

        var otro = new FileSystemCartStore(Options.Create(new CartStorageOptions { Root = raiz }));

        Assert.Equal(IdentityAssertion.IdentityToken, otro.Find(id)!.OpenedWith);
    }

    // ── idempotent ──────────────────────────────────────────────────────────

    /// <summary>
    /// Reintentar devuelve la canasta que ya había, <b>con la afirmación que tuvo</b>.
    /// </summary>
    /// <remarks>
    /// Re-anotarla con la del reintento reescribiría un hecho pasado por un detalle del
    /// transporte: el mismo comprador, con el token ya vencido, degradaría a <c>CmsSession</c> una
    /// canasta que sí se abrió con identidad verificada.
    /// </remarks>
    [Fact]
    public void Un_reintento_NO_reescribe_con_que_se_afirmo()
    {
        var (svc, _) = Nuevo();

        var primera = svc.Open(Duenio, null, IdempotencyKey.Of("misma"), IdentityAssertion.IdentityToken);
        var reintento = svc.Open(Duenio, null, IdempotencyKey.Of("misma"), IdentityAssertion.CmsSession);

        Assert.Equal(primera.Value!.Id, reintento.Value!.Id);
        Assert.Equal(IdentityAssertion.IdentityToken, reintento.Value.OpenedWith);
    }

    // ── empty ───────────────────────────────────────────────────────────────

    /// <summary>Una canasta de antes de esta rebanada dice «no consta», y se queda así.</summary>
    [Fact]
    public void Las_canastas_anteriores_no_mienten_sobre_si_mismas()
    {
        var raiz = Path.Combine(Path.GetTempPath(), "cart-id-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(raiz);

        // Escrito como lo escribía la versión anterior: sin el campo.
        File.WriteAllText(Path.Combine(raiz, "carts.json"),
            """
            [{"id":"vieja","owner":{"kind":"tienda.comprador","id":"m-9"},"lines":[],
              "createdAtUtc":"2026-01-01T00:00:00+00:00","expiresAtUtc":"2036-01-01T00:00:00+00:00",
              "checkedOut":false}]
            """);

        var store = new FileSystemCartStore(Options.Create(new CartStorageOptions { Root = raiz }));

        Assert.Null(store.Find("vieja")!.OpenedWith);
    }
}

using Synergos.Bff.Eventos.Domain;
using Synergos.Core;

namespace Synergos.CMS.Tests.Bff;

/// <summary>
/// El tope de códigos de promoción que no sirven, por comprador y ventana (ADR 0140, el cupón
/// endurecido): lo que corta el oráculo de «cotizar» y de «abrir» con código.
/// </summary>
public sealed class IntentosDeCodigoTests
{
    private sealed class RelojFalso(DateTimeOffset inicio) : TimeProvider
    {
        private DateTimeOffset _ahora = inicio;
        public override DateTimeOffset GetUtcNow() => _ahora;
        public void Avanzar(TimeSpan cuanto) => _ahora += cuanto;
    }

    private static readonly Ref A = new("eventos.comprador", "miembro-a");
    private static readonly Ref B = new("eventos.comprador", "miembro-b");
    private static readonly Rejection NoExiste = Rejection.NotFound("pricing.promotion_not_found", "No existe.");
    private static readonly Rejection Vencido = Rejection.Expired("pricing.promotion_expired", "Venció.");

    [Fact]
    public void Sin_fallos_cualquiera_puede_probar_un_codigo()
    {
        var intentos = new IntentosDeCodigo(new RelojFalso(DateTimeOffset.UnixEpoch), 3, TimeSpan.FromMinutes(15));

        Assert.Null(intentos.Revisar(A));
    }

    [Fact]
    public void Al_agotar_el_tope_se_corta_con_un_conflicto_firme_y_solo_a_ese_comprador()
    {
        var intentos = new IntentosDeCodigo(new RelojFalso(DateTimeOffset.UnixEpoch), 3, TimeSpan.FromMinutes(15));
        intentos.Anotar(A, NoExiste);
        intentos.Anotar(A, Vencido);
        Assert.Null(intentos.Revisar(A));

        intentos.Anotar(A, NoExiste);

        var corte = intentos.Revisar(A);
        Assert.NotNull(corte);
        Assert.Equal(IntentosDeCodigo.Codigo, corte.Code);
        // Firme: la pieza del CMS repite en el acto todo transitorio con llave, y repetirlo da lo mismo.
        Assert.Equal(RejectionKind.Conflict, corte.Kind);
        Assert.False(corte.IsTransient);
        Assert.Null(intentos.Revisar(B));
    }

    [Fact]
    public void Lo_que_no_es_un_rechazo_del_codigo_no_cuenta()
    {
        // Un código que vale (sin rechazo) y un «no» que no es del código —las entradas fuera de venta,
        // Pricing caído— no son enumerar códigos.
        var intentos = new IntentosDeCodigo(new RelojFalso(DateTimeOffset.UnixEpoch), 2, TimeSpan.FromMinutes(15));
        for (var i = 0; i < 5; i++)
        {
            intentos.Anotar(A, null);
            intentos.Anotar(A, Rejection.Conflict("pricing.price_not_in_effect", "Fuera de venta."));
            intentos.Anotar(A, Rejection.Unavailable("pricing.unavailable", "Caído."));
        }

        Assert.Null(intentos.Revisar(A));
    }

    [Fact]
    public void Pasada_la_ventana_los_fallos_viejos_se_olvidan()
    {
        var reloj = new RelojFalso(DateTimeOffset.UnixEpoch);
        var intentos = new IntentosDeCodigo(reloj, 2, TimeSpan.FromMinutes(15));
        intentos.Anotar(A, NoExiste);
        reloj.Avanzar(TimeSpan.FromMinutes(10));
        intentos.Anotar(A, NoExiste);
        Assert.NotNull(intentos.Revisar(A));

        // El primero sale de la ventana: queda uno, y uno no agota un tope de dos.
        reloj.Avanzar(TimeSpan.FromMinutes(6));

        Assert.Null(intentos.Revisar(A));
    }

    [Fact]
    public void Un_tope_o_una_ventana_que_no_son_positivos_no_arrancan()
    {
        var reloj = new RelojFalso(DateTimeOffset.UnixEpoch);

        Assert.Throws<ArgumentOutOfRangeException>(() => new IntentosDeCodigo(reloj, 0, TimeSpan.FromMinutes(1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new IntentosDeCodigo(reloj, 1, TimeSpan.Zero));
    }
}

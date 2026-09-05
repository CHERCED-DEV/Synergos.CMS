using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// El registro del acto notificado (HU #62) — los cuatro casos canónicos más lo que sostiene
/// el término.
/// </summary>
/// <remarks>
/// Lo que se prueba acá no es que se envíe: es que <b>el acceso quede registrado una sola vez y
/// sólo por su dueño</b>. Un correo enviado prueba que salió del servidor; lo que la entidad tiene
/// que poder sostener el día que alguien recurre tarde es cuándo accedió y cómo se supo que era él.
/// </remarks>
public sealed class StubGovActNotificationServiceTests
{
    private static readonly Guid Ciudadano = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Otro = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static (StubGovActNotificationService Svc, Reloj Reloj) Nuevo()
    {
        var reloj = new Reloj(DateTimeOffset.Parse("2026-03-01T10:00:00Z"));
        return (new StubGovActNotificationService(new InMemoryJsonEntityStore(), () => reloj.Ahora), reloj);
    }

    private sealed class Reloj(DateTimeOffset inicio)
    {
        public DateTimeOffset Ahora { get; private set; } = inicio;

        public void Avanzar(TimeSpan cuanto) => Ahora += cuanto;
    }

    private static Task<GovActNotification> NotificarAsync(
        IGovActNotificationService svc, string titulo = "Resolución 1234 de 2026",
        DateTimeOffset? plazo = null)
        => svc.NotifyAsync("case-1", "SG-2026-000001", Ciudadano, titulo,
            "Se resuelve NEGAR la solicitud.", "doc-9", plazo);

    // ── empty ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Un_expediente_sin_notificaciones_devuelve_vacio()
    {
        var (svc, _) = Nuevo();

        Assert.Empty(await svc.GetForCaseAsync("case-1"));
        Assert.Empty(await svc.GetForCaseAsync(string.Empty));
        Assert.Empty(await svc.GetForCitizenAsync(Ciudadano));
        Assert.Empty(await svc.GetForCitizenAsync(Guid.Empty));
    }

    // ── happy ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Notificar_deja_el_acto_a_disposicion_sin_abrir()
    {
        var (svc, _) = Nuevo();

        var n = await NotificarAsync(svc);

        Assert.False(n.Opened);
        Assert.Null(n.OpenedAtUtc);
        Assert.Null(n.OpenedWith);
        Assert.Equal("SG-2026-000001", n.Radicado);
        Assert.Equal("doc-9", n.DocumentRef);
    }

    [Fact]
    public async Task Abrir_registra_cuando_y_con_que()
    {
        var (svc, reloj) = Nuevo();
        var n = await NotificarAsync(svc);
        reloj.Avanzar(TimeSpan.FromHours(30));

        var abierta = await svc.AcknowledgeAsync(n.Id, Ciudadano);

        Assert.True(abierta.Opened);
        Assert.Equal(reloj.Ahora, abierta.OpenedAtUtc);
        Assert.Equal(Ciudadano, abierta.OpenedBy);
        // Sin identidad verificable, esto es lo más fuerte que se puede afirmar — y es honesto.
        Assert.Equal(IdentityAssertions.CmsSession, abierta.OpenedWith);
    }

    // ── filter ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Cada_bandeja_trae_lo_suyo()
    {
        var (svc, _) = Nuevo();
        await NotificarAsync(svc, "Resolución 1");
        await svc.NotifyAsync("case-2", "SG-2026-000002", Otro, "Resolución 2", "cuerpo");

        Assert.Single(await svc.GetForCaseAsync("case-1"));
        Assert.Single(await svc.GetForCitizenAsync(Ciudadano));
        Assert.Single(await svc.GetForCitizenAsync(Otro));
        Assert.Empty(await svc.GetForCaseAsync("case-3"));
    }

    [Fact] // la más reciente primero: la bandeja se lee de arriba
    public async Task La_bandeja_viene_de_la_mas_reciente_a_la_mas_vieja()
    {
        var (svc, reloj) = Nuevo();
        await NotificarAsync(svc, "Primera");
        reloj.Avanzar(TimeSpan.FromDays(1));
        await NotificarAsync(svc, "Segunda");

        var bandeja = await svc.GetForCitizenAsync(Ciudadano);

        Assert.Equal(new[] { "Segunda", "Primera" }, bandeja.Select(n => n.Title));
    }

    // ── idempotent ──────────────────────────────────────────────────────────

    /// <summary>
    /// Un acto se notifica UNA vez.
    /// </summary>
    /// <remarks>
    /// Abrir un segundo le daría al ciudadano dos plazos para el mismo acto — y al que recurre
    /// tarde, un argumento.
    /// </remarks>
    [Fact]
    public async Task Re_notificar_el_mismo_acto_devuelve_el_que_ya_esta()
    {
        var (svc, reloj) = Nuevo();
        var primera = await NotificarAsync(svc);
        reloj.Avanzar(TimeSpan.FromDays(2));

        var segunda = await NotificarAsync(svc);

        Assert.Equal(primera.Id, segunda.Id);
        Assert.Equal(primera.NotifiedAtUtc, segunda.NotifiedAtUtc);
        Assert.Single(await svc.GetForCaseAsync("case-1"));
    }

    /// <summary>
    /// El PRIMER acceso es el que cuenta.
    /// </summary>
    /// <remarks>
    /// Si el segundo pisara la fecha, el término se correría solo cada vez que el ciudadano vuelve
    /// a mirar su expediente — y correría a su favor, que es lo contrario de lo que pretende.
    /// </remarks>
    [Fact]
    public async Task Abrir_dos_veces_no_mueve_la_fecha()
    {
        var (svc, reloj) = Nuevo();
        var n = await NotificarAsync(svc);
        var primera = await svc.AcknowledgeAsync(n.Id, Ciudadano);

        reloj.Avanzar(TimeSpan.FromDays(10));
        var segunda = await svc.AcknowledgeAsync(n.Id, Ciudadano);

        Assert.Equal(primera.OpenedAtUtc, segunda.OpenedAtUtc);
    }

    // ── lo que sostiene el término ──────────────────────────────────────────

    [Fact]
    public async Task Otro_ciudadano_no_puede_abrirla()
    {
        var (svc, _) = Nuevo();
        var n = await NotificarAsync(svc);

        // El TIPO importa: el borde lo traduce a 403, y a «se pasó el plazo» le corresponde un
        // 409. Con el InvalidOperationException genérico, las dos serían la misma respuesta.
        await Assert.ThrowsAsync<GovActNotAddresseeException>(() => svc.AcknowledgeAsync(n.Id, Otro));

        // Y sigue sin abrir: el intento ajeno no arranca el término de nadie.
        var recargada = Assert.Single(await svc.GetForCaseAsync("case-1"));
        Assert.False(recargada.Opened);
    }

    /// <summary>Pasado el plazo, el acceso NO se registra y se dice.</summary>
    /// <remarks>
    /// Registrarlo igual escribiría un término que empezó fuera de lo que la entidad admite;
    /// devolver la notificación como si nada dejaría al ciudadano leyendo un acto que para el
    /// sistema nadie abrió.
    /// </remarks>
    [Fact]
    public async Task Fuera_de_plazo_no_se_registra_el_acceso()
    {
        var (svc, reloj) = Nuevo();
        var n = await NotificarAsync(svc, plazo: reloj.Ahora.AddDays(5));
        reloj.Avanzar(TimeSpan.FromDays(6));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.AcknowledgeAsync(n.Id, Ciudadano));
        Assert.IsNotType<GovActNotAddresseeException>(ex);

        var recargada = Assert.Single(await svc.GetForCaseAsync("case-1"));
        Assert.False(recargada.Opened);
    }

    [Fact] // una notificación que no existe no se abre en silencio
    public async Task Una_notificacion_inexistente_no_se_abre()
    {
        var (svc, _) = Nuevo();

        await Assert.ThrowsAsync<ArgumentException>(() => svc.AcknowledgeAsync("not_nada", Ciudadano));
        await Assert.ThrowsAsync<ArgumentException>(() => svc.AcknowledgeAsync(string.Empty, Ciudadano));
    }

    /// <summary>
    /// El registro SOBREVIVE al reinicio.
    /// </summary>
    /// <remarks>
    /// Es lo único para lo que existe: sostener que un término empezó. Un registro en memoria
    /// diría, tras un reinicio, que nadie abrió nunca nada.
    /// </remarks>
    [Fact]
    public async Task Lo_registrado_sobrevive_al_reinicio()
    {
        var store = new InMemoryJsonEntityStore();
        var antes = new StubGovActNotificationService(store);
        var n = await NotificarAsync(antes);
        await antes.AcknowledgeAsync(n.Id, Ciudadano);

        var despues = new StubGovActNotificationService(store);
        var recargada = Assert.Single(await despues.GetForCitizenAsync(Ciudadano));

        Assert.True(recargada.Opened);
        Assert.Equal(IdentityAssertions.CmsSession, recargada.OpenedWith);
    }

    [Fact] // lo que no se puede notificar se rechaza de frente, no a medias
    public async Task Notificar_sin_expediente_titulo_o_ciudadano_se_rechaza()
    {
        var (svc, _) = Nuevo();

        await Assert.ThrowsAsync<ArgumentException>(
            () => svc.NotifyAsync(" ", "SG-1", Ciudadano, "Título", "cuerpo"));
        await Assert.ThrowsAsync<ArgumentException>(
            () => svc.NotifyAsync("case-1", "SG-1", Ciudadano, " ", "cuerpo"));
        await Assert.ThrowsAsync<ArgumentException>(
            () => svc.NotifyAsync("case-1", "SG-1", Guid.Empty, "Título", "cuerpo"));
    }
}

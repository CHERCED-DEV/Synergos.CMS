using Microsoft.Extensions.Options;
using Synergos.Api.Messaging.Domain;
using Synergos.Api.Messaging.Storage;
using Synergos.Core;
using Synergos.Shared;

namespace Synergos.CMS.Tests.Api;

/// <summary>
/// El acuse de ACCESO — que la persona abrió, no que el mensaje salió (HU #13).
/// </summary>
/// <remarks>
/// <para><b>Estos tests existen porque no había ninguno.</b> Al refinar la HU se descubrió que
/// <c>MarkRead</c> —el germen del acuse— no tenía cobertura: cero menciones en los 30 tests de
/// <c>RestantesRulesTests</c>. El cambio de forma de <c>ReadBy</c> a <c>Acknowledgments</c>
/// compiló sin romper un solo test, que es exactamente la señal de que nadie lo vigilaba.</para>
///
/// <para><b>Lo que se prueba acá es lo que decide si un acuse vale.</b> No «se marcó como
/// leído», sino: que quede el instante, que el <b>primero</b> sea el que cuenta, y que el
/// registro diga con qué fuerza se afirmó la identidad.</para>
/// </remarks>
public sealed class MessagingAcknowledgmentTests
{
    private sealed class RelojFalso : TimeProvider
    {
        private DateTimeOffset _now;
        public RelojFalso(DateTimeOffset inicio) => _now = inicio;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Avanzar(TimeSpan d) => _now += d;
    }

    private static readonly DateTimeOffset Ahora = new(2026, 8, 3, 9, 0, 0, TimeSpan.Zero);
    private static readonly Ref Funcionario = Ref.Create("gov.funcionario", "f-1");
    private static readonly Ref Ciudadano = Ref.Create("gov.ciudadano", "c-1");
    private static readonly Ref Ajeno = Ref.Create("gov.ciudadano", "c-99");
    private static readonly Ref Expediente = Ref.Create("gov.tramite", "t-1");

    private sealed record Contexto(MessagingService Svc, RelojFalso Reloj, string ThreadId, string MessageId)
    {
        /// <summary>El mensaje tal como está guardado — no hay un Get de un mensaje suelto.</summary>
        public Message Leer() => Svc.ListMessages(ThreadId, Funcionario, 0, 10).Value.Items.Single();
    }

    private static Contexto Nuevo(DateTimeOffset? plazo = null)
    {
        var raiz = Path.Combine(Path.GetTempPath(), "msg-ack-" + Guid.NewGuid().ToString("n"));
        var opciones = Options.Create(new MessagingStorageOptions { Root = raiz });
        var reloj = new RelojFalso(Ahora);
        var svc = new MessagingService(
            new FileSystemThreadStore(opciones), new FileSystemMessageStore(opciones),
            new FileIdempotencyLedger(raiz), reloj);

        var hilo = svc.OpenThread(Expediente, new[] { Funcionario, Ciudadano }, IdempotencyKey.Of("h1"));
        Assert.True(hilo.IsOk);
        var msg = svc.Post(hilo.Value.Id, Funcionario, "Su trámite fue resuelto.",
            Array.Empty<Ref>(), IdempotencyKey.Of("m1"), plazo);
        Assert.True(msg.IsOk);

        return new Contexto(svc, reloj, hilo.Value.Id, msg.Value.Id);
    }

    // ── Lo que la forma vieja no podía hacer ────────────────────────────────

    [Fact]
    public void El_acuse_guarda_CUANDO_accedio_y_no_cuando_se_envio()
    {
        // Es el defecto que esta HU existe para arreglar. ReadBy era una lista de QUIÉN sin
        // CUÁNDO, y el único instante del mensaje era el del envío. Con esa forma es imposible
        // certificar fecha y hora de acceso — que es de lo que depende que un término legal
        // haya empezado a correr.
        var ctx = Nuevo();
        ctx.Reloj.Avanzar(TimeSpan.FromDays(3));

        var r = ctx.Svc.Acknowledge(ctx.MessageId, Ciudadano, IdentityAssertion.CmsSession);

        Assert.True(r.IsOk);
        var acuse = r.Value.AcknowledgmentOf(Ciudadano);
        Assert.NotNull(acuse);
        Assert.Equal(Ahora.AddDays(3), acuse!.AtUtc);
        Assert.NotEqual(r.Value.AtUtc, acuse.AtUtc);   // el envío fue tres días antes
    }

    [Fact]
    public void El_acuse_guarda_COMO_se_afirmo_la_identidad()
    {
        // El campo que hace reversible la decisión de quién certifica: sin él, el día que se
        // pase a una afirmación más fuerte, los registros viejos quedarían indistinguibles de
        // los nuevos y todo el archivo afirmaría más de lo que puede sostener.
        var ctx = Nuevo();

        var r = ctx.Svc.Acknowledge(ctx.MessageId, Ciudadano, IdentityAssertion.CmsSession);

        Assert.Equal(IdentityAssertion.CmsSession, r.Value.AcknowledgmentOf(Ciudadano)!.Assertion);
    }

    // ── El primero es el que cuenta ─────────────────────────────────────────

    [Fact]
    public void Un_segundo_acceso_NO_sobreescribe_el_instante_del_primero()
    {
        // Es la garantía entera del acuse. La persona puede abrir el mismo acto diez veces;
        // el término empezó a correr con el primero. Sobreescribir lo correría hacia adelante
        // cada vez que alguien vuelve a mirar — justo lo contrario de lo que hay que garantizar.
        var ctx = Nuevo();
        ctx.Svc.Acknowledge(ctx.MessageId, Ciudadano, IdentityAssertion.CmsSession);
        var primero = Ahora;

        ctx.Reloj.Avanzar(TimeSpan.FromDays(30));
        var r = ctx.Svc.Acknowledge(ctx.MessageId, Ciudadano, IdentityAssertion.CmsSession);

        Assert.True(r.IsOk);                                   // no es un error volver a abrir
        Assert.Equal(primero, r.Value.AcknowledgmentOf(Ciudadano)!.AtUtc);
        Assert.Single(r.Value.Acknowledgments, a => a.Who == Ciudadano);
    }

    [Fact]
    public void Un_segundo_acceso_tampoco_MEJORA_la_afirmacion_de_identidad()
    {
        // Tentador y equivocado: si el primer acceso se certificó con la sesión del CMS, ese
        // acuse vale lo que valía. Dejar que un acceso posterior con token "ascienda" el
        // registro haría que el archivo dijera que se probó algo que no se probó ese día.
        var ctx = Nuevo();
        ctx.Svc.Acknowledge(ctx.MessageId, Ciudadano, IdentityAssertion.CmsSession);

        ctx.Reloj.Avanzar(TimeSpan.FromHours(1));
        var r = ctx.Svc.Acknowledge(ctx.MessageId, Ciudadano, IdentityAssertion.GovFederation);

        Assert.Equal(IdentityAssertion.CmsSession, r.Value.AcknowledgmentOf(Ciudadano)!.Assertion);
    }

    // ── Lo que rechaza ──────────────────────────────────────────────────────

    [Fact]
    public void Sin_decir_COMO_se_afirmo_la_identidad_no_se_registra_nada()
    {
        // Un acuse dice que ESTA persona accedió. Sin decir quién lo certifica, el sistema
        // estaría dando fe de lo que el propio llamador le contó.
        var ctx = Nuevo();

        var r = ctx.Svc.Acknowledge(ctx.MessageId, Ciudadano, null);

        Assert.False(r.IsOk);
        Assert.Equal("messaging.access_requires_identity", r.Rejection!.Code);
    }

    [Fact]
    public void La_identidad_se_exige_ANTES_de_mirar_si_es_participante()
    {
        // El orden filtra información: quien no dice cómo se identificó no debería enterarse de
        // si el hilo existe ni de si él está adentro. Es el mismo razonamiento que ya estaba
        // escrito en CheckPost — pertenencia antes que cierre.
        var ctx = Nuevo();

        var r = ctx.Svc.Acknowledge(ctx.MessageId, Ajeno, null);

        Assert.Equal("messaging.access_requires_identity", r.Rejection!.Code);
        Assert.NotEqual("messaging.not_a_participant", r.Rejection.Code);
    }

    [Fact]
    public void Quien_no_esta_en_el_hilo_no_puede_dejar_acuse()
    {
        var ctx = Nuevo();

        var r = ctx.Svc.Acknowledge(ctx.MessageId, Ajeno, IdentityAssertion.CmsSession);

        Assert.False(r.IsOk);
        Assert.Equal("messaging.not_a_participant", r.Rejection!.Code);
        Assert.Equal(RejectionKind.Forbidden, r.Rejection.Kind);
    }

    [Fact]
    public void Un_mensaje_que_no_existe_no_se_confunde_con_uno_ajeno()
    {
        var ctx = Nuevo();

        var r = ctx.Svc.Acknowledge("no-existe", Ciudadano, IdentityAssertion.CmsSession);

        Assert.Equal("messaging.message_not_found", r.Rejection!.Code);
    }

    // ── El plazo ────────────────────────────────────────────────────────────

    [Fact]
    public void Dentro_del_plazo_el_acceso_se_registra()
    {
        var ctx = Nuevo(plazo: Ahora.AddDays(10));
        ctx.Reloj.Avanzar(TimeSpan.FromDays(9));

        var r = ctx.Svc.Acknowledge(ctx.MessageId, Ciudadano, IdentityAssertion.CmsSession);

        Assert.True(r.IsOk);
        Assert.Equal(Ahora.AddDays(9), r.Value.AcknowledgmentOf(Ciudadano)!.AtUtc);
    }

    [Fact]
    public void Pasado_el_plazo_ya_no_se_certifica()
    {
        // Vencido el término, el canal electrónico deja de servir para notificar: la
        // administración pasa a otra forma. Registrar un acuse acá haría creer que el término
        // empezó a correr cuando no fue así.
        var ctx = Nuevo(plazo: Ahora.AddDays(10));
        ctx.Reloj.Avanzar(TimeSpan.FromDays(11));

        var r = ctx.Svc.Acknowledge(ctx.MessageId, Ciudadano, IdentityAssertion.CmsSession);

        Assert.False(r.IsOk);
        Assert.Equal("messaging.acknowledgment_window_closed", r.Rejection!.Code);
        Assert.Equal(RejectionKind.Conflict, r.Rejection.Kind);
    }

    [Fact]
    public void Quien_acuso_a_tiempo_sigue_recibiendo_SU_acuse_despues_del_cierre()
    {
        // El orden de las guardas, hecho test. Si el plazo se mirara antes de resolver que ya
        // había acuse, el sistema le negaría a la persona el registro que ella misma generó a
        // tiempo — y por una regla que ella ya había cumplido (CLAUDE.md §0.B, punto 16).
        var ctx = Nuevo(plazo: Ahora.AddDays(10));
        ctx.Reloj.Avanzar(TimeSpan.FromDays(2));
        ctx.Svc.Acknowledge(ctx.MessageId, Ciudadano, IdentityAssertion.CmsSession);

        ctx.Reloj.Avanzar(TimeSpan.FromDays(90));
        var r = ctx.Svc.Acknowledge(ctx.MessageId, Ciudadano, IdentityAssertion.CmsSession);

        Assert.True(r.IsOk);
        Assert.Equal(Ahora.AddDays(2), r.Value.AcknowledgmentOf(Ciudadano)!.AtUtc);
    }

    [Fact]
    public void Sin_plazo_no_se_cierra_nunca()
    {
        // El plazo es de Gobierno. Un DM social no tiene término que corra, y obligar a uno
        // haría inservible la capacidad para los otros cinco dominios que la usan.
        var ctx = Nuevo();
        ctx.Reloj.Avanzar(TimeSpan.FromDays(3650));

        var r = ctx.Svc.Acknowledge(ctx.MessageId, Ciudadano, IdentityAssertion.CmsSession);

        Assert.True(r.IsOk);
    }

    // ── El autor ────────────────────────────────────────────────────────────

    [Fact]
    public void Quien_escribe_queda_con_acuse_propio_desde_el_primer_momento()
    {
        // Si no, su propio mensaje le aparecería sin leer y el contador de pendientes nunca
        // llegaría a cero.
        //
        // Y se anota con `CmsSession`, NO con la afirmación más fuerte. Esta línea decía lo
        // contrario y era el defecto #42: el razonamiento —«no hay duda de quién escribió»—
        // mide confianza, y el campo mide quién DA FE. `Api.Identity` no certificó nada acá;
        // ni siquiera sabe emitir tokens todavía.
        var ctx = Nuevo();
        var msg = ctx.Leer();

        var suyo = msg.AcknowledgmentOf(Funcionario);

        Assert.NotNull(suyo);
        Assert.Equal(Ahora, suyo!.AtUtc);
        Assert.Equal(IdentityAssertion.CmsSession, suyo.Assertion);
        Assert.Null(msg.AcknowledgmentOf(Ciudadano));   // el destinatario todavía no accedió
    }
}

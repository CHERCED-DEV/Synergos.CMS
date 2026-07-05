using System;
using System.Linq;
using System.Threading.Tasks;
using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces;
using Xunit;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// Cubre <see cref="StubMessagingService"/> (seam GENÉRICO
/// <see cref="IMessagingService"/>, plan doc 21 §1.4 P7 v1 — hilos 1:1
/// comprador↔vendedor sin typing/grupos): los 4 casos canónicos (ADR 0075) —
/// empty / happy / filter / idempotent — más el ThreadId determinista.
/// </summary>
public class StubMessagingServiceTests
{
    private const string Buyer = "camila@synergos.co";
    private const string Seller = "vendedor@synergos.co";
    private const string Context = "ord_abc123";

    private static IMessagingService Make(Func<DateTimeOffset>? now = null)
        => new StubMessagingService(now);

    [Fact] // empty: bandeja sin hilos (o participante vacío) → lista vacía; hilo desconocido → null
    public async Task InboxAndThread_Unknown_ReturnEmptyOrNull()
    {
        var svc = Make();
        Assert.Empty(await svc.GetInboxAsync(Buyer));
        Assert.Empty(await svc.GetInboxAsync(""));
        Assert.Null(await svc.GetThreadAsync("thr_desconocido"));
        Assert.Null(await svc.GetThreadAsync(""));
    }

    [Fact] // inválido: datos faltantes / from==to / responder hilo inexistente o ajeno lanzan
    public async Task InvalidInputs_Throw()
    {
        var svc = Make();

        await Assert.ThrowsAsync<ArgumentException>(() => svc.StartThreadAsync("", Buyer, Seller, "Hola"));
        await Assert.ThrowsAsync<ArgumentException>(() => svc.StartThreadAsync(Context, "", Seller, "Hola"));
        await Assert.ThrowsAsync<ArgumentException>(() => svc.StartThreadAsync(Context, Buyer, Seller, " "));
        // 1:1 — el remitente no puede escribirse a sí mismo.
        await Assert.ThrowsAsync<ArgumentException>(() => svc.StartThreadAsync(Context, Buyer, Buyer, "Hola"));

        // Responder un hilo que no existe.
        await Assert.ThrowsAsync<ArgumentException>(() => svc.ReplyAsync("thr_fantasma", Buyer, "Hola"));

        // Responder sin ser participante.
        var thread = await svc.StartThreadAsync(Context, Buyer, Seller, "¿Tiene garantía?");
        await Assert.ThrowsAsync<ArgumentException>(() => svc.ReplyAsync(thread.ThreadId, "intruso@synergos.co", "Yo opino"));
    }

    [Fact] // happy: iniciar + responder → hilo con mensajes en orden y bandeja para ambos
    public async Task StartAndReply_BuildThreadAndInbox()
    {
        var clock = new DateTimeOffset(2026, 7, 1, 15, 0, 0, TimeSpan.Zero);
        var svc = Make(() => clock);

        var started = await svc.StartThreadAsync(Context, Buyer, Seller, "¿El producto tiene garantía?");
        clock = clock.AddMinutes(3);
        var replied = await svc.ReplyAsync(started.ThreadId, Seller, "Sí, 12 meses con el fabricante.");

        Assert.Equal(Context, replied.ContextRef);
        Assert.Equal(2, replied.Messages.Count);
        Assert.Equal(Buyer, replied.Messages[0].From);
        Assert.Equal(Seller, replied.Messages[1].From);
        Assert.Equal(replied.Messages[1].SentAt, replied.LastMessageAt);
        Assert.Contains(Buyer, replied.Participants);
        Assert.Contains(Seller, replied.Participants);

        // Ambos participantes ven el hilo en su bandeja con el preview del último mensaje.
        var buyerInbox = Assert.Single(await svc.GetInboxAsync(Buyer));
        Assert.Equal(2, buyerInbox.MessageCount);
        Assert.Equal("Sí, 12 meses con el fabricante.", buyerInbox.LastMessagePreview);
        Assert.Single(await svc.GetInboxAsync(Seller));
    }

    [Fact] // filter: la bandeja solo trae los hilos donde participo, más reciente primero
    public async Task Inbox_IsScopedToParticipant_NewestFirst()
    {
        var clock = new DateTimeOffset(2026, 7, 1, 15, 0, 0, TimeSpan.Zero);
        var svc = Make(() => clock);

        var first = await svc.StartThreadAsync("ord_1", Buyer, Seller, "Hola por la orden 1");
        clock = clock.AddMinutes(10);
        var second = await svc.StartThreadAsync("ord_2", Buyer, "otro-vendedor@synergos.co", "Hola por la orden 2");

        var inbox = await svc.GetInboxAsync(Buyer);
        Assert.Equal(2, inbox.Count);
        Assert.Equal(second.ThreadId, inbox[0].ThreadId); // más reciente primero
        Assert.Equal(first.ThreadId, inbox[1].ThreadId);

        // El primer vendedor solo ve SU hilo.
        var sellerInbox = Assert.Single(await svc.GetInboxAsync(Seller));
        Assert.Equal(first.ThreadId, sellerInbox.ThreadId);
        // Un tercero no ve nada.
        Assert.Empty(await svc.GetInboxAsync("intruso@synergos.co"));
    }

    [Fact] // idempotent: re-iniciar el mismo (contexto + par) retoma el MISMO hilo (id determinista)
    public async Task Start_SameContextAndPair_ReusesThread()
    {
        var svc = Make();

        var first = await svc.StartThreadAsync(Context, Buyer, Seller, "Primer mensaje");
        // Mismo contexto, participantes invertidos y con otra capitalización → mismo hilo.
        var second = await svc.StartThreadAsync(Context, Seller.ToUpperInvariant(), Buyer, "Segundo mensaje");

        Assert.Equal(first.ThreadId, second.ThreadId);
        Assert.Equal(2, second.Messages.Count);
        // Un solo hilo en la bandeja (no se duplicó).
        Assert.Single(await svc.GetInboxAsync(Buyer));

        // Otro contexto → hilo distinto (el contexto discrimina).
        var other = await svc.StartThreadAsync("ord_zzz", Buyer, Seller, "Nueva orden, nuevo hilo");
        Assert.NotEqual(first.ThreadId, other.ThreadId);
    }
}

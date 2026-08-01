using System.Threading.Tasks;
using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces;
using Xunit;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// Cubre <see cref="StubReactionService"/> (seam <see cref="IReactionService"/> —
/// reacciones/likes del dominio Blogs): los 4 casos canónicos (ADR 0075) — empty /
/// happy / filter / idempotent — más el toggle, el reemplazo de tipo y el
/// estado-por-usuario.
/// </summary>
public class StubReactionServiceTests
{
    private static StubReactionService Make() => new();

    [Fact] // empty: objeto sin reacciones → total 0, sin reacción del actor
    public async Task GetState_NoReactions_IsEmpty()
    {
        var state = await Make().GetStateAsync("obj-sin-reacciones", "act-x");

        Assert.Equal(0, state.Total);
        Assert.Empty(state.CountsByType);
        Assert.Null(state.MyReaction);
    }

    [Fact] // happy: reaccionar agrega la reacción y la cuenta
    public async Task React_AddsReaction()
    {
        var state = await Make().ReactAsync("act-x", "obj-1", "like");

        Assert.Equal(1, state.Total);
        Assert.Equal("like", state.MyReaction);
        Assert.Equal(1, state.CountsByType["like"]);
    }

    [Fact] // filter: el conteo se desglosa por tipo
    public async Task React_CountsByType_AreSegmented()
    {
        var svc = Make();
        await svc.ReactAsync("act-1", "obj-2", "like");
        await svc.ReactAsync("act-2", "obj-2", "love");
        var state = await svc.ReactAsync("act-3", "obj-2", "like");

        Assert.Equal(3, state.Total);
        Assert.Equal(2, state.CountsByType["like"]);
        Assert.Equal(1, state.CountsByType["love"]);
    }

    [Fact] // estado-por-usuario: cada actor ve su propia reacción
    public async Task GetState_MyReaction_IsPerActor()
    {
        var svc = Make();
        await svc.ReactAsync("act-1", "obj-3", "like");
        await svc.ReactAsync("act-2", "obj-3", "love");

        Assert.Equal("like", (await svc.GetStateAsync("obj-3", "act-1")).MyReaction);
        Assert.Equal("love", (await svc.GetStateAsync("obj-3", "act-2")).MyReaction);
        Assert.Null((await svc.GetStateAsync("obj-3", "act-3")).MyReaction);
    }

    [Fact] // idempotent (toggle): re-reaccionar con el mismo tipo retira la reacción
    public async Task React_SameTypeTwice_TogglesOff()
    {
        var svc = Make();
        await svc.ReactAsync("act-x", "obj-4", "like");
        var state = await svc.ReactAsync("act-x", "obj-4", "like");

        Assert.Equal(0, state.Total);
        Assert.Null(state.MyReaction);
    }

    [Fact] // toggle: reaccionar con otro tipo reemplaza (un actor = una reacción)
    public async Task React_DifferentType_Replaces()
    {
        var svc = Make();
        await svc.ReactAsync("act-x", "obj-5", "like");
        var state = await svc.ReactAsync("act-x", "obj-5", "love");

        Assert.Equal(1, state.Total); // sigue siendo una sola reacción del actor
        Assert.Equal("love", state.MyReaction);
        Assert.False(state.CountsByType.ContainsKey("like"));
    }

    [Fact] // idempotent: unreact de quien no reaccionó no lanza
    public async Task Unreact_NotReacted_IsNoOp()
    {
        var state = await Make().UnreactAsync("act-x", "obj-6");

        Assert.Equal(0, state.Total);
        Assert.Null(state.MyReaction);
    }

    [Fact] // seed: los objetos sembrados arrancan con reacciones
    public async Task GetState_SeededObject_HasReactions()
    {
        var state = await Make().GetStateAsync("post-001", "act-mateo");

        Assert.True(state.Total >= 1);
        Assert.Equal("like", state.MyReaction); // act-mateo reaccionó "like" a post-001 en el seed
    }

    [Fact] // CountFor: total directo para las métricas del feed
    public async Task CountFor_ReturnsTotal()
    {
        var svc = Make();
        await svc.ReactAsync("act-1", "obj-7", "like");
        await svc.ReactAsync("act-2", "obj-7", "love");

        Assert.Equal(2, await svc.CountForAsync("obj-7"));
        Assert.Equal(0, await svc.CountForAsync("obj-vacio"));
    }
}

using System.Linq;
using System.Threading.Tasks;
using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces;
using Xunit;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// Cubre <see cref="StubSocialGraphService"/> (seam <see cref="ISocialGraphService"/>
/// — grafo social del dominio Blogs): los 4 casos canónicos (ADR 0075) — empty /
/// happy / filter / idempotent — más asimetría y no-auto-seguirse.
/// </summary>
public class StubSocialGraphServiceTests
{
    private static StubSocialGraphService Make() => new();

    [Fact] // empty: un actor sin grafo no sigue a nadie ni tiene seguidores
    public async Task Counts_UnknownActor_AreZero()
    {
        var counts = await Make().GetCountsAsync("act-nadie");

        Assert.Equal(0, counts.Followers);
        Assert.Equal(0, counts.Following);
        Assert.Empty(await Make().GetFollowingAsync("act-nadie"));
        Assert.Empty(await Make().GetFollowersAsync("act-nadie"));
    }

    [Fact] // happy: seguir crea la relación y sube el conteo del followee
    public async Task Follow_CreatesRelationAndIncrementsCount()
    {
        var svc = Make();
        var before = (await svc.GetCountsAsync("act-mateo")).Followers;

        var state = await svc.FollowAsync("act-nuevo", "act-mateo");

        Assert.True(state.Following);
        Assert.Equal(before + 1, state.FolloweeFollowers);
        Assert.True(await svc.IsFollowingAsync("act-nuevo", "act-mateo"));
    }

    [Fact] // happy: unfollow retira la relación
    public async Task Unfollow_RemovesRelation()
    {
        var svc = Make();
        await svc.FollowAsync("act-a", "act-b");

        var state = await svc.UnfollowAsync("act-a", "act-b");

        Assert.False(state.Following);
        Assert.False(await svc.IsFollowingAsync("act-a", "act-b"));
    }

    [Fact] // filter: GetFollowing devuelve SOLO los followees del actor
    public async Task GetFollowing_ReturnsOnlyActorFollowees()
    {
        var svc = Make();
        await svc.FollowAsync("act-x", "act-y");
        await svc.FollowAsync("act-x", "act-z");
        await svc.FollowAsync("act-otro", "act-w"); // ruido de otro actor

        var following = await svc.GetFollowingAsync("act-x");

        Assert.Contains("act-y", following);
        Assert.Contains("act-z", following);
        Assert.DoesNotContain("act-w", following);
    }

    [Fact] // filter: GetFollowers es la vista inversa del grafo
    public async Task GetFollowers_IsInverseOfGraph()
    {
        var svc = Make();
        await svc.FollowAsync("act-1", "act-target");
        await svc.FollowAsync("act-2", "act-target");

        var followers = await svc.GetFollowersAsync("act-target");

        Assert.Contains("act-1", followers);
        Assert.Contains("act-2", followers);
    }

    [Fact] // idempotent: seguir dos veces no duplica ni cambia el conteo
    public async Task Follow_Twice_IsIdempotent()
    {
        var svc = Make();
        var first = await svc.FollowAsync("act-dup", "act-mateo");
        var second = await svc.FollowAsync("act-dup", "act-mateo");

        Assert.True(second.Following);
        Assert.Equal(first.FolloweeFollowers, second.FolloweeFollowers);
        var followers = await svc.GetFollowersAsync("act-mateo");
        Assert.Single(followers.Where(f => f == "act-dup"));
    }

    [Fact] // idempotent: unfollow de quien no se sigue no lanza
    public async Task Unfollow_NotFollowing_IsNoOp()
    {
        var svc = Make();
        var state = await svc.UnfollowAsync("act-a", "act-never");

        Assert.False(state.Following);
    }

    [Fact] // asimetría: A sigue a B no implica B sigue a A
    public async Task Follow_IsAsymmetric()
    {
        var svc = Make();
        await svc.FollowAsync("act-a", "act-b");

        Assert.True(await svc.IsFollowingAsync("act-a", "act-b"));
        Assert.False(await svc.IsFollowingAsync("act-b", "act-a"));
    }

    [Fact] // no auto-seguirse: seguir a sí mismo no crea relación
    public async Task Follow_Self_IsRejectedSilently()
    {
        var svc = Make();
        var state = await svc.FollowAsync("act-solo", "act-solo");

        Assert.False(state.Following);
        Assert.False(await svc.IsFollowingAsync("act-solo", "act-solo"));
    }
}

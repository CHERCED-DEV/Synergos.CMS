using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Synergos.CMS.Web.Services;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;
using Xunit;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// Cubre <see cref="DevMemberRoleSeeder"/> — tooling dev-only (ADR 0013) que crea los
/// member groups de dominio y los asigna. Existe porque cuatro olas de seguridad dejaron
/// consolas cerradas por rol que <b>no se podían demostrar</b> sin crear los grupos a mano.
/// Casos: empty (sin members solo crea grupos) / happy (asigna y devuelve el estado REAL)
/// / filter (con varios members NO elige por el arquitecto) / idempotent (segunda corrida
/// no duplica). ADR 0075.
/// </summary>
public sealed class DevMemberRoleSeederTests
{
    private readonly IMemberService _members = Substitute.For<IMemberService>();

    private DevMemberRoleSeeder Make() => new(_members, NullLogger<DevMemberRoleSeeder>.Instance);

    private static IMemberGroup Group(string name)
    {
        var g = Substitute.For<IMemberGroup>();
        g.Name.Returns(name);
        return g;
    }

    private static IMember Member(int id, string email)
    {
        var m = Substitute.For<IMember>();
        m.Id.Returns(id);
        m.Email.Returns(email);
        return m;
    }

    // NSubstitute: los substitutes se construyen ANTES, nunca dentro de otro Returns()
    // (CouldNotSetReturnDueToNoLastCallException). Lección ya anotada… y repetida.
    private void ExistingGroups(params string[] names)
    {
        var groups = names.Select(Group).ToArray();
        _members.GetAllRoles().Returns(groups);
    }

    [Fact] // empty: sin members, igual crea los grupos (que es media misión)
    public void Seed_NoMembers_StillCreatesGroups()
    {
        ExistingGroups();
        _members.GetAllMembers().Returns(Array.Empty<IMember>());

        var result = Make().Seed(null, null);

        Assert.Contains("funcionario", result.RolesCreated);
        Assert.Contains("organizador", result.RolesCreated);
        Assert.Contains("doctor", result.RolesCreated);
        Assert.Null(result.MemberEmail);
        Assert.Contains("register", result.Note ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact] // idempotent: los grupos que ya existen NO se recrean
    public void Seed_ExistingGroups_AreNotRecreated()
    {
        ExistingGroups("funcionario", "admin");
        _members.GetAllMembers().Returns(Array.Empty<IMember>());

        var result = Make().Seed(null, null);

        Assert.DoesNotContain("funcionario", result.RolesCreated);
        Assert.Contains("funcionario", result.RolesAlreadyPresent);
        _members.DidNotReceive().AddRole("funcionario");
        _members.Received().AddRole("organizador"); // ese sí faltaba
    }

    [Fact] // EL JUICIO QUE NO DELEGA: con varios members no elige a quién dar permisos
    public void Seed_ManyMembers_DoesNotChooseForYou()
    {
        ExistingGroups();
        var many = new[] { Member(1, "a@x.test"), Member(2, "b@x.test") };
        _members.GetAllMembers().Returns(many);

        var result = Make().Seed(null, null);

        Assert.Null(result.MemberEmail);
        Assert.Empty(result.RolesAssigned);
        _members.DidNotReceive().AssignRoles(Arg.Any<int[]>(), Arg.Any<string[]>());
        // …pero dice quiénes son, para que el arquitecto elija.
        Assert.Contains("a@x.test", result.Note ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact] // happy: con email explícito asigna y devuelve el estado FINAL leído del servicio
    public void Seed_WithEmail_AssignsAndReportsActualState()
    {
        ExistingGroups();
        var alice = Member(7, "alice@x.test");
        _members.GetByEmail("alice@x.test").Returns(alice);
        _members.GetAllRoles(7).Returns(Array.Empty<string>(), new[] { "funcionario", "organizador" });

        var result = Make().Seed("alice@x.test", new[] { "funcionario", "organizador" });

        _members.Received(1).AssignRoles(
            Arg.Is<int[]>(ids => ids.Single() == 7),
            Arg.Is<string[]>(r => r.Contains("funcionario") && r.Contains("organizador")));
        Assert.Equal("alice@x.test", result.MemberEmail);
        Assert.Equal(new[] { "funcionario", "organizador" }, result.RolesAssigned);
    }

    [Fact] // idempotent: un rol que el member YA tiene no se reasigna
    public void Seed_RoleAlreadyHeld_IsNotReassigned()
    {
        ExistingGroups("funcionario");
        var alice = Member(7, "alice@x.test");
        _members.GetByEmail("alice@x.test").Returns(alice);
        _members.GetAllRoles(7).Returns(new[] { "funcionario" });

        Make().Seed("alice@x.test", new[] { "funcionario" });

        _members.DidNotReceive().AssignRoles(Arg.Any<int[]>(), Arg.Any<string[]>());
    }

    [Fact] // filter: un rol inventado no se asigna — solo los del contrato con los guards
    public void Seed_UnknownRole_IsIgnored()
    {
        ExistingGroups();
        var alice = Member(7, "alice@x.test");
        _members.GetByEmail("alice@x.test").Returns(alice);
        _members.GetAllRoles(7).Returns(Array.Empty<string>());

        Make().Seed("alice@x.test", new[] { "superusuario-inventado" });

        _members.DidNotReceive().AssignRoles(Arg.Any<int[]>(), Arg.Any<string[]>());
    }

    [Fact] // email que no existe: lo dice claro y NO revienta (los grupos ya se crearon)
    public void Seed_UnknownEmail_ReportsWithoutThrowing()
    {
        ExistingGroups();
        _members.GetByEmail("nadie@x.test").Returns((IMember?)null);

        var result = Make().Seed("nadie@x.test", null);

        Assert.Null(result.MemberEmail);
        Assert.NotEmpty(result.RolesCreated);
        Assert.Contains("no existe", result.Note ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }
}

using NSubstitute;
using Synergos.CMS.Web.Services;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// Cubre <see cref="UmbracoMemberRosterReader"/>, en particular el argumento que dejaba el
/// roster de <c>/admin/members</c> vacío para siempre.
/// </summary>
/// <remarks>
/// <para><b>El defecto.</b> La llamada pasaba <c>memberTypeAlias: string.Empty</c>. Umbraco
/// interpreta <c>null</c> como "todos los tipos", pero con una cadena —aunque sea vacía— arma el
/// predicado <c>ContentTypeAlias == ""</c>, que no matchea ningún member. Resultado: la página
/// mostraba <i>"Members · 0"</i> con members realmente registrados, y con la tabla vacía no había
/// forma de llegar desde la UI a lock, unlock, reset de 2FA, password-reset, edición de roles,
/// delete ni GDPR-erase. Se confirmó contra la app corriendo: mismo build, mismo member, 0 filas
/// con <c>string.Empty</c> y 1 fila con <c>null</c>.</para>
///
/// <para><b>Por qué el test mira el argumento.</b> Es un test de forma de llamada, y eso
/// normalmente se evita. Aquí se justifica porque el defecto era invisible desde afuera: el
/// método devolvía una página bien formada, con su total y su paginación correctos — solo que
/// siempre vacía. Ninguna aserción sobre el valor de retorno con un doble de
/// <see cref="IMemberService"/> lo habría detectado, porque el doble devuelve lo que se le pida
/// sin aplicar el filtro. Lo que hay que fijar es justamente el argumento.</para>
/// </remarks>
public sealed class UmbracoMemberRosterReaderTests
{
    private static (UmbracoMemberRosterReader Reader, IMemberService Members) Create(
        params IMember[] members)
    {
        var memberService = Substitute.For<IMemberService>();
        memberService
            .GetAll(
                Arg.Any<long>(), Arg.Any<int>(), out Arg.Any<long>(),
                Arg.Any<string>(), Arg.Any<Umbraco.Cms.Core.Direction>(),
                Arg.Any<bool>(), Arg.Any<string?>(), Arg.Any<string>())
            .Returns(call =>
            {
                call[2] = (long)members.Length;
                return members;
            });
        memberService.GetAllRoles(Arg.Any<int>()).Returns(Array.Empty<string>());

        var groupService = Substitute.For<IMemberGroupService>();
        groupService.GetAll().Returns(Array.Empty<IMemberGroup>());

        return (new UmbracoMemberRosterReader(memberService, groupService), memberService);
    }

    [Fact]
    public void No_filtra_por_tipo_de_member()
    {
        // El corazón del fix: null y no string.Empty. Si alguien "limpia" ese null creyendo que
        // una cadena vacía es equivalente, el roster vuelve a quedarse vacío en producción y
        // este test es el que lo dice.
        var (reader, members) = Create();

        reader.GetRosterPage(page: 1, pageSize: 25);

        members.Received(1).GetAll(
            Arg.Any<long>(), Arg.Any<int>(), out Arg.Any<long>(),
            Arg.Any<string>(), Arg.Any<Umbraco.Cms.Core.Direction>(),
            Arg.Any<bool>(),
            Arg.Is<string?>(alias => alias == null),
            Arg.Any<string>());
    }

    [Fact]
    public void Traduce_la_pagina_a_indice_base_cero()
    {
        // La API de Umbraco recibe pageINDEX; la UI habla de pageNUMBER. Off-by-one aquí
        // saltearía la primera página entera del roster.
        var (reader, members) = Create();

        reader.GetRosterPage(page: 3, pageSize: 10);

        // Arg.Is y no el literal: `out long` es del mismo tipo que pageIndex y NSubstitute exige
        // especificación para TODOS los argumentos de un mismo tipo, o no sabe cuál es cuál.
        members.Received(1).GetAll(
            Arg.Is<long>(pageIndex => pageIndex == 2),
            Arg.Is<int>(pageSize => pageSize == 10),
            out Arg.Any<long>(),
            Arg.Any<string>(), Arg.Any<Umbraco.Cms.Core.Direction>(),
            Arg.Any<bool>(), Arg.Any<string?>(), Arg.Any<string>());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-4)]
    public void Una_pagina_invalida_cae_a_la_primera(int requested)
    {
        var (reader, members) = Create();

        reader.GetRosterPage(page: requested, pageSize: 25);

        members.Received(1).GetAll(
            Arg.Is<long>(pageIndex => pageIndex == 0),
            Arg.Any<int>(), out Arg.Any<long>(),
            Arg.Any<string>(), Arg.Any<Umbraco.Cms.Core.Direction>(),
            Arg.Any<bool>(), Arg.Any<string?>(), Arg.Any<string>());
    }

    [Fact]
    public void Un_roster_sin_members_devuelve_pagina_vacia_y_no_revienta()
    {
        var (reader, _) = Create();

        var page = reader.GetRosterPage(page: 1, pageSize: 25);

        Assert.Empty(page.Items);
        Assert.Equal(0, page.TotalCount);
    }
}

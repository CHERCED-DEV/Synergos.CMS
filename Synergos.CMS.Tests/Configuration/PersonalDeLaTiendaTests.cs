using Microsoft.Extensions.Configuration;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Web.Services;

namespace Synergos.CMS.Tests.Configuration;

/// <summary>
/// Quién es personal de la tienda (013-seller-operaciones, T002; lote 12, P1 = A): <c>Synergos:Tienda:RolesDelPersonal</c>,
/// con <c>admin</c> por defecto, y un rol en blanco que no deja arrancar.
/// </summary>
public sealed class PersonalDeLaTiendaTests
{
    private static TiendaSettings Leer(Dictionary<string, string?> valores)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(valores).Build();
        var ajustes = new TiendaSettings();
        config.GetSection("Synergos:Tienda").Bind(ajustes);
        return ajustes;
    }

    [Fact]
    public void Sin_declararlo_el_personal_es_admin()
    {
        var ajustes = Leer(new());

        Assert.Equal(new[] { "admin" }, ajustes.Personal());
        Assert.Equal("admin", ajustes.PersonalCsv());
        Assert.True(new ValidadorDeLaTienda().Validate(null, ajustes).Succeeded);
    }

    [Fact]
    public void Declarado_reemplaza_al_de_por_defecto_en_vez_de_sumarse()
    {
        // El binder SUMA a una lista que ya trae valores: por eso la propiedad nace vacía y el «admin» sale de
        // Personal(). Con el valor por defecto dentro de la lista, declarar [vendedor] daría [admin, vendedor].
        var ajustes = Leer(new() { ["Synergos:Tienda:RolesDelPersonal:0"] = "vendedor", ["Synergos:Tienda:RolesDelPersonal:1"] = "bodega" });

        Assert.Equal(new[] { "vendedor", "bodega" }, ajustes.Personal());
        Assert.Equal("vendedor,bodega", ajustes.PersonalCsv());
    }

    [Fact]
    public void Un_rol_en_blanco_no_deja_arrancar_y_dice_donde_esta()
    {
        var ajustes = new TiendaSettings { RolesDelPersonal = ["vendedor", " "] };

        var resultado = new ValidadorDeLaTienda().Validate(null, ajustes);

        Assert.True(resultado.Failed);
        Assert.Contains("Synergos:Tienda:RolesDelPersonal:1", resultado.FailureMessage);
    }
}

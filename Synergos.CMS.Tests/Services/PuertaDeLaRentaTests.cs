using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using NSubstitute;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Web.Services;
using Synergos.CMS.Web.Services.Puerta;
using Synergos.CMS.Web.Services.SynHost;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// La entrada de la renta en la puerta del CMS (línea 005, T027): <c>Synergos:Puerta:Flujos:alquiler.renta</c> del
/// <c>appsettings.json</c> que reciben todos los entornos.
/// </summary>
/// <remarks>
/// <para>La puerta la valida contra los contratos incrustados (el de <c>Bff.Alquiler</c> marca sus operaciones), abre
/// sólo cuando el despliegue le da destino a ese orquestador (<c>Synergos:Alquiler:BaseUrl</c>), y su sujeto es el
/// MISMO <c>Kind</c> con que los clientes del alquiler nombran al inquilino (research R8): con otro, lo que salió por la
/// puerta sería de un dueño que ni la liquidación ni «mis alquileres» encuentran.</para>
/// </remarks>
public sealed class PuertaDeLaRentaTests
{
    private const string Flujo = AlquilerResolutor.FlujoDeLaRenta;

    [Fact]
    public void La_entrada_existe_y_la_puerta_la_valida_contra_el_contrato_incrustado()
    {
        var ajustes = Ajustes();

        Assert.True(ajustes.Flujos.TryGetValue(Flujo, out var renta), $"{Flujo} no está en Synergos:Puerta:Flujos.");
        Assert.Equal(AccesoDeLaPuerta.Miembro, renta!.Acceso);
        Assert.Equal("/alquiler/?alquiler={id}", renta.Aviso?.Ruta);

        // Sólo esta entrada: las demás (la de Eventos lleva su sección de negocio) las vigila su propia línea.
        var sola = new PuertaSettings
        {
            Flujos = new Dictionary<string, FlujoDeLaPuertaSettings>(StringComparer.Ordinal) { [Flujo] = renta },
        };
        var resultado = new ValidadorDeLaPuerta(TablaDeLaPuerta.Incrustada()).Validate(null, sola);
        Assert.True(resultado.Succeeded, resultado.FailureMessage);
    }

    [Fact]
    public void El_sujeto_de_la_puerta_es_el_inquilino_de_los_clientes_del_alquiler()
    {
        Assert.Equal(new AlquilerSettings().RenterKind, Ajustes().Flujos[Flujo].SujetoKind);
        Assert.Equal(ArtefactoDeAlquiler.Flujo, Flujo);
    }

    [Fact]
    public void Abre_solo_con_destino_para_el_orquestador_del_alquiler()
    {
        var tabla = TablaDeLaPuerta.Incrustada();
        Assert.NotEmpty(tabla.DelFlujo(Flujo));
        // El destino sale, por convención, de Synergos:{orquestador}:BaseUrl: aquí, Synergos:Alquiler:BaseUrl.
        Assert.All(tabla.DelFlujo(Flujo), op => Assert.Equal("Alquiler", op.Orquestador));

        var monitor = Substitute.For<IOptionsMonitor<PuertaSettings>>();
        monitor.CurrentValue.Returns(Ajustes());

        Assert.False(new PuertaDeLosFlujos(tabla, monitor, new DestinosDeLaPuerta([])).Abre(Flujo));
        Assert.False(new PuertaDeLosFlujos(tabla, monitor, new DestinosDeLaPuerta(["Eventos"])).Abre(Flujo));
        Assert.True(new PuertaDeLosFlujos(tabla, monitor, new DestinosDeLaPuerta(["Alquiler"])).Abre(Flujo));
    }

    /// <summary><c>Synergos:Puerta</c> del <c>appsettings.json</c> base, enlazada como la enlaza el CMS.</summary>
    private static PuertaSettings Ajustes()
        => new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(RepoRoot(), "Synergos.CMS.Web", "appsettings.json"), optional: false)
            .Build()
            .GetSection("Synergos:Puerta")
            .Get<PuertaSettings>()!;

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Synergos.CMS.sln")))
        {
            dir = dir.Parent;
        }
        Assert.NotNull(dir);
        return dir!.FullName;
    }
}

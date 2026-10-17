using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using NSubstitute;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Web.Services.Puerta;
using Synergos.CMS.Web.Services.SynHost;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// Las entradas de la visita en la puerta del CMS (008-propiedades-visita): <c>Synergos:Puerta:Flujos:propiedades.visita</c>
/// y <c>…:propiedades.visita-cancelacion</c> del <c>appsettings.json</c> que reciben todos los entornos.
/// </summary>
/// <remarks>
/// <para>Las franjas se ven antes de iniciar sesión (008-P3, núcleo NB-3): la puerta abre a invitados SÓLO la lectura
/// <c>disponibilidad</c>, que no pone sujeto, llave ni contacto; agendar sigue pidiendo un miembro. Cancelar una visita
/// confirmada (008-P5) es otro flujo, posterior a la visita, con el MISMO <c>Kind</c> de sujeto: la original se lee sólo
/// si es del mismo dueño, y con otro <c>Kind</c> ninguna visita sería «tuya».</para>
/// </remarks>
public sealed class PuertaDeLaVisitaTests
{
    private const string Visita = RealtyResolutor.FlujoDeLaVisita;
    private const string Cancelacion = "propiedades.visita-cancelacion";

    [Fact]
    public void Las_dos_entradas_existen_y_la_puerta_las_valida_contra_el_contrato_incrustado()
    {
        var ajustes = Ajustes();

        Assert.True(ajustes.Flujos.TryGetValue(Visita, out var visita), $"{Visita} no está en Synergos:Puerta:Flujos.");
        Assert.True(ajustes.Flujos.TryGetValue(Cancelacion, out var cancelacion), $"{Cancelacion} no está en Synergos:Puerta:Flujos.");
        Assert.Equal(AccesoDeLaPuerta.Miembro, visita!.Acceso);
        Assert.Equal(AccesoDeLaPuerta.Miembro, cancelacion!.Acceso);

        // Sólo estas entradas: las demás las vigila su propia línea.
        var solas = new PuertaSettings
        {
            Flujos = new Dictionary<string, FlujoDeLaPuertaSettings>(StringComparer.Ordinal) { [Visita] = visita, [Cancelacion] = cancelacion },
        };
        var resultado = new ValidadorDeLaPuerta(TablaDeLaPuerta.Incrustada()).Validate(null, solas);
        Assert.True(resultado.Succeeded, resultado.FailureMessage);
    }

    [Fact]
    public void A_un_invitado_solo_se_le_abre_la_disponibilidad_y_nada_de_la_cancelacion()
    {
        var ajustes = Ajustes();

        Assert.Equal(["disponibilidad"], ajustes.Flujos[Visita].Invitado);
        Assert.Empty(ajustes.Flujos[Cancelacion].Invitado);
    }

    [Fact]
    public void La_cancelacion_nombra_al_interesado_como_la_visita()
    {
        var ajustes = Ajustes();

        Assert.Equal("propiedades.interesado", ajustes.Flujos[Visita].SujetoKind);
        Assert.Equal(ajustes.Flujos[Visita].SujetoKind, ajustes.Flujos[Cancelacion].SujetoKind);
    }

    [Fact]
    public void Las_dos_abren_solo_con_destino_para_el_orquestador_de_propiedades()
    {
        var tabla = TablaDeLaPuerta.Incrustada();
        var monitor = Substitute.For<IOptionsMonitor<PuertaSettings>>();
        monitor.CurrentValue.Returns(Ajustes());

        foreach (var flujo in new[] { Visita, Cancelacion })
        {
            Assert.NotEmpty(tabla.DelFlujo(flujo));
            Assert.All(tabla.DelFlujo(flujo), op => Assert.Equal("Propiedades", op.Orquestador));
            Assert.False(new PuertaDeLosFlujos(tabla, monitor, new DestinosDeLaPuerta([])).Abre(flujo));
            Assert.True(new PuertaDeLosFlujos(tabla, monitor, new DestinosDeLaPuerta(["Propiedades"])).Abre(flujo));
        }
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

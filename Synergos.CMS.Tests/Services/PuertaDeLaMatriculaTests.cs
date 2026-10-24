using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using NSubstitute;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Web.Services.Puerta;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// La entrada de la matrícula en la puerta del CMS (010-educacion-matricula T011):
/// <c>Synergos:Puerta:Flujos:educacion.matricula</c> del <c>appsettings.json</c> que reciben todos los entornos.
/// </summary>
/// <remarks>
/// Por la puerta sólo miembros (004-P3 y decisión 6 del lote 1): un invitado no abre nada. El camino NO se declara aquí
/// sino en la funcionalidad (<c>Synergos:Features:Academy:EnrollmentPath</c>, «directa» por defecto): dos declaraciones
/// que no coinciden serían un defecto.
/// </remarks>
public sealed class PuertaDeLaMatriculaTests
{
    private const string Matricula = "educacion.matricula";

    [Fact]
    public void La_entrada_existe_y_la_puerta_la_valida_contra_el_contrato_incrustado()
    {
        var ajustes = Ajustes();

        Assert.True(ajustes.Flujos.TryGetValue(Matricula, out var matricula), $"{Matricula} no está en Synergos:Puerta:Flujos.");
        Assert.Equal(AccesoDeLaPuerta.Miembro, matricula!.Acceso);
        Assert.Equal("educacion.alumno", matricula.SujetoKind);
        Assert.Empty(matricula.Invitado);

        // Sólo esta entrada: las demás las vigila su propia línea.
        var sola = new PuertaSettings
        {
            Flujos = new Dictionary<string, FlujoDeLaPuertaSettings>(StringComparer.Ordinal) { [Matricula] = matricula },
        };
        var resultado = new ValidadorDeLaPuerta(TablaDeLaPuerta.Incrustada()).Validate(null, sola);
        Assert.True(resultado.Succeeded, resultado.FailureMessage);
    }

    [Fact]
    public void El_aviso_lleva_al_resultado_de_la_matricula_en_el_sitio_de_educacion()
    {
        Assert.Equal("/educacion/?matricula={id}", Ajustes().Flujos[Matricula].Aviso?.Ruta);
    }

    [Fact]
    public void Abre_solo_con_destino_para_el_orquestador_de_educacion()
    {
        var tabla = TablaDeLaPuerta.Incrustada();
        var monitor = Substitute.For<IOptionsMonitor<PuertaSettings>>();
        monitor.CurrentValue.Returns(Ajustes());

        Assert.NotEmpty(tabla.DelFlujo(Matricula));
        Assert.All(tabla.DelFlujo(Matricula), op => Assert.Equal("Educacion", op.Orquestador));
        Assert.False(new PuertaDeLosFlujos(tabla, monitor, new DestinosDeLaPuerta([])).Abre(Matricula));
        Assert.True(new PuertaDeLosFlujos(tabla, monitor, new DestinosDeLaPuerta(["Educacion"])).Abre(Matricula));
    }

    /// <summary>
    /// La devolución del personal (lote 8, 010-P2 B, T034): sólo <c>admin</c> la abre, y el sujeto es la persona del
    /// personal —el rastro de quién devolvió—. Un alumno o un instructor reciben <c>puerta.personal_requerido</c> (la regla
    /// la prueba <c>FlujosControllerTests</c>); aquí, que la entrada la declare así y que valga contra el contrato.
    /// </summary>
    [Fact]
    public void La_devolucion_es_del_personal_admin_y_la_puerta_la_valida_contra_el_contrato_incrustado()
    {
        const string Devolucion = "educacion.devolucion";
        var ajustes = Ajustes();

        Assert.True(ajustes.Flujos.TryGetValue(Devolucion, out var devolucion), $"{Devolucion} no está en Synergos:Puerta:Flujos.");
        Assert.Equal(AccesoDeLaPuerta.Personal, devolucion!.Acceso);
        Assert.Equal(["admin"], devolucion.Roles);
        Assert.Equal("educacion.personal", devolucion.SujetoKind);
        Assert.Empty(devolucion.Invitado);

        var sola = new PuertaSettings
        {
            Flujos = new Dictionary<string, FlujoDeLaPuertaSettings>(StringComparer.Ordinal) { [Devolucion] = devolucion },
        };
        var resultado = new ValidadorDeLaPuerta(TablaDeLaPuerta.Incrustada()).Validate(null, sola);
        Assert.True(resultado.Succeeded, resultado.FailureMessage);
        Assert.All(TablaDeLaPuerta.Incrustada().DelFlujo(Devolucion), op => Assert.Equal("Educacion", op.Orquestador));
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

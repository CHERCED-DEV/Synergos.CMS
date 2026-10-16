using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Services;
using Synergos.CMS.Web.Services.Puerta;
using Synergos.CMS.Web.Services.SynHost;
using Umbraco.Cms.Core.Models.PublishedContent;

namespace Synergos.CMS.Tests.Controllers;

/// <summary>
/// La cita médica en la puerta (003-salud-cita, T013): la entrada de <c>appsettings.json</c> contra los
/// contratos que el CMS lleva incrustados, y el servicio que sale del negocio del sitio.
/// </summary>
/// <remarks>
/// <para><b>Se lee el <c>appsettings.json</c> de verdad</b>, no una copia: lo que arranca es ese fichero, y una
/// entrada que el validador rechazara dejaría al CMS sin arrancar.</para>
///
/// <para><b>El servicio lo pone la puerta</b> (<c>X-Synergos-Negocio</c>) y la definición de <c>salud.cita</c> lo
/// lee como <c>ref</c> del campo <c>servicio</c>: un <c>{ kind, id }</c>. Si el negocio cambiara su forma o su
/// nombre, la cita se rechazaría en cada petición; aquí se ve antes.</para>
/// </remarks>
public sealed class PuertaDeLaCitaTests
{
    private const string Flujo = "salud.cita";

    private static string Raiz()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Synergos.CMS.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("No se encontró la raíz del repo.");
    }

    private static PuertaSettings LaDelAppsettings()
        => new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(Raiz(), "Synergos.CMS.Web", "appsettings.json"), optional: false)
            .Build()
            .GetSection("Synergos:Puerta")
            .Get<PuertaSettings>()!;

    private static readonly SeccionesDelSitio Secciones = new(
        [new SeccionDeNegocioRegistrada("Ehr", typeof(NegocioDeEhr), _ => new EhrFeatureSettings().Para(null))],
        new ServiceCollection().BuildServiceProvider());

    [Fact]
    public void La_entrada_de_la_cita_del_appsettings_arranca_contra_los_contratos_incrustados()
    {
        var puerta = LaDelAppsettings();
        var cita = new PuertaSettings
        {
            Flujos = new Dictionary<string, FlujoDeLaPuertaSettings>(StringComparer.Ordinal) { [Flujo] = puerta.Flujos[Flujo] },
        };

        var r = new ValidadorDeLaPuerta(TablaDeLaPuerta.Incrustada(), Secciones).Validate(null, cita);

        Assert.True(r.Succeeded, r.FailureMessage);
        Assert.Equal("salud.paciente", cita.Flujos[Flujo].SujetoKind);
        Assert.Contains("{id}", cita.Flujos[Flujo].Aviso!.Ruta, StringComparison.Ordinal);
    }

    [Fact]
    public void Los_campos_de_negocio_que_pone_la_puerta_son_los_que_lee_la_definicion_de_la_cita()
    {
        // Los dos lados del cable en el mismo repo: la entrada de la puerta (CMS) y flujos/salud.cita.json
        // (Bff.Salud). El validador sólo comprueba que el campo exista en el negocio; si la entrada nombrara
        // otro campo real, la cita contestaría «falta servicio» en cada petición.
        var definicion = JsonDocument.Parse(File.ReadAllText(Path.Combine(
            Raiz(), "backend", "orquestadores", "Synergos.Bff.Salud", "flujos", "salud.cita.json")));
        var leidos = definicion.RootElement.GetProperty("tipos").EnumerateObject()
            .Where(t => t.Value.ValueKind == JsonValueKind.Object
                && t.Value.TryGetProperty("de", out var de) && de.GetString() == "negocio")
            .Select(t => t.Value.GetProperty("campo").GetString()!)
            .Order(StringComparer.Ordinal)
            .ToList();

        var puestos = LaDelAppsettings().Flujos[Flujo].Negocio!.Campos
            .Select(JsonNamingPolicy.CamelCase.ConvertName)
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.NotEmpty(leidos);
        Assert.Equal(leidos, puestos);
    }

    [Fact]
    public void El_servicio_viaja_en_el_negocio_como_lo_lee_la_cita()
    {
        var negocio = new EhrFeatureSettings().Para(null);

        var cabecera = LoQuePoneLaPuerta.Negocio(negocio, ["Servicio"]);
        using var json = JsonDocument.Parse(Encoding.UTF8.GetString(DeBase64Url(cabecera)));

        var servicio = json.RootElement.GetProperty("servicio");
        Assert.Equal("salud.servicio", servicio.GetProperty("kind").GetString());
        Assert.Equal("consulta", servicio.GetProperty("id").GetString());
        Assert.Single(json.RootElement.EnumerateObject());
    }

    [Fact]
    public void El_servicio_por_defecto_es_el_de_la_ruta_directa_y_un_sitio_lo_puede_cambiar()
    {
        var porDefecto = new SaludSettings();
        var sitio = Guid.Parse("f289c6d5-4f6c-407f-8c35-ce7e0dec7b22");
        var ajustes = new EhrFeatureSettings
        {
            Sitios = new(StringComparer.OrdinalIgnoreCase) { [sitio.ToString()] = new EhrFeatureSitio { ServiceId = "control" } },
        };

        Assert.Equal(new ServicioAgendable(porDefecto.ServiceKind, porDefecto.ServiceId), new EhrFeatureSettings().Para(null).Servicio);
        Assert.Equal(new ServicioAgendable(porDefecto.ServiceKind, "control"), ajustes.Para(sitio).Servicio);
        Assert.Empty(ajustes.Problemas());
    }

    [Fact]
    public void Un_servicio_vacio_no_deja_arrancar()
    {
        var ajustes = new EhrFeatureSettings { ServiceId = " " };

        Assert.Contains(ajustes.Problemas(), p => p.Contains("ServiceKind y ServiceId", StringComparison.Ordinal));
    }

    // ── El camino de la cita (T021) y la vista que coloca el coordinador (T022) ─────────────────────

    private static string Camino(EhrFeatureSettings ajustes, bool laPuertaAbre, Guid? sitio = null)
    {
        var negocio = Substitute.For<INegocioDelSitio<NegocioDeEhr>>();
        negocio.Actual().Returns(ajustes.Para(sitio));
        var puerta = Substitute.For<IPuertaDeLosFlujos>();
        puerta.Abre(EhrResolutor.FlujoDeLaCita).Returns(laPuertaAbre);

        return new EhrResolutor(negocio, puerta).Resolver(Substitute.For<IPublishedElement>()).Props.AppointmentPath;
    }

    [Fact]
    public void Sin_override_la_cita_va_por_la_puerta_si_abre_y_si_no_por_la_directa()
    {
        Assert.Equal(CaminoDeCompra.Puerta, Camino(new EhrFeatureSettings(), laPuertaAbre: true));
        Assert.Equal(CaminoDeCompra.Directa, Camino(new EhrFeatureSettings(), laPuertaAbre: false));
    }

    [Fact]
    public void El_override_de_negocio_gana_en_los_dos_sentidos_y_por_sitio()
    {
        var sitio = Guid.Parse("f289c6d5-4f6c-407f-8c35-ce7e0dec7b22");

        Assert.Equal(CaminoDeCompra.Directa, Camino(new EhrFeatureSettings { AppointmentPath = "directa" }, laPuertaAbre: true));
        Assert.Equal(CaminoDeCompra.Puerta, Camino(new EhrFeatureSettings { AppointmentPath = " Puerta " }, laPuertaAbre: false));

        var porSitio = new EhrFeatureSettings
        {
            AppointmentPath = "puerta",
            Sitios = new(StringComparer.OrdinalIgnoreCase) { [sitio.ToString()] = new EhrFeatureSitio { AppointmentPath = "directa" } },
        };
        Assert.Equal(CaminoDeCompra.Directa, Camino(porSitio, laPuertaAbre: true, sitio));
        Assert.Equal(CaminoDeCompra.Puerta, Camino(porSitio, laPuertaAbre: false));

        var sitioVacio = new EhrFeatureSettings
        {
            AppointmentPath = "directa",
            Sitios = new(StringComparer.OrdinalIgnoreCase) { [sitio.ToString()] = new EhrFeatureSitio { AppointmentPath = "" } },
        };
        Assert.Equal(CaminoDeCompra.Puerta, Camino(sitioVacio, laPuertaAbre: true, sitio));
    }

    [Fact]
    public void Un_camino_mal_escrito_no_deja_arrancar()
    {
        Assert.Contains(new EhrFeatureSettings { AppointmentPath = "puerto" }.Problemas(), p => p.Contains("AppointmentPath", StringComparison.Ordinal));
    }

    [Fact]
    public void La_vista_coloca_el_coordinador_de_la_cita_con_la_clave_que_abre_la_puerta()
    {
        var vista = File.ReadAllText(Path.Combine(Raiz(), "Synergos.CMS.Web", "Views", "Partials", "blockgrid", "Components", "elementSynEhr.cshtml"));
        var coordinador = System.Text.RegularExpressions.Regex.Match(vista, @"<synergos-flujo\s+flujo=""([^""]+)""\s+style=""display:contents"">");

        Assert.True(coordinador.Success, "elementSynEhr.cshtml no coloca <synergos-flujo flujo=… style=\"display:contents\">.");
        Assert.Equal(EhrResolutor.FlujoDeLaCita, coordinador.Groups[1].Value);
        Assert.True(LaDelAppsettings().Flujos.ContainsKey(coordinador.Groups[1].Value));
        Assert.NotEmpty(TablaDeLaPuerta.Incrustada().DelFlujo(coordinador.Groups[1].Value));
    }

    private static byte[] DeBase64Url(string texto)
    {
        var b64 = texto.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(b64.PadRight(b64.Length + ((4 - b64.Length % 4) % 4), '='));
    }
}

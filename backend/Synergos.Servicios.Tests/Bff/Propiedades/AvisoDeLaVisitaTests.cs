using System.Net;
using Synergos.Bff.Core;

namespace Synergos.CMS.Tests.Bff.Propiedades;

/// <summary>
/// Historia 4 de 008-propiedades-visita: el aviso de la visita es un paso del flujo (ADR 0140), el común del núcleo con
/// sus «valores» (T091, NR-3): UN correo al confirmar, con el día y las horas en la del sitio y la modalidad en palabras, y
/// un aviso caído no deshace la visita.
/// </summary>
/// <remarks>
/// Contra <c>Api.Notifications</c> real del arnés, con la plantilla del fichero que la provisiona de verdad
/// (<c>tools/provisionar.plantillas.json</c>): lo que se mira es la entrega que quedó registrada.
/// </remarks>
public sealed class AvisoDeLaVisitaTests
{
    [Fact]
    public async Task Al_confirmar_sale_un_correo_con_el_numero_la_franja_en_la_hora_del_sitio_y_la_modalidad()
    {
        // Lo que se mira son los valores que el paso MANDA de verdad, capturados en el cable: la entrega que
        // registra la capacidad dice el asunto ya relleno, no el cuerpo.
        string? mandado = null;
        using var r = new VisitaReal(falla: p =>
        {
            if (VisitaReal.Es(p, "POST", "^/v1/deliveries$")) mandado = p.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return null;
        });
        var (_, abierta) = await r.Abrir(cuerpo: VisitaReal.Cuerpo(modalidad: "video"));

        var (estado, cerrada) = await r.Cerrar(abierta.GetProperty("id").GetString()!);

        Assert.True(estado == HttpStatusCode.OK, cerrada.ToString());
        var entrega = Assert.Single(await r.Avisos());
        Assert.Equal("propiedades.visita.confirmada", entrega.GetProperty("templateKey").GetString());
        Assert.Equal($"Tu visita {cerrada.GetProperty("numero").GetString()} quedó agendada", entrega.GetProperty("subject").GetString());
        Assert.Equal("ana@ejemplo.co", entrega.GetProperty("address").GetString());
        Assert.NotNull(mandado);
        // Las 9:00 de Bogotá son las 14:00 UTC: si saliera en UTC diría «14:00».
        Assert.Contains($"\"ventana\":\"{VisitaReal.Inicio:dd/MM/yyyy} 09:00 a 10:00\"", mandado, StringComparison.Ordinal);
        Assert.Contains("\"modoEnTexto\":\"por videollamada\"", mandado, StringComparison.Ordinal);
        // La visita no se cobra: el aviso no escribe un total que nadie fijó.
        Assert.DoesNotContain("\"total\"", mandado, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Con_el_aviso_caido_la_visita_queda_confirmada()
    {
        using var r = new VisitaReal(falla: p => VisitaReal.Es(p, "POST", "^/v1/deliveries$") ? "notifications.unavailable" : null);
        var (_, abierta) = await r.Abrir();
        var id = abierta.GetProperty("id").GetString()!;

        var (estado, cerrada) = await r.Cerrar(id);

        Assert.True(estado == HttpStatusCode.OK, cerrada.ToString());
        Assert.Equal(SagaStatus.Completed, r.Sagas.Find(id)!.Status);
        Assert.Equal(["Confirmed"], await r.Reservas());
        Assert.Empty(await r.Avisos());
    }

    [Fact]
    public async Task Confirmar_dos_veces_no_manda_un_segundo_correo()
    {
        using var r = new VisitaReal();
        var (_, abierta) = await r.Abrir();
        var id = abierta.GetProperty("id").GetString()!;

        await r.Cerrar(id);
        var (estado, _) = await r.Cerrar(id);

        Assert.Equal(HttpStatusCode.OK, estado);
        Assert.Single(await r.Avisos());
    }
}

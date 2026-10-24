using System.Net;
using Synergos.Bff.Core;

namespace Synergos.CMS.Tests.Bff.Educacion;

/// <summary>
/// Historia 3 de 010-educacion-matricula (T022): si algo falla a mitad no queda cobro sin matrícula, y repetir no cobra
/// dos veces el mismo curso, contra las capacidades reales; lo que ninguna sabe hacer a pedido se inyecta en el cable.
/// </summary>
public sealed class FallosDeLaMatriculaTests
{
    [Fact]
    public async Task Un_cobro_rechazado_no_deja_matricula_ni_nada_retenido()
    {
        using var r = await new MatriculaDeCursoReal(falla: p => MatriculaDeCursoReal.Es(p, "POST", "^/v1/payments$") ? "payments.payment_declined" : null)
            .ConOferta();

        var (estado, cuerpo) = await r.Abrir();

        Assert.Equal(HttpStatusCode.Conflict, estado);
        Assert.Equal("payments.payment_declined", cuerpo.GetProperty("code").GetString());
        Assert.NotEqual(SagaStatus.Completed, r.Sagas.Find("matricula-gestion-agil")!.Status);
        Assert.False(r.Tiene("matricula-gestion-agil", "cobroId"));
    }

    [Fact]
    public async Task Una_captura_negada_anula_la_autorizacion_y_no_queda_matricula()
    {
        using var r = await new MatriculaDeCursoReal(falla: p => MatriculaDeCursoReal.Es(p, "POST", "^/v1/payments/[^/]+/capture$") ? "payments.capture_failed" : null)
            .ConOferta();
        var (_, abierta) = await r.Abrir();
        var id = abierta.GetProperty("id").GetString()!;

        var (estado, cuerpo) = await r.Cerrar(id);

        Assert.Equal(HttpStatusCode.Conflict, estado);
        Assert.Equal("payments.capture_failed", cuerpo.GetProperty("code").GetString());
        Assert.Equal(SagaStatus.Compensated, r.Sagas.Find(id)!.Status);
        Assert.Equal("Voided", await r.Cobro(id));
        Assert.Equal(0, await r.Avisos());
    }

    [Fact]
    public async Task Si_el_aviso_falla_la_matricula_queda_activa_igual()
    {
        using var r = await new MatriculaDeCursoReal(falla: p => MatriculaDeCursoReal.Es(p, "POST", "^/v1/deliveries$") ? "notifications.unavailable" : null)
            .ConOferta();
        var (_, abierta) = await r.Abrir();
        var id = abierta.GetProperty("id").GetString()!;

        var (estado, cerrada) = await r.Cerrar(id);

        Assert.True(estado == HttpStatusCode.OK, cerrada.ToString());
        Assert.Equal("Completed", cerrada.GetProperty("estado").GetString());
        Assert.Equal("Captured", await r.Cobro(id));
    }

    [Fact]
    public async Task Con_precios_caido_se_rechaza_como_transitoria_sin_autorizar_y_nunca_gratis()
    {
        using var r = await new MatriculaDeCursoReal(falla: p => MatriculaDeCursoReal.Es(p, "POST", "^/v1/quotes$") ? "caida" : null)
            .ConOferta();

        var (estado, _) = await r.Abrir(llave: "m-caida");

        Assert.True((int)estado >= 500, $"Una caída de precios no es un rechazo del negocio: {(int)estado}.");
        Assert.NotEqual(SagaStatus.Completed, r.Sagas.Find("m-caida")?.Status);
        Assert.False(r.Tiene("m-caida", "cobroId"));
    }

    /// <summary>
    /// La llave de abrir es la del curso (la deriva el elemento): dos pestañas, una recarga o una respuesta perdida caen
    /// en la MISMA matrícula del mismo alumno, con un solo cobro (FR-007).
    /// </summary>
    [Fact]
    public async Task La_misma_llave_dos_veces_es_la_misma_matricula_y_un_solo_cobro()
    {
        using var r = await new MatriculaDeCursoReal().ConOferta();

        var (_, primera) = await r.Abrir();
        var (estado, segunda) = await r.Abrir();

        Assert.True(estado is HttpStatusCode.OK or HttpStatusCode.Created, segunda.ToString());
        var id = primera.GetProperty("id").GetString()!;
        Assert.Equal(id, segunda.GetProperty("id").GetString());
        Assert.Single(r.Sagas.StartedBefore(DateTimeOffset.MaxValue));
        await r.Cerrar(id);
        var (_, tercera) = await r.Abrir();
        Assert.Equal(id, tercera.GetProperty("id").GetString());
        Assert.Equal("Completed", tercera.GetProperty("estado").GetString());
    }

    [Fact]
    public async Task La_misma_llave_de_otro_alumno_es_otra_matricula()
    {
        using var r = await new MatriculaDeCursoReal().ConOferta();

        var (_, ana) = await r.Abrir(alumno: "ana");
        var (_, beto) = await r.Abrir(alumno: "beto");

        Assert.NotEqual(ana.GetProperty("id").GetString(), beto.GetProperty("id").GetString());
    }

    /// <summary>Cambiar de plan con una matrícula abierta: se cancela (la autorización se anula) y se abre otra.</summary>
    [Fact]
    public async Task Cancelar_la_abierta_anula_su_cobro_y_deja_abrir_otra_con_otro_plan()
    {
        using var r = await new MatriculaDeCursoReal().ConOferta();
        var (_, primera) = await r.Abrir();
        var id = primera.GetProperty("id").GetString()!;

        var (cancelo, _) = await r.Cancelar(id);
        var (estado, segunda) = await r.Abrir(cuerpo: MatriculaDeCursoReal.Cuerpo(plan: "premium"));

        Assert.Equal(HttpStatusCode.OK, cancelo);
        Assert.Equal("Voided", await r.Cobro(id));
        Assert.Equal(HttpStatusCode.Created, estado);
        Assert.NotEqual(id, segunda.GetProperty("id").GetString());
        Assert.Equal(MatriculaDeCursoReal.Premium, segunda.GetProperty("datos").GetProperty("total").GetProperty("amount").GetDecimal());
    }

    [Fact]
    public async Task Un_intento_deshecho_no_encierra_la_misma_intencion()
    {
        var rechazar = true;
        using var r = await new MatriculaDeCursoReal(falla: p => rechazar && MatriculaDeCursoReal.Es(p, "POST", "^/v1/payments$") ? "payments.payment_declined" : null)
            .ConOferta();
        Assert.Equal(HttpStatusCode.Conflict, (await r.Abrir()).Estado);

        rechazar = false;
        var (estado, abierta) = await r.Abrir();

        Assert.Equal(HttpStatusCode.Created, estado);
        Assert.NotEqual("matricula-gestion-agil", abierta.GetProperty("id").GetString());
        Assert.Equal("Running", abierta.GetProperty("estado").GetString());
    }
}

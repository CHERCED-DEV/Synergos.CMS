using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// Lo que cada cara puede hacer sobre un expediente, según el proceso (009, D-3 y SC-002).
/// </summary>
/// <remarks>
/// <para>Se prueba con el proceso del motor en proceso —la v2— porque es el que sirve un clon limpio;
/// el gate <c>ProcesoDelTramiteTests</c> obliga a que sea el mismo que la v2 publicada.</para>
/// </remarks>
public sealed class AccionesDelExpedienteTests
{
    private static IReadOnlyList<string> Del(string estado, string rol)
        => AccionesDelExpediente.Calcular(
            StubCaseWorkflowService.Proceso, StubCaseWorkflowService.EstadosFinales, estado, rol);

    private static string[] Lista(string csv) => csv.Split(',', StringSplitOptions.RemoveEmptyEntries);

    [Theory] // cada estado × cada cara: exactamente lo que el proceso le permite desde ahí
    [InlineData("submitted", CaseRoles.Funcionario, "take")]
    [InlineData("submitted", CaseRoles.Ciudadano, "")]
    [InlineData("in-review", CaseRoles.Funcionario, "approve,reject,request-info")]
    [InlineData("in-review", CaseRoles.Ciudadano, "")]
    [InlineData("info-requested", CaseRoles.Funcionario, "")]
    [InlineData("info-requested", CaseRoles.Ciudadano, "resubmit")]
    [InlineData("approved", CaseRoles.Funcionario, "")]
    [InlineData("approved", CaseRoles.Ciudadano, "")]
    [InlineData("rejected", CaseRoles.Funcionario, "")]
    [InlineData("rejected", CaseRoles.Ciudadano, "")]
    public void Cada_estado_ofrece_a_cada_cara_lo_que_el_proceso_le_permite(string estado, string rol, string esperadas)
    {
        Assert.Equal(Lista(esperadas), Del(estado, rol));
    }

    [Fact] // empty: sin estado, sin rol o con un estado que el proceso no conoce, nada
    public void Sin_estado_sin_rol_o_con_un_estado_desconocido_no_se_ofrece_nada()
    {
        Assert.Empty(Del("", CaseRoles.Funcionario));
        Assert.Empty(Del("submitted", " "));
        Assert.Empty(Del("archivado", CaseRoles.Funcionario));
        Assert.Empty(Del("submitted", "admin"));
    }

    [Fact] // el estado y la cara no distinguen mayúsculas ni espacios
    public void El_estado_y_la_cara_se_comparan_sin_mayusculas()
    {
        Assert.Equal(new[] { "take" }, Del(" SUBMITTED ", "Funcionario"));
    }

    /// <summary>
    /// Un final es un final, aunque la definición tuviera una transición que saliera de él.
    /// </summary>
    [Fact]
    public void Lo_cerrado_no_ofrece_nada_aunque_el_proceso_saliera_de_un_final()
    {
        var proceso = new[]
        {
            new AccionesDelExpediente.Paso("reopen", "approved", "in-review", new[] { CaseRoles.Funcionario }),
        };

        Assert.Empty(AccionesDelExpediente.Calcular(proceso, new[] { "approved" }, "approved", CaseRoles.Funcionario));
    }

    /// <summary>
    /// Una transición sin rol es un defecto de la definición (FR-006) y no se le ofrece a nadie.
    /// </summary>
    /// <remarks>
    /// La capacidad la dejaría disparar a cualquiera; ofrecerla así pondría un botón de funcionario
    /// en el portal del ciudadano.
    /// </remarks>
    [Fact]
    public void Una_transicion_sin_rol_no_se_ofrece_a_nadie()
    {
        var proceso = new[]
        {
            new AccionesDelExpediente.Paso("approve", "submitted", "approved", Array.Empty<string>()),
            new AccionesDelExpediente.Paso("take", "submitted", "in-review", new[] { CaseRoles.Funcionario }),
        };

        Assert.Equal(new[] { "take" }, AccionesDelExpediente.Calcular(proceso, Array.Empty<string>(), "submitted", CaseRoles.Funcionario));
        Assert.Empty(AccionesDelExpediente.Calcular(proceso, Array.Empty<string>(), "submitted", CaseRoles.Ciudadano));
    }

    [Fact] // una acción con dos orígenes legales se ofrece una vez, en el orden del proceso
    public void Las_acciones_salen_una_vez_y_en_el_orden_del_proceso()
    {
        var proceso = new[]
        {
            new AccionesDelExpediente.Paso("reject", "submitted", "rejected", new[] { CaseRoles.Funcionario }),
            new AccionesDelExpediente.Paso("approve", "submitted", "approved", new[] { CaseRoles.Funcionario }),
            new AccionesDelExpediente.Paso("reject", "submitted", "rejected", new[] { "admin", CaseRoles.Funcionario }),
        };

        Assert.Equal(new[] { "reject", "approve" },
            AccionesDelExpediente.Calcular(proceso, Array.Empty<string>(), "submitted", CaseRoles.Funcionario));
    }
}

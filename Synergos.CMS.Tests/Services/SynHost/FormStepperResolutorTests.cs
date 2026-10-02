using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Interfaces.SynHost;
using Synergos.CMS.Web.Services;
using Synergos.CMS.Web.Services.SynHost;

namespace Synergos.CMS.Tests.Services.SynHost;

/// <summary>
/// El formulario por pasos sobre el modelo de Forms (#196, tanda D): lo que se pinta y lo que el
/// servidor exige salen de la misma definición.
/// </summary>
public sealed class FormStepperResolutorTests
{
    private static FormStepperResolutor Resolutor(string apiBase = "/api/forms", string trampa = "syn_hp")
    {
        var negocio = Substitute.For<INegocioDelSitio<NegocioDeFormStepper>>();
        negocio.Actual().Returns(new NegocioDeFormStepper(apiBase));
        return new FormStepperResolutor(
            ElementoFalso.Fallback,
            negocio,
            Options.Create(new FormsSettings { HoneypotFieldName = trampa }),
            NullLogger<FormStepperResolutor>.Instance);
    }

    private static Umbraco.Cms.Core.Models.PublishedContent.IPublishedElement Campo(
        string? nombre, string? etiqueta, string? tipo = "text", bool obligatorio = false, string[]? opciones = null)
        => ElementoFalso.DeTipo("elementFormField",
            ("fieldName", nombre), ("fieldLabel", etiqueta), ("fieldType", tipo),
            ("fieldRequired", obligatorio), ("fieldOptions", opciones));

    private static Umbraco.Cms.Core.Models.PublishedContent.IPublishedElement Paso(
        string? titulo, params Umbraco.Cms.Core.Models.PublishedContent.IPublishedElement[] campos)
        => ElementoFalso.DeTipo("elementFormStep", ("stepTitle", titulo), ("fields", ElementoFalso.Lista(campos)));

    [Fact]
    public void Copia_los_pasos_y_campos_del_editor_y_lo_del_despliegue()
    {
        var bloque = ElementoFalso.DeTipo("elementSynFormStepper",
            ("formInternalKey", "reserva-cita"),
            ("steps", ElementoFalso.Lista(
                Paso("Tu reserva", Campo("servicio", "Servicio", "SELECT", true, ["Asesoría", "Auditorio"])),
                Paso("Tus datos", Campo("email", "Email", "email", true), Campo("nota", "Nota", "textarea")))));

        var props = Resolutor(apiBase: "/sitio-b/forms", trampa: "trampa").Resolver(bloque).Props;

        Assert.Equal("reserva-cita", props.FormKey);
        Assert.Equal("/sitio-b/forms", props.ApiBase);
        Assert.Equal("trampa", props.HoneypotField);
        Assert.Null(props.AllowSkip);
        Assert.Equal(["Tu reserva", "Tus datos"], props.Steps!.Select(p => p.Title));
        var servicio = props.Steps![0].Fields.Single();
        Assert.Equal(new CampoDelFormulario("servicio", "Servicio", "select", Required: true), servicio with { Options = null });
        Assert.Equal(["Asesoría", "Auditorio"], servicio.Options!);
        Assert.Equal(["email", "nota"], props.Steps[1].Fields.Select(c => c.Name));
    }

    [Fact]
    public void Un_campo_sin_nombre_o_sin_etiqueta_y_un_paso_sin_titulo_o_sin_campos_no_se_pintan()
    {
        var bloque = ElementoFalso.DeTipo("elementSynFormStepper",
            ("formInternalKey", "contacto"),
            ("steps", ElementoFalso.Lista(
                Paso("Válido", Campo("email", "Email", "email"), Campo(null, "Sin nombre"), Campo("sin-etiqueta", null)),
                Paso(null, Campo("huerfano", "Huérfano")),
                Paso("Sin campos válidos", Campo(null, null)))));

        var props = Resolutor().Resolver(bloque).Props;

        var paso = Assert.Single(props.Steps!);
        Assert.Equal("Válido", paso.Title);
        Assert.Equal("email", Assert.Single(paso.Fields).Name);
    }

    [Fact]
    public void Sin_pasos_no_viaja_la_lista_y_allowSkip_solo_viaja_encendido()
    {
        var vacio = Resolutor().Resolver(ElementoFalso.DeTipo("elementSynFormStepper", ("formInternalKey", "x"))).Props;
        Assert.Null(vacio.Steps);

        var saltable = Resolutor().Resolver(ElementoFalso.DeTipo("elementSynFormStepper", ("allowSkip", true))).Props;
        Assert.True(saltable.AllowSkip);
    }

    [Fact]
    public void El_servidor_lee_los_mismos_campos_que_se_pintan_uniendo_los_pasos()
    {
        var bloque = ElementoFalso.DeTipo("elementSynFormStepper",
            ("formInternalKey", "reserva-cita"),
            ("steps", ElementoFalso.Lista(
                Paso("Tu reserva", Campo("servicio", "Servicio", "select", true)),
                Paso("Tus datos", Campo("email", "Email", "email", true), Campo("nota", "Nota"), Campo(null, "Sin nombre")))));

        var campos = UmbracoFormDefinitionReader.ReadFields(bloque, ElementoFalso.Fallback);

        Assert.Equal(["servicio", "email", "nota"], campos.Select(c => c.Name));
        Assert.Equal(["servicio", "email"], campos.Where(c => c.Required).Select(c => c.Name));
    }

    [Fact]
    public void El_servidor_reconoce_como_formulario_el_contenedor_y_el_stepper_y_nada_mas()
    {
        Assert.True(UmbracoFormDefinitionReader.IsContainer(ElementoFalso.DeTipo("elementFormContainer")));
        Assert.True(UmbracoFormDefinitionReader.IsContainer(ElementoFalso.DeTipo("elementSynFormStepper")));
        Assert.False(UmbracoFormDefinitionReader.IsContainer(ElementoFalso.DeTipo("elementFormField")));
    }

    [Fact]
    public void El_contenedor_SSR_se_sigue_leyendo_igual()
    {
        var contenedor = ElementoFalso.DeTipo("elementFormContainer",
            ("formInternalKey", "contacto"),
            ("fields", ElementoFalso.Lista(Campo("nombre", "Nombre", "text", true), Campo("mensaje", "Mensaje", "textarea"))));

        var campos = UmbracoFormDefinitionReader.ReadFields(contenedor, ElementoFalso.Fallback);

        Assert.Equal([new FormFieldDefinition("nombre", "Nombre", true), new FormFieldDefinition("mensaje", "Mensaje", false)], campos);
    }
}

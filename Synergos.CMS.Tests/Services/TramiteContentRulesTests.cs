using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Services.Catalog;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// Cubre las reglas que convierten un <c>tramitePage</c> autorado en la ficha servible del
/// portal de trámites de Gobierno (ADR 0123).
/// </summary>
/// <remarks>
/// <para>El eje aquí no es el de los otros cuatro verticales. Los requisitos y la tasa de un
/// trámite son, literalmente, <b>regulación</b>: el portal no los inventa, los recoge. De ahí la
/// tesis que estas pruebas fijan — <b>nunca se esconde un trámite que existe, pero tampoco se
/// hace una afirmación que la entidad no hizo</b>. Por eso se descarta menos que en ningún otro
/// vertical (solo slug, nombre y la tasa ambigua) y a la vez hay degradados que se registran
/// como ERROR.</para>
///
/// <para>La regla que más se prueba es la de la tasa: <b>un 0 significa gratis únicamente cuando
/// el editor lo afirmó</b> con la casilla, y cuando las dos declaraciones se contradicen gana el
/// número, porque anunciar gratis lo que se cobra deja a un ciudadano en la caja sin plata.</para>
/// </remarks>
public sealed class TramiteContentRulesTests
{
    private const string Cop = "COP";

    private static readonly TramiteStepLabels Steps = new(
        SubmitTitle: "Radicar la solicitud",
        SubmitDetail: "Diligencie el formulario y adjunte los documentos requeridos.",
        ReviewTitle: "Revisión de la entidad",
        ReviewDetail: "Un funcionario valida sus datos y documentos.",
        ResolutionTitle: "Resolución",
        ResolutionDetail: "Recibe la respuesta y el documento resultante en su carpeta.");

    private static TramiteFieldContent Field(
        string? id = "cedula",
        string? label = "Número de cédula",
        string? type = "number",
        IReadOnlyList<string>? options = null,
        string? pattern = null)
        => new(id, label, type, Required: true, Help: "Solo números, sin puntos.", Options: options, Pattern: pattern);

    private static TramiteSectionContent Section(
        string? id = "solicitante",
        string? title = "Datos del solicitante",
        IReadOnlyList<TramiteFieldContent>? fields = null)
        => new(id, title, fields ?? new[] { Field() });

    private static TramitePageContent Content(
        string? slug = "renovacion-cedula",
        string? name = "Renovación de cédula de ciudadanía",
        string? category = "Identidad",
        string? agency = "Registraduría Nacional",
        string? channel = "presencial",
        IReadOnlyList<string>? required = null,
        bool isFree = false,
        decimal fee = 65_500m,
        int estimatedDays = 15,
        IReadOnlyList<TramiteSectionContent>? sections = null)
        => new(
            Slug: slug,
            Name: name,
            Summary: "Renueve su cédula por deterioro, pérdida o actualización de datos.",
            Category: category,
            Agency: agency,
            Channel: channel,
            Description: "La renovación le permite obtener un documento nuevo.",
            Eligibility: new[] { "Ser mayor de edad colombiano ya registrado." },
            Required: required ?? new[] { "Comprobante de pago de la tasa." },
            Normativa: "Decreto 1010 de 2000.",
            IsFree: isFree,
            Fee: fee,
            EstimatedDays: estimatedDays,
            RequiresAppointment: true,
            Sections: sections ?? new[] { Section() });

    private static EventContentResult<TramiteDetail?> Run(TramitePageContent content)
        => TramiteContentRules.BuildTramite(content, Cop, Steps);

    private static TramiteDetail? Build(TramitePageContent content) => Run(content).Value;

    private static bool HasError(EventContentResult<TramiteDetail?> result)
        => result.Issues.Any(i => i.Level == EventContentIssueLevel.Error);

    // ── Lo que se descarta: tres cosas y nada más ────────────────────────────

    [Fact]
    public void Sin_slug_no_hay_ficha_porque_no_hay_enlace()
    {
        var result = Run(Content(slug: "   "));

        Assert.Null(result.Value);
        Assert.NotEmpty(result.Issues);
    }

    [Fact]
    public void Sin_nombre_se_omite_porque_lo_hereda_cada_expediente_radicado()
    {
        // "Trámite (1)" no solo sale en la tarjeta: es el ServiceName que el funcionario ve en
        // su bandeja para siempre.
        Assert.Null(Build(Content(name: null)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Una_tasa_en_cero_sin_declarar_gratuidad_se_OMITE_y_es_un_ERROR(decimal fee)
    {
        // Es la respuesta a "¿0 es gratis o es sin publicar?": sin la casilla marcada, un 0 es
        // un campo sin diligenciar, y publicarlo anunciaría una gratuidad que nadie concedió.
        var result = Run(Content(isFree: false, fee: fee));

        Assert.Null(result.Value);
        Assert.True(HasError(result));
    }

    // ── La tasa: gratis solo cuando alguien lo afirmó ────────────────────────

    [Fact]
    public void Marcado_gratuito_y_en_cero_se_sirve_gratis_y_sin_una_sola_queja()
    {
        // La gratuidad es lo normal en Gobierno y muchas veces la manda la norma. Descartar todo
        // trámite en 0 borraría justo los que el Estado más quiere que la gente use.
        var result = Run(Content(isFree: true, fee: 0m));

        Assert.NotNull(result.Value);
        Assert.Equal(0m, result.Value!.Summary.FeeMinor);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void Marcado_gratuito_pero_con_tasa_se_sirve_COBRANDO_y_es_un_ERROR()
    {
        // Es la lectura que no puede dejar el viaje perdido: a quien se le anuncia $65.500 y
        // resulta exento no le pasa nada; a quien se le anuncia gratis y hay que pagar, se le
        // acabó el día.
        var result = Run(Content(isFree: true, fee: 65_500m));

        Assert.NotNull(result.Value);
        Assert.Equal(65_500m, result.Value!.Summary.FeeMinor);
        Assert.True(HasError(result));
    }

    [Fact]
    public void Una_tasa_positiva_sin_marcar_gratuito_es_el_caso_normal_y_no_avisa()
    {
        var result = Run(Content(isFree: false, fee: 189_000m));

        Assert.Equal(189_000m, result.Value!.Summary.FeeMinor);
        Assert.Empty(result.Issues);
    }

    // ── Lo que se degrada, empezando por lo que es regulación ────────────────

    [Fact]
    public void Sin_documentos_requeridos_el_tramite_SE_SIRVE_pero_es_un_ERROR()
    {
        // Esconderlo no libera al ciudadano de tener que hacerlo. Se sirve y se grita: es el
        // único degradado que se registra al nivel del que omite.
        var result = Run(Content(required: Array.Empty<string>()));

        Assert.NotNull(result.Value);
        Assert.Empty(result.Value!.Required);
        Assert.True(HasError(result));
    }

    [Fact]
    public void Sin_categoria_se_sirve_igual_porque_sigue_saliendo_en_el_listado()
    {
        // Al revés que la ciudad de un inmueble (ADR 0118 §5): sin categoría queda fuera de la
        // única faceta, pero el listado completo y la búsqueda por texto lo siguen encontrando.
        var result = Run(Content(category: "  "));

        Assert.NotNull(result.Value);
        Assert.Equal(string.Empty, result.Value!.Summary.Category);
        Assert.NotEmpty(result.Issues);
    }

    [Fact]
    public void Sin_entidad_se_sirve_y_se_avisa_porque_nadie_sabra_ante_quien_radica()
    {
        var result = Run(Content(agency: null));

        Assert.NotNull(result.Value);
        Assert.Equal(string.Empty, result.Value!.Summary.Agency);
        Assert.NotEmpty(result.Issues);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("virtual")]
    [InlineData("En línea")]
    public void Un_canal_que_no_se_reconoce_cae_a_presencial_y_avisa(string? channel)
    {
        // Presencial y no "en línea": quien va a la ventanilla de algo que también era digital
        // pierde una mañana; quien se queda en casa creyendo que hay canal digital, no radica.
        var result = Run(Content(channel: channel));

        Assert.Equal(TramiteContentRules.DefaultChannel, result.Value!.Summary.Channel);
        Assert.NotEmpty(result.Issues);
    }

    [Fact]
    public void Un_canal_valido_se_respeta_en_minusculas()
    {
        Assert.Equal("en-linea", Build(Content(channel: "EN-LINEA"))!.Summary.Channel);
    }

    [Fact]
    public void Unos_dias_estimados_negativos_valen_cero_sin_perder_el_tramite()
    {
        var result = Run(Content(estimatedDays: -3));

        Assert.Equal(0, result.Value!.Summary.EstimatedDays);
        Assert.NotEmpty(result.Issues);
    }

    [Fact]
    public void El_slug_ES_el_id_como_en_los_otros_cuatro_verticales()
    {
        var detail = Build(Content(slug: "  Renovacion-Cedula  "))!;

        Assert.Equal("renovacion-cedula", detail.Summary.Id);
        Assert.Equal(detail.Summary.Id, detail.Summary.Slug);
    }

    // ── Los pasos se DERIVAN de la máquina de estados ────────────────────────

    [Fact]
    public void Los_pasos_son_siempre_los_tres_del_expediente_y_no_los_autora_nadie()
    {
        // Si cada entidad escribiera los suyos, el "qué pasa" de la ficha y la timeline del
        // expediente contarían dos historias del mismo procedimiento.
        var steps = Build(Content())!.Steps;

        Assert.Equal(new[] { "radicar", "revision", "resolucion" }, steps.Select(s => s.Id));
        Assert.Equal(Steps.SubmitTitle, steps[0].Title);
        Assert.Equal(Steps.ResolutionDetail, steps[2].Detail);
    }

    // ── El formulario: variar es el producto, pero no a cualquier precio ─────

    [Fact]
    public void El_formulario_autorado_llega_entero_y_en_orden()
    {
        var detail = Build(Content(sections: new[]
        {
            Section("solicitante", "Datos del solicitante", new[] { Field("nombre", "Nombre completo", "text") }),
            Section("motivo", "Motivo", new[] { Field("motivo", "Motivo", "radio", new[] { "Deterioro", "Pérdida" }) }),
        }))!;

        Assert.Equal("form-renovacion-cedula", detail.FormDefinition.Id);
        Assert.Equal(detail.Summary.Name, detail.FormDefinition.Title);
        Assert.Equal(new[] { "solicitante", "motivo" }, detail.FormDefinition.Sections.Select(s => s.Id));
        Assert.Equal(2, detail.FormDefinition.Sections[1].Fields[0].Options.Count);
    }

    [Fact]
    public void Una_pregunta_sin_identificador_se_omite_porque_la_respuesta_no_tendria_clave()
    {
        var result = Run(Content(sections: new[]
        {
            Section(fields: new[] { Field(id: " "), Field("correo", "Correo electrónico", "email") }),
        }));

        var fields = result.Value!.FormDefinition.Sections[0].Fields;
        Assert.Equal(new[] { "correo" }, fields.Select(f => f.Id));
        Assert.NotEmpty(result.Issues);
    }

    [Fact]
    public void Una_pregunta_sin_etiqueta_se_omite_porque_no_hay_nada_que_preguntar()
    {
        var result = Run(Content(sections: new[]
        {
            Section(fields: new[] { Field("cedula", label: null), Field("correo", "Correo", "email") }),
        }));

        Assert.Equal(new[] { "correo" }, result.Value!.FormDefinition.Sections[0].Fields.Select(f => f.Id));
    }

    [Fact]
    public void Un_identificador_de_pregunta_repetido_en_TODO_el_formulario_se_omite_y_es_un_ERROR()
    {
        // FormData es un diccionario por id: la segunda respuesta pisaría a la primera en
        // silencio y el expediente quedaría con una sola. El ámbito es el formulario, no la
        // sección: por eso el choque de abajo cruza dos secciones.
        var result = Run(Content(sections: new[]
        {
            Section("solicitante", "Solicitante", new[] { Field("cedula", "Número de cédula") }),
            Section("empresa", "Empresa", new[] { Field("cedula", "Cédula del representante") }),
        }));

        Assert.Single(result.Value!.FormDefinition.Sections);
        Assert.Equal("solicitante", result.Value.FormDefinition.Sections[0].Id);
        Assert.True(HasError(result));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("slider")]
    public void Un_tipo_que_el_formulario_no_sabe_pintar_cae_a_texto_libre(string? type)
    {
        // Un campo invisible es una pregunta que no se puede responder; una mal tipada, sí.
        var result = Run(Content(sections: new[]
        {
            Section(fields: new[] { Field("cedula", "Número de cédula", type) }),
        }));

        Assert.Equal(
            TramiteContentRules.DefaultFieldType,
            result.Value!.FormDefinition.Sections[0].Fields[0].Type);
        Assert.NotEmpty(result.Issues);
    }

    [Theory]
    [InlineData("select")]
    [InlineData("radio")]
    public void Una_lista_sin_opciones_cae_a_texto_libre_en_vez_de_hacer_imposible_radicar(string type)
    {
        // Si además era obligatoria, dejarla como lista vacía bloquearía un procedimiento al que
        // alguien tiene derecho. Ningún error de autoría puede llegar a eso.
        var result = Run(Content(sections: new[]
        {
            Section(fields: new[] { Field("eps", "EPS de preferencia", type, options: Array.Empty<string>()) }),
        }));

        var field = result.Value!.FormDefinition.Sections[0].Fields[0];
        Assert.Equal(TramiteContentRules.DefaultFieldType, field.Type);
        Assert.Empty(field.Options);
        Assert.NotEmpty(result.Issues);
    }

    [Fact]
    public void Las_opciones_repetidas_se_colapsan_porque_serian_respuestas_indistinguibles()
    {
        var result = Run(Content(sections: new[]
        {
            Section(fields: new[]
            {
                Field("motivo", "Motivo", "radio", new[] { "Deterioro", " deterioro ", "Pérdida", "  " }),
            }),
        }));

        var options = result.Value!.FormDefinition.Sections[0].Fields[0].Options;
        Assert.Equal(new[] { "Deterioro", "Pérdida" }, options.Select(o => o.Value));
        // Valor y etiqueta son lo mismo: el editor escribe una opción por línea y eso es lo que
        // el ciudadano lee y lo que queda en el expediente.
        Assert.All(options, o => Assert.Equal(o.Value, o.Label));
        Assert.NotEmpty(result.Issues);
    }

    [Fact]
    public void Un_patron_vacio_viaja_como_null_y_no_como_cadena_vacia()
    {
        var field = Build(Content(sections: new[]
        {
            Section(fields: new[] { Field("cedula", "Número de cédula", "number", pattern: "   ") }),
        }))!.FormDefinition.Sections[0].Fields[0];

        Assert.Null(field.Pattern);
    }

    [Fact]
    public void Una_seccion_sin_titulo_conserva_sus_preguntas_y_solo_pierde_el_encabezado()
    {
        // Quitar la sección entera le quitaría a la entidad datos que necesita para resolver, y
        // eso es peor que un grupo sin título.
        var result = Run(Content(sections: new[]
        {
            Section(title: "  ", fields: new[] { Field("cedula", "Número de cédula") }),
        }));

        var section = result.Value!.FormDefinition.Sections[0];
        Assert.Equal(string.Empty, section.Title);
        Assert.Single(section.Fields);
        Assert.NotEmpty(result.Issues);
    }

    [Fact]
    public void Una_seccion_sin_preguntas_utilizables_se_omite_entera()
    {
        var result = Run(Content(sections: new[]
        {
            Section("vacia", "Sección vacía", Array.Empty<TramiteFieldContent>()),
            Section("solicitante", "Datos del solicitante", new[] { Field() }),
        }));

        Assert.Equal(new[] { "solicitante" }, result.Value!.FormDefinition.Sections.Select(s => s.Id));
        Assert.NotEmpty(result.Issues);
    }

    [Fact]
    public void Una_seccion_sin_identificador_recibe_uno_por_su_posicion()
    {
        var sections = Build(Content(sections: new[]
        {
            Section(id: null, title: "Primera", fields: new[] { Field("a", "A") }),
            Section(id: "  ", title: "Segunda", fields: new[] { Field("b", "B") }),
        }))!.FormDefinition.Sections;

        Assert.Equal(new[] { "seccion-1", "seccion-2" }, sections.Select(s => s.Id));
    }

    [Fact]
    public void Un_tramite_SIN_formulario_se_sirve_igual_pero_es_un_ERROR()
    {
        // Que el trámite exista es información pública a la que el ciudadano tiene derecho
        // aunque la radicación en línea no esté lista. Es ERROR porque además desarma la
        // validación de obligatorios del servidor.
        var result = Run(Content(sections: Array.Empty<TramiteSectionContent>()));

        Assert.NotNull(result.Value);
        Assert.Empty(result.Value!.FormDefinition.Sections);
        Assert.True(HasError(result));
    }

    // ── El caso completo, y que dos lecturas iguales den lo mismo ────────────

    [Fact]
    public void Un_tramite_bien_autorado_sale_completo_y_sin_una_sola_queja()
    {
        var result = Run(Content());

        Assert.NotNull(result.Value);
        Assert.Empty(result.Issues);

        var detail = result.Value!;
        Assert.Equal("Renovación de cédula de ciudadanía", detail.Summary.Name);
        Assert.Equal("Identidad", detail.Summary.Category);
        Assert.Equal("Registraduría Nacional", detail.Summary.Agency);
        Assert.Equal(Cop, detail.Summary.Currency);
        Assert.Equal(15, detail.Summary.EstimatedDays);
        Assert.True(detail.RequiresAppointment);
        Assert.Equal("Decreto 1010 de 2000.", detail.Normativa);
        Assert.Single(detail.Eligibility);
        Assert.Single(detail.Required);
        Assert.Equal(3, detail.Steps.Count);
    }

    [Fact]
    public void Proyectar_dos_veces_el_mismo_tramite_da_el_mismo_resultado()
    {
        var content = Content();

        var first = Run(content);
        var second = Run(content);

        // El resumen sí compara por valor (solo escalares); las listas de la ficha son
        // instancias nuevas en cada proyección, así que se comparan por su contenido.
        Assert.Equal(first.Value!.Summary, second.Value!.Summary);
        Assert.Equal(
            first.Value.FormDefinition.Sections.SelectMany(s => s.Fields).Select(f => f.Id),
            second.Value.FormDefinition.Sections.SelectMany(s => s.Fields).Select(f => f.Id));
        Assert.Equal(first.Issues.Count, second.Issues.Count);
    }
}

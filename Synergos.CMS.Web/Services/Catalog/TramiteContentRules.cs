using System.Globalization;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Web.Services.Catalog;

/// <summary>Lo que el editor escribió en una pregunta del formulario, sin interpretar.</summary>
public sealed record TramiteFieldContent(
    string? Id,
    string? Label,
    string? Type,
    bool Required = false,
    string? Help = null,
    IReadOnlyList<string>? Options = null,
    string? Pattern = null);

/// <summary>Lo que el editor escribió en una sección del formulario, sin interpretar.</summary>
public sealed record TramiteSectionContent(
    string? Id,
    string? Title,
    IReadOnlyList<TramiteFieldContent>? Fields = null);

/// <summary>Lo que el editor escribió en un <c>tramitePage</c>, sin interpretar.</summary>
public sealed record TramitePageContent(
    string? Slug,
    string? Name,
    string? Summary = null,
    string? Category = null,
    string? Agency = null,
    string? Channel = null,
    string? Description = null,
    IReadOnlyList<string>? Eligibility = null,
    IReadOnlyList<string>? Required = null,
    string? Normativa = null,
    bool IsFree = false,
    decimal Fee = 0m,
    int EstimatedDays = 0,
    bool RequiresAppointment = false,
    IReadOnlyList<TramiteSectionContent>? Sections = null);

/// <summary>
/// Los tres pasos del recorrido "qué pasa", resueltos del Dictionary uSync por el llamador.
/// </summary>
/// <remarks>
/// <b>Viajan como parámetro y no se leen aquí</b> por lo mismo que en
/// <see cref="PropertySpecLabels"/> y <see cref="StaySpecLabels"/>: estas reglas son puras y el
/// Dictionary es de Umbraco. Y son texto del SERVIDOR —<see cref="TramiteStep"/> lleva su
/// <c>Title</c> y su <c>Detail</c> ya escritos en el JSON que consume la ficha—, así que su
/// sitio es el Dictionary del CMS y no la i18n del bundle.
/// </remarks>
public sealed record TramiteStepLabels(
    string SubmitTitle,
    string SubmitDetail,
    string ReviewTitle,
    string ReviewDetail,
    string ResolutionTitle,
    string ResolutionDetail);

/// <summary>
/// Las reglas de negocio que convierten un <c>tramitePage</c> autorado en la ficha de trámite
/// que sirve la cara ciudadana del portal de Gobierno.
/// </summary>
/// <remarks>
/// <b>Clase pura</b>, por la misma razón que <see cref="PropertyContentRules"/> y
/// <see cref="StayContentRules"/>: <c>IPublishedContent.Value&lt;T&gt;</c> no se puede simular
/// con coste razonable, y las reglas que deciden si el portal le anuncia a un ciudadano que un
/// trámite es gratuito no podían ser las únicas sin cobertura. Devuelve los problemas en vez de
/// loguearlos; el llamador —que sí tiene <c>ILogger</c>— los emite.
///
/// <para><b>La tesis de este vertical: el portal nunca ESCONDE un trámite que existe; se niega
/// a hacer afirmaciones que la entidad no hizo.</b> Los requisitos y la tasa de un trámite son,
/// literalmente, regulación: no los inventa el portal, los recoge. De ahí sale la asimetría con
/// los otros cuatro verticales —se descarta menos que en ninguno (solo slug, nombre y la tasa
/// ambigua), pero se grita más fuerte: un trámite ausente no suspende la obligación legal de
/// hacerlo, mientras que un "$0" que la entidad no declaró sí es una afirmación falsa sobre un
/// cobro público.</para>
/// </remarks>
public static class TramiteContentRules
{
    /// <summary>Canal por defecto cuando el valor autorado no se reconoce.</summary>
    /// <remarks>
    /// <b>Presencial</b> y no "en línea" a propósito: es la lectura que no puede dejar a un
    /// ciudadano sin trámite. Quien va a la ventanilla de algo que además se podía hacer en
    /// línea pierde una mañana; quien se queda en la casa creyendo que hay canal digital cuando
    /// no lo hay, no radica nunca.
    /// </remarks>
    public const string DefaultChannel = "presencial";

    /// <summary>Tipo de campo por defecto cuando el autorado no sirve para pintar un control.</summary>
    public const string DefaultFieldType = "text";

    private static readonly HashSet<string> KnownChannels =
        new(StringComparer.OrdinalIgnoreCase) { "presencial", "en-linea", "mixto" };

    /// <summary>Los tipos que el formulario dinámico sabe pintar (contrato de la seam).</summary>
    private static readonly HashSet<string> KnownFieldTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "text", "textarea", "number", "date", "select", "radio", "checkbox", "file", "email", "tel",
    };

    /// <summary>Los tipos que no significan nada sin una lista de opciones detrás.</summary>
    private static readonly HashSet<string> OptionBackedTypes =
        new(StringComparer.OrdinalIgnoreCase) { "select", "radio" };

    /// <summary>
    /// La ficha del trámite servible, o null si no se puede servir sin mentir.
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item><b>Sin slug:</b> no hay identidad. Es lo que resuelve <c>GetAsync</c> y lo que
    /// viaja en la URL de la ficha; un trámite sin él es inalcanzable.</item>
    /// <item><b>Sin nombre:</b> NO se cae al nombre del nodo del árbol — "Trámite (1)" no solo
    /// sale en la tarjeta: es el <c>ServiceName</c> que hereda cada expediente radicado y que el
    /// funcionario ve en su bandeja para siempre.</item>
    /// <item><b>Sin marcar gratuito y con tasa ≤ 0:</b> se OMITE. Es la única respuesta honesta
    /// a "¿una tasa en 0 significa gratis o sin publicar?" — significa gratis <b>solo cuando el
    /// editor lo afirmó</b>. Sin esa afirmación, un 0 es un campo sin diligenciar, y publicarlo
    /// anunciaría una gratuidad que la norma no concedió.</item>
    /// </list>
    ///
    /// <para><b>Todo lo demás se degrada, incluida la lista de requisitos vacía.</b> Un trámite
    /// sin requisitos publicados se sirve igual y se registra como ERROR: la lista es un
    /// recordatorio de lo que la norma ya exige, no una afirmación del portal, y esconder el
    /// trámite no libera al ciudadano de tener que hacerlo. Sin categoría tampoco se pierde
    /// —queda fuera del único filtro pero sigue en el listado y en la búsqueda por texto—, y
    /// esa es la diferencia con la ciudad de un inmueble (ADR 0118 §5), que sí lo dejaba
    /// inencontrable.</para>
    /// </remarks>
    public static EventContentResult<TramiteDetail?> BuildTramite(
        TramitePageContent content,
        string currency,
        TramiteStepLabels stepLabels)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(stepLabels);

        var issues = new List<EventContentIssue>();

        var slug = Normalize(content.Slug);
        if (slug.Length == 0)
        {
            issues.Add(Warn("Un trámite no tiene slug; se omite. Sin él la ficha no se puede enlazar."));
            return new EventContentResult<TramiteDetail?>(null, issues);
        }

        var name = content.Name?.Trim();
        if (string.IsNullOrEmpty(name))
        {
            issues.Add(Warn(
                $"El trámite '{slug}' no tiene nombre; se omite. Sería el nombre que hereda cada " +
                "expediente radicado."));
            return new EventContentResult<TramiteDetail?>(null, issues);
        }

        if (!TryResolveFee(slug, content, issues, out var fee))
        {
            return new EventContentResult<TramiteDetail?>(null, issues);
        }

        var category = content.Category?.Trim() ?? string.Empty;
        if (category.Length == 0)
        {
            // No descarta: el trámite sigue saliendo en el listado completo y en la búsqueda
            // por texto. Solo queda fuera de la única faceta del catálogo.
            issues.Add(Warn(
                $"El trámite '{slug}' no tiene categoría; no saldrá en el filtro por categoría."));
        }

        var agency = content.Agency?.Trim() ?? string.Empty;
        if (agency.Length == 0)
        {
            issues.Add(Warn(
                $"El trámite '{slug}' no declara entidad; el ciudadano no sabrá ante quién radica."));
        }

        var channel = Normalize(content.Channel);
        if (!KnownChannels.Contains(channel))
        {
            issues.Add(Warn(
                $"El trámite '{slug}' declara canal '{channel}', que no es presencial, en-linea ni " +
                $"mixto; se anuncia como {DefaultChannel}."));
            channel = DefaultChannel;
        }

        var estimatedDays = content.EstimatedDays;
        if (estimatedDays < 0)
        {
            issues.Add(Warn(
                $"El trámite '{slug}' declara {estimatedDays} días estimados; se sirve 0 y la ficha no " +
                "promete plazo."));
            estimatedDays = 0;
        }

        var required = EventContentRules.CleanTextList(content.Required);
        if (required.Count == 0)
        {
            // ERROR y NO descarta: es el único degradado que se registra al nivel del que se
            // omite. La lista de documentos es lo que evita que alguien pierda un día de
            // trabajo en una ventanilla, y es lo primero que un editor olvida.
            issues.Add(Err(
                $"El trámite '{slug}' no publica documentos requeridos. Se sirve igual —esconderlo no " +
                "libera al ciudadano de hacerlo— pero llegará a la ventanilla sin los papeles."));
        }

        var summary = new TramiteSummary(
            // El slug ES el id, igual que en Tienda, Eventos, Inmobiliaria y Booking: GetAsync
            // casa por cualquiera de los dos, y un segundo identificador que nadie autora solo
            // abre la puerta a que discrepen.
            Id: slug,
            Slug: slug,
            Name: name,
            Summary: content.Summary?.Trim() ?? string.Empty,
            Category: category,
            Agency: agency,
            Channel: channel,
            EstimatedDays: estimatedDays,
            FeeMinor: fee,
            Currency: currency);

        var detail = new TramiteDetail(
            Summary: summary,
            Description: content.Description?.Trim() ?? string.Empty,
            Steps: BuildSteps(stepLabels),
            Eligibility: EventContentRules.CleanTextList(content.Eligibility),
            Required: required,
            Normativa: content.Normativa?.Trim() ?? string.Empty,
            FormDefinition: BuildForm(slug, name, content.Sections, issues),
            RequiresAppointment: content.RequiresAppointment);

        return new EventContentResult<TramiteDetail?>(detail, issues);
    }

    /// <summary>
    /// La tasa del trámite, o <c>false</c> si la declaración es ambigua y no se puede publicar.
    /// </summary>
    /// <remarks>
    /// <b>Aquí se resuelve el "¿0 es gratis o es sin publicar?".</b> En Inmobiliaria un precio en
    /// 0 descarta siempre (ADR 0118 §5) porque todo inmueble tiene precio. En Gobierno la
    /// gratuidad es lo normal y muchas veces la manda la norma: descartar todo trámite en 0
    /// borraría justo los que el Estado más quiere que la gente use. Por eso el DocType tiene una
    /// casilla aparte, y el 0 significa gratis <b>únicamente</b> cuando esa casilla lo afirma.
    ///
    /// <para><b>Y cuando las dos declaraciones se contradicen gana la TASA, no la casilla.</b> Es
    /// el criterio de "la lectura que no puede dejar el viaje perdido": a quien se le anuncia
    /// $65.500 y resulta exento no le pasa nada; a quien se le anuncia gratis y hay que pagar en
    /// caja, se le acabó el día. La casilla existe para volver intencional el 0, no para tapar un
    /// número que el editor escribió.</para>
    /// </remarks>
    public static bool TryResolveFee(
        string slug,
        TramitePageContent content,
        IList<EventContentIssue> issues,
        out decimal fee)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(issues);

        if (content.Fee > 0m)
        {
            fee = content.Fee;
            if (content.IsFree)
            {
                issues.Add(Err(
                    $"El trámite '{slug}' está marcado como gratuito y a la vez cobra {content.Fee}; se " +
                    "sirve COBRANDO. Anunciar gratis lo que se paga deja al ciudadano en la caja sin plata."));
            }

            return true;
        }

        if (content.IsFree)
        {
            fee = 0m;
            return true;
        }

        issues.Add(Err(
            $"El trámite '{slug}' tiene tasa {content.Fee} y no está marcado como gratuito; se OMITE. " +
            "Una tasa en cero sin esa declaración es un campo sin diligenciar, y publicarla anunciaría " +
            "una gratuidad que la entidad no declaró."));
        fee = 0m;
        return false;
    }

    /// <summary>
    /// El recorrido "qué pasa" de la ficha, DERIVADO de la máquina de estados del expediente.
    /// </summary>
    /// <remarks>
    /// <b>No se autora, y es la decisión de schema más fuerte de esta rebanada.</b> La otra
    /// opción era un BlockList de "Paso / Detalle", y se descartó por una razón más dura que la
    /// del ADR 0118 §2: los pasos no describen el trámite, describen el <b>expediente</b>, y ese
    /// recorre siempre los mismos estados (<c>radicado → en-revisión → resuelto/rechazado</c>)
    /// porque los fija <c>ICaseWorkflowService</c>, no el editor.
    ///
    /// <para>Si cada entidad escribiera los suyos, la lista de "qué pasa" de la ficha y la
    /// <c>Timeline</c> que el ciudadano ve dentro de su expediente contarían dos historias
    /// distintas del mismo procedimiento — y ninguna capa fallaría al hacerlo. En un portal de
    /// Estado eso no es un detalle de copy: es que el ciudadano no sabe cuál de las dos es el
    /// procedimiento real.</para>
    ///
    /// <para>Los ids se calcan del stub (<c>radicar</c>, <c>revision</c>, <c>resolucion</c>) para
    /// que mover el flag a <c>cms</c> no cambie lo que la pantalla ya recibía.</para>
    /// </remarks>
    public static IReadOnlyList<TramiteStep> BuildSteps(TramiteStepLabels labels)
    {
        ArgumentNullException.ThrowIfNull(labels);

        return new[]
        {
            new TramiteStep("radicar", labels.SubmitTitle, labels.SubmitDetail),
            new TramiteStep("revision", labels.ReviewTitle, labels.ReviewDetail),
            new TramiteStep("resolucion", labels.ResolutionTitle, labels.ResolutionDetail),
        };
    }

    /// <summary>
    /// El formulario dinámico de radicación, saneado sección por sección.
    /// </summary>
    /// <remarks>
    /// <b>Este SÍ se autora como bloques anidados</b>, al revés que las características de un
    /// inmueble o los ejes de reseñas de una estadía, y la diferencia no es de gusto: allá la
    /// libertad mataba la comparación entre fichas, aquí la variación ES el producto. Un
    /// formulario de renovación de cédula y uno de subsidio de vivienda no comparten ni una
    /// pregunta, y el patrón GOV.UK de "una tarea por sección" existe precisamente para que cada
    /// trámite varíe sin tocar el módulo.
    ///
    /// <para>Lo que sí se cierra es el <b>tipo</b> de cada campo: es un vocabulario del producto
    /// (lo que el renderer sabe pintar), no de la entidad, así que va por lista desplegable y no
    /// por texto libre.</para>
    ///
    /// <para><b>Un formulario vacío no descarta el trámite.</b> La existencia del trámite —su
    /// nombre, su entidad, su tasa, su normativa— es información pública a la que el ciudadano
    /// tiene derecho aunque la radicación en línea todavía no esté lista. Se registra como ERROR
    /// porque además desarma la validación de obligatorios del servidor: sin campos declarados,
    /// cualquier radicación pasa.</para>
    /// </remarks>
    public static TramiteFormDefinition BuildForm(
        string slug,
        string title,
        IReadOnlyList<TramiteSectionContent>? sections,
        IList<EventContentIssue> issues)
    {
        ArgumentNullException.ThrowIfNull(issues);

        var built = new List<TramiteFormSection>();
        // Los ids de campo son las CLAVES del diccionario de respuestas del expediente, así que
        // el ámbito de unicidad es el formulario entero y no la sección.
        var seenFieldIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var section in sections ?? Array.Empty<TramiteSectionContent>())
        {
            if (section is null)
            {
                continue;
            }

            var fields = BuildFields(slug, section, seenFieldIds, issues);
            if (fields.Count == 0)
            {
                issues.Add(Warn(
                    $"El trámite '{slug}' tiene una sección sin preguntas utilizables; se omite la sección."));
                continue;
            }

            var sectionTitle = section.Title?.Trim() ?? string.Empty;
            if (sectionTitle.Length == 0)
            {
                // Se pierde el encabezado, NO las preguntas: quitar la sección entera le quitaría
                // a la entidad datos que necesita para resolver, y eso es peor que un grupo sin
                // título.
                issues.Add(Warn(
                    $"El trámite '{slug}' tiene una sección sin título; sus {fields.Count} preguntas salen " +
                    "sin encabezado."));
            }

            var sectionId = Normalize(section.Id);
            if (sectionId.Length == 0)
            {
                sectionId = $"seccion-{(built.Count + 1).ToString(CultureInfo.InvariantCulture)}";
            }

            built.Add(new TramiteFormSection(sectionId, sectionTitle, fields));
        }

        if (built.Count == 0)
        {
            issues.Add(Err(
                $"El trámite '{slug}' no tiene formulario utilizable; la ficha sale pero no se puede " +
                "radicar, y el servidor no tendrá campos obligatorios que exigir."));
        }

        return new TramiteFormDefinition($"form-{slug}", title, built);
    }

    /// <summary>
    /// Las preguntas utilizables de una sección, en el orden en que el editor las puso.
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item><b>Sin identificador o sin etiqueta:</b> se omite la pregunta. Sin id la respuesta
    /// se guardaría bajo una clave vacía y el funcionario no sabría qué contestó el ciudadano;
    /// sin etiqueta la pregunta no se puede formular.</item>
    /// <item><b>Identificador repetido en el mismo formulario:</b> se omite la segunda y se grita.
    /// <c>CaseDetail.FormData</c> es un diccionario por id: la segunda respuesta pisaría a la
    /// primera en silencio, y el expediente quedaría con una sola. No se corrige renombrando —
    /// inventar <c>cedula2</c> le cambia el significado al dato.</item>
    /// <item><b>Tipo desconocido:</b> cae a texto libre. Un tipo que el renderer no sabe pintar
    /// es un campo invisible, y una pregunta que se puede responder mal es mejor que una que no
    /// se puede responder.</item>
    /// <item><b>Lista o botones sin opciones:</b> también cae a texto libre. Si además era
    /// obligatoria, dejarla como lista vacía hace el trámite IMPOSIBLE de radicar — y ningún
    /// error de autoría puede bloquear un procedimiento al que alguien tiene derecho.</item>
    /// </list>
    /// </remarks>
    public static IReadOnlyList<TramiteFormField> BuildFields(
        string slug,
        TramiteSectionContent section,
        HashSet<string> seenFieldIds,
        IList<EventContentIssue> issues)
    {
        ArgumentNullException.ThrowIfNull(section);
        ArgumentNullException.ThrowIfNull(seenFieldIds);
        ArgumentNullException.ThrowIfNull(issues);

        var fields = new List<TramiteFormField>();

        foreach (var raw in section.Fields ?? Array.Empty<TramiteFieldContent>())
        {
            if (raw is null)
            {
                continue;
            }

            var id = raw.Id?.Trim() ?? string.Empty;
            if (id.Length == 0)
            {
                issues.Add(Warn(
                    $"El trámite '{slug}' tiene una pregunta sin identificador; se omite. La respuesta se " +
                    "guardaría sin clave."));
                continue;
            }

            var label = raw.Label?.Trim() ?? string.Empty;
            if (label.Length == 0)
            {
                issues.Add(Warn(
                    $"El trámite '{slug}': la pregunta '{id}' no tiene etiqueta; se omite. Sin ella no hay " +
                    "nada que preguntarle al ciudadano."));
                continue;
            }

            if (!seenFieldIds.Add(id))
            {
                issues.Add(Err(
                    $"El trámite '{slug}' repite el identificador de pregunta '{id}'; se omite la segunda. " +
                    "Las respuestas se guardan por identificador: la segunda pisaría a la primera y el " +
                    "expediente quedaría con una sola."));
                continue;
            }

            var type = Normalize(raw.Type);
            if (!KnownFieldTypes.Contains(type))
            {
                issues.Add(Warn(
                    $"El trámite '{slug}': la pregunta '{id}' declara tipo '{type}', que el formulario no " +
                    $"sabe pintar; se sirve como {DefaultFieldType}."));
                type = DefaultFieldType;
            }

            var options = CleanOptions(slug, id, raw.Options, issues);
            if (OptionBackedTypes.Contains(type) && options.Count == 0)
            {
                issues.Add(Warn(
                    $"El trámite '{slug}': la pregunta '{id}' es de tipo '{type}' y no tiene opciones; se " +
                    $"sirve como {DefaultFieldType}. Una lista vacía y obligatoria haría imposible radicar."));
                type = DefaultFieldType;
                options = Array.Empty<TramiteFieldOption>();
            }

            var pattern = raw.Pattern?.Trim();

            fields.Add(new TramiteFormField(
                Id: id,
                Label: label,
                Type: type,
                Required: raw.Required,
                Help: raw.Help?.Trim() ?? string.Empty,
                Options: options,
                // El contrato de la UI distingue "sin patrón" (null) de "patrón vacío": el
                // controller ya emite el null tal cual, así que la cadena vacía no debe llegar.
                Pattern: string.IsNullOrEmpty(pattern) ? null : pattern));
        }

        return fields;
    }

    /// <summary>
    /// Las opciones de una lista, sin blancos y sin repetidas.
    /// </summary>
    /// <remarks>
    /// Valor y etiqueta son <b>lo mismo</b>: el editor escribe una opción por línea y eso es lo
    /// que el ciudadano lee y lo que queda guardado en el expediente. Inventar una sintaxis
    /// <c>valor|etiqueta</c> dentro de un campo de texto habría metido un mini-lenguaje que
    /// nadie documenta en el sitio donde menos se puede fallar, y el propio seed ya emite las
    /// siete listas con valor igual a etiqueta.
    ///
    /// <para>Las repetidas se colapsan porque dos opciones idénticas en un desplegable son dos
    /// respuestas indistinguibles para el funcionario que después lee el expediente.</para>
    /// </remarks>
    public static IReadOnlyList<TramiteFieldOption> CleanOptions(
        string slug,
        string fieldId,
        IReadOnlyList<string>? raw,
        IList<EventContentIssue> issues)
    {
        ArgumentNullException.ThrowIfNull(issues);

        var cleaned = EventContentRules.CleanTextList(raw);
        if (cleaned.Count == 0)
        {
            return Array.Empty<TramiteFieldOption>();
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var options = new List<TramiteFieldOption>(cleaned.Count);
        var duplicates = 0;

        foreach (var value in cleaned)
        {
            if (seen.Add(value))
            {
                options.Add(new TramiteFieldOption(value, value));
            }
            else
            {
                duplicates++;
            }
        }

        if (duplicates > 0)
        {
            issues.Add(Warn(
                $"El trámite '{slug}': la pregunta '{fieldId}' repite {duplicates} opción(es); se dejan las " +
                "distintas. Dos opciones iguales son dos respuestas indistinguibles."));
        }

        return options;
    }

    private static string Normalize(string? raw)
        => raw?.Trim().ToLowerInvariant() ?? string.Empty;

    private static EventContentIssue Warn(string message)
        => new(EventContentIssueLevel.Warning, message);

    private static EventContentIssue Err(string message)
        => new(EventContentIssueLevel.Error, message);
}

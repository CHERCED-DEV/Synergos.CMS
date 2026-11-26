using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Application.Services.Impl;

/// <summary>
/// Default <see cref="ITramiteCatalogProvider"/> — catálogo STUB del portal de
/// trámites del vertical Gobierno (doc gobierno.md), calcando
/// <c>StubEventCatalogProvider</c> / <c>StubPropertyCatalogProvider</c>. Sirve un
/// catálogo sembrado en memoria (7 trámites reales CO de distintas categorías, cada uno
/// con su formulario dinámico seccionado + pasos + elegibilidad + requisitos + tasa:
/// gratuitos y con-costo) para que el catálogo facetado + la ficha + el wizard de
/// radicación corran end-to-end en demo.
/// </summary>
/// <remarks>
/// Lógica pura/determinista en <c>Synergos.CMS.Application</c> — cero dependencia de
/// Umbraco/AspNetCore (ADR 0002). El search filtra por texto (nombre/entidad/categoría/
/// resumen) + categoría en AND. El adapter real (SUIT / catálogo de la entidad vía
/// Content Delivery API) implementa la misma seam y se registra en su lugar vía el
/// composer sin tocar el motor. ADR 0075.
/// </remarks>
public sealed class StubTramiteCatalogProvider : ITramiteCatalogProvider
{
    private const string Cop = "COP";

    private readonly IReadOnlyList<TramiteDetail> _catalog = Seed();
    private readonly ICatalogIndex<TramiteSummary> _index;

    /// <summary>Ctor por defecto: el motor, sin tope de página (esta seam devuelve todo).</summary>
    public StubTramiteCatalogProvider()
        : this(new InMemoryCatalogIndex<TramiteSummary>(Descriptor, CatalogSettings.Unpaged))
    {
    }

    internal StubTramiteCatalogProvider(ICatalogIndex<TramiteSummary> index)
        => _index = index ?? throw new ArgumentNullException(nameof(index));

    /// <summary>
    /// Lo que Trámites DECLARA sobre su catálogo. Cero lógica.
    /// </summary>
    /// <remarks>
    /// Los pesos ordenan lo que antes era un OR plano entre cuatro campos: el nombre del
    /// trámite manda, la entidad que lo presta va después, y el resumen —prosa larga que casa
    /// por accidente— pesa lo mínimo para no desplazar nunca a un nombre.
    /// </remarks>
    internal static CatalogDescriptor<TramiteSummary> Descriptor { get; } = new(
        idOf: t => t.Id,
        searchFields: new[]
        {
            new CatalogSearchField<TramiteSummary>(5, t => t.Name),
            new CatalogSearchField<TramiteSummary>(3, t => t.Agency),
            new CatalogSearchField<TramiteSummary>(2, t => t.Category),
            new CatalogSearchField<TramiteSummary>(1, t => t.Summary),
        },
        // El orden histórico: alfabético por nombre. Se preserva para que la demo no cambie
        // de aspecto salvo que se escriba texto (ahí manda la relevancia).
        defaultOrder: tramites => tramites.OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase),
        filters: new CatalogFilter<TramiteSummary>[]
        {
            new CatalogTermFilter<TramiteSummary>("category", "Categoría", t => new[] { t.Category }),
        });

    public Task<IReadOnlyList<TramiteSummary>> SearchAsync(string? query, string? category, CancellationToken cancellationToken = default)
    {
        var filters = string.IsNullOrWhiteSpace(category)
            ? null
            : new Dictionary<string, IReadOnlyList<string>> { ["category"] = new[] { category.Trim() } };

        // Take explícito + CatalogSettings.Unpaged en el ctor: esta seam promete TODAS las
        // coincidencias, y el default del motor (24) las truncaría en silencio.
        var result = _index.Search(
            _catalog.Select(d => d.Summary).ToList(),
            new CatalogQuery(Text: query, Filters: filters, Take: int.MaxValue));

        return Task.FromResult(result.Items);
    }

    public Task<TramiteDetail?> GetAsync(string tramiteId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(tramiteId))
        {
            return Task.FromResult<TramiteDetail?>(null);
        }

        var id = tramiteId.Trim();
        var detail = _catalog.FirstOrDefault(d =>
            string.Equals(d.Summary.Id, id, StringComparison.OrdinalIgnoreCase)
            || string.Equals(d.Summary.Slug, id, StringComparison.OrdinalIgnoreCase));

        return Task.FromResult(detail);
    }

    // Helpers de composición del seed (menos ruido por trámite).
    private static TramiteFieldOption Opt(string value) => new(value, value);

    private static TramiteFormField Field(
        string id, string label, string type, bool required, string help,
        string[]? options = null, string? pattern = null)
        => new(
            id, label, type, required, help,
            (options ?? Array.Empty<string>()).Select(Opt).ToList(),
            pattern);

    private static readonly TramiteStep[] DefaultSteps =
    {
        new("radicar", "Radicar la solicitud", "Diligencie el formulario y adjunte los documentos requeridos."),
        new("revision", "Revisión de la entidad", "Un funcionario valida sus datos y documentos."),
        new("resolucion", "Resolución", "Reciba la respuesta en su carpeta; si el documento es físico, allí se le indica dónde recogerlo."),
    };

    private static IReadOnlyList<TramiteDetail> Seed()
    {
        return new List<TramiteDetail>
        {
            // 1) Identidad — con tasa (renovación de cédula).
            new(
                Summary: new TramiteSummary(
                    Id: "trm-cedula",
                    Slug: "renovacion-cedula",
                    Name: "Duplicado de cédula de ciudadanía",
                    Summary: "Solicite el duplicado de su cédula de ciudadanía por deterioro, pérdida o corrección de datos.",
                    Category: "Identidad",
                    Agency: "Ventanilla de Identificación (ejemplo)",
                    Channel: "presencial",
                    EstimatedDays: 15,
                    FeeMinor: 65_500m,
                    Currency: Cop),
                Description: "El duplicado de la cédula de ciudadanía le permite obtener un documento nuevo por deterioro, pérdida, hurto o rectificación de datos. Aplica para mayores de edad ya registrados. Es un trámite de ejemplo de esta demostración.",
                Steps: DefaultSteps,
                Eligibility: new[]
                {
                    "Ser mayor de edad colombiano ya registrado.",
                    "Tener la cédula deteriorada, perdida o con datos por corregir.",
                },
                Required: new[]
                {
                    "Denuncio por pérdida o hurto (si aplica).",
                    "Comprobante de pago de la tasa.",
                    "Foto reciente fondo blanco.",
                },
                Normativa: "Decreto 1010 de 2000.",
                FormDefinition: new TramiteFormDefinition(
                    Id: "form-cedula",
                    Title: "Duplicado de cédula de ciudadanía",
                    Sections: new[]
                    {
                        new TramiteFormSection("solicitante", "Datos del solicitante", new[]
                        {
                            Field("nombreCompleto", "Nombre completo (de prueba)", "text", true, "Use un nombre de prueba."),
                            Field("cedula", "Número de cédula (de prueba)", "number", true, "Use un número de prueba, no el suyo."),
                            Field("correo", "Correo electrónico (de prueba)", "email", true, "Use un correo de prueba."),
                        }),
                        new TramiteFormSection("motivo", "Motivo del duplicado", new[]
                        {
                            Field("motivo", "Motivo", "radio", true, "Seleccione una opción.",
                                new[] { "Deterioro", "Pérdida o hurto", "Rectificación de datos" }),
                            Field("observaciones", "Observaciones", "textarea", false, "Opcional. No escriba datos personales."),
                        }),
                    }),
                RequiresAppointment: true),

            // 2) Vehículos — con tasa (licencia de conducción).
            new(
                Summary: new TramiteSummary(
                    Id: "trm-licencia-conduccion",
                    Slug: "renovacion-licencia-conduccion",
                    Name: "Renovación de licencia de conducción",
                    Summary: "Renueve su licencia de conducción vigente o vencida. Requiere examen médico aprobado.",
                    Category: "Vehículos",
                    Agency: "Ventanilla de Tránsito (ejemplo)",
                    Channel: "mixto",
                    EstimatedDays: 5,
                    FeeMinor: 95_000m,
                    Currency: Cop),
                Description: "La renovación de la licencia de conducción actualiza su documento para seguir conduciendo legalmente. Requiere un certificado de aptitud física, mental y de coordinación motriz vigente.",
                Steps: DefaultSteps,
                Eligibility: new[]
                {
                    "Ser titular de una licencia de conducción categoría A o B.",
                    "Contar con examen médico de aptitud vigente.",
                },
                Required: new[] { "Licencia anterior", "Certificado de aptitud física", "Comprobante de pago" },
                Normativa: "Ley 769 de 2002 (Código Nacional de Tránsito).",
                FormDefinition: new TramiteFormDefinition(
                    Id: "form-licencia",
                    Title: "Renovación de licencia de conducción",
                    Sections: new[]
                    {
                        new TramiteFormSection("solicitante", "Datos del solicitante", new[]
                        {
                            Field("nombreCompleto", "Nombre completo (de prueba)", "text", true, "Use un nombre de prueba."),
                            Field("cedula", "Número de cédula (de prueba)", "number", true, "Use un número de prueba, no el suyo."),
                            Field("correo", "Correo electrónico (de prueba)", "email", true, "Use un correo de prueba."),
                        }),
                        new TramiteFormSection("licencia", "Datos de la licencia", new[]
                        {
                            Field("categoria", "Categoría de licencia", "select", true, "Categoría a renovar.",
                                new[] { "A1", "A2", "B1", "B2", "C1" }),
                            Field("examenMedico", "Declaro que tengo examen médico vigente", "checkbox", true, "Obligatorio para renovar."),
                        }),
                    }),
                RequiresAppointment: false),

            // 3) Identidad — GRATUITO (certificado de residencia).
            new(
                Summary: new TramiteSummary(
                    Id: "trm-certificado-residencia",
                    Slug: "certificado-residencia",
                    Name: "Certificado de residencia",
                    Summary: "Obtenga un certificado que acredita su lugar de residencia. Trámite gratuito y 100% en línea.",
                    Category: "Identidad",
                    Agency: "Ventanilla de Atención al Ciudadano (ejemplo)",
                    Channel: "en-linea",
                    EstimatedDays: 2,
                    FeeMinor: 0m,
                    Currency: Cop),
                Description: "El certificado de residencia acredita su domicilio ante entidades públicas y privadas. Es un trámite gratuito que se resuelve completamente en línea.",
                Steps: DefaultSteps,
                Eligibility: new[] { "Ser residente del municipio con domicilio verificable." },
                Required: new[] { "Recibo de servicio público reciente" },
                Normativa: "Acuerdo municipal 012 de 2019.",
                FormDefinition: new TramiteFormDefinition(
                    Id: "form-residencia",
                    Title: "Certificado de residencia",
                    Sections: new[]
                    {
                        new TramiteFormSection("solicitante", "Datos del solicitante", new[]
                        {
                            Field("nombreCompleto", "Nombre completo (de prueba)", "text", true, "Use un nombre de prueba."),
                            Field("cedula", "Número de cédula (de prueba)", "number", true, "Use un número de prueba, no el suyo."),
                            Field("correo", "Correo electrónico (de prueba)", "email", true, "Use un correo de prueba."),
                        }),
                        new TramiteFormSection("domicilio", "Domicilio", new[]
                        {
                            Field("direccion", "Dirección de residencia (de prueba)", "text", true, "Use una dirección de prueba."),
                        }),
                    }),
                RequiresAppointment: false),

            // 4) Empresa — con tasa (registro mercantil).
            new(
                Summary: new TramiteSummary(
                    Id: "trm-registro-mercantil",
                    Slug: "registro-mercantil",
                    Name: "Registro mercantil de empresa",
                    Summary: "Inscriba su empresa en el registro mercantil para operar formalmente.",
                    Category: "Empresa",
                    Agency: "Ventanilla de Empresas (ejemplo)",
                    Channel: "en-linea",
                    EstimatedDays: 3,
                    FeeMinor: 320_000m,
                    Currency: Cop),
                Description: "El registro mercantil formaliza su actividad comercial e inscribe su empresa en el registro público. Es requisito para facturar y contratar legalmente.",
                Steps: DefaultSteps,
                Eligibility: new[] { "Ser persona natural o jurídica que ejerza actividad comercial." },
                Required: new[] { "Documento de constitución", "RUT", "Comprobante de pago" },
                Normativa: "Código de Comercio, art. 19.",
                FormDefinition: new TramiteFormDefinition(
                    Id: "form-mercantil",
                    Title: "Registro mercantil de empresa",
                    Sections: new[]
                    {
                        new TramiteFormSection("empresa", "Datos de la empresa", new[]
                        {
                            Field("razonSocial", "Razón social (de prueba)", "text", true, "Use un nombre de prueba."),
                            Field("nit", "NIT (de prueba)", "number", true, "Use un NIT de prueba, sin dígito de verificación."),
                            Field("actividad", "Actividad económica (CIIU)", "text", true, "Código y descripción."),
                        }),
                        new TramiteFormSection("representante", "Representante legal", new[]
                        {
                            Field("representante", "Nombre del representante legal (de prueba)", "text", true, "Use un nombre de prueba."),
                            Field("correo", "Correo electrónico (de prueba)", "email", true, "Use un correo de prueba."),
                        }),
                    }),
                RequiresAppointment: false),

            // 5) Salud — GRATUITO (afiliación régimen subsidiado), requiere cita.
            new(
                Summary: new TramiteSummary(
                    Id: "trm-afiliacion-salud",
                    Slug: "afiliacion-salud",
                    Name: "Afiliación al régimen subsidiado de salud",
                    Summary: "Afíliese al régimen subsidiado de salud. Trámite gratuito, requiere cita presencial.",
                    Category: "Salud",
                    Agency: "Ventanilla de Salud (ejemplo)",
                    Channel: "presencial",
                    EstimatedDays: 10,
                    FeeMinor: 0m,
                    Currency: Cop),
                Description: "La afiliación al régimen subsidiado da acceso a servicios de salud sin costo a personas clasificadas sin capacidad de pago. En esta demostración la clasificación es de ejemplo.",
                Steps: DefaultSteps,
                Eligibility: new[] { "Estar clasificado sin capacidad de pago." },
                Required: new[] { "Documento de identidad", "Certificado de clasificación" },
                Normativa: "Ley 100 de 1993.",
                FormDefinition: new TramiteFormDefinition(
                    Id: "form-salud",
                    Title: "Afiliación al régimen subsidiado de salud",
                    Sections: new[]
                    {
                        new TramiteFormSection("solicitante", "Datos del solicitante", new[]
                        {
                            Field("nombreCompleto", "Nombre completo (de prueba)", "text", true, "Use un nombre de prueba."),
                            Field("cedula", "Número de documento (de prueba)", "number", true, "Use un número de prueba, no el suyo."),
                            Field("sisben", "Clasificación (ejemplo)", "select", true, "Elija una clasificación de ejemplo.",
                                new[] { "Grupo A (ejemplo)", "Grupo B (ejemplo)", "Grupo C (ejemplo)" }),
                        }),
                        new TramiteFormSection("eps", "EPS", new[]
                        {
                            Field("eps", "EPS de preferencia", "select", true, "Elija una EPS de ejemplo.",
                                new[] { "EPS Ejemplo Norte", "EPS Ejemplo Centro", "EPS Ejemplo Sur" }),
                            Field("correo", "Correo electrónico (de prueba)", "email", false, "Opcional. Use un correo de prueba."),
                        }),
                    }),
                RequiresAppointment: true),

            // 6) Identidad — con tasa (pasaporte), requiere cita.
            new(
                Summary: new TramiteSummary(
                    Id: "trm-pasaporte",
                    Slug: "expedicion-pasaporte",
                    Name: "Expedición de pasaporte",
                    Summary: "Solicite la expedición de su pasaporte para viajar al exterior.",
                    Category: "Identidad",
                    Agency: "Ventanilla de Pasaportes (ejemplo)",
                    Channel: "presencial",
                    EstimatedDays: 8,
                    FeeMinor: 189_000m,
                    Currency: Cop),
                Description: "El pasaporte es el documento de viaje que lo identifica en el exterior. Se expide a ciudadanos con cédula vigente, con cita presencial. Es un trámite de ejemplo de esta demostración.",
                Steps: DefaultSteps,
                Eligibility: new[] { "Ser ciudadano colombiano con documento de identidad vigente." },
                Required: new[] { "Cédula de ciudadanía original", "Foto reciente fondo blanco", "Comprobante de pago de la tasa" },
                Normativa: "Decreto 1067 de 2015.",
                FormDefinition: new TramiteFormDefinition(
                    Id: "form-pasaporte",
                    Title: "Expedición de pasaporte",
                    Sections: new[]
                    {
                        new TramiteFormSection("solicitante", "Datos del solicitante", new[]
                        {
                            Field("nombreCompleto", "Nombre completo (de prueba)", "text", true, "Use un nombre de prueba."),
                            Field("cedula", "Número de cédula (de prueba)", "number", true, "Use un número de prueba, no el suyo."),
                            Field("correo", "Correo electrónico (de prueba)", "email", true, "Use un correo de prueba."),
                        }),
                        new TramiteFormSection("expedicion", "Expedición", new[]
                        {
                            Field("ciudad", "Ciudad de expedición", "select", true, "Sede donde recogerá el pasaporte.",
                                new[] { "Bogotá", "Medellín", "Cali", "Barranquilla" }),
                        }),
                    }),
                RequiresAppointment: true),

            // 7) Subsidios — GRATUITO (subsidio de vivienda).
            new(
                Summary: new TramiteSummary(
                    Id: "trm-subsidio-vivienda",
                    Slug: "subsidio-vivienda",
                    Name: "Solicitud de subsidio de vivienda",
                    Summary: "Postúlese al subsidio familiar de vivienda para compra o mejoramiento.",
                    Category: "Subsidios",
                    Agency: "Ventanilla de Vivienda (ejemplo)",
                    Channel: "en-linea",
                    EstimatedDays: 30,
                    FeeMinor: 0m,
                    Currency: Cop),
                Description: "El subsidio familiar de vivienda es un aporte estatal para facilitar la compra, construcción o mejoramiento de vivienda de interés social.",
                Steps: DefaultSteps,
                Eligibility: new[]
                {
                    "Ser jefe de hogar sin vivienda propia.",
                    "Tener ingresos familiares dentro del rango del programa.",
                },
                Required: new[] { "Certificado de ingresos", "Declaración de no poseer vivienda", "Documento de identidad" },
                Normativa: "Ley 1537 de 2012.",
                FormDefinition: new TramiteFormDefinition(
                    Id: "form-subsidio",
                    Title: "Solicitud de subsidio de vivienda",
                    Sections: new[]
                    {
                        new TramiteFormSection("hogar", "Datos del hogar", new[]
                        {
                            Field("nombreCompleto", "Nombre del jefe de hogar (de prueba)", "text", true, "Use un nombre de prueba."),
                            Field("cedula", "Número de cédula (de prueba)", "number", true, "Use un número de prueba, no el suyo."),
                            Field("integrantes", "Número de integrantes del hogar", "number", true, ""),
                            Field("ingresos", "Rango de ingresos del hogar (ejemplo)", "select", true, "Elija un rango de ejemplo.",
                                new[] { "Hasta 2 salarios mínimos", "Entre 2 y 4 salarios mínimos" }),
                        }),
                        new TramiteFormSection("solicitud", "Solicitud", new[]
                        {
                            Field("modalidad", "Modalidad", "radio", true, "Seleccione una opción.",
                                new[] { "Compra de vivienda nueva", "Construcción en sitio propio", "Mejoramiento" }),
                            Field("correo", "Correo electrónico (de prueba)", "email", true, "Use un correo de prueba."),
                        }),
                    }),
                RequiresAppointment: false),
        };
    }
}

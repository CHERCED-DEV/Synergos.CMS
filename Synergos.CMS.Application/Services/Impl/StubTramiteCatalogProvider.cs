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

    public Task<IReadOnlyList<TramiteSummary>> SearchAsync(string? query, string? category, CancellationToken cancellationToken = default)
    {
        var matches = _catalog
            .Select(d => d.Summary)
            .Where(t => MatchesText(t, query))
            .Where(t => string.IsNullOrWhiteSpace(category)
                || string.Equals(t.Category, category, StringComparison.OrdinalIgnoreCase))
            .OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return Task.FromResult<IReadOnlyList<TramiteSummary>>(matches);
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

    private static bool MatchesText(TramiteSummary t, string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return true;
        }
        var q = query.Trim();
        return t.Name.Contains(q, StringComparison.OrdinalIgnoreCase)
            || t.Agency.Contains(q, StringComparison.OrdinalIgnoreCase)
            || t.Category.Contains(q, StringComparison.OrdinalIgnoreCase)
            || t.Summary.Contains(q, StringComparison.OrdinalIgnoreCase);
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
        new("resolucion", "Resolución", "Recibe la respuesta y el documento resultante en su carpeta."),
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
                    Name: "Renovación de cédula de ciudadanía",
                    Summary: "Renueve su cédula de ciudadanía por deterioro, pérdida o actualización de datos.",
                    Category: "Identidad",
                    Agency: "Registraduría Nacional",
                    Channel: "presencial",
                    EstimatedDays: 15,
                    FeeMinor: 65_500m,
                    Currency: Cop),
                Description: "La renovación de la cédula de ciudadanía le permite obtener un documento nuevo por deterioro, pérdida, hurto o rectificación de datos. Aplica para mayores de edad ya registrados.",
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
                    Title: "Renovación de cédula de ciudadanía",
                    Sections: new[]
                    {
                        new TramiteFormSection("solicitante", "Datos del solicitante", new[]
                        {
                            Field("nombreCompleto", "Nombre completo", "text", true, "Como aparece en el registro civil."),
                            Field("cedula", "Número de cédula actual", "number", true, "Solo números, sin puntos."),
                            Field("correo", "Correo electrónico", "email", true, "Para notificarle el resultado."),
                            Field("telefono", "Teléfono de contacto", "tel", true, "Celular con indicativo."),
                        }),
                        new TramiteFormSection("motivo", "Motivo de la renovación", new[]
                        {
                            Field("motivo", "Motivo", "radio", true, "Seleccione una opción.",
                                new[] { "Deterioro", "Pérdida o hurto", "Rectificación de datos" }),
                            Field("observaciones", "Observaciones", "textarea", false, "Detalle adicional (opcional)."),
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
                    Agency: "Secretaría de Movilidad",
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
                            Field("nombreCompleto", "Nombre completo", "text", true, ""),
                            Field("cedula", "Número de cédula", "number", true, "Solo números, sin puntos."),
                            Field("correo", "Correo electrónico", "email", true, ""),
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
                    Agency: "Alcaldía Municipal",
                    Channel: "en-linea",
                    EstimatedDays: 2,
                    FeeMinor: 0m,
                    Currency: Cop),
                Description: "El certificado de residencia acredita tu domicilio ante entidades públicas y privadas. Es un trámite gratuito que se resuelve completamente en línea.",
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
                            Field("nombreCompleto", "Nombre completo", "text", true, ""),
                            Field("cedula", "Número de cédula", "number", true, "Solo números, sin puntos."),
                            Field("correo", "Correo electrónico", "email", true, ""),
                        }),
                        new TramiteFormSection("domicilio", "Domicilio", new[]
                        {
                            Field("direccion", "Dirección de residencia", "text", true, ""),
                            Field("barrio", "Barrio", "text", false, "Opcional."),
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
                    Agency: "Cámara de Comercio",
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
                            Field("razonSocial", "Razón social", "text", true, ""),
                            Field("nit", "NIT", "number", true, "Sin dígito de verificación."),
                            Field("actividad", "Actividad económica (CIIU)", "text", true, "Código y descripción."),
                        }),
                        new TramiteFormSection("representante", "Representante legal", new[]
                        {
                            Field("representante", "Nombre del representante legal", "text", true, ""),
                            Field("correo", "Correo electrónico", "email", true, ""),
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
                    Agency: "Secretaría de Salud",
                    Channel: "presencial",
                    EstimatedDays: 10,
                    FeeMinor: 0m,
                    Currency: Cop),
                Description: "La afiliación al régimen subsidiado garantiza su acceso a servicios de salud sin costo, para personas clasificadas en el SISBÉN sin capacidad de pago.",
                Steps: DefaultSteps,
                Eligibility: new[] { "Estar clasificado en el SISBÉN sin capacidad de pago." },
                Required: new[] { "Documento de identidad", "Certificado SISBÉN" },
                Normativa: "Ley 100 de 1993.",
                FormDefinition: new TramiteFormDefinition(
                    Id: "form-salud",
                    Title: "Afiliación al régimen subsidiado de salud",
                    Sections: new[]
                    {
                        new TramiteFormSection("solicitante", "Datos del solicitante", new[]
                        {
                            Field("nombreCompleto", "Nombre completo", "text", true, ""),
                            Field("cedula", "Número de documento", "number", true, ""),
                            Field("sisben", "Puntaje SISBÉN", "text", true, "Grupo y puntaje (ej. C5)."),
                        }),
                        new TramiteFormSection("eps", "EPS", new[]
                        {
                            Field("eps", "EPS de preferencia", "select", true, "Elija su EPS.",
                                new[] { "Nueva EPS", "Coosalud", "Mutual SER" }),
                            Field("correo", "Correo electrónico", "email", false, "Opcional."),
                        }),
                    }),
                RequiresAppointment: true),

            // 6) Identidad — con tasa (pasaporte), requiere cita.
            new(
                Summary: new TramiteSummary(
                    Id: "trm-pasaporte",
                    Slug: "expedicion-pasaporte",
                    Name: "Expedición de pasaporte",
                    Summary: "Solicite la expedición de su pasaporte colombiano para viajar al exterior.",
                    Category: "Identidad",
                    Agency: "Cancillería",
                    Channel: "presencial",
                    EstimatedDays: 8,
                    FeeMinor: 189_000m,
                    Currency: Cop),
                Description: "El pasaporte es el documento de viaje que lo identifica en el exterior. Se expide a ciudadanos colombianos con cédula vigente, con cita presencial.",
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
                            Field("nombreCompleto", "Nombre completo", "text", true, ""),
                            Field("cedula", "Número de cédula", "number", true, ""),
                            Field("fechaNacimiento", "Fecha de nacimiento", "date", true, ""),
                            Field("correo", "Correo electrónico", "email", true, ""),
                        }),
                        new TramiteFormSection("expedicion", "Expedición", new[]
                        {
                            Field("ciudad", "Ciudad de expedición", "select", true, "Sede donde recogerás el pasaporte.",
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
                    Agency: "Ministerio de Vivienda",
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
                            Field("nombreCompleto", "Nombre del jefe de hogar", "text", true, ""),
                            Field("cedula", "Número de cédula", "number", true, ""),
                            Field("integrantes", "Número de integrantes del hogar", "number", true, ""),
                            Field("ingresos", "Ingresos familiares mensuales (COP)", "number", true, "Suma de todos los ingresos."),
                        }),
                        new TramiteFormSection("solicitud", "Solicitud", new[]
                        {
                            Field("modalidad", "Modalidad", "radio", true, "Seleccione una opción.",
                                new[] { "Compra de vivienda nueva", "Construcción en sitio propio", "Mejoramiento" }),
                            Field("correo", "Correo electrónico", "email", true, ""),
                        }),
                    }),
                RequiresAppointment: false),
        };
    }
}

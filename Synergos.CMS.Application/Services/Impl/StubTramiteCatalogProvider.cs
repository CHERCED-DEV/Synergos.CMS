using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Application.Services.Impl;

/// <summary>
/// Default <see cref="ITramiteCatalogProvider"/> — catálogo STUB del portal de
/// trámites del vertical Gobierno (doc gobierno-app-spec), calcando
/// <c>StubEventCatalogProvider</c> / <c>StubPropertyCatalogProvider</c>. Sirve un
/// catálogo sembrado en memoria (varios trámites de distintas categorías, con su
/// formulario dinámico + requisitos + tasa: gratuitos y con-costo) para que el catálogo
/// facetado + la ficha + el wizard de radicación corran end-to-end en demo.
/// </summary>
/// <remarks>
/// Lógica pura/determinista en <c>Synergos.CMS.Application</c> — cero dependencia de
/// Umbraco/AspNetCore (ADR 0002). El search filtra por texto (nombre/entidad/categoría)
/// + categoría en AND. El adapter real (SUIT / catálogo de la entidad vía Content
/// Delivery API) implementa la misma seam y se registra en su lugar vía el composer sin
/// tocar el motor. ADR 0075.
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
            || t.Entity.Contains(q, StringComparison.OrdinalIgnoreCase)
            || t.Category.Contains(q, StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<TramiteDetail> Seed()
    {
        return new List<TramiteDetail>
        {
            // 1) Identidad — con tasa (pasaporte).
            new(
                Summary: new TramiteSummary(
                    Id: "trm-pasaporte",
                    Slug: "expedicion-pasaporte",
                    Name: "Expedición de pasaporte",
                    Entity: "Cancillería",
                    Category: "Identidad",
                    Channel: "presencial",
                    EstimatedTime: "8 días hábiles",
                    Fee: 189_000m,
                    Currency: Cop),
                Description: "Solicita la expedición de tu pasaporte colombiano. Aplica para mayores de edad con cédula vigente.",
                EligibilityText: "Ciudadanos colombianos con documento de identidad vigente.",
                Normativa: "Decreto 1067 de 2015.",
                FormDefinition: new TramiteFormDefinition(new List<TramiteFormField>
                {
                    new("nombreCompleto", "Nombre completo", "text", true, Array.Empty<string>()),
                    new("cedula", "Número de cédula", "number", true, Array.Empty<string>()),
                    new("fechaNacimiento", "Fecha de nacimiento", "date", true, Array.Empty<string>()),
                    new("ciudad", "Ciudad de expedición", "select", true, new[] { "Bogotá", "Medellín", "Cali", "Barranquilla" }),
                    new("correo", "Correo electrónico", "email", true, Array.Empty<string>()),
                }),
                Requirements: new[] { "Cédula de ciudadanía original", "Foto reciente fondo blanco", "Comprobante de pago de la tasa" },
                Fee: 189_000m,
                Currency: Cop,
                RequiresAppointment: true),

            // 2) Vehículos — con tasa (licencia).
            new(
                Summary: new TramiteSummary(
                    Id: "trm-licencia-conduccion",
                    Slug: "renovacion-licencia-conduccion",
                    Name: "Renovación de licencia de conducción",
                    Entity: "Secretaría de Movilidad",
                    Category: "Vehículos",
                    Channel: "mixto",
                    EstimatedTime: "5 días hábiles",
                    Fee: 95_000m,
                    Currency: Cop),
                Description: "Renueva tu licencia de conducción vigente o vencida. Requiere examen médico aprobado.",
                EligibilityText: "Titulares de licencia de conducción categoría A o B.",
                Normativa: "Ley 769 de 2002 (Código Nacional de Tránsito).",
                FormDefinition: new TramiteFormDefinition(new List<TramiteFormField>
                {
                    new("nombreCompleto", "Nombre completo", "text", true, Array.Empty<string>()),
                    new("cedula", "Número de cédula", "number", true, Array.Empty<string>()),
                    new("categoria", "Categoría de licencia", "select", true, new[] { "A1", "A2", "B1", "B2", "C1" }),
                    new("examenMedico", "¿Tiene examen médico vigente?", "checkbox", true, Array.Empty<string>()),
                    new("correo", "Correo electrónico", "email", true, Array.Empty<string>()),
                }),
                Requirements: new[] { "Licencia anterior", "Certificado de aptitud física", "Comprobante de pago" },
                Fee: 95_000m,
                Currency: Cop,
                RequiresAppointment: false),

            // 3) Tributario — GRATUITO (certificado).
            new(
                Summary: new TramiteSummary(
                    Id: "trm-certificado-residencia",
                    Slug: "certificado-residencia",
                    Name: "Certificado de residencia",
                    Entity: "Alcaldía Municipal",
                    Category: "Identidad",
                    Channel: "en-linea",
                    EstimatedTime: "2 días hábiles",
                    Fee: 0m,
                    Currency: Cop),
                Description: "Obtén un certificado que acredita tu lugar de residencia. Trámite gratuito y 100% en línea.",
                EligibilityText: "Residentes del municipio con domicilio verificable.",
                Normativa: "Acuerdo municipal 012 de 2019.",
                FormDefinition: new TramiteFormDefinition(new List<TramiteFormField>
                {
                    new("nombreCompleto", "Nombre completo", "text", true, Array.Empty<string>()),
                    new("cedula", "Número de cédula", "number", true, Array.Empty<string>()),
                    new("direccion", "Dirección de residencia", "text", true, Array.Empty<string>()),
                    new("barrio", "Barrio", "text", false, Array.Empty<string>()),
                    new("correo", "Correo electrónico", "email", true, Array.Empty<string>()),
                }),
                Requirements: new[] { "Recibo de servicio público reciente" },
                Fee: 0m,
                Currency: Cop,
                RequiresAppointment: false),

            // 4) Empresa — con tasa (registro mercantil).
            new(
                Summary: new TramiteSummary(
                    Id: "trm-registro-mercantil",
                    Slug: "registro-mercantil",
                    Name: "Registro mercantil de empresa",
                    Entity: "Cámara de Comercio",
                    Category: "Empresa",
                    Channel: "en-linea",
                    EstimatedTime: "3 días hábiles",
                    Fee: 320_000m,
                    Currency: Cop),
                Description: "Inscribe tu empresa en el registro mercantil para operar formalmente.",
                EligibilityText: "Personas naturales o jurídicas que ejerzan actividad comercial.",
                Normativa: "Código de Comercio, art. 19.",
                FormDefinition: new TramiteFormDefinition(new List<TramiteFormField>
                {
                    new("razonSocial", "Razón social", "text", true, Array.Empty<string>()),
                    new("nit", "NIT", "number", true, Array.Empty<string>()),
                    new("actividad", "Actividad económica (CIIU)", "text", true, Array.Empty<string>()),
                    new("representante", "Representante legal", "text", true, Array.Empty<string>()),
                    new("correo", "Correo electrónico", "email", true, Array.Empty<string>()),
                }),
                Requirements: new[] { "Documento de constitución", "RUT", "Comprobante de pago" },
                Fee: 320_000m,
                Currency: Cop,
                RequiresAppointment: false),

            // 5) Salud — GRATUITO (afiliación).
            new(
                Summary: new TramiteSummary(
                    Id: "trm-afiliacion-salud",
                    Slug: "afiliacion-salud",
                    Name: "Afiliación al régimen subsidiado de salud",
                    Entity: "Secretaría de Salud",
                    Category: "Salud",
                    Channel: "presencial",
                    EstimatedTime: "10 días hábiles",
                    Fee: 0m,
                    Currency: Cop),
                Description: "Afíliate al régimen subsidiado de salud. Trámite gratuito, requiere cita presencial.",
                EligibilityText: "Personas sin capacidad de pago clasificadas en el SISBÉN.",
                Normativa: "Ley 100 de 1993.",
                FormDefinition: new TramiteFormDefinition(new List<TramiteFormField>
                {
                    new("nombreCompleto", "Nombre completo", "text", true, Array.Empty<string>()),
                    new("cedula", "Número de documento", "number", true, Array.Empty<string>()),
                    new("sisben", "Puntaje SISBÉN", "text", true, Array.Empty<string>()),
                    new("eps", "EPS de preferencia", "select", true, new[] { "Nueva EPS", "Coosalud", "Mutual SER" }),
                    new("correo", "Correo electrónico", "email", false, Array.Empty<string>()),
                }),
                Requirements: new[] { "Documento de identidad", "Certificado SISBÉN" },
                Fee: 0m,
                Currency: Cop,
                RequiresAppointment: true),
        };
    }
}

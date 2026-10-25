using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Application.Configuration;

/// <summary>
/// La configuración de negocio de la academia (ADR 0137) — sección <c>Synergos:Features:Academy</c>.
/// </summary>
/// <remarks>
/// Antes, dónde vive la API estaba tres veces: campo del editor, valor por defecto en la vista
/// Razor y <c>DEFAULT_API_BASE</c> en el bundle. Medido el 2026-10-02 en la base: el editor nunca
/// lo cambió. Ahora es del despliegue, por sitio (#196).
/// </remarks>
public sealed class AcademyFeatureSettings : SeccionDeNegocio<AcademyFeatureSitio, NegocioDeAcademy>
{
    /// <summary>La sección de configuración.</summary>
    public const string Seccion = "Synergos:Features:Academy";

    /// <summary>Dónde vive la API de la funcionalidad: relativa al sitio o absoluta.</summary>
    public string ApiBase { get; init; } = "/api/academy";

    /// <summary>
    /// Por dónde se matricula: <c>puerta</c> (el flujo <c>educacion.matricula</c>, con sesión) o <c>directa</c> (la ruta
    /// de la academia, con invitados como siempre). Sin valor, <c>directa</c> (010-educacion-matricula, como 006-P5 y
    /// 008-P1): desplegar el orquestador no le pide cuenta a nadie, y un sitio declara <c>puerta</c> después de
    /// verificarla en vivo.
    /// </summary>
    public string? EnrollmentPath { get; init; }

    /// <inheritdoc />
    protected override NegocioDeAcademy Fusionar(AcademyFeatureSitio? propio)
        => new(
            ApiBase: propio?.ApiBase ?? ApiBase,
            EnrollmentPath: CaminoDelFlujo.Normalizar(propio?.EnrollmentPath ?? EnrollmentPath) ?? CaminoDelFlujo.Directa);

    /// <inheritdoc />
    protected override IEnumerable<string> ProblemasDe(NegocioDeAcademy negocio)
        => new[]
        {
            ReglasDeNegocio.ApiBase("ApiBase", negocio.ApiBase),
            CaminoDelFlujo.EsValido(negocio.EnrollmentPath)
                ? null
                : $"EnrollmentPath: «{negocio.EnrollmentPath}» tiene que ser «{CaminoDelFlujo.Puerta}» o «{CaminoDelFlujo.Directa}», o no estar (y la matrícula va por la directa).",
        }.OfType<string>();
}

/// <summary>
/// Lo que un sitio cambia de <see cref="AcademyFeatureSettings"/>. Una clave sin valor la hereda.
/// </summary>
public sealed class AcademyFeatureSitio
{
    /// <inheritdoc cref="AcademyFeatureSettings.ApiBase"/>
    public string? ApiBase { get; init; }

    /// <inheritdoc cref="AcademyFeatureSettings.EnrollmentPath"/>
    public string? EnrollmentPath { get; init; }
}

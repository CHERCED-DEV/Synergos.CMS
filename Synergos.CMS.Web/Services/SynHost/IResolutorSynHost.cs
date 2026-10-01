using Umbraco.Cms.Core.Models.PublishedContent;

namespace Synergos.CMS.Web.Services.SynHost;

/// <summary>
/// Arma, a partir de lo que el editor autoró en un bloque, el record que viaja a su elemento
/// (ADR 0135). Hay uno por elemento migrado.
/// </summary>
/// <remarks>
/// <para><b>Por qué una interfaz y no una clase base.</b> Hay N implementaciones —una por
/// elemento— y ninguna comparte lógica con las demás salvo leer propiedades, que vive en
/// <see cref="LectorDelEditor"/> por composición. Una base con «lo de todos» es el sitio donde
/// empieza a crecer un <c>if (elemento == "x")</c>.</para>
///
/// <para><b>Vive en Web y no en Interfaces</b> porque su firma nombra
/// <see cref="IPublishedElement"/>, que es Umbraco (ADR 0002). El RECORD sí vive en Interfaces:
/// es lo que se comparte; el resolver es cómo se llena.</para>
///
/// <para><b>El precedente es <see cref="SeatMapProjection"/></b> (ADR 0127): la decisión en C#,
/// donde el compilador y la suite la ven, y la vista con dos llamadas. Una excepción en Razor
/// tumba la página entera y la vista se compila en caliente.</para>
///
/// <para><b>Contrato de fallo:</b> un resolver NO lanza por lo que el editor escribió mal (un JSON
/// que no parsea, un número que no es número): lo deja fuera, lo registra en el log y el elemento
/// pinta su estado vacío. Lanzar es para un defecto del código.</para>
/// </remarks>
/// <typeparam name="TProps">Un record marcado con
/// <see cref="Interfaces.SynHost.ElementoSynHostAttribute"/>.</typeparam>
public interface IResolutorSynHost<TProps>
    where TProps : class
{
    /// <summary>Lo que viaja al elemento de <paramref name="elemento"/>, y su respaldo SSR si lo tiene.</summary>
    ElementoResuelto<TProps> Resolver(IPublishedElement elemento);
}

/// <summary>
/// Lo que resolvió un <see cref="IResolutorSynHost{TProps}"/>.
/// </summary>
/// <param name="Props">El record que viaja en el <c>config</c> del tag.</param>
/// <param name="RespaldoHtml">
/// El marcado SSR que va DENTRO del tag hasta que el bundle lo hidrata
/// (<c>SynHostEmitRequest.FallbackHtml</c>). <c>null</c> si el elemento no tiene. Sale de los
/// mismos valores que el record, y por eso lo arma el mismo resolver: si se armara en la vista,
/// el SSR y lo que hidrata podrían volver a leer claves distintas.
/// </param>
/// <param name="DatosEstructurados">
/// El JSON-LD (schema.org) del bloque, que el emitter escribe JUNTO al tag y no dentro
/// (<c>SynHostEmitRequest.StructuredDataJson</c>). <c>null</c> si el elemento no tiene. No va en
/// <paramref name="RespaldoHtml"/> porque lo de dentro del tag lo borra la hidratación —Angular
/// vacía el host antes de pintar—, y el elemento tampoco puede emitirlo: el compilador de Angular
/// quita los <c>&lt;script&gt;</c> de las plantillas (Synergos.UI#90). Sale del mismo resolver por
/// la misma razón que el respaldo: de los mismos valores que viajan.
/// </param>
public sealed record ElementoResuelto<TProps>(TProps Props, string? RespaldoHtml = null, string? DatosEstructurados = null)
    where TProps : class;

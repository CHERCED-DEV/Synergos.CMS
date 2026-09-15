namespace Synergos.CMS.Interfaces;

/// <summary>
/// Consumes the bundle registry published by the CDN to resolve the
/// entry point and dependencies for a given element.
/// </summary>
/// <remarks>
/// <para>Extension seam per ADR 0009 (Extension seams are mandatory) and
/// ADR 0012 (CDN contract is consumed, not owned). The CMS never
/// hardcodes the shape of CDN paths; it asks this client.</para>
///
/// <para><b>Este texto decía que el adapter HTTP estaba «bloqueado esperando al equipo del
/// CDN», y llevaba así desde la HU #20</b>, que lo entregó. Un <c>&lt;remarks&gt;</c> que
/// afirma un bloqueo levantado es peor que uno corto: el siguiente que lea la seam concluye
/// que no hay por dónde consumir el CDN y propone de cero lo que ya existe. Los tres modos
/// hoy son <c>Stub</c> (default, siempre <c>null</c>), <c>FileSystem</c> y <c>Http</c>.</para>
/// </remarks>
public interface IBundleRegistryClient
{
    /// <summary>
    /// Tries to resolve the bundle descriptor for the given element key.
    /// Returns <c>null</c> when the element is unknown to the registry,
    /// the registry is unavailable, or a stub is in use.
    /// </summary>
    Task<BundleDescriptor?> TryResolveAsync(
        string elementKey,
        CancellationToken ct = default);

    /// <summary>
    /// Resuelve <b>cualquier</b> elemento que el registry sepa servir. Devuelve <c>null</c> si el
    /// registry no está disponible, está vacío, o no logra servir ninguno de los que lista.
    /// </summary>
    /// <remarks>
    /// <para><b>Existe para poder preguntar «¿el registry sirve?» sin nombrar un elemento.</b>
    /// Un chequeo de salud atado a un tag concreto no vigila el registry: vigila que <i>ese</i>
    /// elemento siga publicado, que es otra cosa y que además no es decisión del CMS.</para>
    ///
    /// <para>Lo destapó un caso real (defecto #39): el probe sondeaba <c>synergos-column</c> y el
    /// CDN lo retiró a propósito junto con otros ocho. El probe se puso rojo, el registry estaba
    /// perfecto, y el rojo era indistinguible del de un CDN caído.</para>
    ///
    /// <para><b>Qué NO es.</b> No es «listame el registry» — eso invitaría al CMS a razonar sobre
    /// el catálogo del CDN, que es justo lo que ADR 0012 prohíbe. Devuelve <b>uno</b>, y quien
    /// pregunta sólo puede concluir que el registry responde.</para>
    /// </remarks>
    Task<BundleDescriptor?> TryResolveAnyAsync(CancellationToken ct = default);

    /// <summary>
    /// El <c>import map</c> que el navegador necesita para resolver los bare specifiers de los
    /// bundles. Devuelve <c>null</c> cuando el registry no lo publica, no está disponible, o se
    /// usa un stub.
    /// </summary>
    /// <remarks>
    /// <para><b>Por qué está en la seam y no en una vista</b> (defecto #126). El
    /// <c>&lt;script type="importmap"&gt;</c> lo armaba <c>_SynHostRuntime.cshtml</c> leyendo
    /// <c>{LocalPath}/{ns}/runtime/angular/latest/import-map.json</c> <b>del disco</b>. En la
    /// imagen de producción no hay ningún <c>/cdn</c> montado —el compose monta cinco volúmenes
    /// y ninguno es ése— así que con <c>Mode=Http</c> el fichero no existía, no se emitía mapa
    /// alguno, y <b>ningún <c>&lt;synergos-*&gt;</c> llegaba a registrarse</b>: los
    /// <c>&lt;script type="module"&gt;</c> salían escritos y el navegador no podía resolver
    /// <c>@angular/core</c>. La página contestaba 200 con el SSR entero y todo lo interactivo
    /// muerto.</para>
    ///
    /// <para>Leer del disco algo que la seam ya sabe pedir por la red es justo lo que ADR 0012
    /// prohíbe. El mapa es parte del contrato que se <b>consume</b>, igual que el descriptor de
    /// un bundle, así que lo sirve quien sirve el resto.</para>
    ///
    /// <para><b>Las URLs vuelven ya reescritas a la base pública.</b> Quien llama emite HTML y no
    /// tiene por qué saber que el registry publica hosts internos — ésa es exactamente la forma
    /// de las rutas del CDN, que esta interfaz existe para que nadie cablee.</para>
    /// </remarks>
    Task<ImportMap?> TryGetImportMapAsync(CancellationToken ct = default);
}

/// <summary>
/// El mapa de importaciones que se emite como <c>&lt;script type="importmap"&gt;</c>.
/// </summary>
/// <param name="Imports">Specifier → URL absoluta o relativa a la base pública, ya reescrita.</param>
/// <remarks>
/// <para><b>Es un tipo y no un <c>string</c> de JSON</b> a propósito: quien lo emite tiene que
/// serializarlo él, y así el HTML no puede acabar llevando lo que vino del registry tal cual. Un
/// mapa que se pasa como texto se emite con <c>Html.Raw</c> sin mirarlo.</para>
///
/// <para><b>Vacío no es lo mismo que ausente</b>, y por eso puede existir un <c>ImportMap</c> con
/// cero entradas: significa que el registry contestó y no declara ninguna importación, que es una
/// respuesta. <c>null</c> significa que no se pudo preguntar. El probe distingue las dos.</para>
/// </remarks>
public sealed record ImportMap(IReadOnlyDictionary<string, string> Imports);

/// <summary>
/// Immutable descriptor of a CDN bundle entry. The record grows only
/// when CDN publishes new mandatory fields — never speculatively.
/// </summary>
/// <param name="MainEntryUri">Absolute URI of the main JS entry point.</param>
/// <param name="Dependencies">Absolute URIs of dependent bundles, in load order.</param>
/// <param name="Version">Opaque version string (format owned by CDN).</param>
/// <param name="Tag">Custom element DOM tag (e.g. <c>synergos-column</c>).
///   Optional — null cuando el registry no expone el dato.</param>
/// <param name="Alias">CMS schema DocType alias (e.g.
///   <c>elementStructColumn</c>). Optional.</param>
/// <param name="Tier">Bundle tier <c>primitive</c> | <c>composition</c> |
///   <c>module</c> | <c>experience</c>. Optional — útil para budgets.</param>
/// <param name="Integrity">SRI hash <c>sha384-{base64}</c> emitido al
///   <c>&lt;script integrity="..."&gt;</c> del HTML. Optional — null
///   cuando el adapter no calcula hashes.</param>
/// <param name="Framework">Framework del bundle (<c>angular</c>,
///   <c>react</c>, <c>svelte</c>, <c>vanilla</c>). Optional.</param>
public sealed record BundleDescriptor(
    Uri MainEntryUri,
    IReadOnlyList<Uri> Dependencies,
    string Version,
    string? Tag = null,
    string? Alias = null,
    string? Tier = null,
    string? Integrity = null,
    string? Framework = null);

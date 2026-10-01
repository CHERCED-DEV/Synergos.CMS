namespace Synergos.CMS.Interfaces.SynHost;

/// <summary>
/// Lo que viaja a <c>&lt;synergos-kpi-card&gt;</c>.
/// </summary>
/// <remarks>
/// <para><b>El defecto que cierra (D1).</b> La vista emitía <c>kpiLabel</c>, <c>kpiValue</c>,
/// <c>kpiTrend</c>, <c>kpiDelta</c> y <c>kpiPeriod</c> —los alias del ElementType— y el elemento
/// lee <c>label</c>, <c>value</c>, <c>trend</c>, <c>deltaLabel</c> y <c>period</c>: el respaldo SSR
/// salía con el texto del editor y el bundle lo reemplazaba por «— Sin dato».</para>
///
/// <para><b><c>kpiDelta</c> viaja como <c>deltaLabel</c>, no como <c>delta</c>.</b> El editor
/// escribe texto libre («+12 %», «+$120K»); el <c>delta</c> del elemento es un número que usa
/// para DERIVAR la tendencia y el rótulo cuando no se los dan. Parsear «+$120K» a número sería
/// inventar; el rótulo se muestra tal cual y la tendencia la decide el editor.</para>
///
/// <para><b>La sección <c>Synhost.Kpi</c> la usan los dos lados</b> (ADR 0136, piloto #186): el
/// respaldo SSR (la frase de la tendencia para un lector de pantalla) y el elemento al hidratar,
/// con <c>t()</c>. Antes el elemento escribía «al alza» a mano y reemplazaba la frase del
/// diccionario que el SSR había pintado.</para>
/// </remarks>
[ElementoSynHost("kpi-card", TipoDeColocable.Pieza, Diccionario = ["Synhost.Kpi"])]
public sealed record KpiCardProps(
    [property: CampoSynHost(OrigenDelCampo.Contenido)] string? Label,
    [property: CampoSynHost(OrigenDelCampo.Contenido)] string? Value,
    [property: CampoSynHost(OrigenDelCampo.Decision)] string? Trend,
    [property: CampoSynHost(OrigenDelCampo.Contenido)] string? DeltaLabel,
    [property: CampoSynHost(OrigenDelCampo.Contenido)] string? Period);

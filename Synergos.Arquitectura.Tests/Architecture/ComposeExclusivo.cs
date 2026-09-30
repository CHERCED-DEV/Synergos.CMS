namespace Synergos.CMS.Tests.Architecture;

/// <summary>
/// Las clases que tocan <c>compose.prod.yml</c> corren en serie entre ellas.
/// </summary>
/// <remarks>
/// <para>xUnit paraleliza <b>entre clases</b> por defecto, y cuatro clases de esta suite leen el
/// mismo fichero del árbol de trabajo. Mientras todas leían, eso era gratis.</para>
///
/// <para><b>Dejó de serlo cuando una empezó a escribir.</b>
/// <c>ComposeStackTests.El_check_ignora_el_fin_de_linea_pero_NO_el_contenido</c> tiene que mutar
/// el fichero de verdad —es la única forma de ejercer la tolerancia de <c>--check</c>, que lee una
/// ruta fija— y lo restaura en un <c>finally</c>. Pero entre la mutación y la restauración hay una
/// ventana, y las otras tres clases pueden estar leyendo justo ahí.</para>
///
/// <para>Eso no da un rojo honesto: da un rojo <b>intermitente</b>, en una clase que no tiene nada
/// que ver con el cambio, y que desaparece al volver a correr. Un gate que se pone rojo por su
/// vecino enseña a correr la suite otra vez hasta que pase, que es exactamente cómo se deja de
/// creer en ella.</para>
///
/// <para>Se marcan las cuatro y no sólo la que escribe: la exclusión tiene que cubrir a quien lee,
/// que es quien se ve afectado. Si mañana aparece una quinta clase que lea
/// <c>compose.prod.yml</c>, va acá también.</para>
/// </remarks>
[CollectionDefinition(Nombre)]
public sealed class ComposeExclusivo
{
    public const string Nombre = "compose.prod.yml";
}

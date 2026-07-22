namespace Synergos.CMS.Interfaces;

/// <summary>
/// Resuelve la DEFINICIÓN de un formulario (qué campos declaró el autor) a partir de su
/// <c>formInternalKey</c>, para que el servidor pueda validar lo que recibe.
/// </summary>
/// <remarks>
/// Existe porque el endpoint de envío no tenía forma de saber qué campos son obligatorios:
/// recorre <c>Request.Form</c> genéricamente. El resultado era que el <c>required</c> que el
/// renderer emite —y que el lector de pantalla anuncia vía <c>aria-required</c>— no lo hacía
/// cumplir NINGUNA capa: ni el navegador (el form llevaba <c>novalidate</c>), ni JS (no hay),
/// ni el servidor. Un formulario entero vacío se persistía y disparaba la notificación.
///
/// Se lee la FUENTE DE VERDAD (el contenido que el autor publicó) en vez de que el cliente
/// declare qué es obligatorio: una lista enviada en el POST la controla quien envía, y el
/// backstop de servidor existe precisamente para el POST que se salta el navegador.
/// </remarks>
public interface IFormDefinitionReader
{
    /// <summary>
    /// Devuelve la definición del formulario con ese <c>formInternalKey</c>, o <c>null</c> si
    /// no existe ninguno publicado con esa clave.
    /// </summary>
    FormDefinition? GetByKey(string formKey);
}

/// <summary>Un formulario publicado y los campos que declara.</summary>
/// <param name="FormKey">El <c>formInternalKey</c> del contenedor.</param>
/// <param name="Fields">Campos en el orden en que los declaró el autor.</param>
public sealed record FormDefinition(string FormKey, IReadOnlyList<FormFieldDefinition> Fields);

/// <summary>Un campo declarado por el autor.</summary>
/// <param name="Name">El <c>fieldName</c>: la clave con la que viaja en el POST.</param>
/// <param name="Label">Etiqueta visible; se usa para poder nombrar el campo en el error.</param>
/// <param name="Required">Si el autor lo marcó obligatorio.</param>
public sealed record FormFieldDefinition(string Name, string Label, bool Required);

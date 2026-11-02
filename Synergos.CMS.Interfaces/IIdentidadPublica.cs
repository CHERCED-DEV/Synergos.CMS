namespace Synergos.CMS.Interfaces;

/// <summary>
/// Cómo sale una persona en lo que el sitio muestra a otros (una red, un hilo de comentarios, una bandeja): su nombre,
/// un alias y una llave pública. <b>Nunca su correo</b>, que es la llave con la que el CMS la conoce por dentro.
/// </summary>
/// <remarks>
/// <para><b>Por qué hace falta</b> (012-blogs-social, I0). La red firmaba lo que publicaba un miembro con el correo de su
/// sesión, y como su perfil no estaba en la semilla de la demo, el autor salía como <c>(correo, correo, correo)</c>: en
/// el nombre, en el alias y en la llave, a la vista de cualquiera que abriera el inicio sin sesión.</para>
///
/// <para><b>La llave pública es el seudónimo</b> (P3 A del plan de 012): la huella del correo con la que las
/// capacidades ya conocen a una persona. No pretende ser anonimato —quien ya tiene un correo puede calcular su huella—:
/// es no esparcir el correo. El nombre y el alias salen de esa llave y del nombre del miembro, así que son estables
/// mientras el miembro no cambie su nombre.</para>
///
/// <para><b>De la llave o el alias no se vuelve al correo con una cuenta</b>, porque la huella no se invierte. Por eso
/// <see cref="RecordarAsync"/> anota la cara pública cuando la persona actúa (publica, comenta, sigue, escribe), y
/// <see cref="CorreoDeAsync"/> sólo encuentra a quien alguna vez salió así en público.</para>
/// </remarks>
public interface IIdentidadPublica
{
    /// <summary>La cara pública de quien el CMS conoce por <paramref name="correo"/>.</summary>
    IdentidadPublica De(string correo);

    /// <summary>Anota la cara pública de <paramref name="correo"/> para poder volver de su llave o de su alias.</summary>
    Task RecordarAsync(string correo, CancellationToken cancellationToken = default);

    /// <summary>El correo de quien tiene esta llave pública o este alias, o <c>null</c> si nunca salió en público.</summary>
    Task<string?> CorreoDeAsync(string llaveOAlias, CancellationToken cancellationToken = default);
}

/// <summary>Una persona tal como sale en público.</summary>
/// <param name="Llave">La llave pública: el seudónimo del correo. Es lo que se usa para seguirla o escribirle.</param>
/// <param name="Alias">El alias legible, sin <c>@</c>: el nombre en minúsculas y sin tildes, y el comienzo de la llave.</param>
/// <param name="Nombre">El nombre del miembro, o «Miembro» si no tiene uno o el que tiene parece un correo.</param>
public sealed record IdentidadPublica(string Llave, string Alias, string Nombre);

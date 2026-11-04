namespace Synergos.CMS.Application.Configuration;

/// <summary>El secreto con que se calcula la llave pública de una persona (012-blogs-social, I0.b).</summary>
/// <remarks>
/// <para>Sección <c>Synergos:IdentidadPublica</c>. <b>Vacío significa «generá uno y guardalo cifrado»</b>, que es lo
/// que <c>CustodiaDeLlaveDeFirma</c> hace con las tres firmas opcionales (entradas, contratos y diplomas): ninguna
/// variable nueva es obligatoria en el despliegue, y el secreto cifrado va en el respaldo con aquéllas.</para>
///
/// <para>Ponerlo es lo que permite rotarlo y llevarlo a otra instalación. Rotarlo cambia la llave y el alias públicos
/// de cada miembro: lo anotado con los de antes sigue llevando a su correo, y la cara nueva se anota cuando la persona
/// vuelve a actuar.</para>
/// </remarks>
public sealed class IdentidadPublicaSettings
{
    /// <summary>El secreto. Vacío genera uno y lo guarda cifrado.</summary>
    public string Secreto { get; init; } = string.Empty;
}

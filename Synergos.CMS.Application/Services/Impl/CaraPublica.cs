using System.Globalization;
using System.Text;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Application.Services.Impl;

/// <summary>
/// La regla de la cara pública de una persona (<see cref="IdentidadPublica"/>): qué nombre se muestra y qué alias
/// sale de ese nombre y de su llave. Pura, sin Umbraco (ADR 0002), para que quien arma la cara y quien la repone desde
/// un comentario guardado den lo mismo.
/// </summary>
public static class CaraPublica
{
    /// <summary>El nombre que se muestra cuando el miembro no tiene uno publicable.</summary>
    public const string NombreSinNombre = "Miembro";

    /// <summary>Cuántos caracteres de la llave lleva el alias: separan a dos personas que se llaman igual.</summary>
    public const int LargoDeLaLlaveEnElAlias = 6;

    private const int LargoMaximoDelNombreEnElAlias = 32;

    /// <summary>La cara pública de quien tiene esta <paramref name="llave"/> y este nombre de miembro.</summary>
    public static IdentidadPublica De(string llave, string? nombreDelMiembro)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(llave);
        var nombre = Nombre(nombreDelMiembro);
        return new IdentidadPublica(llave, Alias(nombre, llave), nombre);
    }

    /// <summary>
    /// El nombre publicable: el del miembro, recortado; «Miembro» si no tiene, o si el que tiene parece un correo.
    /// </summary>
    /// <remarks>
    /// Un miembro creado desde el backoffice puede llevar el correo por nombre, y publicarlo sería la misma fuga por la
    /// otra puerta: es la regla de <c>CurrentMemberDisplayName</c> («nunca su correo»).
    /// </remarks>
    public static string Nombre(string? nombreDelMiembro)
    {
        var nombre = nombreDelMiembro?.Trim();
        return string.IsNullOrWhiteSpace(nombre) || EsCorreo(nombre) ? NombreSinNombre : nombre;
    }

    /// <summary>El alias: el nombre en minúsculas, sin tildes y con guiones, y el comienzo de la llave.</summary>
    public static string Alias(string nombre, string llave)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(llave);
        var nombreEnElAlias = Slug(Nombre(nombre));
        var sufijo = llave.Length <= LargoDeLaLlaveEnElAlias ? llave : llave[..LargoDeLaLlaveEnElAlias];
        return $"{nombreEnElAlias}-{sufijo.ToLowerInvariant()}";
    }

    /// <summary>Si el valor parece un correo. Es la misma prueba que usa el gate de la sesión: tiene una arroba.</summary>
    public static bool EsCorreo(string? valor) => valor?.Contains('@', StringComparison.Ordinal) == true;

    /// <summary>
    /// Si el valor tiene la forma de una llave pública: los 16 caracteres hexadecimales en minúsculas del seudónimo.
    /// </summary>
    public static bool EsLlave(string? valor)
        => valor is { Length: LargoDeUnaLlave } && valor.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');

    /// <summary>El largo de la huella que da el seudónimo de una persona.</summary>
    public const int LargoDeUnaLlave = 16;

    private static string Slug(string nombre)
    {
        var sinTildes = new StringBuilder(nombre.Length);
        foreach (var c in nombre.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                sinTildes.Append(c);
            }
        }

        var slug = new StringBuilder(sinTildes.Length);
        foreach (var c in sinTildes.ToString().ToLowerInvariant())
        {
            if (c is >= 'a' and <= 'z' or >= '0' and <= '9')
            {
                slug.Append(c);
            }
            else if (slug.Length > 0 && slug[^1] != '-')
            {
                slug.Append('-');
            }
        }

        var texto = slug.ToString().Trim('-');
        if (texto.Length > LargoMaximoDelNombreEnElAlias)
        {
            texto = texto[..LargoMaximoDelNombreEnElAlias].TrimEnd('-');
        }
        return texto.Length == 0 ? "miembro" : texto;
    }
}

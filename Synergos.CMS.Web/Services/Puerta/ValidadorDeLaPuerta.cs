using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;

namespace Synergos.CMS.Web.Services.Puerta;

/// <summary>
/// Valida al arrancar cada flujo de <c>Synergos:Puerta</c> contra los contratos incrustados y las
/// secciones de negocio registradas (ADR 0140 F3).
/// </summary>
/// <remarks>
/// <para><b>Lo que se rechaza es lo que fallaría en CADA petición</b>, y por eso no espera a la primera:
/// un flujo que ningún orquestador expone, un acceso que la puerta no sabe dar, un sujeto sin nombre,
/// una operación que exige el negocio y un flujo que no dice de qué sección, un campo que la sección no
/// tiene. Y también lo que no haría nada —un negocio o un aviso que ninguna operación lee—: es la forma
/// de una clave mal escrita.</para>
/// </remarks>
public sealed class ValidadorDeLaPuerta : IValidateOptions<PuertaSettings>
{
    private static readonly Regex Kind = new(@"^[a-z][a-z0-9]*([.\-][a-z0-9]+)*$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    private static readonly Regex Marcador = new(@"\{([^}]+)\}", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    private readonly TablaDeLaPuerta _tabla;
    private readonly SeccionesDelSitio? _secciones;

    public ValidadorDeLaPuerta(TablaDeLaPuerta tabla, SeccionesDelSitio? secciones = null)
    {
        _tabla = tabla;
        _secciones = secciones;
    }

    public ValidateOptionsResult Validate(string? name, PuertaSettings options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var errores = new List<string>();

        foreach (var (clave, flujo) in options.Flujos)
        {
            var donde = $"{PuertaSettings.Seccion}:Flujos:{clave}";
            var ops = _tabla.DelFlujo(clave);
            if (ops.Count == 0)
            {
                errores.Add($"{donde}: ningún contrato incrustado expone el flujo «{clave}» (x-synergos-flujo).");
                continue;
            }

            if (!string.Equals(flujo.Acceso, AccesoDeLaPuerta.Miembro, StringComparison.Ordinal))
            {
                errores.Add($"{donde}:Acceso es «{flujo.Acceso}» y la puerta sólo sabe dar «{AccesoDeLaPuerta.Miembro}».");
            }

            if (ops.Any(o => o.Declara(LoQuePoneLaPuerta.CabeceraDelSujeto))
                && (flujo.SujetoKind is null || !Kind.IsMatch(flujo.SujetoKind)))
            {
                errores.Add($"{donde}:SujetoKind «{flujo.SujetoKind}» no es un Kind (minúsculas, puntos y guiones).");
            }

            RevisarNegocio(donde, ops, flujo.Negocio, errores);
            RevisarAviso(donde, ops, flujo.Aviso, errores);
        }

        return errores.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errores);
    }

    private void RevisarNegocio(string donde, IReadOnlyList<OperacionDeLaPuerta> ops, NegocioDelFlujoSettings? negocio, List<string> errores)
    {
        var loLeen = ops.Any(o => o.Declara(LoQuePoneLaPuerta.CabeceraDelNegocio));
        if (negocio is null)
        {
            if (loLeen) errores.Add($"{donde}:Negocio falta y una operación del flujo lo lee.");
            return;
        }
        if (!loLeen)
        {
            errores.Add($"{donde}:Negocio no lo lee ninguna operación del flujo: ¿la clave está bien escrita?");
            return;
        }

        var tipo = negocio.Seccion is null ? null : _secciones?.TipoDe(negocio.Seccion);
        if (tipo is null)
        {
            errores.Add($"{donde}:Negocio:Seccion «{negocio.Seccion}» no es una sección de negocio registrada.");
            return;
        }
        if (negocio.Campos.Count == 0) errores.Add($"{donde}:Negocio:Campos está vacío: no viajaría nada.");
        foreach (var campo in negocio.Campos.Where(c => LoQuePoneLaPuerta.Propiedad(tipo, c) is null))
        {
            errores.Add($"{donde}:Negocio:Campos «{campo}» no es un campo de {tipo.Name}.");
        }
    }

    private static void RevisarAviso(string donde, IReadOnlyList<OperacionDeLaPuerta> ops, AvisoDelFlujoSettings? aviso, List<string> errores)
    {
        if (aviso is null) return;

        var avisan = ops.Where(o => o.Declara(LoQuePoneLaPuerta.CabeceraDelContacto)).ToList();
        if (avisan.Count == 0)
        {
            errores.Add($"{donde}:Aviso no lo usa ninguna operación del flujo: ¿la clave está bien escrita?");
            return;
        }
        if (aviso.Ruta is not { Length: > 1 } ruta || ruta[0] != '/' || ruta.StartsWith("//", StringComparison.Ordinal))
        {
            errores.Add($"{donde}:Aviso:Ruta «{aviso.Ruta}» tiene que ser una ruta del sitio (/…).");
            return;
        }
        foreach (Match m in Marcador.Matches(ruta))
        {
            if (avisan.Any(o => o.Parametros.All(p => !string.Equals(p.Nombre, m.Groups[1].Value, StringComparison.Ordinal))))
            {
                errores.Add($"{donde}:Aviso:Ruta pide {{{m.Groups[1].Value}}} y la operación que avisa no lo trae.");
            }
        }
    }
}

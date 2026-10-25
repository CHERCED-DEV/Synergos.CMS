using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;

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

            RevisarAcceso(donde, ops, flujo, errores);

            if (ops.Any(o => o.Declara(LoQuePoneLaPuerta.CabeceraDelSujeto))
                && (flujo.SujetoKind is null || !Kind.IsMatch(flujo.SujetoKind)))
            {
                errores.Add($"{donde}:SujetoKind «{flujo.SujetoKind}» no es un Kind (minúsculas, puntos y guiones).");
            }

            RevisarNegocio(donde, ops, flujo.Negocio, errores);
            RevisarAviso(donde, ops, flujo.Aviso, errores);
            RevisarInvitado(donde, ops, flujo, errores);

            foreach (var aviso in flujo.EnVivo)
            {
                if (!ops.Any(o => string.Equals(o.Operacion, aviso.Operacion, StringComparison.Ordinal)))
                {
                    errores.Add($"{donde}:EnVivo avisa tras «{aviso.Operacion}», que no es una operación del flujo.");
                }
                if (string.IsNullOrWhiteSpace(aviso.Canal) || string.IsNullOrWhiteSpace(aviso.Nombre))
                {
                    errores.Add($"{donde}:EnVivo tras «{aviso.Operacion}» necesita Canal y Nombre.");
                }
            }

            if (SujetoDeLaPuerta.Normalizar(flujo.SujetoDe) is not (SujetoDeLaPuerta.Miembro or SujetoDeLaPuerta.Correo))
            {
                errores.Add($"{donde}:SujetoDe «{flujo.SujetoDe}» tiene que ser «{SujetoDeLaPuerta.Miembro}» o «{SujetoDeLaPuerta.Correo}», "
                    + "o no estar (y el sujeto es el miembro).");
            }

            // Mal escrito no se ignora: caería a la regla y el flujo iría por un camino que nadie eligió.
            if (!CaminoDelFlujo.EsValido(CaminoDelFlujo.Normalizar(flujo.Camino)))
            {
                errores.Add($"{donde}:Camino «{flujo.Camino}» tiene que ser «{CaminoDelFlujo.Puerta}» o «{CaminoDelFlujo.Directa}», "
                    + "o no estar (y decide la regla: la puerta si la abre, y si no la directa).");
            }
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

    /// <summary>
    /// Quién pide (núcleo, P8 = A del lote 6): el miembro dueño, o el personal en las operaciones que el CONTRATO marca
    /// como suyas. Se cruza en los dos sentidos, como el negocio: una operación del personal abierta a cualquier miembro
    /// dejaría a uno devolver la compra de otro, y un acceso de personal sobre operaciones del dueño pediría un rol que
    /// nadie puso a quien sólo quiere ver lo suyo.
    /// </summary>
    private static void RevisarAcceso(string donde, IReadOnlyList<OperacionDeLaPuerta> ops, FlujoDeLaPuertaSettings flujo, List<string> errores)
    {
        var personales = ops.Where(o => o.Personal).Select(o => o.Operacion).Order(StringComparer.Ordinal).ToList();
        var delDueno = ops.Where(o => !o.Personal).Select(o => o.Operacion).Order(StringComparer.Ordinal).ToList();

        switch (flujo.Acceso)
        {
            case AccesoDeLaPuerta.Miembro:
                if (personales.Count > 0)
                {
                    errores.Add($"{donde}:Acceso es «{AccesoDeLaPuerta.Miembro}» y el contrato marca del personal "
                        + $"{string.Join(", ", personales)}: cualquier miembro las pediría. Tiene que ser «{AccesoDeLaPuerta.Personal}», con sus Roles.");
                }
                break;
            case AccesoDeLaPuerta.Personal:
                if (delDueno.Count > 0)
                {
                    errores.Add($"{donde}:Acceso es «{AccesoDeLaPuerta.Personal}» y el contrato no marca del personal "
                        + $"{string.Join(", ", delDueno)}: son operaciones del dueño.");
                }
                if (flujo.Roles.Count == 0 || flujo.Roles.Any(string.IsNullOrWhiteSpace))
                {
                    errores.Add($"{donde}:Roles está vacío o tiene uno en blanco: con «{AccesoDeLaPuerta.Personal}» hace falta al menos un rol que comprobar.");
                }
                break;
            default:
                errores.Add($"{donde}:Acceso es «{flujo.Acceso}» y la puerta sólo sabe dar «{AccesoDeLaPuerta.Miembro}» o «{AccesoDeLaPuerta.Personal}».");
                break;
        }

        if (flujo.Roles.Count > 0 && !string.Equals(flujo.Acceso, AccesoDeLaPuerta.Personal, StringComparison.Ordinal))
        {
            errores.Add($"{donde}:Roles sólo vale con Acceso «{AccesoDeLaPuerta.Personal}»: así no lo comprobaría nadie.");
        }
    }

    /// <summary>
    /// Lo que se abre a quien no inició sesión (NB-3 de 011): sólo una operación que no pone sujeto, ni llave, ni
    /// contacto. Lo dice el CONTRATO del orquestador, no un nombre: una operación de alguien no se vuelve de nadie
    /// porque la configuración la liste.
    /// </summary>
    private static void RevisarInvitado(string donde, IReadOnlyList<OperacionDeLaPuerta> ops, FlujoDeLaPuertaSettings flujo, List<string> errores)
    {
        foreach (var operacion in flujo.Invitado)
        {
            var op = ops.FirstOrDefault(o => string.Equals(o.Operacion, operacion, StringComparison.Ordinal));
            if (op is null)
            {
                errores.Add($"{donde}:Invitado abre «{operacion}», que no es una operación del flujo.");
            }
            else if (op.Declara(LoQuePoneLaPuerta.CabeceraDelSujeto) || op.Declara(LoQuePoneLaPuerta.CabeceraDelContacto)
                     || op.LargoDeLaLlave is not null || op.Personal)
            {
                errores.Add($"{donde}:Invitado abre «{operacion}», que pone un sujeto, una llave o un contacto, o es del personal: a "
                    + "quien no inició sesión sólo se le abre una lectura sin dueño.");
            }
        }
        if (flujo.FrenoPorMinuto is <= 0)
        {
            errores.Add($"{donde}:FrenoPorMinuto es {flujo.FrenoPorMinuto}: con eso no pasaría nadie. O un número de veces por minuto, o no estar.");
        }
    }

    private void RevisarAviso(string donde, IReadOnlyList<OperacionDeLaPuerta> ops, AvisoDelFlujoSettings? aviso, List<string> errores)
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

        // La ruta por sitio (D-006-AVISO): opcional, y con las dos claves. La de cada sitio la valida su sección; la que
        // no se pueda armar en una petición cae en Ruta.
        if (aviso.Seccion is null && aviso.Campo is null) return;
        if (aviso.Seccion is null || aviso.Campo is null)
        {
            errores.Add($"{donde}:Aviso necesita Seccion y Campo juntos para la ruta por sitio.");
            return;
        }
        var tipo = _secciones?.TipoDe(aviso.Seccion);
        if (tipo is null)
        {
            errores.Add($"{donde}:Aviso:Seccion «{aviso.Seccion}» no es una sección de negocio registrada.");
            return;
        }
        if (LoQuePoneLaPuerta.Propiedad(tipo, aviso.Campo)?.PropertyType != typeof(string))
        {
            errores.Add($"{donde}:Aviso:Campo «{aviso.Campo}» no es un campo de texto de {tipo.Name}.");
        }
    }
}

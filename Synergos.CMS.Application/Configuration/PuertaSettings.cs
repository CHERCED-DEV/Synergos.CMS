namespace Synergos.CMS.Application.Configuration;

/// <summary>
/// La puerta de los flujos (ADR 0140 F3) — sección <c>Synergos:Puerta</c>: qué flujos abre y, de
/// cada uno, lo que sólo el CMS sabe.
/// </summary>
/// <remarks>
/// <para><b>Sólo lo que es de cada flujo</b>: quién puede entrar, cómo se llama su sujeto, qué
/// campos de qué sección de negocio adjunta y adónde lleva el enlace del aviso. Qué operaciones tiene
/// un flujo, por qué método y con qué cabeceras NO va acá: lo dice el contrato del orquestador, que la
/// puerta lleva incrustado. Una segunda lista acá sería una copia del contrato sin nada que la cruce.</para>
///
/// <para><b>Sin flujos, la puerta no abre nada</b>: todo da 503 <c>puerta.flujo_no_disponible</c> o 404,
/// y es el valor por defecto. Cada flujo se valida al arrancar contra el contrato incrustado y las
/// secciones de negocio registradas: una clave mal escrita no deja arrancar.</para>
/// </remarks>
public sealed class PuertaSettings
{
    /// <summary>La sección de configuración.</summary>
    public const string Seccion = "Synergos:Puerta";

    /// <summary>Los flujos que la puerta abre, por su clave (<c>eventos.compra</c>).</summary>
    public Dictionary<string, FlujoDeLaPuertaSettings> Flujos { get; init; } = new(StringComparer.Ordinal);
}

/// <summary>Lo que el CMS pone de su parte en un flujo.</summary>
public sealed class FlujoDeLaPuertaSettings
{
    /// <summary>Quién puede pedir sus operaciones. Hoy sólo <see cref="AccesoDeLaPuerta.Miembro"/>.</summary>
    public string? Acceso { get; init; }

    /// <summary>El <c>Kind</c> con que se nombra al sujeto (<c>eventos.comprador</c>); el id es el del miembro.</summary>
    public string? SujetoKind { get; init; }

    /// <summary>Qué campos de qué sección de negocio adjunta, donde la operación los declara.</summary>
    public NegocioDelFlujoSettings? Negocio { get; init; }

    /// <summary>El enlace del aviso, donde la operación declara el contacto.</summary>
    public AvisoDelFlujoSettings? Aviso { get; init; }
}

/// <summary>Los campos de negocio que el flujo adjunta (ADR 0137): sólo éstos viajan.</summary>
public sealed class NegocioDelFlujoSettings
{
    /// <summary>El nombre de la sección de negocio registrada: <c>Eventos</c> por <c>Synergos:Features:Eventos</c>.</summary>
    public string? Seccion { get; init; }

    /// <summary>Los campos de esa sección, por su nombre: <c>FeePercent</c>.</summary>
    public List<string> Campos { get; init; } = [];
}

/// <summary>A dónde lleva el enlace del aviso.</summary>
public sealed class AvisoDelFlujoSettings
{
    /// <summary>Ruta del sitio, con los parámetros de la operación entre llaves: <c>/eventos/compra?compra={id}</c>.</summary>
    public string? Ruta { get; init; }
}

/// <summary>Quién puede pedir las operaciones de un flujo.</summary>
public static class AccesoDeLaPuerta
{
    /// <summary>Un miembro con sesión: el sujeto es su <c>MemberKey</c>.</summary>
    public const string Miembro = "Miembro";
}

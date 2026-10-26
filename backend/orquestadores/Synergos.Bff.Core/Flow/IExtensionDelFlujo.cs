namespace Synergos.Bff.Core.Flow;

/// <summary>
/// Lo que un vertical contesta por un flujo GENÉRICO cuando un cierre por ítem declara <c>al_fallar: soltar</c>: la
/// confirmación parcial en el host genérico (núcleo, NV-13 de Viajes).
/// </summary>
/// <remarks>
/// <para><b>Por qué hace falta.</b> NV-2 (T048) dejó la parcial en dos preguntas al enlace, <c>SueltaSiFalla</c> y
/// <c>ConItemSoltado</c>, que sólo contesta un enlace tipado. Lo posterior al cierre (lote 6) exige una original
/// genérica, y su enlace (<see cref="BindingDeFlujo"/>) no sabe nada del negocio. Así, un vertical no podía tener las
/// dos (006, research R0). Con esto el genérico pregunta, y contesta el vertical.</para>
///
/// <para><b>Se registra por clave de flujo</b> (<c>builder.Services.AddSingleton&lt;IExtensionDelFlujo&gt;(…)</c>), una por
/// flujo. Se cruza al arrancar, en los dos sentidos:
/// <list type="bullet">
/// <item>un flujo genérico que declara <c>soltar</c> sin extensión no arranca: nadie diría nunca que sí;</item>
/// <item>una extensión de un flujo que no declara <c>soltar</c>, o que no existe, tampoco arranca;</item>
/// <item>lo que arma tiene que estar entre lo que sabe deshacer el ejecutor de la saga.</item>
/// </list></para>
///
/// <para><b>La política no la evalúa la definición.</b> La guarda la saga desde lo que el flujo leyó al abrir (por
/// ejemplo, un <c>{ "de": "negocio" }</c>), y la extensión la lee del contexto de la fase. Lo que declara en
/// <see cref="Lee"/> y en <see cref="Escribe"/> se guarda en la saga como lo que reconstruye una fase que continúa, con
/// las mismas reglas: un tipo conocido y que se pueda guardar.</para>
/// </remarks>
public interface IExtensionDelFlujo
{
    /// <summary>La clave del flujo genérico que extiende.</summary>
    string Flujo { get; }

    /// <summary>Lo que lee del contexto para contestar (la política del sitio, por ejemplo).</summary>
    IReadOnlyCollection<string> Lee { get; }

    /// <summary>Lo que reescribe en el contexto al soltar un ítem (la vista del ítem no cumplido, por ejemplo).</summary>
    IReadOnlyCollection<string> Escribe { get; }

    /// <summary>Los tipos de compensación que puede armar al soltar un ítem (la devolución de su parte, por ejemplo).</summary>
    IReadOnlyCollection<string> Arma { get; }

    /// <summary>Si ESTA saga admite soltar el ítem cuyo cierre falló y seguir con los demás.</summary>
    /// <param name="saga">La saga en curso.</param>
    /// <param name="ctx">El contexto de la fase, con lo que la saga reconstruye.</param>
    bool SueltaSiFalla(SagaDeFlujo saga, FlowContext ctx);

    /// <summary>
    /// Lo que hace con el ítem que se soltó, con su compensación ya intentada. Puede reescribir en
    /// <paramref name="ctx"/> lo que declara en <see cref="Escribe"/> y añadir compensaciones NUEVAS de lo que declara en
    /// <see cref="Arma"/>. El intérprete guarda el contexto con la saga y ejecuta en el acto lo nuevo; lo que falla queda
    /// pendiente.
    /// </summary>
    /// <param name="saga">La saga.</param>
    /// <param name="ctx">El contexto de la fase.</param>
    /// <param name="reservante">El paso cuya reserva se soltó.</param>
    /// <param name="soltada">La reserva del ítem.</param>
    SagaDeFlujo ConItemSoltado(SagaDeFlujo saga, FlowContext ctx, string reservante, HoldLeg soltada);
}

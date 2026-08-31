using Synergos.Api.Sessions.Domain;

namespace Synergos.CMS.Tests.Api;

/// <summary>
/// Lo que las señales de sesión rechazan solas (#58).
/// </summary>
/// <remarks>
/// <para><b>Estos tests no existían, y no por descuido: no había dónde ponerlos.</b> La regla
/// vivía dentro del método del endpoint, así que probarla exigía levantar el host. Ese es todo el
/// argumento de #58, y la prueba de que el argumento era real es que este fichero se pudo escribir
/// en cuanto la regla salió de ahí.</para>
///
/// <para>El gate estructural (<c>ApiMoldTests.Toda_capacidad_tiene_su_fichero_de_reglas</c>)
/// comprueba que el fichero <b>exista</b>. Que la regla <b>acierte</b> lo comprueban estos: son
/// dos preguntas distintas y hacen falta las dos.</para>
/// </remarks>
public sealed class SessionsRulesTests
{
    private static readonly DateTime Ayer = new(2026, 8, 30, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Hoy = new(2026, 8, 31, 12, 0, 0, DateTimeKind.Utc);

    [Fact] // happy
    public void Una_ventana_que_avanza_pasa()
        => Assert.Null(SessionsRules.CheckWindow(Ayer, Hoy));

    [Fact]
    public void Una_ventana_al_reves_se_rechaza()
    {
        var rechazo = SessionsRules.CheckWindow(Hoy, Ayer);

        Assert.NotNull(rechazo);
        Assert.Equal($"{SessionsRules.CodePrefix}.bad_window", rechazo!.Code);
    }

    /// <summary>
    /// Una ventana de duración cero tampoco vale.
    /// </summary>
    /// <remarks>
    /// Es el borde que un <c>&lt;</c> en vez de un <c>&lt;=</c> deja pasar, y el que más cuesta
    /// ver leyendo: <c>[t, t)</c> no contiene nada, así que preguntar por ella y recibir una lista
    /// vacía sería indistinguible de «no hubo búsquedas», que es una respuesta legítima.
    /// </remarks>
    [Fact]
    public void Una_ventana_de_duracion_cero_se_rechaza()
        => Assert.NotNull(SessionsRules.CheckWindow(Hoy, Hoy));

    /// <summary>El mensaje dice las dos fechas, o no se puede depurar un tablero mal configurado.</summary>
    [Fact]
    public void El_rechazo_dice_QUE_ventana_no_avanza()
    {
        var rechazo = SessionsRules.CheckWindow(Hoy, Ayer);

        Assert.Contains(Hoy.ToString("O"), rechazo!.Message, StringComparison.Ordinal);
        Assert.Contains(Ayer.ToString("O"), rechazo.Message, StringComparison.Ordinal);
    }
}

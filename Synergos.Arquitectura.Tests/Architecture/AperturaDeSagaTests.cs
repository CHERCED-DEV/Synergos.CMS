using System.Text.RegularExpressions;

namespace Synergos.CMS.Tests.Architecture;

/// <summary>
/// Todo flujo de orquestador abre su saga por <c>SagaEngine.Abrir</c> (defecto #166).
/// </summary>
/// <remarks>
/// <para><b>Qué se rompió, y por qué un gate y no sólo tests.</b> El defecto #41 —«encontrar una
/// llave de idempotencia no significa que esto ya pasó»— se centralizó en
/// <c>SagaEngine.Abrir</c>, y el <c>&lt;remarks&gt;</c> de ese método dice por qué con todas las
/// letras: <i>«una regla sutil copiada dos veces se corrige una vez y se olvida la otra»</i>.</para>
///
/// <para>Lo que pasó después es peor que copiarla: se centralizó para los dos orquestadores que
/// existían —<c>Tienda</c> y <c>Eventos</c>— y los dos que se escribieron luego
/// —<c>Salud</c> y <c>Viajes</c>— <b>nunca la llamaron</b>. Hacían <c>Find(sagaId)</c> y devolvían
/// lo que hubiera, incluida una saga <c>Compensated</c>: a quien se le caía el cobro le quedaba esa
/// cita —o ese viaje— encerrada para siempre, porque la llave se deriva de lo que se pide y nunca
/// cambia.</para>
///
/// <para><b>La abstracción existía y nadie la usaba, que es el modo de fallo que ningún test de la
/// regla puede ver.</b> <c>LlaveDeIdempotenciaTests</c> tiene ocho tests sobre <c>Abrir</c> y los
/// ocho pasaban — la regla 5 del repo hermano: un test que llama al MÉTODO no ve que falte el
/// llamador. Los tests de comportamiento por flujo viven en
/// <c>ReintentoTrasDeshacerTests</c>; esto de acá es lo que hace que el QUINTO flujo no pueda
/// nacer sin ello.</para>
///
/// <para><b>Es trinquete absoluto y no línea base porque el árbol ya lo cumple</b> — el criterio
/// del #134: un umbral absoluto sólo vale cuando se cumple hoy; con deuda declarada iría una línea
/// base. Medido al escribirlo: los cuatro flujos pasan — los dos que ya llamaban a `Abrir` y los dos que este mismo ticket arregló.</para>
///
/// <para><b>Se quitan los comentarios antes de mirar, y contra qué protege NO es lo que escribí
/// primero.</b> Puse que «acá ES la medida» y es falso: medido, con el barrido apagado el cruce
/// sobre el disco de hoy da exactamente lo mismo, porque los cuatro flujos llaman a <c>Abrir</c>
/// de verdad.</para>
///
/// <para>Contra lo que protege es contra un <b>falso NEGATIVO</b>, y el caso es justo el que este
/// arreglo invita a escribir. Medido con el par de mutaciones que lo separa: dejando en un flujo
/// un comentario que dice <i>«acá habría que llamar a <c>_sagas.Abrir(sagaId)</c> — pendiente»</i>
/// y el <c>Find</c> viejo debajo, <b>sin barrido el gate pasa en VERDE</b> y con barrido se pone
/// rojo. O sea que la prosa que nombra la pieza correcta es exactamente cómo un flujo sin ella
/// pasaría por bueno.</para>
///
/// <para>Y la dirección importa porque es la contraria a la del gate hermano
/// <c>humo-tras-desplegar</c> del repo de UI, escrito el mismo día: allá el barrido evita que el
/// gate ACUSE al fichero que documenta el defecto (falso positivo) y acá evita que se lo CREA
/// (falso negativo). Queda escrito cuál de las dos es, porque afirmar «el barrido sostiene el
/// cruce» sin medirlo salió mal en los dos.</para>
/// </remarks>
public sealed class AperturaDeSagaTests
{
    /// <summary>La familia de los orquestadores.</summary>
    private const string Prefijo = "Synergos.Bff.";

    /// <summary>
    /// <c>Bff.Core</c> no es un vertical: es donde vive <c>Abrir</c>, así que no lo llama.
    /// </summary>
    private const string Motor = "Synergos.Bff.Core";

    /// <summary>Lo que hay que llamar.</summary>
    private const string Apertura = ".Abrir(";

    /// <summary>
    /// Los flujos de cada orquestador, derivados del disco.
    /// </summary>
    /// <remarks>
    /// Se pregunta por NOMBRE de proyecto y <c>Proyectos</c> lo encuentra donde esté (#136):
    /// escribir <c>backend/orquestadores/…</c> a mano funciona hoy y se rompe en la próxima
    /// reorganización, con el coste pagado por quien mueva.
    /// </remarks>
    private static IReadOnlyList<(string Orquestador, string Fichero, string Codigo)> Flujos()
    {
        var salida = new List<(string, string, string)>();

        foreach (var nombre in Proyectos.Nombres(Prefijo))
        {
            if (string.Equals(nombre, Motor, StringComparison.OrdinalIgnoreCase)) continue;

            var dominio = Proyectos.Dir(nombre, "Domain");
            if (!Directory.Exists(dominio)) continue;

            foreach (var fichero in Directory.EnumerateFiles(dominio, "*Flow.cs"))
            {
                salida.Add((nombre, fichero, Desnuda(File.ReadAllText(fichero))));
            }
        }

        return salida;
    }

    /// <summary>La fuente sin comentarios — ver el <c>&lt;remarks&gt;</c> de la clase.</summary>
    private static string Desnuda(string fuente)
    {
        var sinBloques = Regex.Replace(fuente, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);
        return Regex.Replace(sinBloques, @"^\s*//.*$", string.Empty, RegexOptions.Multiline);
    }

    [Fact]
    public void Hay_flujos_que_mirar()
    {
        // Red de seguridad. Si el descubrimiento deja de ver —otra reorganización, otro sufijo—
        // todo lo de abajo pasa en verde SIN MIRAR NADA, que es el modo de fallo caro: un rojo se
        // arregla y un verde sobre el vacío se hereda (#136).
        var flujos = Flujos();
        Assert.True(flujos.Count >= 4,
            $"se descubrieron {flujos.Count} flujos y hay cuatro orquestadores construidos: " +
            "el descubrimiento dejó de ver.");
    }

    [Fact]
    public void Todo_flujo_abre_su_saga_por_Abrir()
    {
        var sinApertura = Flujos()
            .Where(f => !f.Codigo.Contains(Apertura, StringComparison.Ordinal))
            .Select(f => $"{f.Orquestador}/{Path.GetFileName(f.Fichero)}")
            .ToList();

        Assert.True(sinApertura.Count == 0,
            "abren saga sin pasar por `SagaEngine.Abrir`, así que una saga Compensated se " +
            "devuelve tal cual y encierra a quien la pidió (#41, #166): " +
            string.Join(", ", sinApertura));
    }

    // ── EL TERCER DIENTE QUE NO SE ESCRIBIÓ, Y POR QUÉ ──────────────────────
    //
    // El diente que faltaría es «ningún flujo resuelve la llave con `.Find(` a mano», porque el
    // primero no ve a un flujo que llame a `Abrir` en una rama y conserve el `Find` viejo en la de
    // al lado. **Se escribió, se midió, y marcaba al bueno.**
    //
    // Medido: `.Find(` aparece **14 veces en los cuatro flujos** y todas son legítimas —
    // `ConfirmAsync`, `CancelAsync`, `RefundAsync`, `Get` cargan una saga por un id que el
    // llamador YA tiene. Eso no es resolver una llave de idempotencia: es leer un registro
    // conocido. Un gate que las prohíba nace con catorce excepciones, que es el muro que deja de
    // leerse; y este repo ya decidió esto mismo una vez —el diente de «tiene gemelo `Http*`» de
    // `MoldeDelVerticalTests` **se quitó** por marcar un caso legítimo que no sabía distinguir—.
    //
    // Distinguirlo exigiría recortar el CUERPO del método que construye la saga y mirar ahí, como
    // el addendum #14 de `IdentityGateTests` recorta el cuerpo de un endpoint. Se puede hacer y es
    // otro trabajo; el disparador para escribirlo es el primer flujo que llame a `Abrir` y siga
    // resolviendo la llave con `Find` en otra rama.
    //
    // Queda dicho en vez de insinuar que el cruce es completo: **lo que estos dos dientes cazan es
    // un flujo que no pase por `Abrir` en ninguna parte**, que es la forma exacta de #166 y la que
    // tendrá el quinto orquestador si nadie mira.
}

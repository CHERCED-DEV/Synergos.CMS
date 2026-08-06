namespace Synergos.CMS.Tests.Architecture;

/// <summary>
/// Los cuatro productos de viaje se apartan contra <c>Api.Booking</c>, como invariante ejecutable
/// (HU #36).
/// </summary>
/// <remarks>
/// <para><b>Esto vigila una DECISIÓN, no un descuido.</b> La HU #36 llegó diciendo «no todo va a
/// <c>Api.Booking</c>; un asiento de vuelo se parece más a un pozo contable», y la respuesta
/// —mirando el código y no la intuición— fue que los cuatro van a Booking: su <c>Resource</c> ya
/// lleva <c>Capacity</c> («1 para un consultorio; 40 para un aula»), así que el aspecto de pozo
/// está dentro; y su regla de «horario vacío = siempre abierto» se tomó nombrando el caso
/// hotel.</para>
///
/// <para><b>Por qué merece un gate y no solo un comentario.</b> Partir el vuelo hacia
/// <c>Api.Inventory</c> es una tentación razonable —el argumento a favor existe y está escrito—,
/// y hacerlo en silencio duplicaría los kinds de compensación y obligaría a la saga a recordar
/// <i>contra cuál</i> capacidad se apartó cada ítem. Lo que este fichero consigue es que esa
/// decisión no se pueda tomar sin borrar una línea de acá, que es exactamente la conversación que
/// debería pasar antes.</para>
///
/// <para><b>Y el disparador para revisarla está escrito</b>, para que no haya que reconstruir el
/// razonamiento: que un vuelo necesite sobreventa por clase tarifaria. Eso es comportamiento de
/// pozo contable y <c>Api.Booking</c> no lo sabe expresar — su capacidad es un techo duro.</para>
/// </remarks>
public sealed class ViajesCapabilityChoiceTests
{
    /// <summary>Lo que este orquestador NO llama, y por qué cada una.</summary>
    private static readonly (string Ruta, string PorQue)[] Prohibidas =
    {
        ("v1/items", "apartar un producto de viaje es una ventana sobre un recurso, no un pozo contable"),
        ("v1/carts", "un viaje no se arma en una canasta: se aparta y se confirma"),
        ("v1/orders", "un viaje no se despacha, así que registrar un pedido sería papeleo"),
        ("v1/shipments", "ídem — no hay nada que enviar"),
    };

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Synergos.CMS.sln")))
        {
            dir = dir.Parent;
        }
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    /// <summary>El fichero sin comentarios: la prosa explica el código, no lo es.</summary>
    /// <remarks>
    /// Hace falta y lo demostraron dos mutaciones anteriores (HU #29 y #33a): estos ficheros
    /// explican en prosa lo que hacen, así que un gate que busque <c>v1/items</c> sobre el texto
    /// crudo lo encuentra en el <c>&lt;remarks&gt;</c> —donde justamente se explica por qué NO se
    /// usa— y se pone rojo sin que nadie haya roto nada.
    /// </remarks>
    private static string SinComentarios(string ruta)
        => string.Join('\n', File.ReadAllLines(ruta).Select(l =>
        {
            var t = l.TrimStart();
            if (t.StartsWith("//", StringComparison.Ordinal) || t.StartsWith("*", StringComparison.Ordinal))
            {
                return string.Empty;
            }
            var i = l.IndexOf("//", StringComparison.Ordinal);
            return i >= 0 ? l[..i] : l;
        }));

    private static IReadOnlyList<string> FuentesDelOrquestador()
    {
        var raiz = Path.Combine(RepoRoot(), "Synergos.Bff.Viajes");
        var ficheros = Directory.EnumerateFiles(raiz, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                     && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .ToList();

        // "No encontré nada" no puede parecerse a "está todo bien": si el barrido se queda sin
        // ficheros —un rename de carpeta, un path mal armado— el gate pasaría en verde vigilando
        // el vacío. Ya se cometió ese error dos veces en este repo.
        Assert.True(ficheros.Count >= 5,
            $"El barrido encontró {ficheros.Count} ficheros en Synergos.Bff.Viajes; el orquestador tiene más.");
        return ficheros;
    }

    [Fact]
    public void Todo_producto_de_viaje_se_aparta_contra_Api_Booking()
    {
        var culpables = new List<string>();

        foreach (var fichero in FuentesDelOrquestador())
        {
            var codigo = SinComentarios(fichero);
            foreach (var (ruta, porQue) in Prohibidas)
            {
                if (codigo.Contains(ruta, StringComparison.Ordinal))
                {
                    culpables.Add($"{Path.GetFileName(fichero)} → {ruta} ({porQue})");
                }
            }
        }

        Assert.True(culpables.Count == 0,
            "Viajes habla con Booking, Pricing y Payments, y con nadie más. Si de verdad hace "
            + "falta un pozo contable —sobreventa por clase tarifaria es el caso—, eso es una "
            + "decisión que va ANTES que el código, no un import. Lo hacen: "
            + string.Join(" · ", culpables));

        // Y lo que sí tiene que estar: si esto desapareciera, lo de arriba pasaría vigilando un
        // orquestador que ya no aparta nada.
        var todo = string.Join('\n', FuentesDelOrquestador().Select(SinComentarios));
        Assert.Contains("v1/holds", todo, StringComparison.Ordinal);
        Assert.Contains("v1/resources", todo, StringComparison.Ordinal);
        Assert.Contains("v1/reservations/", todo, StringComparison.Ordinal);
    }

    /// <summary>
    /// Deshacer un apartado y deshacer una reserva son DOS compensaciones, no una.
    /// </summary>
    /// <remarks>
    /// <para>Es lo delicado de este dominio. Al confirmar el tercer ítem, los dos primeros ya son
    /// reservas — y <c>Api.Booking</c> rechaza soltar un apartado que ya se confirmó. Con un solo
    /// kind, esas compensaciones fallarían para siempre por una razón que no tiene nada que ver
    /// con el mundo real (<c>feedback_compensation_changes_character</c>), y el viajero se
    /// quedaría con reservas que nadie va a usar y con la plata cobrada.</para>
    ///
    /// <para>El comportamiento lo cubre <c>TripCompensationTests</c>; esto vigila que el par no
    /// se colapse en uno «simplificando».</para>
    /// </remarks>
    [Fact]
    public void Cada_compensacion_que_cambia_de_caracter_tiene_SUS_DOS_formas()
    {
        var codigo = SinComentarios(Path.Combine(
            RepoRoot(), "Synergos.Bff.Viajes", "Domain", "Saga.cs"));

        foreach (var kind in new[]
                 {
                     "ReleaseBookingHold", "CancelReservation",   // el cupo, antes y después de confirmar
                     "VoidPayment", "RefundPayment",              // la plata, antes y después de capturar
                 })
        {
            Assert.Contains(kind, codigo, StringComparison.Ordinal);
        }

        // Y el ejecutor sabe hacer las cuatro: declararlas sin ejecutarlas dejaría la
        // compensación cayendo en `unknown_compensation` hasta rendirse.
        var ejecutor = SinComentarios(Path.Combine(
            RepoRoot(), "Synergos.Bff.Viajes", "Domain", "ViajesCompensationExecutor.cs"));

        foreach (var kind in new[]
                 {
                     "ViajesCompensations.ReleaseBookingHold", "ViajesCompensations.CancelReservation",
                     "ViajesCompensations.VoidPayment", "ViajesCompensations.RefundPayment",
                 })
        {
            Assert.Contains(kind, ejecutor, StringComparison.Ordinal);
        }
    }
}

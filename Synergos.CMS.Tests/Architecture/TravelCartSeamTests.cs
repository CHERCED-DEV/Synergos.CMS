namespace Synergos.CMS.Tests.Architecture;

/// <summary>
/// Dónde está la frontera entre el MOTOR del carrito y el EXPEDIENTE de la compra (#40).
/// </summary>
/// <remarks>
/// <para>La extracción existe para que comprar contra un orquestador no obligue a reimplementar
/// el expediente. Si el servicio vuelve a hablar con las capacidades por su cuenta, la seam sigue
/// ahí y ya no sirve para nada — y eso no rompe ningún test de comportamiento, porque el
/// resultado sería idéntico.</para>
/// </remarks>
public sealed class TravelCartSeamTests
{
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

    private static string SinComentarios(string ruta)
    {
        Assert.True(File.Exists(ruta), $"No existe {ruta}: revisar este gate.");
        return string.Join('\n', File.ReadAllLines(ruta).Select(l =>
        {
            var t = l.TrimStart();
            if (t.StartsWith("//", StringComparison.Ordinal) || t.StartsWith("*", StringComparison.Ordinal)
                || t.StartsWith("///", StringComparison.Ordinal))
            {
                return string.Empty;
            }
            var i = l.IndexOf("//", StringComparison.Ordinal);
            return i >= 0 ? l[..i] : l;
        }));
    }

    private static string Servicio() => SinComentarios(Path.Combine(
        RepoRoot(), "Synergos.CMS.Application", "Services", "Impl", "TravelCartService.cs"));

    private static string Seam() => SinComentarios(Path.Combine(
        RepoRoot(), "Synergos.CMS.Interfaces", "ITravelCartEngine.cs"));

    /// <summary>
    /// El servicio ya no aparta, ni cobra, ni devuelve por su cuenta.
    /// </summary>
    /// <remarks>
    /// Es la mitad que hace posible el segundo motor. Volver a llamar a <c>IPaymentProvider</c>
    /// desde acá dejaría la mitad del flujo cableada al motor en proceso, y el orquestador sólo
    /// se llevaría la otra — con dos sitios decidiendo sobre la misma compra.
    /// </remarks>
    [Fact]
    public void El_expediente_no_habla_con_las_capacidades()
    {
        var servicio = Servicio();

        foreach (var prohibido in new[]
                 {
                     "_payments.CreateSessionAsync", "_payments.CaptureAsync", "_payments.RefundAsync",
                     "_reservations.HoldItemAsync", "_reservations.ConfirmAsync", "_reservations.CancelAsync",
                 })
        {
            Assert.DoesNotContain(prohibido, servicio, StringComparison.Ordinal);
        }

        // Y sí delega las tres operaciones del motor.
        foreach (var delegado in new[] { "_engine.HoldAllAsync", "_engine.SettleAsync", "_engine.ReleaseAsync" })
        {
            Assert.Contains(delegado, servicio, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// El EXPEDIENTE se queda de este lado.
    /// </summary>
    /// <remarks>
    /// Huésped, código de confirmación, etapa del timeline y rastro de la cancelación no los
    /// guarda un orquestador —a propósito, por la lección de privacidad de #35 y #47— así que
    /// llevárselos obligaría a reimplementarlos allá, y la segunda copia divergiría.
    /// </remarks>
    [Fact]
    public void El_expediente_se_queda_en_el_CMS()
    {
        var servicio = Servicio();

        // Se miran los USOS, no las declaraciones. Buscar «BuildConfirmationCode» a secas pasaba
        // en verde con el método declarado y nadie llamándolo — una mutación que quitó su uso no
        // se puso roja, y un gate que encuentra su propia declaración no vigila nada.
        foreach (var propio in new[]
                 {
                     "await WriteAsync(", "_tracking.AdvanceAsync", "_notifier,", "AuditCancellationAsync(",
                     "BuildConfirmationCode(order.OrderRef)",
                 })
        {
            Assert.Contains(propio, servicio, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// El motor reporta POR ÍTEM, no con un booleano.
    /// </summary>
    /// <remarks>
    /// <para>Es la decisión que hubo que tomar antes de escribir la seam (#40): un carrito
    /// multi-producto puede quedar a medias de forma legítima —quien compró un vuelo, un hotel y
    /// un auto no pierde el vuelo porque el auto se agotó— y una firma de todo-o-nada habría
    /// cerrado esa puerta <b>desde el tipo</b>, obligando a rehacerla al cablear.</para>
    /// </remarks>
    [Fact]
    public void El_motor_reporta_por_item()
    {
        var seam = Seam();

        Assert.Contains("record TravelCartSettledItem(", seam, StringComparison.Ordinal);
        Assert.Contains("IReadOnlyList<TravelCartSettledItem> Items", seam, StringComparison.Ordinal);

        // Y liquidar no devuelve un sí/no.
        Assert.DoesNotContain("Task<bool> SettleAsync", seam, StringComparison.Ordinal);
    }

    /// <summary>
    /// La seam vive en <c>Interfaces</c> y no sabe de HTTP ni de Umbraco.
    /// </summary>
    /// <remarks>
    /// El grafo de dependencias (<c>CLAUDE.md</c> §0.A.1): el día que el segundo motor sea un
    /// cliente HTTP, ese cliente vive en <c>Web/Services/</c> — la seam no puede arrastrar hasta
    /// acá lo que sólo el borde necesita.
    /// </remarks>
    [Fact]
    public void La_seam_no_sabe_de_HTTP_ni_de_Umbraco()
    {
        var seam = Seam();

        foreach (var prohibido in new[] { "HttpClient", "Umbraco", "Microsoft.AspNetCore", "IHttpClientFactory" })
        {
            Assert.DoesNotContain(prohibido, seam, StringComparison.Ordinal);
        }
    }
}

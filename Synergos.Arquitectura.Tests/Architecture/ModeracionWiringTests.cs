namespace Synergos.CMS.Tests.Architecture;

/// <summary>
/// Cómo guarda el CMS las denuncias en <c>Api.Moderation</c> (012-blogs-social, I5): el interruptor
/// <c>Synergos:Moderation:Mode</c>, su sección y lo que el cliente <c>ModeracionEnApi</c> rechaza.
/// </summary>
/// <remarks>
/// <para><b>Lo que se protege es una denuncia dada por recibida que no llegó</b>, y una cola pintada vacía que no se
/// pudo leer: las dos le mienten a quien denuncia y a quien modera. Todo lo que no contestó —la red, un 5xx, un 401 de
/// llave, un cuerpo ilegible, un despliegue sin dirección— es «la moderación no contestó», y el controlador lo dice con
/// un 503.</para>
/// <para>El molde (<c>MoldeDelVerticalTests</c>) comprueba la forma del cableado; esto dice qué rechaza y por qué.</para>
/// </remarks>
public sealed class ModeracionWiringTests
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

    /// <summary>El fichero sin comentarios: la prosa explica el código, no lo es.</summary>
    private static string SinComentarios(params string[] partes)
    {
        var ruta = Path.Combine([RepoRoot(), .. partes]);
        Assert.True(File.Exists(ruta), $"No existe {ruta}: revisar este gate.");
        return string.Join('\n', File.ReadAllLines(ruta).Select(l =>
        {
            var t = l.TrimStart();
            if (t.StartsWith("//", StringComparison.Ordinal) || t.StartsWith('*'))
            {
                return string.Empty;
            }

            var i = l.IndexOf("//", StringComparison.Ordinal);
            return i >= 0 ? l[..i] : l;
        }));
    }

    private static string Composer() => SinComentarios("Synergos.CMS.Web", "Composers", "SeamComposer.ModerationDevNotifications.cs");

    private static string Cliente() => SinComentarios("Synergos.CMS.Web", "Services", "Moderacion", "ModeracionEnApi.cs");

    private static string Controlador() => SinComentarios("Synergos.CMS.Web", "Controllers", "ModeracionController.cs");

    /// <summary>La sección se ENLAZA, o la llave y el plazo del cliente se quedan en su default en silencio.</summary>
    [Fact]
    public void La_seccion_de_la_moderacion_se_enlaza()
    {
        Assert.Contains(
            "services.Configure<ModeracionSettings>(builder.Config.GetSection(\"Synergos:Moderation\"))",
            Composer(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Se enciende con «Api» por la pieza que valida la palabra (#182); con el modo y sin dirección, la moderación que no
    /// contesta y no un puerto adivinado. Sin el modo, la de siempre en el almacén común.
    /// </summary>
    [Fact]
    public void Con_Api_los_casos_van_a_Api_Moderation_y_sin_direccion_no_contesta()
    {
        var c = Composer();
        var i = c.IndexOf("Interruptor.Encendido(builder.Config, \"Synergos:Moderation:Mode\", \"Api\"", StringComparison.Ordinal);
        Assert.True(i > 0, "La moderación dejó de encenderse por la pieza que valida la palabra: una errata caería a Local sin avisar.");
        var bloque = c[i..Math.Min(c.Length, i + 1100)];
        Assert.Contains("ModeracionEnApi.ClientName", bloque, StringComparison.Ordinal);
        Assert.Contains("moderacion[\"BaseUrl\"]!", bloque, StringComparison.Ordinal);
        Assert.Contains("AddSingleton<IModeracion, ModeracionSinDestino>", bloque, StringComparison.Ordinal);
        Assert.Contains("new ModeracionLocal(", bloque, StringComparison.Ordinal);
        Assert.DoesNotContain("127.0.0.1", bloque, StringComparison.Ordinal);
    }

    /// <summary>
    /// El cliente: la red y el plazo (no la cancelación de quien llama), cualquier estado que no sea 2xx y un cuerpo
    /// ilegible son «no contestó»; por el cable va la llave pública de quien denuncia, nunca un correo de la sesión.
    /// </summary>
    [Fact]
    public void El_cliente_no_da_por_hecho_lo_que_no_contesto()
    {
        var l = Cliente();
        Assert.Contains(
            "catch (Exception ex) when (ex is HttpRequestException || (ex is TaskCanceledException && !ct.IsCancellationRequested))",
            l, StringComparison.Ordinal);
        Assert.Contains("if (!respuesta.IsSuccessStatusCode)", l, StringComparison.Ordinal);
        Assert.Contains("catch (JsonException ex)", l, StringComparison.Ordinal);
        Assert.Contains("denuncia.Denunciante", l, StringComparison.Ordinal);
        Assert.DoesNotContain("CurrentMemberEmail", l, StringComparison.Ordinal);
    }

    /// <summary>Denunciar, la cola y decidir dicen que no se pudo cuando la moderación no contesta: un 503, no un 200.</summary>
    [Fact]
    public void El_controlador_dice_que_no_se_pudo_en_las_tres()
    {
        var c = Controlador();
        Assert.Equal(3, c.Split("catch (ModeracionNoDisponibleException)").Length - 1);
        Assert.Contains("StatusCodes.Status503ServiceUnavailable", c, StringComparison.Ordinal);
        Assert.Contains("_identidad.De(yo).Llave", c, StringComparison.Ordinal);
    }
}

namespace Synergos.CMS.Tests.Architecture;

/// <summary>
/// Cómo lee el CMS el permiso del paciente en <c>Api.Consent</c> (003-salud-cita, I2 T054, D-003-I2-1): el interruptor
/// <c>Synergos:Consent:Mode</c>, su sección y lo que el lector <c>HttpConsentReader</c> rechaza.
/// </summary>
/// <remarks>
/// <para><b>Lo que se protege es una historia clínica abierta sin permiso.</b> La guardia deja entrar al médico sólo con el
/// permiso <c>salud.historia</c> leído en la capacidad; todo lo que no se pudo leer —la red, un 5xx, un 401 de llave, un
/// cuerpo ilegible, un despliegue sin dirección— NIEGA. Un lector que contestara «vigente» por no saber abriría la
/// historia de cualquiera.</para>
/// <para>El molde (<c>MoldeDelVerticalTests</c>) comprueba la forma del cableado; esto dice qué rechaza y por qué.</para>
/// </remarks>
public sealed class ConsentWiringTests
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

    private static string Composer() => SinComentarios("Synergos.CMS.Web", "Composers", "SeamComposer.PlatformAndHealthcare.cs");

    private static string Lector() => SinComentarios("Synergos.CMS.Web", "Services", "HttpConsentReader.cs");

    private static string Guardia() => SinComentarios("Synergos.CMS.Web", "Services", "DefaultPhiAccessGuard.cs");

    /// <summary>La sección se ENLAZA, o la llave y el plazo del lector se quedan en su default en silencio.</summary>
    [Fact]
    public void La_seccion_del_permiso_se_enlaza()
    {
        Assert.Contains(
            "services.Configure<ConsentSettings>(builder.Config.GetSection(\"Synergos:Consent\"))",
            Composer(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Se enciende con «Api» por la pieza que valida la palabra (#182); con el modo y sin dirección, el lector que niega
    /// todo y no un puerto adivinado.
    /// </summary>
    [Fact]
    public void Con_Api_la_guardia_lee_Api_Consent_y_sin_direccion_niega_todo()
    {
        var c = Composer();
        var i = c.IndexOf("Interruptor.Encendido(builder.Config, \"Synergos:Consent:Mode\", \"Api\"", StringComparison.Ordinal);
        Assert.True(i > 0, "El permiso dejó de encenderse por la pieza que valida la palabra: una errata caería al libro local sin avisar.");

        var bloque = c[i..Math.Min(c.Length, i + 900)];
        Assert.Contains("HttpConsentReader.ClientName", bloque, StringComparison.Ordinal);
        Assert.Contains("consent[\"BaseUrl\"]!", bloque, StringComparison.Ordinal);
        Assert.Contains("AddSingleton<IConsentReader, LectorDePermisosSinDestino>", bloque, StringComparison.Ordinal);
        Assert.DoesNotContain("127.0.0.1", bloque, StringComparison.Ordinal);
    }

    /// <summary>
    /// El lector: sólo un 2xx con el id del permiso es «vigente»; sólo un no de la capacidad (<c>consent.*</c>) es «no
    /// vigente»; la red, el plazo y un cuerpo ilegible son «no se pudo leer».
    /// </summary>
    [Fact]
    public void El_lector_no_da_por_vigente_lo_que_no_pudo_leer()
    {
        var l = Lector();
        Assert.Contains("new PermisoLeido(EstadoDelPermiso.Vigente, permiso)", l, StringComparison.Ordinal);
        Assert.Contains("StartsWith(\"consent.\", StringComparison.Ordinal)", l, StringComparison.Ordinal);
        Assert.Contains("catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)", l,
            StringComparison.Ordinal);
        Assert.Equal(1, l.Split("EstadoDelPermiso.Vigente").Length - 1);
    }

    /// <summary>
    /// La guardia: con lector, lo que no es vigente niega (y «no se pudo leer» niega con su motivo); sin lector, el libro
    /// local de antes, médico por médico, que es la vuelta atrás.
    /// </summary>
    [Fact]
    public void La_guardia_niega_lo_que_no_es_un_permiso_vigente_y_sin_lector_vuelve_al_libro_local()
    {
        var g = Guardia();
        Assert.Contains("EstadoDelPermiso.Vigente => (true,", g, StringComparison.Ordinal);
        Assert.Contains("_ => (false, \"consent-unavailable\"", g, StringComparison.Ordinal);

        var conLector = g.IndexOf("if (_permisos is not null)", StringComparison.Ordinal);
        var libro = g.IndexOf("_consent.HasActiveConsentAsync", StringComparison.Ordinal);
        Assert.True(conLector > 0 && libro > conLector,
            "El libro local tiene que ir DESPUÉS del lector: con Api.Consent encendido, el libro local no decide.");
    }
}

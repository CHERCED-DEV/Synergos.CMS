namespace Synergos.CMS.Tests.Architecture;

/// <summary>
/// Que el intento rechazado deje rastro, y que el rastro no mienta (HU #15).
/// </summary>
/// <remarks>
/// <para>Cinco cosas que el compilador no ve y que, rotas, <b>no rompen nada visiblemente</b> —
/// que en una bitácora es el único modo de fallo que importa, porque se descubre el día que
/// alguien pregunta qué pasó y ya no hay nada que mirar:</para>
///
/// <list type="number">
///   <item>que el 403 de un acto ajeno deje asiento — hasta la HU #15 salía y no se escribía en
///   ninguna parte;</item>
///   <item>que el asiento no afirme como verificado lo que sólo está declarado — es el defecto
///   #42 sobre el registro que más se conserva;</item>
///   <item>que se escriba LOCAL antes de salir a la red, porque lo que se lee sale de ahí;</item>
///   <item>que un reenvío fallido quede escrito y no en un <c>catch</c> vacío — el §5 de la HU
///   con todas las letras;</item>
///   <item>que el correo de quien actuó no salga de esta máquina, y que el stub siga siendo el
///   default.</item>
/// </list>
/// </remarks>
public sealed class AuditWiringTests
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
    private static string SinComentarios(string ruta)
    {
        Assert.True(File.Exists(ruta), $"No existe {ruta}: revisar este gate.");
        return string.Join('\n', File.ReadAllLines(ruta).Select(l =>
        {
            var t = l.TrimStart();
            if (t.StartsWith("//", StringComparison.Ordinal) || t.StartsWith("///", StringComparison.Ordinal)
                || t.StartsWith("*", StringComparison.Ordinal))
            {
                return string.Empty;
            }
            var i = l.IndexOf("//", StringComparison.Ordinal);
            return i >= 0 ? l[..i] : l;
        }));
    }

    private static string Escritor() => SinComentarios(Path.Combine(
        RepoRoot(), "Synergos.CMS.Web", "Services", "HttpAuditTrailWriter.cs"));

    private static string Controlador() => SinComentarios(Path.Combine(
        RepoRoot(), "Synergos.CMS.Web", "Controllers", "GovController.cs"));

    private static string Composer() => SinComentarios(Path.Combine(
        RepoRoot(), "Synergos.CMS.Web", "Composers", "SeamComposer.FormsSearchMemberAdmin.cs"));

    // ── 1. El asiento existe ────────────────────────────────────────────────

    /// <summary>
    /// El acceso rechazado a un acto ajeno deja asiento.
    /// </summary>
    /// <remarks>
    /// Es la HU entera. El gate mira que el <c>catch</c> del rechazo llame al escritor antes de
    /// devolver el 403 — no que exista un método con buen nombre, que es lo que estaría verde con
    /// el asiento nunca escrito.
    /// </remarks>
    [Fact]
    public void El_acceso_rechazado_a_un_acto_ajeno_deja_asiento()
    {
        var c = Controlador();

        var i = c.IndexOf("catch (GovActNotAddresseeException)", StringComparison.Ordinal);
        Assert.True(i > 0, "GovController ya no rechaza al que no es destinatario: revisar este gate.");

        var j = c.IndexOf("Status403Forbidden", i, StringComparison.Ordinal);
        Assert.True(j > i, "El rechazo dejó de devolver 403: revisar este gate.");

        var bloque = c[i..j];
        Assert.Contains("AsentarAccesoRechazadoAsync", bloque, StringComparison.Ordinal);

        Assert.Contains("_audit.WriteAsync", c, StringComparison.Ordinal);
    }

    /// <summary>
    /// El asiento dice sobre QUÉ y POR QUÉ se rechazó, no sólo que pasó algo.
    /// </summary>
    [Fact]
    public void El_asiento_nombra_el_recurso_y_la_causa()
    {
        var c = Controlador();
        var i = c.IndexOf("AsentarAccesoRechazadoAsync(string", StringComparison.Ordinal);
        Assert.True(i > 0, "Se renombró el método del asiento: revisar este gate.");

        var cuerpo = c[i..Math.Min(c.Length, i + 1400)];
        Assert.Contains("Resource: notificationId", cuerpo, StringComparison.Ordinal);
        Assert.Contains("not_the_addressee", cuerpo, StringComparison.Ordinal);
        Assert.Contains("Outcome: \"failure\"", cuerpo, StringComparison.Ordinal);
    }

    // ── 2. El asiento no miente sobre la fuerza de lo afirmado ──────────────

    /// <summary>
    /// El asiento afirma <c>CmsSession</c> y nunca <c>IdentityToken</c>.
    /// </summary>
    /// <remarks>
    /// <para>Este camino no presenta ni verifica ningún token: lo único que respalda al actor es
    /// nuestra cookie. Que el despliegue sepa emitir identidad verificable no cambia lo que aquí
    /// se comprobó, y escribir <c>IdentityToken</c> «porque se podría haber presentado uno»
    /// guardaría como hecho lo que nadie verificó.</para>
    ///
    /// <para>Es el defecto #42 con otro disfraz, y por eso el gate mira la constante concreta y no
    /// que «haya una afirmación».</para>
    /// </remarks>
    [Fact]
    public void El_asiento_no_afirma_como_verificado_lo_que_solo_esta_declarado()
    {
        var c = Controlador();
        var i = c.IndexOf("AsentarAccesoRechazadoAsync(string", StringComparison.Ordinal);
        Assert.True(i > 0, "Se renombró el método del asiento: revisar este gate.");

        var cuerpo = c[i..Math.Min(c.Length, i + 1400)];
        Assert.Contains("Assertion: IdentityAssertions.CmsSession", cuerpo, StringComparison.Ordinal);
        Assert.DoesNotContain("IdentityAssertions.IdentityToken", cuerpo, StringComparison.Ordinal);
    }

    /// <summary>
    /// La afirmación viaja en el campo que la capacidad resuelve, y nunca de más.
    /// </summary>
    /// <remarks>
    /// <para>Desde la #72 <c>Api.Audit</c> comprueba la afirmación y la guarda como
    /// <c>ActedWith</c>. Mandarla además suelta en <c>details</c> daría dos sitios que pueden
    /// discrepar sobre el mismo hecho, y el opaco ganaría al comprobado.</para>
    ///
    /// <para>El suelo <c>CmsSession</c> es la AUSENCIA de comprobación —«nos fiamos de quien
    /// llama»— y no un relleno: la capacidad exige una afirmación y sin ella cada asiento se
    /// volvería un hueco. Lo que no puede aparecer nunca es <c>IdentityToken</c> puesto por el
    /// camino, que sí sería inventar una comprobación (defecto #42).</para>
    /// </remarks>
    [Fact]
    public void La_afirmacion_viaja_en_su_campo_y_nunca_de_mas()
    {
        var e = Escritor();

        var i = e.IndexOf("HttpMethod.Post, \"v1/entries\"", StringComparison.Ordinal);
        Assert.True(i > 0, "Cambió el endpoint del reenvío: revisar este gate.");
        var peticion = e[i..Math.Min(e.Length, i + 2000)];

        Assert.Contains("assertion = string.IsNullOrWhiteSpace(evt.Assertion)", peticion, StringComparison.Ordinal);
        Assert.Contains("? IdentityAssertions.CmsSession", peticion, StringComparison.Ordinal);
        Assert.Contains(": evt.Assertion", peticion, StringComparison.Ordinal);
        Assert.DoesNotContain("IdentityAssertions.IdentityToken", peticion, StringComparison.Ordinal);

        var j = e.IndexOf("Dictionary<string, string> Detalles", StringComparison.Ordinal);
        Assert.True(j > 0, "Se renombró el armado de detalles: revisar este gate.");
        Assert.DoesNotContain("assertion", e[j..Math.Min(e.Length, j + 700)], StringComparison.Ordinal);
    }

    // ── 3. Local primero ────────────────────────────────────────────────────

    /// <summary>
    /// Se escribe local ANTES de salir a la red.
    /// </summary>
    /// <remarks>
    /// Las lecturas del seam son síncronas y la bitácora del backoffice se pinta en cada carga, así
    /// que el JSONL es el modelo de lectura también con la capacidad encendida. Reenviar primero
    /// dejaría al administrador sin un asiento que sí se podía guardar cada vez que la red falla.
    /// </remarks>
    [Fact]
    public void El_asiento_se_guarda_local_antes_de_salir_a_la_red()
    {
        var e = Escritor();

        var local = e.IndexOf("_local.WriteAsync(evt", StringComparison.Ordinal);
        var red = e.IndexOf("ReenviarAsync(evt", StringComparison.Ordinal);

        Assert.True(local > 0, "El escritor dejó de guardar local: es el modelo de lectura.");
        Assert.True(red > 0, "El escritor dejó de reenviar: revisar este gate.");
        Assert.True(local < red,
            "Se reenvía antes de guardar local: un fallo de red deja al backoffice sin el asiento.");
    }

    /// <summary>
    /// Lo que se LEE nunca sale a la red.
    /// </summary>
    [Fact]
    public void Las_lecturas_no_dependen_de_la_capacidad()
    {
        var e = Escritor();
        Assert.Contains("GetRecent(int maxItems, string? actorEmailFilter = null, string? actionFilter = null)\n        => _local.GetRecent(", e, StringComparison.Ordinal);
        Assert.Contains("GetById(string id) => _local.GetById(id)", e, StringComparison.Ordinal);
        Assert.Contains("=> _local.GetByDateRange(", e, StringComparison.Ordinal);
    }

    // ── 4. El hueco queda escrito ───────────────────────────────────────────

    /// <summary>
    /// Un reenvío que falla deja asiento del hueco, no un <c>catch</c> vacío.
    /// </summary>
    /// <remarks>
    /// Es el §5 de la HU: «si el destino de auditoría no responde, se dice qué pasa — y lo que pase
    /// es una decisión escrita». Un rastro que se pierde en silencio se pierde el día que hace
    /// falta, y no queda forma de saber que faltaba.
    /// </remarks>
    [Fact]
    public void Un_reenvio_fallido_deja_asiento_del_hueco()
    {
        var e = Escritor();

        var i = e.IndexOf("if (causa is null) return;", StringComparison.Ordinal);
        Assert.True(i > 0, "El escritor dejó de distinguir «llegó» de «no llegó»: revisar este gate.");

        var cuerpo = e[i..Math.Min(e.Length, i + 1200)];
        Assert.Contains("_local.WriteAsync(", cuerpo, StringComparison.Ordinal);
        Assert.Contains("ForwardFailureAction", cuerpo, StringComparison.Ordinal);
        Assert.Contains("Resource: evt.Id", cuerpo, StringComparison.Ordinal);
        Assert.Contains("LogError", cuerpo, StringComparison.Ordinal);
    }

    /// <summary>
    /// El asiento del hueco no se reenvía.
    /// </summary>
    /// <remarks>
    /// Sería contarle a la capacidad caída que no se pudo hablar con ella; su fallo generaría otro
    /// asiento del hueco, y otro, mientras dure la caída.
    /// </remarks>
    [Fact]
    public void El_asiento_del_hueco_no_se_reenvia()
    {
        var e = Escritor();
        Assert.Contains(
            "if (string.Equals(evt.Action, ForwardFailureAction, StringComparison.Ordinal)) return;",
            e, StringComparison.Ordinal);
    }

    // ── 5. Lo personal se queda, y el stub es el default ────────────────────

    /// <summary>
    /// El correo de quien actuó no sale de esta máquina.
    /// </summary>
    /// <remarks>
    /// Lección de #35 y del defecto #47 aplicada de entrada: un dato personal en el disco de otro
    /// servicio es un segundo sitio donde borrar cuando alguien ejerce el derecho al olvido — y la
    /// bitácora es justo la que más se conserva.
    /// </remarks>
    [Fact]
    public void El_correo_de_quien_actuo_no_sale_de_esta_maquina()
    {
        var e = Escritor();

        Assert.Contains("actorId = Seudonimo(evt.ActorEmail)", e, StringComparison.Ordinal);

        var i = e.IndexOf("HttpMethod.Post, \"v1/entries\"", StringComparison.Ordinal);
        Assert.True(i > 0, "Cambió el endpoint del reenvío: revisar este gate.");
        var peticion = e[i..Math.Min(e.Length, i + 1400)];

        Assert.DoesNotContain("evt.ActorName", peticion, StringComparison.Ordinal);
        Assert.DoesNotContain("actorId = evt.ActorEmail", peticion, StringComparison.Ordinal);
    }

    /// <summary>
    /// El seudónimo es estable entre procesos.
    /// </summary>
    /// <remarks>
    /// <c>string.GetHashCode()</c> lo aleatoriza por proceso: la misma persona sería un actor
    /// distinto tras cada reinicio y la bitácora dejaría de agrupar, sin que nada fallara.
    /// </remarks>
    [Fact]
    public void El_seudonimo_no_depende_del_proceso()
    {
        var e = Escritor();
        var i = e.IndexOf("string Seudonimo(", StringComparison.Ordinal);
        Assert.True(i > 0, "Se renombró el seudónimo: revisar este gate.");

        var cuerpo = e[i..Math.Min(e.Length, i + 600)];
        Assert.Contains("SHA256.HashData", cuerpo, StringComparison.Ordinal);
        Assert.DoesNotContain("GetHashCode", cuerpo, StringComparison.Ordinal);
    }

    /// <summary>
    /// El reenvío lleva llave de idempotencia.
    /// </summary>
    [Fact]
    public void El_reenvio_lleva_llave()
    {
        var e = Escritor();
        Assert.Contains("\"Idempotency-Key\", $\"cms-audit-{evt.Id}\"", e, StringComparison.Ordinal);
    }

    /// <summary>
    /// Sin encender nada, la bitácora es la local. Es el camino del clon limpio.
    /// </summary>
    [Fact]
    public void El_default_sigue_siendo_el_JSONL_local()
    {
        var c = Composer();

        var i = c.IndexOf("Synergos:Audit:Mode", StringComparison.Ordinal);
        Assert.True(i > 0, "Desapareció el interruptor de la bitácora: revisar este gate.");

        Assert.Contains("\"Api\", StringComparison.OrdinalIgnoreCase", c[i..Math.Min(c.Length, i + 200)],
            StringComparison.Ordinal);

        var otro = c.IndexOf("else", i, StringComparison.Ordinal);
        Assert.True(otro > i, "El modo Api dejó de tener alternativa: el clon limpio no arrancaría.");
        Assert.Contains("FileSystemAuditTrailWriter", c[otro..Math.Min(c.Length, otro + 300)],
            StringComparison.Ordinal);
    }

    /// <summary>
    /// La sección se ENLAZA, o los <c>Kind</c> se quedan en su default en silencio.
    /// </summary>
    /// <remarks>
    /// El olvido que arrastraron Tienda (#24), Salud (#25), Viajes (#36) y las notificaciones de
    /// Gobierno (#62): lo que no viaja por el <c>HttpClient</c> no llega si nadie enlaza la
    /// sección, y no falla — se comporta como un despliegue mal configurado.
    /// </remarks>
    [Fact]
    public void La_seccion_de_la_bitacora_se_enlaza()
    {
        Assert.Contains(
            "services.Configure<AuditSettings>(builder.Config.GetSection(\"Synergos:Audit\"))",
            Composer(), StringComparison.Ordinal);
    }

    /// <summary>
    /// El vocabulario de la afirmación se declara UNA vez en el árbol del CMS.
    /// </summary>
    /// <remarks>
    /// <para>Nació en la HU #62 como <c>GovActAssertions</c> con un solo consumidor y subió al
    /// aparecer el segundo (<c>CLAUDE.md</c> §17). Una segunda declaración es la copia que hay que
    /// acordarse de cambiar a la vez, y de la que se sale con esta HU.</para>
    ///
    /// <para>Mira la DECLARACIÓN, no la mención: usar <c>IdentityAssertions.CmsSession</c> desde
    /// donde sea es legítimo; declarar otra clase de constantes es la copia. Y no mira el árbol de
    /// servicios: allá el mismo concepto vive en <c>Synergos.Core</c> a propósito, porque los dos
    /// árboles no se referencian.</para>
    /// </remarks>
    [Fact]
    public void El_vocabulario_de_la_afirmacion_no_se_declara_dos_veces()
    {
        var raiz = RepoRoot();
        var arboles = new[] { "Synergos.CMS.Interfaces", "Synergos.CMS.Application", "Synergos.CMS.Web" };

        var declaraciones = new List<string>();
        foreach (var arbol in arboles)
        {
            foreach (var f in Directory.EnumerateFiles(Path.Combine(raiz, arbol), "*.cs", SearchOption.AllDirectories))
            {
                var texto = SinComentarios(f);
                if (texto.Contains("const string CmsSession", StringComparison.Ordinal))
                {
                    declaraciones.Add(Path.GetRelativePath(raiz, f));
                }
            }
        }

        Assert.Equal(
            new[] { Path.Combine("Synergos.CMS.Interfaces", "IdentityAssertions.cs") },
            declaraciones.OrderBy(x => x, StringComparer.Ordinal).ToArray());
    }
}

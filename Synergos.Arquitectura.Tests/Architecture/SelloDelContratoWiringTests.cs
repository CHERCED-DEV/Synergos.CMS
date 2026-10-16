using Synergos.CMS.Application.Configuration;

namespace Synergos.CMS.Tests.Architecture;

/// <summary>
/// Quién custodia la llave del contrato de alquiler, y qué pasa con lo ya emitido (research R11 de la línea 005, T023).
/// </summary>
/// <remarks>
/// <para>Lo que el compilador no vigila y que, roto, <b>no rompe nada visiblemente</b>: que el sellador local siga
/// registrado para comprobar lo anterior al interruptor, que el de <c>Api.Signing</c> selle y no firme, que su
/// propósito no sea el de los diplomas, y que una capacidad caída no se traduzca en «sello con la llave local».</para>
/// <para>Es el mismo gate que <c>AcademyWiringTests</c> le puso al diploma (#45), con la regla de este vertical: aquí
/// el contrato REGISTRA quién lo selló, así que no hace falta reconocer lo viejo por la forma.</para>
/// </remarks>
public sealed class SelloDelContratoWiringTests
{
    private const string ClaveDelModo = "\"Synergos:Alquiler:Agreement:Mode\"";

    private static string SinComentarios(string ruta)
    {
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

    private static string Sellador() => SinComentarios(Proyectos.Ruta("Synergos.CMS.Web", "Services", "HttpAgreementSealer.cs"));

    private static string Composer() => SinComentarios(Proyectos.Ruta("Synergos.CMS.Web", "Composers", "SeamComposer.Alquiler.cs"));

    /// <summary>El interruptor tiene dos palabras, su default es la llave local y lo lee la pieza común.</summary>
    [Fact]
    public void El_interruptor_del_sello_es_Local_por_defecto_y_Api_lo_enciende()
    {
        Assert.Equal("Local", new AgreementSettings().Mode);

        var composer = Composer();
        Assert.Contains(
            $"Interruptor.Encendido(builder.Config, {ClaveDelModo}, \"Api\", new AgreementSettings().Mode)",
            composer, StringComparison.Ordinal);
    }

    /// <summary>
    /// El sellador local se registra en los DOS modos, y el emisor lo recibe en las dos ramas.
    /// </summary>
    /// <remarks>
    /// Sin esto, cada contrato emitido antes del interruptor deja de comprobar el día que se pone <c>Api</c> — y no
    /// falla ruidosamente: el comprobante dice que su sello no cuadra, que es lo peor que puede decir.
    /// </remarks>
    [Fact]
    public void El_sellador_local_se_registra_en_los_dos_modos()
    {
        var composer = Composer();

        var registro = composer.IndexOf("new SelladorLocal(", StringComparison.Ordinal);
        var modo = composer.IndexOf(ClaveDelModo, StringComparison.Ordinal);
        Assert.True(registro > 0 && modo > 0 && registro < modo,
            "El sellador local tiene que registrarse ANTES y FUERA del interruptor del sello.");

        var rama = composer[modo..];
        var otra = rama.IndexOf("else", StringComparison.Ordinal);
        Assert.True(otra > 0, "Sin rama else no hay camino por defecto: revisar este gate.");

        // Con Api, el local va como comprobador; con Local, es el que sella.
        Assert.Contains("[sp.GetRequiredService<SelladorLocal>()]", rama[..otra], StringComparison.Ordinal);
        Assert.Contains("sp.GetRequiredService<SelladorLocal>(),", rama[otra..], StringComparison.Ordinal);
    }

    /// <summary>Va a <c>/v1/seals</c> y NUNCA a <c>/v1/signatures</c>, sin vencimiento.</summary>
    /// <remarks>Aquel token vence, no es determinista y publica su payload: un contrato no vence y se re-emite igual.</remarks>
    [Fact]
    public void El_contrato_se_sella_y_no_se_firma()
    {
        var sellador = Sellador();

        Assert.Contains("\"v1/seals\"", sellador, StringComparison.Ordinal);
        Assert.Contains("\"v1/seals/verify\"", sellador, StringComparison.Ordinal);
        Assert.DoesNotContain("v1/signatures", sellador, StringComparison.Ordinal);
        Assert.DoesNotContain("LifetimeMinutes", sellador, StringComparison.Ordinal);
    }

    /// <summary>El propósito es PROPIO: las llaves viven por propósito, y compartirlo dejaría a un diploma comprobar como contrato.</summary>
    [Fact]
    public void El_proposito_del_contrato_no_es_el_de_Academia()
    {
        var proposito = new AgreementSettings().Signing.Purpose;

        Assert.Equal("alquiler.contrato", proposito);
        Assert.NotEqual(new AcademySettings().SealPurpose, proposito);
        Assert.DoesNotContain("academy", Sellador(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// El sellador de <c>Api.Signing</c> no conoce al local: una caída no puede convertirse en un sello local.
    /// </summary>
    /// <remarks>
    /// Caer a la llave local registraría un contrato que nadie eligió sellar así, y ningún test de comportamiento lo ve
    /// si el doble del local no está en ese test. Y ningún <c>catch</c> da un contrato por bueno.
    /// </remarks>
    [Fact]
    public void Una_caida_no_se_sella_con_la_llave_local_ni_se_da_por_buena()
    {
        var sellador = Sellador();

        foreach (var local in new[]
                 {
                     "SelladorLocal", "IAgreementSigner", "HmacAgreementSigner", "LazyAgreementSigner",
                     "AgreementSigningKeyProvider",
                 })
        {
            Assert.DoesNotContain(local, sellador, StringComparison.Ordinal);
        }

        foreach (var bloque in sellador.Split("catch", StringSplitOptions.None).Skip(1))
        {
            var hasta = bloque.IndexOf("\n        }", StringComparison.Ordinal);
            var cuerpo = hasta > 0 ? bloque[..hasta] : bloque;
            Assert.DoesNotContain("true", cuerpo, StringComparison.Ordinal);
        }
    }

    /// <summary>La sección del sello va ANIDADA y se enlaza entera: sin ella, el propósito y el destino quedan en su default.</summary>
    [Fact]
    public void La_seccion_del_sello_va_anidada_bajo_la_del_contrato()
    {
        var composer = Composer();

        Assert.Contains("GetSection(\"Synergos:Alquiler:Agreement:Signing\")", composer, StringComparison.Ordinal);
        Assert.Contains("HttpAgreementSealer.ClientName", composer, StringComparison.Ordinal);
    }
}

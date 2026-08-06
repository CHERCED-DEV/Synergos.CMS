using System.Text;
using Synergos.Api.Documents.Domain;
using Synergos.Core;

namespace Synergos.CMS.Tests.Api;

/// <summary>Cubre <see cref="DocumentRules"/> — tipos, tamaño y enlaces firmados.</summary>
public sealed class DocumentRulesTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 3, 2, 10, 0, 0, TimeSpan.Zero);
    private const string Llave = "llave-de-firma-de-prueba";
    private static readonly byte[] Contenido = Encoding.UTF8.GetBytes("hola");

    [Fact]
    public void La_lista_de_tipos_es_BLANCA_y_lo_que_no_este_no_entra()
    {
        // Una lista negra deja pasar todo lo que nadie pensó en prohibir, y basta un tipo
        // ejecutable servido desde el dominio de la aplicación para que el navegador lo trate
        // como propio.
        Assert.Null(DocumentRules.CheckUpload("a.pdf", "application/pdf", Contenido));
        Assert.Equal("documents.content_type_not_allowed",
            DocumentRules.CheckUpload("a.exe", "application/x-msdownload", Contenido)?.Code);
        Assert.Equal("documents.content_type_not_allowed",
            DocumentRules.CheckUpload("a.svg", "image/svg+xml", Contenido)?.Code);
    }

    [Fact]
    public void Un_documento_vacio_o_sin_nombre_no_entra()
    {
        Assert.Equal("documents.name_required", DocumentRules.CheckUpload(" ", "application/pdf", Contenido)?.Code);
        Assert.Equal("documents.empty_content", DocumentRules.CheckUpload("a.pdf", "application/pdf", Array.Empty<byte>())?.Code);
    }

    [Fact]
    public void El_tamano_tiene_tope()
    {
        var enorme = new byte[DocumentRules.MaxSizeBytes + 1];

        Assert.Equal("documents.too_large", DocumentRules.CheckUpload("a.pdf", "application/pdf", enorme)?.Code);
    }

    [Fact]
    public void La_huella_cambia_si_cambia_un_byte()
    {
        var a = DocumentRules.Fingerprint(Encoding.UTF8.GetBytes("hola"));
        var b = DocumentRules.Fingerprint(Encoding.UTF8.GetBytes("holA"));

        Assert.NotEqual(a, b);
        Assert.Equal(a, DocumentRules.Fingerprint(Encoding.UTF8.GetBytes("hola")));
    }

    [Fact]
    public void Un_enlace_bien_firmado_y_vigente_pasa()
    {
        var expira = Ahora.AddMinutes(15);
        var token = DocumentRules.SignLink("doc-1", expira, Llave);

        Assert.Null(DocumentRules.CheckLink("doc-1", expira, token, Llave, Ahora));
    }

    [Fact]
    public void Correr_el_VENCIMIENTO_invalida_la_firma()
    {
        // El vencimiento va DENTRO de lo firmado. Si viajara aparte, cualquiera lo correría
        // hacia adelante y convertiría un enlace de quince minutos en uno eterno.
        var expira = Ahora.AddMinutes(15);
        var token = DocumentRules.SignLink("doc-1", expira, Llave);

        var bad = DocumentRules.CheckLink("doc-1", expira.AddYears(1), token, Llave, Ahora);

        Assert.Equal("documents.link_bad_signature", bad?.Code);
    }

    [Fact]
    public void Un_token_de_OTRO_documento_no_sirve()
    {
        var expira = Ahora.AddMinutes(15);
        var token = DocumentRules.SignLink("doc-1", expira, Llave);

        Assert.Equal("documents.link_bad_signature",
            DocumentRules.CheckLink("doc-2", expira, token, Llave, Ahora)?.Code);
    }

    [Fact]
    public void Un_enlace_vencido_da_EXPIRED()
    {
        var expira = Ahora;
        var token = DocumentRules.SignLink("doc-1", expira, Llave);

        Assert.Equal(RejectionKind.Expired, DocumentRules.CheckLink("doc-1", expira, token, Llave, Ahora)?.Kind);
        Assert.Null(DocumentRules.CheckLink("doc-1", expira, token, Llave, Ahora.AddTicks(-1)));
    }

    [Fact]
    public void La_FIRMA_se_comprueba_antes_que_el_vencimiento()
    {
        // Al revés, un enlace vencido diría "vencido" aunque su firma fuera inventada — y eso
        // confirma que el documento existe.
        var expira = Ahora.AddHours(-1);

        var bad = DocumentRules.CheckLink("doc-1", expira, "firma-inventada", Llave, Ahora);

        Assert.Equal("documents.link_bad_signature", bad?.Code);
    }

    [Fact]
    public void Sin_token_el_enlace_no_pasa()
    {
        Assert.Equal("documents.link_unsigned",
            DocumentRules.CheckLink("doc-1", Ahora.AddMinutes(5), null, Llave, Ahora)?.Code);
    }
}

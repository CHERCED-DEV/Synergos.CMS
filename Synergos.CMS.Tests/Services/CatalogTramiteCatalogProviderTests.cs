using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// Cubre el catálogo de trámites servido desde el CONTENIDO que autoró la entidad (ADR 0123),
/// que es lo que se registra cuando <c>Synergos:Catalog:Sources:Gov = cms</c>.
/// </summary>
/// <remarks>
/// <para>La propiedad protegida es de regresión silenciosa: <b>el buscador se comporta igual
/// vengan los trámites del contenido o del seed</b>. Mismo descriptor —nombre 5, entidad 3,
/// categoría 2, resumen 1—, misma faceta única por categoría y el mismo <c>Take</c> explícito,
/// porque la seam promete TODAS las coincidencias y el tope por defecto del motor (24) las
/// truncaría en silencio. Un portal de trámites en el que buscar "Registraduría" deje de
/// funcionar al mover un flag de configuración no falla: simplemente deja de encontrar.</para>
///
/// <para>Aquí <b>no hay capa durable que probar</b>, y eso es la decisión y no un hueco:
/// <see cref="ITramiteCatalogProvider"/> es de solo lectura, no existe un segundo autor que
/// publique trámites por fuera del backoffice, y un overlay sin escritor sería un campo que
/// promete algo que nadie cumple.</para>
/// </remarks>
public sealed class CatalogTramiteCatalogProviderTests
{
    private sealed class FakeSource : ICatalogSource<TramiteDetail>
    {
        private readonly IReadOnlyList<TramiteDetail> _items;

        public FakeSource(params TramiteDetail[] items) => _items = items;

        public int Calls { get; private set; }

        public Task<IReadOnlyList<TramiteDetail>> GetAllAsync(
            string? scope = null,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(_items);
        }
    }

    private static TramiteDetail Tramite(
        string slug,
        string name,
        string category = "Identidad",
        string agency = "Registraduría Nacional",
        decimal fee = 65_500m)
        => new(
            Summary: new TramiteSummary(
                Id: slug,
                Slug: slug,
                Name: name,
                Summary: "Resumen del trámite.",
                Category: category,
                Agency: agency,
                Channel: "presencial",
                EstimatedDays: 15,
                FeeMinor: fee,
                Currency: "COP"),
            Description: "Descripción en lenguaje claro.",
            Steps: Array.Empty<TramiteStep>(),
            Eligibility: Array.Empty<string>(),
            Required: Array.Empty<string>(),
            Normativa: "Decreto 1010 de 2000.",
            FormDefinition: new TramiteFormDefinition($"form-{slug}", name, Array.Empty<TramiteFormSection>()),
            RequiresAppointment: false);

    [Fact]
    public async Task Un_catalogo_vacio_devuelve_lista_vacia_y_no_revienta()
    {
        var provider = new CatalogTramiteCatalogProvider(new FakeSource());

        Assert.Empty(await provider.SearchAsync(null, null));
        Assert.Null(await provider.GetAsync("renovacion-cedula"));
    }

    [Fact]
    public async Task Sin_texto_ni_categoria_devuelve_TODO_el_catalogo()
    {
        var provider = new CatalogTramiteCatalogProvider(new FakeSource(
            Tramite("renovacion-cedula", "Renovación de cédula de ciudadanía"),
            Tramite("certificado-residencia", "Certificado de residencia", "Identidad", "Alcaldía Municipal", 0m),
            Tramite("registro-mercantil", "Registro mercantil de empresa", "Empresa", "Cámara de Comercio", 320_000m)));

        Assert.Equal(3, (await provider.SearchAsync(null, null)).Count);
    }

    [Fact]
    public async Task El_texto_filtra_de_verdad_y_no_devuelve_el_catalogo_entero()
    {
        var provider = new CatalogTramiteCatalogProvider(new FakeSource(
            Tramite("renovacion-cedula", "Renovación de cédula de ciudadanía"),
            Tramite("registro-mercantil", "Registro mercantil de empresa", "Empresa", "Cámara de Comercio")));

        var hits = await provider.SearchAsync("mercantil", null);

        Assert.Equal(new[] { "registro-mercantil" }, hits.Select(t => t.Id));
        Assert.Empty(await provider.SearchAsync("xxnoexiste", null));
    }

    [Fact]
    public async Task El_texto_tambien_busca_por_entidad_y_pliega_las_tildes()
    {
        // "Cámara" tecleado sin tilde tiene que encontrar a la Cámara de Comercio: es el caso
        // que hace inservible un buscador es-CO comparado con Contains ordinal.
        var provider = new CatalogTramiteCatalogProvider(new FakeSource(
            Tramite("renovacion-cedula", "Renovación de cédula de ciudadanía"),
            Tramite("registro-mercantil", "Registro mercantil de empresa", "Empresa", "Cámara de Comercio")));

        Assert.Equal(new[] { "registro-mercantil" }, (await provider.SearchAsync("camara", null)).Select(t => t.Id));
    }

    [Fact]
    public async Task La_categoria_es_la_unica_faceta_y_acota_en_AND_con_el_texto()
    {
        var provider = new CatalogTramiteCatalogProvider(new FakeSource(
            Tramite("renovacion-cedula", "Renovación de cédula de ciudadanía"),
            Tramite("expedicion-pasaporte", "Expedición de pasaporte", "Identidad", "Cancillería", 189_000m),
            Tramite("registro-mercantil", "Registro mercantil de empresa", "Empresa", "Cámara de Comercio")));

        Assert.Equal(2, (await provider.SearchAsync(null, "Identidad")).Count);
        Assert.Equal(
            new[] { "expedicion-pasaporte" },
            (await provider.SearchAsync("pasaporte", "Identidad")).Select(t => t.Id));
        Assert.Empty(await provider.SearchAsync("pasaporte", "Empresa"));
    }

    [Fact]
    public async Task Un_trámite_sin_categoria_sigue_saliendo_en_el_listado_completo()
    {
        // Es la mitad que justifica que las reglas no lo descarten: queda fuera de la faceta,
        // no del portal.
        var provider = new CatalogTramiteCatalogProvider(new FakeSource(
            Tramite("subsidio-vivienda", "Solicitud de subsidio de vivienda", category: string.Empty)));

        Assert.Single(await provider.SearchAsync(null, null));
        Assert.Single(await provider.SearchAsync("subsidio", null));
    }

    [Fact]
    public async Task La_ficha_se_resuelve_por_id_o_por_slug_sin_importar_mayusculas_ni_espacios()
    {
        var provider = new CatalogTramiteCatalogProvider(new FakeSource(
            Tramite("renovacion-cedula", "Renovación de cédula de ciudadanía")));

        Assert.NotNull(await provider.GetAsync("  Renovacion-Cedula "));
        Assert.Null(await provider.GetAsync("no-existe"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Una_referencia_vacia_devuelve_null_sin_tocar_la_fuente(string tramiteId)
    {
        // Recorrer el árbol de contenido para no comparar nada sería trabajo puro por petición.
        var source = new FakeSource(Tramite("renovacion-cedula", "Renovación de cédula de ciudadanía"));
        var provider = new CatalogTramiteCatalogProvider(source);

        Assert.Null(await provider.GetAsync(tramiteId));
        Assert.Equal(0, source.Calls);
    }

    [Fact]
    public async Task Sin_fuente_no_se_construye()
    {
        Assert.Throws<ArgumentNullException>(() => new CatalogTramiteCatalogProvider(null!));
        await Task.CompletedTask;
    }
}

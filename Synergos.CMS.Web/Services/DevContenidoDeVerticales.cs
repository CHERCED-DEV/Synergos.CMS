using System.Text.Json.Nodes;
using Umbraco.Cms.Core.Services;

namespace Synergos.CMS.Web.Services;

/// <summary>
/// Le pone contenido real a las portadas de las verticales que hoy se ven pobres (016-contenido-piezas): suma
/// secciones al final del cuerpo de una página, compuestas con piezas que ya están en el catálogo.
/// </summary>
/// <remarks>
/// <para><b>Herramienta, no siembra</b> (ADR 0013): no corre sola; se invoca con <c>POST /dev/contenido/{vertical}</c>
/// detrás de <c>Synergos:DevSeed:Enabled</c>. Lo que deja se exporta con uSync (ADR 0129) y al repo va ese export.</para>
///
/// <para><b>Suma, no pisa.</b> Lo que la página ya tiene queda igual y en su sitio; las secciones nuevas van debajo. La
/// primera sección nueva lleva una clase-marca (<c>syn-contenido--{vertical}</c>): si la página ya la tiene, la
/// herramienta no hace nada y lo dice, así que repetirla no duplica ni se lleva lo que el editor cambió después.</para>
///
/// <para><b>Fotos reales, no relleno</b>: las tarjetas usan las fotos CC0 ya versionadas en <c>wwwroot/media/&lt;dominio&gt;/</c>,
/// importadas como nodos de Media en la carpeta <c>Fotos</c> (editables y reemplazables desde el backoffice).</para>
/// </remarks>
public sealed class DevContenidoDeVerticales
{
    private const string Cultura = "es-CO";
    private const string Cuerpo = "sections";

    /// <summary>La raíz de sitio Viajes (<c>/SynergosLabs/Viajes</c>), por su Key de uSync.</summary>
    public static readonly Guid Viajes = Guid.Parse("54097be5-a62e-4c93-b674-416751667009");

    private static readonly Guid AreaDeLaSeccion = LayoutComposerKeys.SectionContentArea;
    private static readonly Guid[] AreasDeTresColumnas =
    [
        new("b3141704-5e2d-4adf-9c83-654377a9717f"),
        new("316ace81-08bb-4688-a54b-930b8378d9e7"),
        new("ecd5685c-7b37-4311-bb0b-6453949cd898"),
    ];
    /// <summary>Las fotos de <c>wwwroot/media/&lt;dominio&gt;/</c> llegaron de Openverse con licencia CC0 o PDM (CMS 0b07e662).</summary>
    private const string LicenciaCc0 = "CC0 o dominio público (Openverse)";
    private const string ClaseDeTresColumnas = "syn-display--grid syn-grid--tpl-3col syn-grid--gap-lg";

    private readonly IContentService _contentService;
    private readonly IContentTypeService _contentTypeService;
    private readonly SchemaBlockDefaults _defaults;
    private readonly DevMediaFactory _media;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<DevContenidoDeVerticales> _logger;

    public DevContenidoDeVerticales(
        IContentService contentService,
        IContentTypeService contentTypeService,
        SchemaBlockDefaults defaults,
        DevMediaFactory media,
        IWebHostEnvironment env,
        ILogger<DevContenidoDeVerticales> logger)
    {
        _contentService = contentService;
        _contentTypeService = contentTypeService;
        _defaults = defaults;
        _media = media;
        _env = env;
        _logger = logger;
    }

    /// <summary>Las verticales que la herramienta sabe componer, con la página y el cuerpo de cada una.</summary>
    public IReadOnlyDictionary<string, (Guid Pagina, Func<BlockGridJsonBuilder, string> Componer)> Verticales =>
        new Dictionary<string, (Guid, Func<BlockGridJsonBuilder, string>)>(StringComparer.OrdinalIgnoreCase)
        {
            ["viajes"] = (Viajes, ComponerViajes),
        };

    /// <summary>Suma el contenido de <paramref name="vertical"/> al final de su página. Idempotente.</summary>
    public Resultado Componer(string vertical)
    {
        if (!Verticales.TryGetValue(vertical, out var receta))
        {
            return new Resultado(false, vertical, "vertical-desconocida", 0);
        }

        var pagina = _contentService.GetById(receta.Pagina);
        if (pagina is null) { return new Resultado(false, vertical, "pagina-no-encontrada", 0); }

        var marca = Marca(vertical);
        var actual = pagina.GetValue<string>(Cuerpo, Cultura) ?? string.Empty;
        if (actual.Contains(marca, StringComparison.Ordinal))
        {
            _logger.LogInformation("Contenido {Vertical}: la página ya tiene la marca {Marca} — no se toca.", vertical, marca);
            return new Resultado(true, vertical, "ya-compuesta", 0);
        }

        var b = new BlockGridJsonBuilder();
        var detalle = receta.Componer(b);
        var nuevo = b.Build();
        var (unido, secciones) = Sumar(actual, nuevo);

        pagina.SetValue(Cuerpo, unido, Cultura);
        var save = _contentService.SaveAndPublish(pagina, new[] { Cultura });
        if (!save.Success)
        {
            var invalidas = save.InvalidProperties is null ? "(null)" : string.Join(",", save.InvalidProperties.Select(p => p.Alias));
            _logger.LogError("Contenido {Vertical}: fallo al publicar. {Result}; invalid=[{Invalid}]", vertical, save.Result, invalidas);
            return new Resultado(false, vertical, $"save-failed:{save.Result}:[{invalidas}]", 0);
        }

        _logger.LogInformation("Contenido {Vertical}: {N} secciones sumadas ({Detalle}).", vertical, secciones, detalle);
        return new Resultado(true, vertical, detalle, secciones);
    }

    public static string Marca(string vertical) => $"syn-contenido--{vertical.ToLowerInvariant()}";

    /// <summary>
    /// Une dos cuerpos de Block Grid: lo que había primero, lo nuevo debajo. Público para probarlo sin Umbraco.
    /// </summary>
    public static (string Json, int SeccionesNuevas) Sumar(string actual, string nuevo)
    {
        var b = JsonNode.Parse(nuevo)!.AsObject();
        if (string.IsNullOrWhiteSpace(actual)) { return (b.ToJsonString(), LayoutDe(b).Count); }

        var a = JsonNode.Parse(actual)!.AsObject();
        var layoutA = LayoutDe(a);
        var layoutB = LayoutDe(b);
        var n = layoutB.Count;
        foreach (var item in layoutB.ToList()) { layoutB.Remove(item); layoutA.Add(item); }
        foreach (var prop in new[] { "contentData", "settingsData" })
        {
            var destino = a[prop] as JsonArray ?? new JsonArray();
            a[prop] = destino;
            if (b[prop] is JsonArray origen)
            {
                foreach (var item in origen.ToList()) { origen.Remove(item); destino.Add(item); }
            }
        }
        return (a.ToJsonString(), n);
    }

    private static JsonArray LayoutDe(JsonObject cuerpo)
    {
        var layout = cuerpo["layout"] as JsonObject ?? new JsonObject();
        cuerpo["layout"] = layout;
        var grid = layout["Umbraco.BlockGrid"] as JsonArray ?? new JsonArray();
        layout["Umbraco.BlockGrid"] = grid;
        return grid;
    }

    // ───────────────────────────── Viajes ─────────────────────────────

    private sealed record Estadia(string Id, string Nombre, string Lugar, string Resumen, string Alt);

    private static readonly Estadia[] EstadiasDeViajes =
    [
        new("ctg-getsemani", "Casa Getsemaní Boutique", "Cartagena · Bolívar",
            "Casa republicana restaurada a cinco minutos a pie de la Ciudad Amurallada, con patios coloniales y terraza sobre los techos del centro histórico.",
            "Patio colonial con arcos blancos y jardín en una casa de Getsemaní"),
        new("ctg-bocagrande", "Torre Bocagrande Beach Resort", "Cartagena · Bolívar",
            "Frente al mar en la primera línea de Bocagrande: playa privada, tres piscinas y club de niños para viajar en familia.",
            "Piscina rodeada de palmas y sombrillas en un hotel de Bocagrande"),
        new("med-provenza", "Provenza Lofts El Poblado", "Medellín · Antioquia",
            "Lofts de diseño en la Provenza, con rooftop, coworking y café de especialidad a pasos de los restaurantes de El Poblado.",
            "Terraza en la azotea con vista abierta sobre la ciudad"),
        new("bog-zonat", "Zona T Executive Suites", "Bogotá · Cundinamarca",
            "Suites para el viajero de negocios sobre la Zona T, con salas de reuniones, gimnasio 24 horas y desayuno buffet.",
            "Recepción de hotel con muebles de madera tallada y lámparas cálidas"),
        new("eje-salento", "Finca Mirador del Cocora", "Salento · Quindío",
            "Finca cafetera frente al Valle de Cocora, con tour de café propio, chimenea y caminatas guiadas al amanecer.",
            "Rama de cafeto con granos verdes en una finca del Quindío"),
        new("eje-filandia", "Hacienda Café & Bruma", "Filandia · Quindío",
            "Hacienda centenaria entre cafetales, con spa de montaña y catas de café de origen en el Paisaje Cultural Cafetero.",
            "Campo abierto al atardecer con montañas al fondo"),
    ];

    private string ComponerViajes(BlockGridJsonBuilder b)
    {
        // 1. Estadías destacadas: seis tarjetas con foto que abren la ficha de la estadía en el portal.
        var destacadas = Seccion(b, null, Marca("viajes"));
        Encabezado(destacadas, "Estadías para tu próximo viaje");
        var tarjetas = EstadiasDeViajes.Select(e => new Tarjeta(
            e.Nombre, e.Lugar, e.Resumen,
            _media.GetOrCreatePhotoPickerValue($"Foto Viajes {e.Nombre}", e.Alt, Foto("booking", e.Id + ".jpg"), LicenciaCc0),
            e.Alt, "Ver la estadía", $"/viajes/#/travel/estadia/{e.Id}")).ToArray();
        FilasDeTarjetas(destacadas, tarjetas);

        // 2. Lo que el portal sí hace (medido en travel-shell y en /api/travel): carrito con estadía, vuelo y auto;
        //    tarifas que dicen si son reembolsables; «Mis viajes» con la referencia y la cancelación.
        Rejilla(b, "surface", "Por qué reservar con nosotros", 3,
            ("Todo el viaje en un carrito", "Suma la estadía, el vuelo y el auto, y confirma todo de una vez."),
            ("Tarifas claras", "Cada tarifa dice qué incluye y si es reembolsable antes de que la elijas."),
            ("Tus viajes a la mano", "En «Mis viajes» ves cada reserva con su referencia y puedes cancelarla."));

        Rejilla(b, null, "Cómo funciona", 3,
            ("1 · Busca", "Elige el destino, las fechas y cuántos viajan."),
            ("2 · Arma tu viaje", "Compara las opciones y agrega al carrito la estadía, el vuelo o el auto."),
            ("3 · Confirma", "Completa los datos del viajero y recibe la referencia de tu reserva."));

        Preguntas(b, "Preguntas frecuentes",
            ("¿En qué moneda se muestran los precios?", "En pesos colombianos, con el total de la estadía según las noches y los huéspedes que elegiste."),
            ("¿Puedo cancelar una reserva?", "Sí, desde «Mis viajes». Si la tarifa es reembolsable lo verás indicado antes de reservar."),
            ("¿Puedo combinar estadía, vuelo y auto?", "Sí. Todo lo que agregas queda en el mismo carrito y se confirma en un solo paso."),
            ("¿Se realizan cobros?", "No. Este es un sitio de demostración: las reservas no generan cobros ni compromisos con los alojamientos."));

        Cierre(b, "¿Ya sabes a dónde vas?", "Busca tu estadía y arma el viaje en minutos.", "Buscar estadías", "/viajes/#/travel/estadias");

        return $"estadias:{tarjetas.Length};por-que:3;como-funciona:3;preguntas:4;cierre:1";
    }

    // ───────────────────────────── Piezas de composición ─────────────────────────────

    private sealed record Tarjeta(string Titulo, string Subtitulo, string Texto, string Foto, string Alt, string Cta, string Url);

    private string Foto(string dominio, string archivo) =>
        Path.Combine(_env.WebRootPath, "media", dominio, archivo);

    private BlockGridJsonBuilder.BlockBuilder Seccion(BlockGridJsonBuilder b, string? tema, string? clase = null)
    {
        var key = KeyOf("elementLayoutSection");
        var s = b.AddTopLevelBlock(key);
        if (tema is not null) { s.Set("theme", $"[\"{tema}\"]"); }
        if (clase is not null) { s.Set("cssClass", clase); }
        s.ApplyDefaults(_defaults.DefaultsFor(key));
        return s;
    }

    private void Encabezado(BlockGridJsonBuilder.BlockBuilder seccion, string titulo)
    {
        var key = KeyOf("elementTextHeading");
        seccion.AddChild(AreaDeLaSeccion, key, h => h
            .Set("headingTitle", titulo)
            .ApplyDefaults(_defaults.DefaultsFor(key)));
    }

    private void FilasDeTarjetas(BlockGridJsonBuilder.BlockBuilder seccion, IReadOnlyList<Tarjeta> tarjetas)
    {
        var tresKey = KeyOf("elementLayout3Col");
        var cardKey = KeyOf("elementCompCard");
        for (var i = 0; i < tarjetas.Count; i += 3)
        {
            var fila = tarjetas.Skip(i).Take(3).ToArray();
            seccion.AddChild(AreaDeLaSeccion, tresKey, col =>
            {
                col.Set("cssClass", ClaseDeTresColumnas);
                col.ApplyDefaults(_defaults.DefaultsFor(tresKey));
                for (var j = 0; j < fila.Length; j++)
                {
                    var t = fila[j];
                    col.AddChild(AreasDeTresColumnas[j], cardKey, c => c
                        .Set("headingTitle", t.Titulo)
                        .Set("headingSubtitle", t.Subtitulo)
                        .Set("textBody", $"<p>{System.Net.WebUtility.HtmlEncode(t.Texto)}</p>")
                        .Set("mediaReference", t.Foto)
                        .Set("mediaAlt", t.Alt)
                        .Set("ctaLabel", t.Cta)
                        .Set("ctaLink", Enlace(t.Cta, t.Url))
                        .ApplyDefaults(_defaults.DefaultsFor(cardKey)));
                }
            });
        }
    }

    private void Rejilla(BlockGridJsonBuilder b, string? tema, string titulo, int columnas, params (string titulo, string texto)[] items)
    {
        var key = KeyOf("elementSynFeatureGrid");
        var s = Seccion(b, tema);
        var json = new JsonArray(items.Select(i => (JsonNode)new JsonObject { ["heading"] = i.titulo, ["body"] = i.texto }).ToArray());
        s.AddChild(AreaDeLaSeccion, key, c => c
            .Set("headingText", titulo)
            .Set("itemsJson", json.ToJsonString())
            .Set("columns", columnas.ToString(System.Globalization.CultureInfo.InvariantCulture))
            .ApplyDefaults(_defaults.DefaultsFor(key)));
    }

    private void Preguntas(BlockGridJsonBuilder b, string titulo, params (string pregunta, string respuesta)[] items)
    {
        var key = KeyOf("elementSynFaqSection");
        var s = Seccion(b, "surface");
        var json = new JsonArray(items.Select(i => (JsonNode)new JsonObject { ["question"] = i.pregunta, ["answer"] = i.respuesta }).ToArray());
        s.AddChild(AreaDeLaSeccion, key, c => c
            .Set("headingText", titulo)
            .Set("itemsJson", json.ToJsonString())
            .ApplyDefaults(_defaults.DefaultsFor(key)));
    }

    private void Cierre(BlockGridJsonBuilder b, string titulo, string subtitulo, string cta, string url)
    {
        var key = KeyOf("elementCompCtaBanner");
        var s = Seccion(b, "dark");
        s.AddChild(AreaDeLaSeccion, key, c => c
            .Set("headingTitle", titulo)
            .Set("headingSubtitle", subtitulo)
            .Set("ctaLabel", cta)
            .Set("ctaLink", Enlace(cta, url))
            .ApplyDefaults(_defaults.DefaultsFor(key)));
    }

    private static string Enlace(string nombre, string url) =>
        new JsonArray(new JsonObject
        {
            ["name"] = nombre, ["url"] = url, ["target"] = "", ["udi"] = null, ["icon"] = null, ["queryString"] = null,
        }).ToJsonString();

    private Guid KeyOf(string alias) => _contentTypeService.Get(alias)?.Key
        ?? throw new InvalidOperationException($"ContentType '{alias}' no existe — corré el import de uSync.");

    public sealed record Resultado(bool Ok, string Vertical, string Detalle, int SeccionesNuevas);
}

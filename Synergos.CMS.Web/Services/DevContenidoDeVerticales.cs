using System.Text.Json.Nodes;
using Umbraco.Cms.Core.Models;
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

    /// <summary>Las raíces de sitio Propiedades, Booking y Hoteles, por su Key de uSync.</summary>
    public static readonly Guid Propiedades = Guid.Parse("5b2d7960-2cc3-4dad-9514-f7d0bbdcacf7");
    public static readonly Guid Booking = Guid.Parse("37bf2ce2-76e7-402e-b3b0-f2b93c447c86");
    public static readonly Guid Hoteles = Guid.Parse("64304927-0464-4231-968b-08f39393ffbc");

    /// <summary>Eventos, Educación, Gobierno, la raíz Synergos y su página Casos, por su Key de uSync.</summary>
    public static readonly Guid Eventos = Guid.Parse("7a675dad-b6d2-473b-99aa-9958f61f27d0");
    public static readonly Guid Educacion = Guid.Parse("5e1b13c8-de7f-4517-b1dd-e8bedbe806f9");
    public static readonly Guid Gobierno = Guid.Parse("e373b166-8e33-4e70-979b-799553a047e0");
    public static readonly Guid Synergos = Guid.Parse("71976524-5053-4127-a677-adf2c5e078e7");
    public static readonly Guid Casos = Guid.Parse("017b3ae2-b39f-4120-82d6-96ef984048bc");

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

    /// <summary>Las verticales que la herramienta sabe componer: la página y la receta de cada una.</summary>
    public IReadOnlyDictionary<string, (Guid Pagina, Func<CuerpoEditable, string> Receta)> Verticales =>
        new Dictionary<string, (Guid, Func<CuerpoEditable, string>)>(StringComparer.OrdinalIgnoreCase)
        {
            ["viajes"] = (Viajes, RecetaViajes),
            ["propiedades"] = (Propiedades, RecetaPropiedades),
            ["booking"] = (Booking, RecetaBooking),
            ["hoteles"] = (Hoteles, RecetaHoteles),
            ["educacion"] = (Educacion, RecetaEducacion),
            ["eventos"] = (Eventos, RecetaEventos),
            ["gobierno"] = (Gobierno, RecetaGobierno),
            ["tienda"] = (DevTiendaPorPaginas.Tienda, RecetaTienda),
        };

    /// <summary>
    /// Aplica a <paramref name="vertical"/> su receta (si tiene) y su atribución (si tiene): la descripción del sitio y
    /// los testimonios por rol y ciudad. Idempotente.
    /// </summary>
    public Resultado Componer(string vertical)
    {
        if (string.Equals(vertical, "tienda-fichas", StringComparison.OrdinalIgnoreCase)) { return FichasDeLaTienda(); }
        if (string.Equals(vertical, "hoteles-promesas", StringComparison.OrdinalIgnoreCase))
        {
            return new Resultado(true, "hoteles-promesas", RecetaPromesasDeHoteles(), 0);
        }
        if (string.Equals(vertical, "creditos", StringComparison.OrdinalIgnoreCase))
        {
            return new Resultado(true, "creditos", RecetaCreditos(), 0);
        }
        if (string.Equals(vertical, "tienda-vender", StringComparison.OrdinalIgnoreCase))
        {
            return new Resultado(true, "tienda-vender", RecetaTiendaYVender(), 0);
        }
        if (string.Equals(vertical, "categorias", StringComparison.OrdinalIgnoreCase))
        {
            var hecho = RecetaEntradasDeCategorias();
            return new Resultado(hecho.EndsWith("faltan:0", StringComparison.Ordinal), "categorias", hecho, 0);
        }
        if (string.Equals(vertical, "evento-y-curso", StringComparison.OrdinalIgnoreCase))
        {
            return new Resultado(true, "evento-y-curso", RecetaEventoYCurso(), 0);
        }
        if (string.Equals(vertical, "propiedades-fichas", StringComparison.OrdinalIgnoreCase))
        {
            var hecho = RecetaFichasDePropiedades();
            return new Resultado(hecho.Contains("sin-datos:0", StringComparison.Ordinal), "propiedades-fichas", hecho, 0);
        }

        var tieneReceta = Verticales.TryGetValue(vertical, out var receta);
        var tieneAtribucion = Atribuciones.TryGetValue(vertical, out var atribucion);
        if (!tieneReceta && !tieneAtribucion)
        {
            return new Resultado(false, vertical, "vertical-desconocida", 0);
        }

        var pagina = _contentService.GetById(tieneReceta ? receta.Pagina : atribucion!.Pagina);
        if (pagina is null) { return new Resultado(false, vertical, "pagina-no-encontrada", 0); }

        var cuerpo = new CuerpoEditable(pagina.GetValue<string>(Cuerpo, Cultura));
        var detalle = tieneReceta ? receta.Receta(cuerpo) : "sin-receta";
        var descripcionNueva = false;
        if (tieneAtribucion)
        {
            detalle += $";testimonios:{Reatribuir(cuerpo, atribucion!.Testimonios)}";
            if (atribucion.Descripcion is { } descripcion && pagina.HasProperty("seoDescription")
                && !string.Equals(pagina.GetValue<string>("seoDescription", Cultura), descripcion, StringComparison.Ordinal))
            {
                pagina.SetValue("seoDescription", descripcion, Cultura);
                descripcionNueva = true;
                detalle += ";descripcion:1";
            }
        }
        if (!cuerpo.Cambio && !descripcionNueva)
        {
            _logger.LogInformation("Contenido {Vertical}: nada que cambiar — no se toca.", vertical);
            return new Resultado(true, vertical, "sin-cambios", 0);
        }

        if (cuerpo.Cambio) { pagina.SetValue(Cuerpo, cuerpo.Json, Cultura); }
        var save = _contentService.SaveAndPublish(pagina, new[] { Cultura });
        if (!save.Success)
        {
            var invalidas = save.InvalidProperties is null ? "(null)" : string.Join(",", save.InvalidProperties.Select(p => p.Alias));
            _logger.LogError("Contenido {Vertical}: fallo al publicar. {Result}; invalid=[{Invalid}]", vertical, save.Result, invalidas);
            return new Resultado(false, vertical, $"save-failed:{save.Result}:[{invalidas}]", 0);
        }

        _logger.LogInformation("Contenido {Vertical}: {Detalle}.", vertical, detalle);
        return new Resultado(true, vertical, detalle, cuerpo.Cambios);
    }

    public static string Marca(string vertical) => $"syn-contenido--{vertical.ToLowerInvariant()}";

    /// <summary>
    /// Une dos cuerpos de Block Grid: lo que había primero, lo nuevo debajo. Público para probarlo sin Umbraco.
    /// </summary>
    public static (string Json, int SeccionesNuevas) Sumar(string actual, string nuevo)
    {
        var cuerpo = new CuerpoEditable(actual);
        var n = cuerpo.Sumar(nuevo);
        return (cuerpo.Json, n);
    }

    /// <summary>Lo que la receta de Viajes deja: las secciones nuevas al final, una sola vez.</summary>
    private string RecetaViajes(CuerpoEditable cuerpo)
    {
        if (cuerpo.Contiene(Marca("viajes"))) { return "ya-compuesta"; }
        var b = new BlockGridJsonBuilder();
        var detalle = ComponerViajes(b);
        cuerpo.Sumar(b.Build());
        return detalle;
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

    // ───────────────────────────── Propiedades ─────────────────────────────

    private sealed record Inmueble(string Slug, string Titulo, string Ciudad, string Alt);

    /// <summary>Los inmuebles del catálogo de la app (<c>/api/realty/listings</c>), con su foto CC0 de <c>media/realty</c>.</summary>
    private static readonly Inmueble[] EnVenta =
    [
        new("apto-chico-norte-bogota", "Apartamento en Chicó Norte", "Bogotá", "Sala con sofá de cuero negro y un cuadro de letras en la pared"),
        new("apto-bocagrande-cartagena", "Apartamento en Bocagrande", "Cartagena", "Sala blanca con chimenea y ventanales hacia el mar"),
        new("casa-laureles-medellin", "Casa en Laureles", "Medellín", "Casa de madera amarilla de un piso con jardín al frente"),
        new("apto-manga-cartagena", "Apartamento en Manga", "Cartagena", "Edificio de apartamentos con balcones y comercio en el primer piso"),
    ];

    private static readonly Inmueble[] EnArriendoYComercial =
    [
        new("apto-arriendo-poblado-medellin", "Apartamento en El Poblado", "Medellín", "Sala con sofás claros, chimenea y lámpara de araña"),
        new("casa-arriendo-cedritos-bogota", "Casa en Cedritos", "Bogotá", "Casa blanca de madera con porche y jardín"),
        new("oficina-santa-monica-cali", "Oficina en Santa Mónica", "Cali", "Persona trabajando en un portátil sobre planos en una oficina"),
        new("local-comercial-centro-cali", "Local comercial en el Centro", "Cali", "Ropa colgada en los exhibidores de un local comercial"),
    ];

    private string RecetaPropiedades(CuerpoEditable cuerpo)
    {
        string Url(Inmueble i) => _media.GetOrCreatePhotoUrl($"Foto Propiedades {i.Titulo}", i.Alt, Foto("realty", i.Slug + ".jpg"), LicenciaCc0);
        static string Ficha(Inmueble i) => $"/propiedades/#/realty/inmueble/{i.Slug}";

        var carrusel = KeyOf("elementSynCarousel");
        var galeria = KeyOf("elementSynLightboxGallery");

        var diapositivas = new JsonArray(EnVenta.Select(i => (JsonNode)new JsonObject
        {
            ["imageUrl"] = Url(i), ["alt"] = i.Alt, ["caption"] = $"{i.Titulo} · {i.Ciudad}", ["linkUrl"] = Ficha(i),
        }).ToArray());
        var fotos = new JsonArray(EnArriendoYComercial.Select(i => (JsonNode)new JsonObject
        {
            ["thumbUrl"] = Url(i), ["fullUrl"] = Url(i), ["alt"] = i.Alt, ["caption"] = $"{i.Titulo} · {i.Ciudad}",
        }).ToArray());

        var n = cuerpo.Reemplazar(carrusel, "slidesJson", diapositivas.ToJsonString());
        n += cuerpo.Reemplazar(galeria, "imagesJson", fotos.ToJsonString());
        n += cuerpo.Reemplazar(galeria, "columns", "4");
        var t1 = cuerpo.InsertarAntesDe(carrusel, UnEncabezado("Inmuebles destacados en venta"), "Inmuebles destacados en venta");
        var t2 = cuerpo.InsertarAntesDe(galeria, UnEncabezado("En arriendo y para tu negocio"), "En arriendo y para tu negocio");
        return $"carrusel:{EnVenta.Length};galeria:{EnArriendoYComercial.Length};propiedades-cambiadas:{n};titulos:{(t1 ? 1 : 0) + (t2 ? 1 : 0)}";
    }

    // ───────────────────────────── Booking ─────────────────────────────

    private sealed record Categoria(string Nombre, string Servicios, string Texto, string Url, string? Ilustracion, string Alt);

    /// <summary>Las tres categorías de <c>/booking/</c> con sus servicios (las páginas hijas del sitio).</summary>
    private static readonly Categoria[] CategoriasDeBooking =
    [
        new("Consultoría", "Asesoría express · 30 min y consultoría estratégica · 60 min",
            "Una sesión corta para resolver una duda puntual o una hora completa para revisar la estrategia de tu negocio.",
            "/booking/consultoria/", "imgs/infofeat 4.png", "Ilustración de un bombillo con un engranaje y gráficas de análisis"),
        new("Espacios", "Sala de reuniones · 2 h y auditorio · jornada",
            "Pide la sala para tu equipo o el auditorio para un evento; te contactan para confirmar el horario.",
            "/booking/espacios/", "imgs/infofeat 5.png", "Ilustración de un equipo reunido alrededor de una mesa de trabajo"),
        new("Bienestar", "Masaje terapéutico · 60 min y plan nutricional · valoración",
            "Agenda tu sesión de masaje o la valoración para empezar un plan nutricional a tu medida.",
            "/booking/bienestar/", null, "Bienestar"),
    ];

    private string RecetaBooking(CuerpoEditable cuerpo)
    {
        // Desconectar, no borrar (decisión del director, 2026-11-13): el portal de viajes sale de ESTA portada; el bloque
        // y su tipo siguen en el esquema y Viajes conserva su portal en /viajes/.
        var fuera = cuerpo.DesconectarSeccionesCon(KeyOf("elementSynTravelShell"));
        if (cuerpo.Contiene(Marca("booking"))) { return $"buscador-desconectado:{fuera};servicios:ya"; }

        var b = new BlockGridJsonBuilder();
        Portada(b, "Agenda tu cita en minutos",
            "Consultoría, espacios y bienestar, sin llamadas.",
            "<p>Elige el servicio, el día y la hora y deja tus datos: tu solicitud le llega al negocio al instante y te contactan para confirmarla.</p>",
            "Booking Hero", "Portada de reservas de servicios", "#0F2A4A", "#1F7A8C",
            ("Ver servicios", "/booking/servicios/"), ("Reservar", "/booking/reservar/"));

        var servicios = Seccion(b, null, Marca("booking"));
        Encabezado(servicios, "Lo que puedes reservar");
        var tarjetas = CategoriasDeBooking.Select(c => new Tarjeta(
            c.Nombre, c.Servicios, c.Texto,
            c.Ilustracion is null ? null : IlustracionDelKit($"Ilustración Booking {c.Nombre}", c.Alt, c.Ilustracion),
            c.Alt, $"Ver {c.Nombre.ToLowerInvariant()}", c.Url)).ToArray();
        FilasDeTarjetas(servicios, tarjetas);

        var nuevas = cuerpo.InsertarAlPrincipio(b.Build());
        return $"buscador-desconectado:{fuera};secciones-arriba:{nuevas};categorias:{tarjetas.Length}";
    }

    // ───────────────────────────── Hoteles ─────────────────────────────

    private string RecetaHoteles(CuerpoEditable cuerpo)
    {
        // El asistente de /hoteles/ tiene cuatro pasos (Fechas, Habitación, Huésped, Revisar): el bloque decía tres.
        var mision = KeyOf("elementCorpMissionBlock");
        var n = cuerpo.Reemplazar(mision, "headingTitle", "Reserva en cuatro pasos");
        // El texto del hero hablaba del motor («sobre el mismo motor»): al huésped le habla de su estadía.
        n += cuerpo.Reemplazar(KeyOf("elementCompHero"), "textBody",
            "<p>Elige tus fechas, escoge la habitación que te sirve y confirma tu estadía con la disponibilidad al día.</p>");
        n += cuerpo.Reemplazar(mision, "textBody",
            "<p>Elige fechas y huéspedes, escoge la habitación, completa tus datos y revisa todo antes de confirmar. El asistente te guía paso a paso.</p>");

        if (cuerpo.Contiene(Marca("hoteles"))) { return $"pasos:{n};habitaciones:ya"; }

        // Las habitaciones que vende el asistente (StubRoomAvailabilityProvider): tipo y cuántos caben. El precio sale al
        // elegir fechas, por eso no se promete aquí.
        var b = new BlockGridJsonBuilder();
        RejillaConMarca(b, null, Marca("hoteles"), "Nuestras habitaciones", 4,
            ("Habitación Estándar", "Hasta 2 huéspedes. Para un viaje corto o de trabajo."),
            ("Habitación Deluxe", "Hasta 3 huéspedes, con más espacio para descansar."),
            ("Suite Junior", "Hasta 4 huéspedes, con zona de estar aparte."),
            ("Habitación Familiar", "Hasta 5 huéspedes, para viajar con los niños."));
        var nuevas = cuerpo.InsertarSeccionesAntesDe(mision, b.Build());
        return $"pasos:{n};habitaciones:{nuevas}";
    }

    // ───────────────────────────── Fotos reales (I9 y extras) ─────────────────────────────

    private static readonly System.Text.Json.JsonSerializerOptions LecturaDelManifiesto = new() { PropertyNameCaseInsensitive = true };

    /// <summary>
    /// Una foto ya en disco y a dónde va: a un producto (por el nombre de su página), al ambiente de la galería de una
    /// ficha de inmueble, o a un nodo de Media por su nombre. Con su crédito y su licencia.
    /// </summary>
    public sealed record FotoImportada(
        string Archivo, string Alt, string Licencia, string Credito,
        string? Producto = null, string? Ambiente = null, string? Medio = null, string? Rotulo = null);

    /// <summary>
    /// Importa las fotos de <paramref name="carpeta"/> según sus manifiestos (<c>tienda.json</c>, <c>booking.json</c>,
    /// <c>propiedades.json</c>, <c>laureles.json</c>; el que falte se salta). Cada foto va a los nodos de Media que ya
    /// usa su destino —misma Key, así que páginas y catálogo siguen apuntando bien— y la página se republica. Idempotente:
    /// un medio que ya tiene esa licencia y ese crédito no se toca.
    /// </summary>
    public Resultado ImportarFotos(string carpeta)
    {
        var partes = new List<string>();
        var cambios = 0;
        var ok = true;

        if (Leer(carpeta, "tienda.json") is { } tienda)
        {
            var (n, sinPagina) = FotosDeProductos(DevTiendaPorPaginas.Tienda, tienda, carpeta);
            partes.Add($"tienda:{n}"); cambios += n; ok &= sinPagina == 0;
            if (sinPagina > 0) { partes.Add($"tienda-sin-pagina:{sinPagina}"); }
        }
        if (Leer(carpeta, "booking.json") is { } booking)
        {
            var (n, sinPagina) = FotosDeProductos(Booking, booking, carpeta);
            partes.Add($"booking:{n}"); cambios += n; ok &= sinPagina == 0;
            if (sinPagina > 0) { partes.Add($"booking-sin-pagina:{sinPagina}"); }
            partes.Add($"tarjeta-bienestar:{TarjetaConLaFotoDe("Bienestar", "Masaje terapéutico · 60 min")}");
        }
        if (Leer(carpeta, "propiedades.json") is { } propiedades)
        {
            var (n, faltan) = FotosDeFichas(propiedades, carpeta);
            partes.Add($"fichas:{n}"); cambios += n; ok &= faltan == 0;
            if (faltan > 0) { partes.Add($"ambientes-no-encontrados:{faltan}"); }
        }
        if (Leer(carpeta, "laureles.json") is { } medios)
        {
            var n = 0;
            foreach (var f in medios.Where(f => f.Medio is not null))
            {
                var clave = _media.KeyByName(f.Medio!);
                if (clave is null) { ok = false; partes.Add($"medio-no-encontrado:{f.Medio}"); continue; }
                if (_media.ReplacePhoto(clave.Value, Path.Combine(carpeta, f.Archivo), f.Medio!, f.Alt, f.Licencia, f.Credito)) { n++; }
            }
            partes.Add($"medios:{n}"); cambios += n;
            // El carrusel de Propiedades guarda URLs: con la foto nueva, su receta vuelve a escribirlas.
            if (n > 0) { partes.Add($"propiedades:{Componer("propiedades").Detalle}"); }
        }

        var detalle = string.Join(";", partes);
        _logger.LogInformation("Fotos importadas: {Detalle}.", detalle);
        return new Resultado(ok, "fotos", detalle, cambios);
    }

    /// <summary>Compatibilidad: <c>/dev/fotos-de-la-tienda</c> importa sólo el manifiesto de la Tienda.</summary>
    public Resultado FotosDeLaTienda(string carpeta)
    {
        var fotos = Leer(carpeta, "tienda.json") ?? Leer(carpeta, "manifiesto.json");
        if (fotos is null) { return new Resultado(false, "tienda", "sin-manifiesto", 0); }
        var (n, sinPagina) = FotosDeProductos(DevTiendaPorPaginas.Tienda, fotos, carpeta);
        return new Resultado(sinPagina == 0, "tienda", $"productos-con-foto-nueva:{n};sin-producto:{sinPagina}", n);
    }

    private static List<FotoImportada>? Leer(string carpeta, string nombre)
    {
        var ruta = Path.Combine(carpeta, nombre);
        return System.IO.File.Exists(ruta)
            ? System.Text.Json.JsonSerializer.Deserialize<List<FotoImportada>>(System.IO.File.ReadAllText(ruta), LecturaDelManifiesto)
            : null;
    }

    // ───────────────────────────── Evento y Curso del catálogo (2.ª medición, S3) ─────────────────────────────

    /// <summary>La página «Evento» de Eventos, por su Key de uSync.</summary>
    public static readonly Guid PaginaDelEvento = Guid.Parse("c90ae9cb-2cff-422a-837d-d54cc2b3e4fe");

    /// <summary>La página «Curso» de Educación, por su Key de uSync.</summary>
    public static readonly Guid PaginaDelCurso = Guid.Parse("46d6f33c-cd8c-463d-910d-5a5f6f4fb0ef");

    /// <summary>La ficha de la Cumbre Tech en la app de eventos, que vive en la portada de Eventos (su id en el catálogo).</summary>
    public const string EnlaceDelEvento = "/eventos/#/eventos/e/evt-cumbre-tech";

    /// <summary>La ficha del curso de design systems en la app de cursos, que vive en la portada de Educación.</summary>
    public const string EnlaceDelCurso = "/educacion/#/academy/curso/design-systems-atomic";

    /// <summary>
    /// «Evento» y «Curso» eran muestras sobre el motor: un «Synergos Summit» con un cartel de ponentes inventados y un
    /// formulario de registro que no vende nada, y un curso con un instructor inventado, «acceso de por vida» y «14
    /// días de garantía». Ahora cuentan un evento y un curso que SÍ están en el catálogo —la Cumbre Tech y «Design
    /// Systems con Atomic Design»— y llevan a su ficha en la app, donde se compran y se inscriben de verdad. Lo que el
    /// catálogo no respalda (grabaciones, factura, garantía) sale.
    /// </summary>
    private string RecetaEventoYCurso()
    {
        var n = 0;
        var hero = KeyOf("elementCompHero");
        var mision = KeyOf("elementCorpMissionBlock");
        var preguntas = KeyOf("elementSynFaqSection");
        var cierre = KeyOf("elementCompCtaBanner");
        var formulario = KeyOf("elementSynFormStepper");

        var evento = _contentService.GetById(PaginaDelEvento);
        if (evento is not null)
        {
            var c = new CuerpoEditable(evento.GetValue<string>(Cuerpo, Cultura));
            c.Reemplazar(hero, "headingTitle", "Cumbre Tech");
            c.Reemplazar(hero, "headingSubtitle", "Conferencia · Bogotá · Ágora Centro de Convenciones");
            c.Reemplazar(hero, "textBody",
                "<p>Keynotes de líderes de la industria, talleres prácticos y networking. La fecha, los planes y los cupos están en la cartelera, donde compras tu entrada.</p>");
            c.Reemplazar(hero, "mediaAlt", "Cumbre Tech, conferencia de tecnología en Bogotá");
            c.CambiarBotones(hero, "ctaItems",
                [("Comprar entradas", Enlace("Comprar entradas", EnlaceDelEvento)), ("Ver la cartelera", Enlace("Ver la cartelera", "/eventos/agenda"))]);
            c.Reemplazar(mision, "textBody",
                "<p>Una jornada en el Ágora Centro de Convenciones, en el corazón de Bogotá: keynotes de líderes de producto e ingeniería, demos en vivo de startups colombianas y networking. Con el plan Pro suman los talleres prácticos de la tarde.</p>",
                CuerpoEditable.Donde("headingTitle", "Lo que vas a vivir"));
            var programa = KeyOf("elementSynTimelineHorizontal");
            // El ajuste por franjas vuelve (016, V6): el componente ya alinea la hora después del rótulo fijo.
            c.Reemplazar(programa, "snapEnabled", "1");
            c.TransformarLista(programa, "eventsJson", _ => new JsonArray(
                Momento("08:30", "Registro y acreditación", "Recoges tu escarapela en la entrada del Ágora."),
                Momento("09:30", "Keynote de apertura", "El futuro del software en Colombia."),
                Momento("11:00", "Panel", "Escalar producto en mercados latinoamericanos."),
                Momento("13:30", "Talleres prácticos", "Sesiones simultáneas, con acceso del plan Pro."),
                Momento("16:00", "Networking y cierre", "Conversaciones abiertas para despedir la jornada.")));
            var rejilla = KeyOf("elementSynFeatureGrid");
            var esElCartel = Alguno("headingText", "Lineup de speakers", "Planes de entrada");
            c.TransformarLista(rejilla, "itemsJson", _ => new JsonArray(
                new JsonObject { ["heading"] = "Estándar", ["body"] = "La jornada completa en el auditorio principal, de la apertura al cierre: keynote, panel, zona de demos, almuerzo y café." },
                new JsonObject { ["heading"] = "Pro", ["body"] = "Todo lo del plan Estándar más los talleres prácticos de la tarde, con cupo en el que elijas." }),
                esElCartel);
            c.Reemplazar(rejilla, "columns", "2", esElCartel);
            c.Reemplazar(rejilla, "headingText", "Planes de entrada", esElCartel);
            var esLaCompra = Alguno("headingTitle", "Registro y tickets", "Cómo compras tu entrada");
            c.Reemplazar(mision, "textBody",
                "<p>En la cartelera eliges el plan y la cantidad, pagas y ves tu entrada con su código QR en «Mis tickets». Desde ahí también la puedes transferir.</p>",
                esLaCompra);
            c.Reemplazar(mision, "headingTitle", "Cómo compras tu entrada", esLaCompra);
            c.DesconectarSeccionesCon(formulario, CuerpoEditable.Donde("formInternalKey", "registro-evento"));
            var mapa = KeyOf("elementCorpMapEmbed");
            c.Reemplazar(mapa, "mapTitle", "Ágora Centro de Convenciones, Bogotá");
            // El Ágora queda junto a Corferias: la misma coordenada que su evento en el catálogo.
            c.Reemplazar(mapa, "mapUrl",
                "https://www.openstreetmap.org/export/embed.html?bbox=-74.1015%2C4.6221%2C-74.0815%2C4.6381&layer=mapnik&marker=4.6301%2C-74.0915");
            var antesDeRegistrarte = Alguno("headingText", "Antes de registrarte", "Antes de comprar");
            c.TransformarLista(preguntas, "itemsJson", _ => new JsonArray(
                Pregunta("¿Qué incluye cada plan?", "El Estándar da la jornada completa en el auditorio principal; el Pro suma los talleres prácticos de la tarde. El detalle de cada plan está en la cartelera."),
                Pregunta("¿Dónde queda mi entrada?", "En «Mis tickets», con su código QR. Preséntalo en el acceso el día del evento."),
                Pregunta("¿Puedo comprar para otras personas?", "Sí. Eliges la cantidad al comprar, y si alguien más va a usar una entrada, se la transfieres desde «Mis tickets».")),
                antesDeRegistrarte);
            c.Reemplazar(preguntas, "headingText", "Antes de comprar", antesDeRegistrarte);
            c.Reemplazar(cierre, "headingSubtitle", "Elige tu plan y compra tu entrada en la cartelera.");
            c.Reemplazar(cierre, "ctaLabel", "Comprar entradas");
            c.Reemplazar(cierre, "ctaLink", JsonNode.Parse(Enlace("Comprar entradas", EnlaceDelEvento))!);
            n += Guardar(evento, c, "Cumbre Tech", "Cumbre Tech · Eventos",
                "Cumbre Tech en Bogotá: keynotes, panel, talleres prácticos y networking en el Ágora Centro de Convenciones.");
        }

        var curso = _contentService.GetById(PaginaDelCurso);
        if (curso is not null)
        {
            var c = new CuerpoEditable(curso.GetValue<string>(Cuerpo, Cultura));
            c.Reemplazar(hero, "headingTitle", "Design Systems con Atomic Design");
            c.Reemplazar(hero, "headingSubtitle", "Diseño · Nivel principiante · 3 lecciones · Certificado al completar");
            c.Reemplazar(hero, "textBody",
                "<p>De los tokens semánticos a los organismos: cómo modelar un design system que sobreviva a un rebrand entero cambiando un solo archivo. Incluye la grilla de 8 puntos y el ritmo vertical.</p>");
            c.Reemplazar(hero, "mediaAlt", "Curso Design Systems con Atomic Design");
            c.CambiarBotones(hero, "ctaItems",
                [("Ver el curso", Enlace("Ver el curso", EnlaceDelCurso)), ("Ver más cursos", Enlace("Ver más cursos", "/educacion/cursos"))]);
            c.Reemplazar(mision, "textBody",
                "<p>Al terminar vas a poder modelar tokens semánticos que aguanten un rebrand completo, aplicar la grilla de 8 puntos para un ritmo vertical consistente y componer átomos, moléculas y organismos reutilizables.</p>",
                CuerpoEditable.Donde("headingTitle", "Lo que vas a lograr"));
            c.TransformarLista(KeyOf("elementSynAccordion"), "itemsJson", _ => new JsonArray(
                new JsonObject { ["title"] = "Módulo 1 · Tokens y fundamentos", ["content"] = "Tokens semánticos frente a literales y la grilla de 8 puntos, con una hoja de referencia de espaciados." },
                new JsonObject { ["title"] = "Módulo 2 · Componiendo el sistema", ["content"] = "Átomos, moléculas y organismos: del botón a la tarjeta y al encabezado, y dónde trazar la frontera de cada nivel." }));
            c.DesconectarSeccionesCon(KeyOf("elementSynMediaText"), CuerpoEditable.Donde("headingText", "Tu instructor"));
            c.Reemplazar(mision, "textBody",
                "<p>Te inscribes en la ficha del curso, con tu cuenta. Las lecciones quedan en «Mi aprendizaje» y, al completarlas, obtienes tu certificado.</p>",
                CuerpoEditable.Donde("headingTitle", "Inscríbete al curso"));
            c.DesconectarSeccionesCon(formulario, CuerpoEditable.Donde("formInternalKey", "inscripcion-curso"));
            c.TransformarLista(preguntas, "itemsJson", _ => new JsonArray(
                Pregunta("¿Cuánto dura?", "Tres lecciones cortas, de 11 a 17 minutos cada una. Avanzas a tu ritmo."),
                Pregunta("¿Recibo certificado?", "Sí, al completar el curso."),
                Pregunta("¿Necesito experiencia previa?", "No. Es un curso de nivel principiante.")),
                CuerpoEditable.Donde("headingText", "Antes de inscribirte"));
            c.Reemplazar(cierre, "headingSubtitle", "Mira el temario en la ficha del curso y empieza por la primera lección.");
            c.Reemplazar(cierre, "ctaLabel", "Ver el curso");
            c.Reemplazar(cierre, "ctaLink", JsonNode.Parse(Enlace("Ver el curso", EnlaceDelCurso))!);
            n += Guardar(curso, c, "Design Systems con Atomic Design", "Design Systems con Atomic Design · Educación",
                "Curso de nivel principiante: tokens semánticos, la grilla de 8 puntos y átomos, moléculas y organismos. Certificado al completar.");
        }
        return $"paginas:{n}";

        // Un bloque se reconoce por su título de antes o por el de ahora: la receta se puede volver a pasar.
        static Func<JsonObject, bool> Alguno(string propiedad, params string[] valores) =>
            d => valores.Any(v => CuerpoEditable.Donde(propiedad, v)(d));
        static JsonObject Momento(string hora, string titulo, string texto) =>
            new() { ["date"] = hora, ["title"] = titulo, ["description"] = texto };
        static JsonObject Pregunta(string pregunta, string respuesta) =>
            new() { ["question"] = pregunta, ["answer"] = respuesta };
    }

    /// <summary>Guarda y publica el cuerpo y los títulos de la página si algo cambió. Devuelve 1 si la publicó.</summary>
    private int Guardar(IContent pagina, CuerpoEditable cuerpo, string titulo, string seoTitulo, string seoDescripcion)
    {
        var cambio = cuerpo.Cambio;
        foreach (var (alias, valor) in new[] { ("heading", titulo), ("seoTitle", seoTitulo), ("seoDescription", seoDescripcion) })
        {
            if (pagina.HasProperty(alias) && pagina.GetValue<string>(alias, Cultura) != valor)
            {
                pagina.SetValue(alias, valor, Cultura);
                cambio = true;
            }
        }
        if (!cambio) { return 0; }
        if (cuerpo.Cambio) { pagina.SetValue(Cuerpo, cuerpo.Json, Cultura); }
        _contentService.SaveAndPublish(pagina, new[] { Cultura });
        return 1;
    }

    // ───────────────────────────── Entradas de las categorías (2.ª medición, S4) ─────────────────────────────

    /// <summary>
    /// La entrada de cada categoría, por la Key de uSync de su página: qué hay, para quién es y cómo se consigue. Antes
    /// era una línea («Sesiones con especialistas.»). Comprar en línea en la Tienda, agendar la visita en Propiedades y
    /// reservar en Booking: lo que de verdad hace la ficha de cada sitio.
    /// </summary>
    private static readonly (Guid Pagina, string Entrada)[] EntradasDeCategorias =
    [
        (Guid.Parse("a7275dec-c0a0-4dc2-ab65-6b473bab1491"),
            "Termos, tazas, morrales, llaveros y otros detalles para el día a día o para regalar. Agregas al carrito lo que te guste y finalizas tu pedido en línea."),
        (Guid.Parse("d12a749e-c0b6-4036-9de7-b942996035dd"),
            "Libretas, bolígrafos, notas adhesivas y organizadores para quien trabaja o estudia en su escritorio. Agregas al carrito lo que necesites y finalizas tu pedido en línea."),
        (Guid.Parse("3338ca4f-1b7c-48be-8ac5-ca56878ebb43"),
            "Camisetas, buzos, una chaqueta cortavientos y gorras para el uso diario. Agregas al carrito lo que te guste y finalizas tu pedido en línea."),
        (Guid.Parse("623ce787-89d7-49e8-803b-a8623c3c118e"),
            "Audífonos, mouse, hubs USB-C, fundas y organizadores para tu escritorio y tus dispositivos. Agregas al carrito lo que necesites y finalizas tu pedido en línea."),
        (Guid.Parse("10195c7f-440a-46c1-82b1-284ccc890206"),
            "Apartamentos en Bogotá para vivir en familia o con las amenidades de un conjunto. Cada ficha trae su ficha técnica, y desde ella agendas una visita."),
        (Guid.Parse("6e68d98e-f370-4d81-bc08-ea7b47a685f6"),
            "Casas con espacio propio: jardín, terraza o un lote amplio. Cada ficha trae su ficha técnica, y desde ella agendas una visita."),
        (Guid.Parse("abb93d6b-414f-44b7-91e0-669b93c90143"),
            "Un local a pie de calle en la Zona T y una oficina en torre empresarial, para tu negocio. Cada ficha trae su ficha técnica, y desde ella agendas una visita."),
        (Guid.Parse("a5104fec-03c2-454a-adb3-3f5300a56cec"),
            "Un masaje terapéutico de 60 minutos y una valoración con plan nutricional, para cuidarte. Eliges el servicio y lo reservas desde su ficha."),
        (Guid.Parse("c3ba00e2-5d3a-49a3-a21e-8d1073f254d9"),
            "Asesorías de 30 minutos y consultorías estratégicas de 60 con especialistas, para resolver una duda o planear tu próximo paso. Eliges la sesión y la reservas desde su ficha."),
        (Guid.Parse("c61376b9-dafa-490a-89b6-16ab0dbb25bc"),
            "Salas de reuniones por dos horas y un auditorio por jornada, para tu equipo o tu evento. Eliges el espacio y lo reservas desde su ficha."),
    ];

    private string RecetaEntradasDeCategorias()
    {
        int cambiadas = 0, faltan = 0;
        foreach (var (key, entrada) in EntradasDeCategorias)
        {
            var pagina = _contentService.GetById(key);
            if (pagina is null || !pagina.HasProperty("categoryDescription")) { faltan++; continue; }
            if (pagina.GetValue<string>("categoryDescription", Cultura) == entrada) { continue; }
            pagina.SetValue("categoryDescription", entrada, Cultura);
            _contentService.SaveAndPublish(pagina, new[] { Cultura });
            cambiadas++;
        }
        return $"categorias:{cambiadas};faltan:{faltan}";
    }

    // ───────────────────────────── La Tienda y su consola (2.ª medición, S7) ─────────────────────────────

    /// <summary>La página «Vender» de la Tienda (la consola del vendedor), por su Key de uSync.</summary>
    public static readonly Guid PaginaVender = Guid.Parse("13dda046-015b-4639-b6cc-bdf37329b19d");

    /// <summary>
    /// «Vender» era, para quien llega sin sesión —casi todo el que pulsa «Vender»—, un candado en un panel. Ahora dice
    /// qué se hace desde la consola y quién entra. Y la portada de la Tienda deja de prometer lo que el sitio no hace:
    /// envíos a toda Colombia, pago contra entrega, «lanza tu tienda esta semana» hacia una consola del personal, y
    /// tres testimonios de «cliente verificado» que nadie dio (la sección se desconecta; el tipo sigue en el esquema).
    /// </summary>
    private string RecetaTiendaYVender()
    {
        var n = 0;
        var vender = _contentService.GetById(PaginaVender);
        if (vender is not null)
        {
            var cuerpo = new CuerpoEditable(vender.GetValue<string>(Cuerpo, Cultura));
            if (!cuerpo.Contiene("Qué haces desde la consola"))
            {
                var b = new BlockGridJsonBuilder();
                Rejilla(b, "surface", "Qué haces desde la consola", 2,
                    ("Panel", "Las ventas, las publicaciones activas, pausadas o agotadas, y las devoluciones por revisar."),
                    ("Publicar", "Un producto nuevo en cuatro pasos: datos, fotos, precio y stock, y una revisión antes de que quede visible en la tienda."),
                    ("Mensajes y preguntas", "Respondes a los compradores desde el mismo panel."),
                    ("Reputación", "Entregas a tiempo, órdenes con reclamo y preguntas respondidas."));
                Preguntas(b, "Sobre la consola",
                    ("¿Quién entra a la consola?", "El personal de la tienda, con su cuenta. Si eres parte del equipo y no puedes entrar, pide a quien administra la tienda que te dé el rol de personal."),
                    ("¿Lo que publico se ve enseguida?", "Sí. Al terminar la revisión, el producto queda visible en el catálogo de la tienda."));
                cuerpo.Sumar(b.Build());
            }
            if (cuerpo.Cambio)
            {
                vender.SetValue(Cuerpo, cuerpo.Json, Cultura);
                _contentService.SaveAndPublish(vender, new[] { Cultura });
                n++;
            }
        }

        var tienda = _contentService.GetById(DevTiendaPorPaginas.Tienda);
        if (tienda is not null)
        {
            var cuerpo = new CuerpoEditable(tienda.GetValue<string>(Cuerpo, Cultura));
            cuerpo.DesconectarSeccionesCon(KeyOf("elementSynTestimonialCarousel"));
            cuerpo.TransformarLista(KeyOf("elementSynFaqSection"), "itemsJson", _ => new JsonArray(
                    new JsonObject { ["question"] = "¿Hacen envíos?", ["answer"] = "No. Es un sitio de demostración: puedes hacer todo el recorrido de compra, pero no se cobra ni se despacha nada." },
                    new JsonObject { ["question"] = "¿Puedo devolver un producto?", ["answer"] = "Sí. Desde «Mi cuenta» pides la devolución de una compra, y la revisa el personal de la tienda." },
                    new JsonObject { ["question"] = "¿Cómo sigo mi pedido?", ["answer"] = "En «Mi cuenta» ves tus compras y el estado de cada una." }),
                CuerpoEditable.Donde("headingText", "Preguntas frecuentes"));
            var cierre = KeyOf("elementCompCtaBanner");
            cuerpo.Reemplazar(cierre, "headingTitle", "¿Atiendes la tienda?");
            cuerpo.Reemplazar(cierre, "headingSubtitle", "Ventas, publicaciones, mensajes y devoluciones en un solo panel.");
            cuerpo.Reemplazar(cierre, "ctaLabel", "Ir a la consola");
            cuerpo.Reemplazar(cierre, "ctaLink", JsonNode.Parse(Enlace("Ir a la consola", "/tienda/vender/"))!);
            if (cuerpo.Cambio)
            {
                tienda.SetValue(Cuerpo, cuerpo.Json, Cultura);
                _contentService.SaveAndPublish(tienda, new[] { Cultura });
                n++;
            }
        }
        return $"paginas:{n}";
    }

    // ───────────────────────────── Créditos de las fotos (CC BY visibles) ─────────────────────────────

    /// <summary>La página «Legal y privacidad», que enlaza la nota del pie de todos los sitios.</summary>
    public static readonly Guid PaginaLegal = Guid.Parse("ee12bad1-ede8-4ba2-9ef8-0566f0f3c1f9");

    /// <summary>«Términos y condiciones»: la hermana de la que la página de créditos toma su forma.</summary>
    private static readonly Guid PaginaTerminos = Guid.Parse("078221ac-103d-4b1e-9fad-2236ac31c1b2");

    /// <summary>La nota del pie «Legal y privacidad», que sale en todas las páginas.</summary>
    private static readonly Guid NotaLegalDelPie = Guid.Parse("c42d6650-306b-420e-b841-324e362537a7");

    private const string TituloDeCreditos = "Créditos de las fotos";

    /// <summary>
    /// Una foto CC BY sólo se puede usar si su crédito SE VE. La forma con menos piezas: una página «Créditos de las
    /// fotos» hija de Legal —el mismo tipo y los mismos bloques que sus hermanas—, enlazada desde la lista de Legal y
    /// nombrada en la nota del pie, que sale en todas las páginas. La lista sale de la metadata de cada medio
    /// (<c>license</c> y <c>creditLabel</c>), así que una foto CC BY nueva aparece al volver a pasar esta receta.
    /// </summary>
    private string RecetaCreditos()
    {
        var legal = _contentService.GetById(PaginaLegal) ?? throw new InvalidOperationException("No existe la página Legal.");
        var terminos = _contentService.GetById(PaginaTerminos) ?? throw new InvalidOperationException("No existe Términos.");
        var texto = KeyOf("elementTextRichtext");
        var lista = CreditosDeFotos.Html(_media.ConLicencia(CreditosDeFotos.PideAtribucion).Select(m => (m.Alt, m.Licencia, m.Credito)));
        var cuerpoDeLaLista = "<p>Estas fotos se usan con una licencia que pide decir quién las hizo. Las demás son de dominio público o CC0.</p>" + lista;

        var pagina = _contentService.GetPagedChildren(legal.Id, 0, 100, out _)
            .FirstOrDefault(c => string.Equals(c.GetCultureName(Cultura) ?? c.Name, TituloDeCreditos, StringComparison.Ordinal));
        var cambio = false;
        if (pagina is null)
        {
            pagina = _contentService.Create(TituloDeCreditos, legal.Key, terminos.ContentType.Alias);
            pagina.SetCultureName(TituloDeCreditos, Cultura);
            foreach (var propiedad in terminos.Properties)
            {
                if (propiedad.Alias is Cuerpo or "heading" or "seoTitle" or "seoDescription" or "canonicalLink") { continue; }
                if (!pagina.HasProperty(propiedad.Alias)) { continue; }
                if (propiedad.PropertyType.VariesByCulture()) { pagina.SetValue(propiedad.Alias, terminos.GetValue(propiedad.Alias, Cultura), Cultura); }
                else { pagina.SetValue(propiedad.Alias, terminos.GetValue(propiedad.Alias)); }
            }
            cambio = true;
        }
        if (cambio || !pagina.Published)
        {
            // Nueva, o guardada sin publicar por un intento anterior: el cuerpo entero otra vez.
            pagina.SetValue(Cuerpo, CuerpoDeCreditos(terminos, cuerpoDeLaLista), Cultura);
            cambio = true;
        }
        else
        {
            var cuerpo = new CuerpoEditable(pagina.GetValue<string>(Cuerpo, Cultura));
            cuerpo.Reemplazar(texto, "textBody", cuerpoDeLaLista);
            if (cuerpo.Cambio) { pagina.SetValue(Cuerpo, cuerpo.Json, Cultura); cambio = true; }
        }
        foreach (var (alias, valor) in new[]
                 {
                     ("heading", TituloDeCreditos),
                     ("seoTitle", "Créditos de las fotos · SynergosLabs"),
                     ("seoDescription", "Autor, origen y licencia de las fotos del sitio que piden crédito."),
                 })
        {
            if (pagina.HasProperty(alias) && pagina.GetValue<string>(alias, Cultura) != valor) { pagina.SetValue(alias, valor, Cultura); cambio = true; }
        }
        if (cambio || !pagina.Published)
        {
            var publicada = _contentService.SaveAndPublish(pagina, new[] { Cultura });
            if (!publicada.Success)
            {
                var invalidas = string.Join(",", publicada.InvalidProperties?.Select(p => p.Alias) ?? []);
                return $"pagina-sin-publicar:{publicada.Result};invalidas:{invalidas}";
            }
        }

        // La lista de Legal la enlaza, como a sus hermanas.
        var enlace = $"<li><a href=\"/{{localLink:umb://document/{pagina.Key:N}}}\" title=\"{TituloDeCreditos}\">{TituloDeCreditos}</a></li>";
        var cuerpoLegal = new CuerpoEditable(legal.GetValue<string>(Cuerpo, Cultura));
        if (!cuerpoLegal.Contiene(pagina.Key.ToString("N")))
        {
            cuerpoLegal.TransformarTexto(texto, "textBody", html => html.Replace("</ul>", enlace + "</ul>", StringComparison.Ordinal));
        }
        cuerpoLegal.Reemplazar(KeyOf("elementCompHero"), "headingSubtitle",
            "Términos y condiciones, tratamiento de datos personales, cookies y créditos de las fotos de SynergosLabs.");
        if (cuerpoLegal.Cambio)
        {
            legal.SetValue(Cuerpo, cuerpoLegal.Json, Cultura);
            _contentService.SaveAndPublish(legal, new[] { Cultura });
        }

        // La nota del pie, en todas las páginas, lo nombra.
        var nota = _contentService.GetById(NotaLegalDelPie);
        const string TextoDeLaNota = "SynergosLabs es un sitio de demostración. Términos, tratamiento de datos, cookies y créditos de las fotos:";
        if (nota is not null && nota.HasProperty("footerNoteText") && nota.GetValue<string>("footerNoteText", Cultura) != TextoDeLaNota)
        {
            nota.SetValue("footerNoteText", TextoDeLaNota, Cultura);
            _contentService.SaveAndPublish(nota, new[] { Cultura });
        }

        var cuantas = _media.ConLicencia(CreditosDeFotos.PideAtribucion).Select(m => (m.Credito, m.Licencia)).Distinct().Count();
        return $"creditos:{cuantas};pagina:{(cambio ? "publicada" : "igual")}";
    }

    /// <summary>El cuerpo de la página de créditos: la portada de su hermana Términos (misma imagen) y la lista.</summary>
    private string CuerpoDeCreditos(IContent terminos, string lista)
    {
        var heroKey = KeyOf("elementCompHero");
        var original = System.Text.Json.Nodes.JsonNode.Parse(terminos.GetValue<string>(Cuerpo, Cultura) ?? "{}");
        var heroDeTerminos = (original?["contentData"] as JsonArray)?
            .OfType<JsonObject>()
            .FirstOrDefault(d => string.Equals(d["contentTypeKey"]?.GetValue<string>(), heroKey.ToString(), StringComparison.OrdinalIgnoreCase));

        var b = new BlockGridJsonBuilder();
        var seccionKey = KeyOf("elementLayoutSection");
        var portada = b.AddTopLevelBlock(seccionKey);
        portada.Set("spacingTop", "[\"none\"]").Set("spacingBottom", "[\"none\"]").Set("spacingInline", "[\"none\"]");
        portada.ApplyDefaults(_defaults.DefaultsFor(seccionKey));
        // Como sus hermanas, la portada lleva su botón de vuelta: la lista de botones no se queda vacía.
        var botonKey = KeyOf("elementActionButton");
        var botones = new BlockListJsonBuilder();
        botones.AddBlock(botonKey).Set("ctaLabel", "Todo lo legal")
            .Set("ctaLink", new JsonArray(new JsonObject
            {
                ["name"] = "Todo lo legal", ["url"] = null, ["target"] = "", ["udi"] = $"umb://document/{PaginaLegal:N}",
                ["icon"] = null, ["queryString"] = null,
            }).ToJsonString())
            .ApplyDefaults(_defaults.DefaultsFor(botonKey));
        portada.AddChild(AreaDeLaSeccion, heroKey, h =>
        {
            h.Set("ctaItems", botones.Build());
            h.Set("headingTitle", TituloDeCreditos)
             .Set("headingSubtitle", "Autor, origen y licencia de las fotos que piden crédito.")
             .Set("mediaAlt", heroDeTerminos?["mediaAlt"]?.GetValue<string>() ?? string.Empty);
            // En la base el selector ya es TEXTO con JSON: se copia tal cual, sin volver a serializarlo.
            var imagen = heroDeTerminos?["mediaReference"];
            if (imagen is JsonValue v && v.TryGetValue<string>(out var comoTexto)) { h.Set("mediaReference", comoTexto); }
            else if (imagen is not null) { h.Set("mediaReference", imagen.ToJsonString()); }
            h.ApplyDefaults(_defaults.DefaultsFor(heroKey));
        });
        var texto = KeyOf("elementTextRichtext");
        Seccion(b, null).AddChild(AreaDeLaSeccion, texto, t => t.Set("textBody", lista).ApplyDefaults(_defaults.DefaultsFor(texto)));
        return b.Build();
    }

    private Dictionary<string, IContent> PaginasDeProducto(Guid raiz)
    {
        var nodo = _contentService.GetById(raiz)
            ?? throw new InvalidOperationException($"No existe la raíz {raiz}.");
        return _contentService.GetPagedDescendants(nodo.Id, 0, 1000, out _)
            .Where(c => c.ContentType.Alias == "productPage")
            .ToDictionary(c => c.GetCultureName(Cultura) ?? c.Name ?? string.Empty, StringComparer.Ordinal);
    }

    /// <summary>La foto de cada producto en TODOS los medios que referencia su página (tarjeta y detalle).</summary>
    private (int Cambiados, int SinPagina) FotosDeProductos(Guid raiz, IReadOnlyList<FotoImportada> fotos, string carpeta)
    {
        var productos = PaginasDeProducto(raiz);
        int cambiados = 0, sinPagina = 0;
        foreach (var foto in fotos.Where(f => f.Producto is not null))
        {
            if (!productos.TryGetValue(foto.Producto!, out var pagina)) { sinPagina++; continue; }
            var ruta = Path.Combine(carpeta, foto.Archivo);
            var cambio = false;
            foreach (var clave in ClavesDeMedios(pagina))
            {
                cambio |= _media.ReplacePhoto(clave, ruta, $"producto {foto.Producto}", foto.Alt, foto.Licencia, foto.Credito);
            }
            if (!cambio) { continue; }
            cambiados++;
            _contentService.SaveAndPublish(pagina, new[] { Cultura });
        }
        return (cambiados, sinPagina);
    }

    /// <summary>
    /// Las fichas de inmueble del CMS: cada foto va al nodo de su AMBIENTE en la galería (<c>imagesJson</c> guarda URLs,
    /// así que la galería se reescribe con la URL nueva) y la fachada —o, sin ella, la sala— va además a la tarjeta.
    /// </summary>
    private (int Cambiados, int AmbientesNoEncontrados) FotosDeFichas(IReadOnlyList<FotoImportada> fotos, string carpeta)
    {
        var fichas = PaginasDeProducto(Propiedades);
        var galeria = KeyOf("elementSynLightboxGallery");
        int cambiados = 0, faltan = 0;

        foreach (var grupo in fotos.Where(f => f.Producto is not null && f.Ambiente is not null).GroupBy(f => f.Producto!))
        {
            if (!fichas.TryGetValue(grupo.Key, out var pagina)) { faltan += grupo.Count(); continue; }
            var cuerpo = new CuerpoEditable(pagina.GetValue<string>(Cuerpo, Cultura));
            var cambioAqui = false;

            foreach (var foto in grupo)
            {
                var ruta = Path.Combine(carpeta, foto.Archivo);
                var marca = $"-{foto.Ambiente}.";
                var encontrado = false;
                // Sin archivo, el ambiente sale de la galería: un marcador genérico no se queda haciéndose pasar por
                // una foto del inmueble (una «Habitación principal» en un local comercial).
                var quitar = string.IsNullOrWhiteSpace(foto.Archivo);
                cuerpo.TransformarLista(galeria, "imagesJson", lista =>
                {
                    var nueva = new JsonArray();
                    foreach (var item in lista)
                    {
                        var copia = (JsonObject)item!.DeepClone();
                        var url = copia["fullUrl"]?.GetValue<string>() ?? string.Empty;
                        if (quitar && url.Contains(marca, StringComparison.OrdinalIgnoreCase))
                        {
                            encontrado = true;
                            continue;
                        }
                        if (quitar) { nueva.Add(copia); continue; }
                        if (url.Contains(marca, StringComparison.OrdinalIgnoreCase) && _media.KeyByUrl(url) is { } clave)
                        {
                            encontrado = true;
                            cambioAqui |= _media.ReplacePhoto(clave, ruta, $"propiedad {grupo.Key} {foto.Ambiente}", foto.Alt, foto.Licencia, foto.Credito);
                            var nuevaUrl = _media.UrlOf(clave);
                            copia["fullUrl"] = nuevaUrl;
                            copia["thumbUrl"] = nuevaUrl;
                            copia["alt"] = foto.Alt;
                            if (foto.Rotulo is not null) { copia["caption"] = foto.Rotulo; }
                        }
                        nueva.Add(copia);
                    }
                    return nueva;
                });
                // Quitar lo que ya no está no es una falta: así la receta se puede volver a pasar.
                if (!encontrado && !quitar) { faltan++; }
            }

            // La tarjeta del inmueble es su fachada: una tanda que sólo trae la sala no se la cambia.
            var tarjeta = grupo.FirstOrDefault(f => f.Ambiente == "exterior" && !string.IsNullOrWhiteSpace(f.Archivo));
            var imagenesDeLaFicha = pagina.HasProperty("productImages")
                ? (pagina.Properties["productImages"]!.PropertyType.VariesByCulture()
                    ? pagina.GetValue<string>("productImages", Cultura)
                    : pagina.GetValue<string>("productImages"))
                : null;
            if (tarjeta is not null && imagenesDeLaFicha is { } imagenes)
            {
                foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(imagenes, @"""mediaKey""\s*:\s*""([0-9a-fA-F-]{36})"""))
                {
                    cambioAqui |= _media.ReplacePhoto(Guid.Parse(m.Groups[1].Value), Path.Combine(carpeta, tarjeta.Archivo),
                        $"producto {grupo.Key}", tarjeta.Alt, tarjeta.Licencia, tarjeta.Credito);
                }
            }

            if (cuerpo.Cambio) { pagina.SetValue(Cuerpo, cuerpo.Json, Cultura); }
            if (cuerpo.Cambio || cambioAqui)
            {
                _contentService.SaveAndPublish(pagina, new[] { Cultura });
                cambiados++;
            }
        }
        return (cambiados, faltan);
    }

    /// <summary>
    /// La tarjeta <paramref name="categoria"/> de la portada de Booking toma el medio que ya usa el producto
    /// <paramref name="producto"/> (no se duplica el fichero). Sólo si la tarjeta no tiene imagen.
    /// </summary>
    private int TarjetaConLaFotoDe(string categoria, string producto)
    {
        if (!PaginasDeProducto(Booking).TryGetValue(producto, out var paginaDelProducto)) { return 0; }
        var claves = ClavesDeMedios(paginaDelProducto);
        if (claves.Count == 0) { return 0; }
        var clave = claves[0];
        var portada = _contentService.GetById(Booking);
        if (portada is null) { return 0; }

        var cuerpo = new CuerpoEditable(portada.GetValue<string>(Cuerpo, Cultura));
        var tarjeta = KeyOf("elementCompCard");
        var sinImagen = (Func<JsonObject, bool>)(d =>
            CuerpoEditable.Donde("headingTitle", categoria)(d)
            && (d["mediaReference"] is not JsonArray a || a.Count == 0));
        var picker = JsonNode.Parse($"[{{\"key\":\"{Guid.NewGuid()}\",\"mediaKey\":\"{clave}\",\"crops\":[],\"focalPoint\":null}}]")!;
        var n = cuerpo.Reemplazar(tarjeta, "mediaReference", picker, sinImagen);
        if (n == 0) { return 0; }
        portada.SetValue(Cuerpo, cuerpo.Json, Cultura);
        _contentService.SaveAndPublish(portada, new[] { Cultura });
        return n;
    }

    /// <summary>
    /// Las Keys de los medios que referencia una página (cualquier MediaPicker3 de sus propiedades o bloques). Dentro de
    /// un Block Grid el valor anidado puede venir como texto con las comillas escapadas (<c>\"</c> o <c>"</c>).
    /// </summary>
    private static IReadOnlyList<Guid> ClavesDeMedios(IContentBase pagina) =>
        pagina.Properties
            .SelectMany(p => p.Values)
            .Select(v => v.EditedValue?.ToString())
            .Where(v => v is not null && v.Contains("mediaKey", StringComparison.Ordinal))
            .Select(v => v!.Replace("\\u0022", "\"", StringComparison.Ordinal).Replace("\\\"", "\"", StringComparison.Ordinal))
            .SelectMany(v => System.Text.RegularExpressions.Regex.Matches(v, @"""mediaKey""\s*:\s*""([0-9a-fA-F-]{36})")
                .Select(m => Guid.Parse(m.Groups[1].Value)))
            .Distinct()
            .ToList();

    // ───────────────────────────── Educación (I5) ─────────────────────────────

    private sealed record Curso(string Id, string Titulo, string Linea, string Resumen, string Archivo, string Alt);

    /// <summary>Tres cursos del catálogo de la app (<c>/api/academy/courses</c>), con su foto CC0 de <c>media/academy/courses</c>.</summary>
    private static readonly Curso[] CursosDestacados =
    [
        new("data-ml-applied", "Machine Learning Aplicado", "Datos · Avanzado · $ 480.000",
            "Del dato sucio al modelo en producción, sin humo.", "ml-applied.jpg",
            "Persona frente a varios monitores con gráficas de datos"),
        new("dev-clean-architecture", "Arquitectura Limpia en .NET", "Desarrollo · Intermedio · $ 320.000",
            "Diseña sistemas mantenibles con Clean Architecture, SOLID y seams que se pueden probar.", "clean-architecture.jpg",
            "Código fuente en una pantalla, en primer plano"),
        new("design-systems-atomic", "Design Systems con Atomic Design", "Diseño · Principiante · $ 240.000",
            "Construye un design system escalable: tokens, átomos, moléculas y organismos.", "design-systems.jpg",
            "Lámina antigua de alfabetos con letras de distintos estilos"),
    ];

    private string RecetaEducacion(CuerpoEditable cuerpo)
    {
        // Los «Cursos destacados» hablaban del motor (plataformas componibles, tokens) y no estaban en el catálogo de
        // la app: la sección se desconecta (no se borra la pieza) y en su lugar van tres cursos reales con su foto.
        if (cuerpo.Contiene(Marca("educacion"))) { return "destacados:ya"; }
        var rejilla = KeyOf("elementSynFeatureGrid");
        var esLaDeDestacados = CuerpoEditable.Donde("headingText", "Cursos destacados");

        var b = new BlockGridJsonBuilder();
        var s = Seccion(b, null, Marca("educacion"));
        Encabezado(s, "Cursos destacados");
        FilasDeTarjetas(s, CursosDestacados.Select(c => new Tarjeta(
            c.Titulo, c.Linea, c.Resumen,
            _media.GetOrCreatePhotoPickerValue($"Foto Educación {c.Titulo}", c.Alt, Path.Combine(_env.WebRootPath, "media", "academy", "courses", c.Archivo), LicenciaCc0),
            c.Alt, "Ver el curso", $"/educacion/#/academy/curso/{c.Id}")).ToArray());

        var nuevas = cuerpo.InsertarSeccionesAntesDe(rejilla, b.Build(), esLaDeDestacados);
        var fuera = cuerpo.DesconectarSeccionesCon(rejilla, esLaDeDestacados);
        return $"destacados:{CursosDestacados.Length};secciones-nuevas:{nuevas};desconectadas:{fuera}";
    }

    // ───────────────────────────── Eventos (I5) ─────────────────────────────

    private string RecetaEventos(CuerpoEditable cuerpo)
    {
        // Los «Eventos destacados» eran tres tarjetas de texto sobre el motor (Synergos Summit, tokens). Un texto fijo
        // envejece: en su lugar va el listado que arma el servidor con la fuente «eventos», que sólo trae los eventos
        // que no han empezado (EventosDelCatalogo). La sección vieja queda desconectada, la pieza sigue.
        if (cuerpo.Contiene(Marca("eventos"))) { return "proximos:ya"; }
        var rejilla = KeyOf("elementSynFeatureGrid");
        var esLaDeDestacados = CuerpoEditable.Donde("headingText", "Eventos destacados");
        var listado = KeyOf("elementSynDataGrid");

        var b = new BlockGridJsonBuilder();
        var s = Seccion(b, null, Marca("eventos"));
        Encabezado(s, "Próximos eventos");
        s.AddChild(AreaDeLaSeccion, listado, g => g
            .Set("fuente", "[\"eventos\"]")
            .ApplyDefaults(_defaults.DefaultsFor(listado)));

        var nuevas = cuerpo.InsertarSeccionesAntesDe(rejilla, b.Build(), esLaDeDestacados);
        var fuera = cuerpo.DesconectarSeccionesCon(rejilla, esLaDeDestacados);
        return $"proximos:listado;secciones-nuevas:{nuevas};desconectadas:{fuera}";
    }

    // ───────────────────────────── Gobierno (I7) ─────────────────────────────

    private sealed record Tramite(string Id, string Nombre, string Linea, string Resumen);

    /// <summary>Tres trámites del catálogo de la app (<c>/api/gov/services</c>): entidad, tasa y plazo como los pinta la app.</summary>
    private static readonly Tramite[] TramitesMasConsultados =
    [
        new("trm-cedula", "Renovación de cédula de ciudadanía", "Registraduría Nacional · $ 65.500 · unos 15 días hábiles",
            "Renueva tu cédula por deterioro, pérdida o actualización de datos."),
        new("trm-pasaporte", "Expedición de pasaporte", "Cancillería · $ 189.000 · unos 8 días hábiles",
            "Pide la expedición de tu pasaporte colombiano para viajar al exterior."),
        new("trm-licencia-conduccion", "Renovación de licencia de conducción", "Secretaría de Movilidad · $ 95.000 · unos 5 días hábiles",
            "Renueva tu licencia vigente o vencida. Necesitas el examen médico aprobado."),
    ];

    private string RecetaGobierno(CuerpoEditable cuerpo)
    {
        // La portada era la app y «Cómo funciona». Debajo: los trámites más pedidos con enlace a su ficha, las entidades
        // que atienden aquí, las preguntas de siempre (radicado, tasa, plazos, subsanar) y el cierre a «Mis solicitudes».
        if (cuerpo.Contiene(Marca("gobierno"))) { return "ya-compuesta"; }

        var b = new BlockGridJsonBuilder();
        var destacados = Seccion(b, null, Marca("gobierno"));
        Encabezado(destacados, "Trámites más consultados");
        FilasDeTarjetas(destacados, TramitesMasConsultados.Select(t => new Tarjeta(
            t.Nombre, t.Linea, t.Resumen, null, t.Nombre, "Ver el trámite", $"/gobierno/#/gov/tramite/{t.Id}")).ToArray());

        Rejilla(b, "surface", "Entidades que atienden aquí", 4,
            ("Registraduría Nacional", "Renovación de la cédula de ciudadanía."),
            ("Cancillería", "Expedición del pasaporte."),
            ("Secretaría de Movilidad", "Renovación de la licencia de conducción."),
            ("Secretaría de Salud", "Afiliación al régimen subsidiado de salud."),
            ("Alcaldía Municipal", "Certificado de residencia."),
            ("Cámara de Comercio", "Registro mercantil de tu empresa."),
            ("Ministerio de Vivienda", "Solicitud del subsidio de vivienda."));

        Preguntas(b, "Preguntas frecuentes",
            ("¿Qué es el número de radicado?", "Es el código que recibe tu solicitud al enviarla. Con él la encuentras en «Mis solicitudes» y la identifica la entidad."),
            ("¿Cuánto tarda un trámite?", "Cada trámite dice su plazo en días hábiles. Se cuentan desde que radicas; los fines de semana y festivos no cuentan."),
            ("¿Cómo pago la tasa?", "Si el trámite tiene tasa, la pagas en línea al radicar. Los trámites gratuitos lo dicen en su ficha."),
            ("¿Qué pasa si falta un documento?", "La entidad te pide subsanar y te avisamos en «Notificaciones». Completas lo que falta y el trámite sigue."),
            ("¿Se realizan cobros reales?", "No. Este es un sitio de demostración: las solicitudes y los pagos no tienen efecto ante ninguna entidad."));

        Cierre(b, "¿Ya radicaste?", "Consulta en qué etapa va tu expediente y lo que te ha pedido la entidad.",
            "Ver mis solicitudes", "/gobierno/#/gov/mis-solicitudes");

        var n = cuerpo.Sumar(b.Build());
        return $"mas-consultados:{TramitesMasConsultados.Length};entidades:7;preguntas:5;cierre:1;secciones:{n}";
    }

    // ───────────────────────────── Tienda (I8) ─────────────────────────────

    /// <summary>Las páginas del flujo de compra: se llega a ellas desde la tienda, no desde el pie.</summary>
    private static readonly string[] PaginasDelFlujo = ["Catálogo", "Producto", "Carrito", "Pago", "Confirmación", "Mi cuenta"];

    private string RecetaTienda(CuerpoEditable cuerpo)
    {
        // 1. La rejilla SSR de productos repetía, debajo, lo que ya muestra la app arriba: se desconecta (la pieza sigue).
        var fuera = cuerpo.DesconectarSeccionesCon(KeyOf("elementShopProductGrid"));

        // 2. El cierre hablaba en voseo («Lanzá») y su botón llevaba al catálogo de ropa: ahora invita a vender y lleva
        //    a la consola del vendedor.
        var cierre = KeyOf("elementCompCtaBanner");
        var esElDeVender = CuerpoEditable.Donde("headingTitle", "¿Listo para vender online?");
        var n = cuerpo.Reemplazar(cierre, "headingSubtitle", "Lanza tu tienda esta semana: publica tus productos desde la consola del vendedor.", esElDeVender);
        n += cuerpo.Reemplazar(cierre, "ctaLabel", "Empieza a vender", esElDeVender);
        n += cuerpo.Reemplazar(cierre, "ctaLink", JsonNode.Parse(Enlace("Empieza a vender", "/tienda/vender/"))!, esElDeVender);

        // 3. El pie listaba las páginas internas del flujo de compra.
        var ocultas = OcultarDelPie(DevTiendaPorPaginas.Tienda, PaginasDelFlujo);
        return $"rejilla-repetida:{fuera};cierre:{n};ocultas-del-pie:{ocultas}";
    }

    private int OcultarDelPie(Guid raiz, IReadOnlyCollection<string> nombres)
    {
        var padre = _contentService.GetById(raiz);
        if (padre is null) { return 0; }
        var n = 0;
        foreach (var hija in _contentService.GetPagedChildren(padre.Id, 0, 200, out _))
        {
            var nombre = hija.GetCultureName(Cultura) ?? hija.Name ?? string.Empty;
            if (!nombres.Contains(nombre) || !hija.HasProperty("hideFromFooter")) { continue; }
            if (hija.GetValue<bool>("hideFromFooter")) { continue; }
            hija.SetValue("hideFromFooter", true);
            _contentService.SaveAndPublish(hija, new[] { Cultura });
            n++;
        }
        return n;
    }

    private sealed record Ficha(string Uso, string[] Detalles);

    /// <summary>
    /// Lo que cada ficha añade a su descripción (para qué sirve) y su lista de detalles, sacados de lo que el producto ya
    /// declara (subtítulo y descripción del catálogo). Nada de promesas nuevas: ni garantías ni envíos.
    /// </summary>
    private static readonly Dictionary<string, Ficha> Fichas = new(StringComparer.Ordinal)
    {
        ["Audífonos de diadema"] = new("Para concentrarte en la oficina, estudiar o viajar sin el ruido de fondo.",
            ["Cancelación activa de ruido", "Hasta 30 horas de batería", "Diseño de diadema, sobre la oreja"]),
        ["Base para celular"] = new("Deja el teléfono a la vista en el escritorio para videollamadas, recetas o mensajes.",
            ["Cuerpo de aluminio", "Ángulo ajustable", "Base con peso que no se desliza"]),
        ["Bolígrafo de aluminio"] = new("Un bolígrafo de todos los días que se siente firme en la mano.",
            ["Aluminio anodizado", "Peso equilibrado", "Recarga estándar fácil de conseguir"]),
        ["Botella térmica"] = new("Lleva agua fría al gimnasio o café caliente a la oficina en la misma botella.",
            ["Acero inoxidable de doble pared", "Capacidad de 750 ml", "12 horas frío y 6 horas caliente"]),
        ["Buzo con cierre"] = new("La capa que te pones y te quitas según cambie el clima durante el día.",
            ["Cierre completo", "Bolsillos laterales", "Uso como capa intermedia"]),
        ["Camiseta esencial negra"] = new("El básico para combinar con jeans, chaqueta o lo que tengas a mano.",
            ["Algodón premium de 180 g", "Corte regular", "Color negro"]),
        ["Camiseta oversize blanca"] = new("Una silueta suelta para el día a día, sola o debajo de una chaqueta.",
            ["Algodón peinado", "Corte amplio con hombro caído", "Color blanco"]),
        ["Chaqueta cortavientos"] = new("Va en el morral doblada y sale cuando el cielo cambia de opinión.",
            ["Tejido repelente al agua", "Se pliega en su propio bolsillo", "Capucha para la lluvia"]),
        ["Funda para laptop"] = new("Protege el portátil dentro del morral o cuando lo llevas en la mano.",
            ["Neopreno de 5 mm", "Interior afelpado", "Para portátiles de 14 pulgadas"]),
        ["Gorra clásica"] = new("Para el sol de mediodía o para completar el atuendo de fin de semana.",
            ["Correa ajustable", "Bordado al frente", "Visera curva"]),
        ["Hoodie gris premium"] = new("Abriga en las mañanas frías sin verse como ropa de casa.",
            ["Felpa pesada con interior perchado", "Capucha forrada", "Color gris"]),
        ["Hub USB-C"] = new("Conecta pantalla, memorias y cargador a un portátil que sólo trae puertos USB-C.",
            ["7 puertos en uno", "HDMI 4K y tres USB-A", "Lector SD y carga passthrough de 100 W"]),
        ["Libreta de puntos"] = new("Sirve para listas, bocetos, diarios o planes: la retícula guía sin imponer renglones.",
            ["Tamaño A5", "Retícula de puntos cada 5 mm", "Para escribir o dibujar"]),
        ["Libreta de tapa dura"] = new("Para tomar notas en reuniones o llevar un diario que aguante el uso diario.",
            ["Tamaño A5", "160 páginas de papel de 100 g", "Abre plana y no traspasa la tinta"]),
        ["Llavero de cuero"] = new("Un llavero sencillo que gana carácter con el uso.",
            ["Cuero curtido al vegetal", "Herraje macizo", "Para llaves de casa, carro o moto"]),
        ["Morral urbano"] = new("Para ir al trabajo o a clase con el portátil y lo del día en un solo morral.",
            ["Capacidad de 22 litros", "Compartimento acolchado para laptop de 15 pulgadas", "Espalda ventilada"]),
        ["Mouse inalámbrico"] = new("Cambia entre el portátil y el computador de escritorio sin volver a emparejar.",
            ["Clic silencioso", "Conexión 2.4 GHz y Bluetooth", "Botón para cambiar de equipo"]),
        ["Mousepad de tela"] = new("Ordena el escritorio debajo del teclado y del mouse a la vez.",
            ["90 × 40 cm", "Tela con borde cosido", "Base antideslizante"]),
        ["Organizador de cables"] = new("Mantiene los cables del cargador y los audífonos al borde del escritorio.",
            ["Set de 6 piezas", "Silicona", "Se pegan al escritorio"]),
        ["Organizador de escritorio"] = new("Un sitio para bolígrafos, notas y el celular, para que el escritorio respire.",
            ["Bambú macizo", "5 compartimentos", "Para lápices, notas y celular"]),
        ["Pack de stickers"] = new("Para personalizar la laptop, la agenda o la botella.",
            ["10 unidades", "Vinilo resistente al agua", "Para superficies lisas"]),
        ["Set de notas adhesivas"] = new("Para recordatorios, lluvias de ideas o marcar páginas.",
            ["5 blocs", "Adhesivo reposicionable", "No deja residuo"]),
        ["Taza cerámica"] = new("La taza del café de la mañana, en casa o en la oficina.",
            ["Cerámica de 350 ml", "Apta para microondas", "Apta para lavavajillas"]),
        ["Tote bag de lona"] = new("Para el mercado, la playa o el día a día, en lugar de las bolsas plásticas.",
            ["Lona resistente", "Asas reforzadas", "Reutilizable"]),
    };

    /// <summary>
    /// Las 24 fichas de la Tienda: la descripción suma para qué sirve el producto, «Detalles» deja el texto genérico
    /// (igual en todas) por la lista de lo que el producto declara, y el cierre deja el voseo («Agregá… completá»).
    /// Idempotente: una ficha que ya dice lo nuevo no se toca.
    /// </summary>
    public Resultado FichasDeLaTienda()
    {
        var productos = PaginasDeProducto(DevTiendaPorPaginas.Tienda);
        var mision = KeyOf("elementCorpMissionBlock");
        var split = KeyOf("elementCompMediaTextSplit");
        var cierre = KeyOf("elementCompCtaBanner");
        int cambiadas = 0, sinFicha = 0;

        foreach (var (nombre, pagina) in productos)
        {
            if (!Fichas.TryGetValue(nombre, out var ficha)) { sinFicha++; continue; }
            var descripcion = (pagina.GetValue<string>("productDescriptionText", Cultura) ?? pagina.GetValue<string>("productDescriptionText") ?? string.Empty)
                .Replace("Personalizá", "Personaliza", StringComparison.Ordinal);
            var cuerpo = new CuerpoEditable(pagina.GetValue<string>(Cuerpo, Cultura));
            Func<string, string> html = TextoHtml;

            cuerpo.Reemplazar(mision, "textBody", $"<p>{html(descripcion)}</p><p>{html(ficha.Uso)}</p>",
                CuerpoEditable.Donde("headingTitle", "Descripción"));
            cuerpo.Reemplazar(split, "textBody",
                "<ul>" + string.Concat(ficha.Detalles.Select(d => $"<li>{html(d)}</li>")) + "</ul>",
                CuerpoEditable.Donde("headingTitle", "Detalles"));
            cuerpo.Reemplazar(cierre, "headingSubtitle", "Agrega al carrito y completa tu compra en minutos.",
                CuerpoEditable.Donde("headingTitle", "¿Listo para comprar?"));

            var descripcionCambio = false;
            foreach (var cultura in new string?[] { Cultura, null })
            {
                if (!pagina.HasProperty("productDescriptionText")) { break; }
                var actual = pagina.GetValue<string>("productDescriptionText", cultura);
                if (actual is not null && actual.Contains("Personalizá", StringComparison.Ordinal))
                {
                    pagina.SetValue("productDescriptionText", actual.Replace("Personalizá", "Personaliza", StringComparison.Ordinal), cultura);
                    descripcionCambio = true;
                }
            }

            if (!cuerpo.Cambio && !descripcionCambio) { continue; }
            if (cuerpo.Cambio) { pagina.SetValue(Cuerpo, cuerpo.Json, Cultura); }
            _contentService.SaveAndPublish(pagina, new[] { Cultura });
            cambiadas++;
        }

        var detalle = $"fichas:{cambiadas};sin-ficha:{sinFicha}";
        _logger.LogInformation("Fichas de la Tienda: {Detalle}.", detalle);
        return new Resultado(sinFicha == 0, "tienda-fichas", detalle, cambiadas);
    }

    // ───────────────────────────── Fichas de Propiedades (2.ª medición, punto 1) ─────────────────────────────

    /// <summary>
    /// La ficha técnica de cada inmueble con SUS datos, sacados de lo que su ficha ya dice en «Sobre la propiedad».
    /// Antes era la misma en las seis, y en el local y la oficina hablaba de «Dormitorios con buena iluminación».
    /// </summary>
    private static readonly Dictionary<string, (string Rotulo, string Valor)[]> FichasTecnicas = new(StringComparer.Ordinal)
    {
        ["Apartamento de lujo en Chicó"] =
            [("Área", "120 m²"), ("Habitaciones", "3"), ("Parqueaderos", "2"), ("Destacado", "Balcón con vista panorámica")],
        ["Apartamento familiar en Cedritos"] =
            [("Área", "78 m²"), ("Habitaciones", "2"), ("Cocina", "Integral"), ("Conjunto", "Piscina y gimnasio")],
        ["Casa campestre en Los Lagos"] =
            [("Área", "180 m²"), ("Habitaciones", "3"), ("Exteriores", "Terraza y BBQ"), ("Lote", "Amplio y arborizado")],
        ["Casa con jardín en Usaquén"] =
            [("Área", "240 m² en dos niveles"), ("Habitaciones", "4 y estudio"), ("Jardín", "Privado"), ("Garaje", "Doble")],
        ["Local comercial en Zona T"] =
            [("Área", "140 m²"), ("Ubicación", "A pie de calle, en la Zona T"), ("Vitrina", "Amplia, con alto flujo peatonal"), ("Uso", "Retail o gastronomía")],
        ["Oficina en torre empresarial"] =
            [("Área", "95 m²"), ("Edificio", "Torre AAA con recepción"), ("Salas de reunión", "2"), ("Vista", "A la ciudad")],
    };

    private string RecetaFichasDePropiedades()
    {
        var fichas = PaginasDeProducto(Propiedades);
        var rejilla = KeyOf("elementSynFeatureGrid");
        var esLaFichaTecnica = CuerpoEditable.Donde("headingText", "Ficha técnica");
        int cambiadas = 0, sinDatos = 0;

        foreach (var (nombre, pagina) in fichas)
        {
            if (!FichasTecnicas.TryGetValue(nombre, out var datos)) { sinDatos++; continue; }
            var cuerpo = new CuerpoEditable(pagina.GetValue<string>(Cuerpo, Cultura));
            cuerpo.TransformarLista(rejilla, "itemsJson",
                _ => new JsonArray(datos.Select(d => (JsonNode)new JsonObject { ["heading"] = d.Rotulo, ["body"] = d.Valor }).ToArray()),
                esLaFichaTecnica);
            if (!cuerpo.Cambio) { continue; }
            pagina.SetValue(Cuerpo, cuerpo.Json, Cultura);
            _contentService.SaveAndPublish(pagina, new[] { Cultura });
            cambiadas++;
        }
        return $"fichas-tecnicas:{cambiadas};sin-datos:{sinDatos}";
    }

    // ───────────────────────────── Promesas de Hoteles (2.ª medición, punto 2) ─────────────────────────────

    /// <summary>La página Reservar de Hoteles, por su Key de uSync.</summary>
    public static readonly Guid ReservarHoteles = Guid.Parse("cd926cac-d872-4acd-bd51-9b6764ad7625");

    /// <summary>
    /// Cambia, en los elementos de una lista JSON de los bloques del tipo dado, los campos de <paramref name="cambios"/>
    /// de los elementos cuyo <paramref name="clave"/> vale <paramref name="valorClave"/>.
    /// </summary>
    private static int CambiarEnLista(CuerpoEditable cuerpo, Guid tipo, string propiedad, string clave, string valorClave,
        params (string Campo, string Valor)[] cambios) =>
        cuerpo.TransformarLista(tipo, propiedad, lista =>
        {
            var nueva = new JsonArray();
            foreach (var item in lista)
            {
                var copia = (JsonObject)item!.DeepClone();
                if (copia[clave] is JsonValue v && v.TryGetValue<string>(out var texto) && texto == valorClave)
                {
                    foreach (var (campo, valor) in cambios) { copia[campo] = valor; }
                }
                nueva.Add(copia);
            }
            return nueva;
        });

    /// <summary>
    /// Hoteles prometía lo que su asistente no hace: «Mejor precio garantizado», confirmación y recordatorios «por
    /// correo», «Cancelación gratuita hasta 48 horas». El asistente muestra el comprobante con el número de la reserva y,
    /// por habitación, si la tarifa es reembolsable y su política; el correo sólo sale con Bff.Viajes encendido, que no es
    /// el modo por defecto. Los textos dicen eso.
    /// </summary>
    private string RecetaPromesasDeHoteles()
    {
        var n = 0;
        var rejilla = KeyOf("elementSynFeatureGrid");
        var portada = _contentService.GetById(Hoteles);
        if (portada is not null)
        {
            var cuerpo = new CuerpoEditable(portada.GetValue<string>(Cuerpo, Cultura));
            CambiarEnLista(cuerpo, rejilla, "itemsJson", "heading", "Confirmación inmediata",
                ("body", "Al confirmar ves tu comprobante con el número de la reserva, sin esperar una llamada."));
            CambiarEnLista(cuerpo, rejilla, "itemsJson", "heading", "Mejor precio garantizado",
                ("heading", "Tarifas claras"),
                ("body", "Cada habitación muestra su precio y si la tarifa es reembolsable antes de que confirmes."));
            if (cuerpo.Cambio)
            {
                portada.SetValue(Cuerpo, cuerpo.Json, Cultura);
                _contentService.SaveAndPublish(portada, new[] { Cultura });
                n++;
            }
        }

        var reservar = _contentService.GetById(ReservarHoteles);
        if (reservar is not null)
        {
            var cuerpo = new CuerpoEditable(reservar.GetValue<string>(Cuerpo, Cultura));
            cuerpo.Reemplazar(KeyOf("elementCompHero"), "textBody",
                "<p>Completa tu reserva en pocos pasos: fechas, huéspedes, habitación y datos de contacto. Al confirmar ves tu comprobante con el número de la reserva.</p>");
            cuerpo.Reemplazar(KeyOf("elementCorpMissionBlock"), "textBody",
                "<p>El asistente te guía paso a paso y te muestra las habitaciones libres para tus fechas. Antes de confirmar ves el total y si la tarifa es reembolsable.</p>",
                CuerpoEditable.Donde("headingTitle", "Completa tu reserva"));
            CambiarEnLista(cuerpo, KeyOf("elementSynAccordion"), "itemsJson", "title", "Política de cancelación",
                ("content", "Depende de la tarifa: el asistente te muestra si es reembolsable y su política de cancelación antes de confirmar."));
            var preguntas = KeyOf("elementSynFaqSection");
            CambiarEnLista(cuerpo, preguntas, "itemsJson", "question", "¿Puedo modificar mi reserva?",
                ("answer", "Escríbenos con el número de tu reserva. Los cambios dependen de la política de la tarifa que elegiste."));
            CambiarEnLista(cuerpo, preguntas, "itemsJson", "question", "¿Recibo recordatorios?",
                ("answer", "Por ahora no enviamos recordatorios. Guarda el número de reserva que ves al confirmar."));
            CambiarEnLista(cuerpo, preguntas, "itemsJson", "question", "¿Cómo se cobra la estadía?",
                ("answer", "El precio se muestra en pesos colombianos y el total aparece antes de que confirmes."));
            if (cuerpo.Cambio)
            {
                reservar.SetValue(Cuerpo, cuerpo.Json, Cultura);
                _contentService.SaveAndPublish(reservar, new[] { Cultura });
                n++;
            }
        }
        return $"paginas:{n}";
    }

    // ───────────────────────────── Atribuciones (I6) ─────────────────────────────

    /// <summary>
    /// Lo que cada sitio dice de sí (su <c>seoDescription</c>, que el pie usa como frase) y a quién atribuye sus
    /// testimonios: rol y ciudad, nunca un nombre inventado (decisión del director, 2026-11-13).
    /// </summary>
    private sealed record Atribucion(Guid Pagina, string? Descripcion, (string Autor, string Ciudad)[] Testimonios);

    private static readonly Dictionary<string, Atribucion> Atribuciones = new(StringComparer.OrdinalIgnoreCase)
    {
        ["viajes"] = new(Viajes, "Estadías, vuelos y autos en un solo carrito, con tarifas que dicen si son reembolsables.", []),
        ["propiedades"] = new(Propiedades, "Apartamentos, casas y locales en venta y arriendo: busca en lista o mapa y agenda una visita.",
            [("Compradora", "Bogotá"), ("Comprador", "Medellín"), ("Vendedora", "Cali")]),
        ["booking"] = new(Booking, "Consultoría, espacios y bienestar: elige el servicio y la hora, y te contactan para confirmar.",
            [("Gerente de operaciones", "Bogotá"), ("Director de servicio", "Medellín"), ("Coordinadora de espacios", "Cali")]),
        ["hoteles"] = new(Hoteles, "Habitaciones para dos a cinco huéspedes, con la disponibilidad al día y la reserva en cuatro pasos.",
            [("Huésped frecuente", "Bogotá"), ("Huésped en viaje de negocios", "Medellín"), ("Huésped de fin de semana", "Cartagena")]),
        ["tienda"] = new(DevTiendaPorPaginas.Tienda, "Ropa, accesorios, escritorio y tecnología, con carrito y pago en un solo lugar.",
            [("Clienta verificada", "Bogotá"), ("Cliente verificado", "Medellín"), ("Clienta verificada", "Cali")]),
        ["eventos"] = new(Eventos, "Conciertos, conferencias y teatro: encuentra tu evento y asegura tu entrada.",
            [("Líder de producto", "Bogotá"), ("Ingeniero de plataforma", "Medellín"), ("Directora de diseño", "Cali")]),
        ["educacion"] = new(Educacion, "Cursos de datos, desarrollo y diseño para aprender a tu ritmo, con proyecto y certificado.",
            [("Estudiante de frontend", "Medellín"), ("Estudiante de backend", "Bogotá"), ("Egresada", "Cali")]),
        ["gobierno"] = new(Gobierno, "Trámites en línea en lenguaje claro: radica, paga la tasa si aplica y sigue tu expediente.", []),
        ["synergos"] = new(Synergos, null,
            [("CTO de un grupo empresarial", "Bogotá"), ("Líder de producto", "Medellín"), ("Directora digital", "Cali")]),
        ["casos"] = new(Casos, null,
            [("COO de una cadena de retail", "Barranquilla"), ("Gerente de e-commerce", "Cali"), ("Director de tecnología", "Bogotá")]),
    };

    /// <summary>Cambia autor y rol de cada testimonio por rol y ciudad; la cita y la calificación se quedan.</summary>
    private int Reatribuir(CuerpoEditable cuerpo, (string Autor, string Ciudad)[] testimonios)
    {
        if (testimonios.Length == 0) { return 0; }
        return cuerpo.TransformarLista(KeyOf("elementSynTestimonialCarousel"), "testimonialsJson", lista =>
        {
            var nueva = new JsonArray();
            for (var i = 0; i < lista.Count; i++)
            {
                var t = (JsonObject)lista[i]!.DeepClone();
                var (autor, ciudad) = testimonios[i % testimonios.Length];
                t["author"] = autor;
                t["role"] = ciudad;
                nueva.Add(t);
            }
            return nueva;
        });
    }

    // ───────────────────────────── Piezas de composición ─────────────────────────────

    private sealed record Tarjeta(string Titulo, string Subtitulo, string Texto, string? Foto, string Alt, string Cta, string Url);

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
                    col.AddChild(AreasDeTresColumnas[j], cardKey, c =>
                    {
                        c.Set("headingTitle", t.Titulo)
                         .Set("headingSubtitle", t.Subtitulo)
                         .Set("textBody", $"<p>{TextoHtml(t.Texto)}</p>")
                         .Set("mediaAlt", t.Alt)
                         .Set("ctaLabel", t.Cta)
                         .Set("ctaLink", Enlace(t.Cta, t.Url));
                        if (t.Foto is not null) { c.Set("mediaReference", t.Foto); }
                        c.ApplyDefaults(_defaults.DefaultsFor(cardKey));
                    });
                }
            });
        }
    }

    private void Rejilla(BlockGridJsonBuilder b, string? tema, string titulo, int columnas, params (string titulo, string texto)[] items)
        => RejillaConMarca(b, tema, null, titulo, columnas, items);

    private void RejillaConMarca(BlockGridJsonBuilder b, string? tema, string? clase, string titulo, int columnas,
        params (string titulo, string texto)[] items)
    {
        var key = KeyOf("elementSynFeatureGrid");
        var s = Seccion(b, tema, clase);
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

    /// <summary>Un cuerpo de una sola pieza, un encabezado, para ponerlo antes de otro bloque.</summary>
    private string UnEncabezado(string titulo)
    {
        var key = KeyOf("elementTextHeading");
        var b = new BlockGridJsonBuilder();
        b.AddTopLevelBlock(key).Set("headingTitle", titulo).ApplyDefaults(_defaults.DefaultsFor(key));
        return b.Build();
    }

    private string IlustracionDelKit(string nombre, string alt, string relativa) =>
        DevMediaFactory.KitPath(relativa) is { } ruta
            ? _media.GetOrCreatePhotoPickerValue(nombre, alt, ruta, null, DevMediaFactory.FolderIlustraciones)
            : throw new FileNotFoundException($"No encuentro la ilustración del kit '{relativa}' (_archive/multimedia; SYNERGOS_ARCHIVE_ROOT).");

    /// <summary>
    /// La portada de una página: <c>elementCompHero</c> pegado al encabezado, con botones. El fondo es el gradiente
    /// limpio de marca de la carpeta Heroes (la regla de imágenes: los heroes oscuros no llevan ilustración).
    /// </summary>
    private void Portada(BlockGridJsonBuilder b, string titulo, string subtitulo, string cuerpo,
        string imagen, string alt, string desde, string hasta, params (string texto, string url)[] botones)
    {
        var seccionKey = KeyOf("elementLayoutSection");
        var heroKey = KeyOf("elementCompHero");
        var botonKey = KeyOf("elementActionButton");
        var s = b.AddTopLevelBlock(seccionKey);
        s.Set("spacingTop", "[\"none\"]").Set("spacingBottom", "[\"none\"]").Set("spacingInline", "[\"none\"]");
        s.ApplyDefaults(_defaults.DefaultsFor(seccionKey));

        var lista = new BlockListJsonBuilder();
        for (var i = 0; i < botones.Length; i++)
        {
            var boton = lista.AddBlock(botonKey).Set("ctaLabel", botones[i].texto).Set("ctaLink", Enlace(botones[i].texto, botones[i].url));
            if (i > 0) { boton.Set("variantKey", "[\"secondary\"]"); }
            boton.ApplyDefaults(_defaults.DefaultsFor(botonKey));
        }

        var picker = _media.GetOrCreatePickerValue(imagen, alt, desde, hasta, 1600, 720);
        s.AddChild(AreaDeLaSeccion, heroKey, h => h
            .Set("headingTitle", titulo)
            .Set("headingSubtitle", subtitulo)
            .Set("textBody", cuerpo)
            .Set("mediaReference", picker)
            .Set("mediaAlt", alt)
            .Set("ctaItems", lista.Build())
            .ApplyDefaults(_defaults.DefaultsFor(heroKey)));
    }

    /// <summary>Texto plano dentro de HTML: escapa lo que rompe el marcado y deja las tildes como se leen en el editor.</summary>
    private static string TextoHtml(string texto) =>
        texto.Replace("&", "&amp;", StringComparison.Ordinal)
             .Replace("<", "&lt;", StringComparison.Ordinal)
             .Replace(">", "&gt;", StringComparison.Ordinal);

    private static string Enlace(string nombre, string url) =>
        new JsonArray(new JsonObject
        {
            ["name"] = nombre, ["url"] = url, ["target"] = "", ["udi"] = null, ["icon"] = null, ["queryString"] = null,
        }).ToJsonString();

    private Guid KeyOf(string alias) => _contentTypeService.Get(alias)?.Key
        ?? throw new InvalidOperationException($"ContentType '{alias}' no existe — corré el import de uSync.");

    public sealed record Resultado(bool Ok, string Vertical, string Detalle, int SeccionesNuevas);
}

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

    /// <summary>La raíz de sitio Blogs, por su Key de uSync.</summary>
    public static readonly Guid Blogs = Guid.Parse("0d857159-703a-410c-b9f5-a0ccc2c9b3eb");

    /// <summary>La raíz de sitio Healthcare, por su Key de uSync.</summary>
    public static readonly Guid Healthcare = Guid.Parse("f289c6d5-4f6c-407f-8c35-ce7e0dec7b22");

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
        if (string.Equals(vertical, "alquiler", StringComparison.OrdinalIgnoreCase))
        {
            var hecho = RecetaAlquiler();
            return new Resultado(!hecho.Contains("sin-publicar", StringComparison.Ordinal), "alquiler", hecho, 0);
        }
        if (string.Equals(vertical, "titulo-apps", StringComparison.OrdinalIgnoreCase))
        {
            return new Resultado(true, "titulo-apps", RecetaTituloDeApps(), 0);
        }
        if (string.Equals(vertical, "inmuebles", StringComparison.OrdinalIgnoreCase))
        {
            var hecho = RecetaInmuebles();
            return new Resultado(!hecho.Contains("sin-publicar", StringComparison.Ordinal) && !hecho.Contains("esquema", StringComparison.Ordinal), "inmuebles", hecho, 0);
        }
        if (string.Equals(vertical, "propiedad-oculta", StringComparison.OrdinalIgnoreCase))
        {
            var hecho = OcultarDeLaNavegacion(PaginaPropiedad);
            return new Resultado(!hecho.Contains("fallo", StringComparison.Ordinal), "propiedad-oculta", hecho, 0);
        }
        if (string.Equals(vertical, "casos", StringComparison.OrdinalIgnoreCase))
        {
            var hecho = RecetaCasosSinHistoriasInventadas();
            return new Resultado(!hecho.Contains("fallo", StringComparison.Ordinal), "casos", hecho, 0);
        }
        if (string.Equals(vertical, "booking-reservar", StringComparison.OrdinalIgnoreCase))
        {
            var hecho = RecetaReservarDesdeLaFicha();
            return new Resultado(!hecho.Contains("fallo", StringComparison.Ordinal), "booking-reservar", hecho, 0);
        }
        if (string.Equals(vertical, "testimonios", StringComparison.OrdinalIgnoreCase))
        {
            var hecho = RecetaSinTestimoniosInventados();
            return new Resultado(!hecho.Contains("fallo", StringComparison.Ordinal), "testimonios", hecho, 0);
        }
        if (string.Equals(vertical, "promesas", StringComparison.OrdinalIgnoreCase))
        {
            return new Resultado(true, "promesas", RecetaPromesasDeLasVerticales(), 0);
        }
        if (string.Equals(vertical, "titulos-y-descripciones", StringComparison.OrdinalIgnoreCase))
        {
            return new Resultado(true, "titulos-y-descripciones", RecetaTitulosYDescripciones(), 0);
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
        new("med-provenza", "Lofts del Ciprés", "Medellín · Antioquia",
            "Lofts de diseño en la Provenza, con rooftop, coworking y café de especialidad a pasos de los restaurantes de El Poblado.",
            "Terraza en la azotea con vista abierta sobre la ciudad"),
        new("bog-zonat", "Zona T Executive Suites", "Bogotá · Cundinamarca",
            "Suites para el viajero de negocios sobre la Zona T, con salas de reuniones, gimnasio 24 horas y desayuno buffet.",
            "Recepción de hotel con muebles de madera tallada y lámparas cálidas"),
        new("eje-salento", "Finca Niebla Alta", "Salento · Quindío",
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

    /// <summary>
    /// La portada de Propiedades: el carrusel de «Inmuebles destacados en venta» y la galería de «En arriendo y para tu
    /// negocio» salen del inventario del CMS (<see cref="InmueblesDelCms"/>, D-016-C22), con la foto de cada inmueble y
    /// un enlace a su ficha en la app. Antes salían de una lista del catálogo de demostración.
    /// </summary>
    private string RecetaPropiedades(CuerpoEditable cuerpo)
    {
        static string Ficha(InmuebleDelCms i) => $"/propiedades/#/realty/inmueble/{i.Slug}";
        string? Url(InmuebleDelCms i) => FotoDelInmueble(i) is var k && k != Guid.Empty ? _media.UrlOf(k) : null;

        var carrusel = KeyOf("elementSynCarousel");
        var galeria = KeyOf("elementSynLightboxGallery");

        var ventas = InmueblesDelCms.Where(i => i.Operacion == "venta" && i.Tipo is "apartamento" or "casa").ToArray();
        var negocio = InmueblesDelCms.Where(i => i.Operacion == "arriendo" || i.Tipo == "local").ToArray();

        var diapositivas = new JsonArray(ventas.Where(i => Url(i) is not null).Select(i => (JsonNode)new JsonObject
        {
            ["imageUrl"] = Url(i), ["alt"] = i.Titulo, ["caption"] = $"{i.Titulo} · {i.Ciudad}", ["linkUrl"] = Ficha(i),
        }).ToArray());
        var fotos = new JsonArray(negocio.Where(i => Url(i) is not null).Select(i => (JsonNode)new JsonObject
        {
            ["thumbUrl"] = Url(i), ["fullUrl"] = Url(i), ["alt"] = i.Titulo, ["caption"] = $"{i.Titulo} · {i.Ciudad}",
        }).ToArray());

        var n = cuerpo.Reemplazar(carrusel, "slidesJson", diapositivas.ToJsonString());
        n += cuerpo.Reemplazar(galeria, "imagesJson", fotos.ToJsonString());
        n += cuerpo.Reemplazar(galeria, "columns", "4");
        var t1 = cuerpo.InsertarAntesDe(carrusel, UnEncabezado("Inmuebles destacados en venta"), "Inmuebles destacados en venta");
        var t2 = cuerpo.InsertarAntesDe(galeria, UnEncabezado("En arriendo y para tu negocio"), "En arriendo y para tu negocio");
        return $"carrusel:{diapositivas.Count};galeria:{fotos.Count};propiedades-cambiadas:{n};titulos:{(t1 ? 1 : 0) + (t2 ? 1 : 0)}";
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
    /// <c>propiedades.json</c>, <c>alquiler.json</c>, <c>laureles.json</c>; el que falte se salta). Cada foto va a los nodos de Media que ya
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
        if (Leer(carpeta, "alquiler.json") is { } alquiler)
        {
            var (n, sinEquipo) = FotosDeEquipos(alquiler, carpeta);
            partes.Add($"alquiler:{n}"); cambios += n; ok &= sinEquipo == 0;
            if (sinEquipo > 0) { partes.Add($"alquiler-sin-equipo:{sinEquipo}"); }
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

    /// <summary>
    /// La portada (<c>equipmentCover</c>) de cada equipo de Alquiler, por su slug (<c>producto</c> en el manifiesto
    /// <c>alquiler.json</c>), con su licencia y su crédito (C7). Idempotente: una portada con esa licencia y ese crédito
    /// no se toca.
    /// </summary>
    private (int Cambiados, int SinEquipo) FotosDeEquipos(IReadOnlyList<FotoImportada> fotos, string carpeta)
    {
        var plataforma = _contentService.GetById(RaizDeLaPlataforma);
        var raiz = plataforma is null ? null : _contentService.GetPagedChildren(plataforma.Id, 0, 100, out _)
            .FirstOrDefault(c => c.ContentType.Alias == "siteRoot" && string.Equals(c.GetCultureName(Cultura) ?? c.Name, NombreDeAlquiler, StringComparison.Ordinal));
        if (raiz is null) { return (0, fotos.Count); }
        var equipos = _contentService.GetPagedChildren(raiz.Id, 0, 200, out _).Where(c => c.ContentType.Alias == "equipmentPage").ToList();
        int cambiados = 0, sinEquipo = 0;
        foreach (var foto in fotos.Where(f => f.Producto is not null && !string.IsNullOrWhiteSpace(f.Archivo)))
        {
            var pagina = equipos.FirstOrDefault(c => string.Equals(c.GetValue<string>("equipmentSlug"), foto.Producto, StringComparison.Ordinal));
            if (pagina is null) { sinEquipo++; continue; }
            var ruta = Path.Combine(carpeta, foto.Archivo);
            var nombre = $"equipo {foto.Producto}";
            var cambio = false;
            var clave = _media.KeyByName(nombre);
            if (clave is null)
            {
                Poner(pagina, "equipmentCover", _media.GetOrCreatePhotoPickerValue(nombre, foto.Alt, ruta, foto.Licencia));
                clave = _media.KeyByName(nombre);
                cambio = true;
            }
            if (clave is { } k)
            {
                cambio |= _media.ReplacePhoto(k, ruta, nombre, foto.Alt, foto.Licencia, foto.Credito);
            }
            if (!cambio) { continue; }
            _contentService.SaveAndPublish(pagina, pagina.ContentType.VariesByCulture() ? new[] { Cultura } : Array.Empty<string>());
            cambiados++;
        }
        return (cambiados, sinEquipo);
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

    /// <summary>La página «Apps» de Synergos, por su Key de uSync.</summary>
    private static readonly Guid PaginaApps = Guid.Parse("1c9e334b-08ac-4d20-91cf-ec3aa8fd97b8");

    /// <summary>
    /// «Apps — Apps SynergosLabs» repetía la palabra, y la página no tenía descripción (016, junto a V2).
    /// </summary>
    private string RecetaTituloDeApps()
    {
        var apps = _contentService.GetById(PaginaApps);
        if (apps is null) { return "sin-pagina"; }
        var cambio = false;
        foreach (var (alias, valor) in new[]
                 {
                     ("seoTitle", "Apps — SynergosLabs"),
                     ("seoDescription", "Las apps de SynergosLabs por dentro: qué hace cada una, cómo se ve y dónde probarla."),
                 })
        {
            if (apps.HasProperty(alias) && apps.GetValue<string>(alias, Cultura) != valor) { apps.SetValue(alias, valor, Cultura); cambio = true; }
        }
        if (cambio) { _contentService.SaveAndPublish(apps, new[] { Cultura }); }

        // La portada de Educación no tenía seoTitle y caía al nombre del nodo, «Educacion» (4.ª vuelta, A-1).
        var educacion = _contentService.GetById(Educacion);
        var cambioEducacion = false;
        if (educacion is not null && educacion.HasProperty("seoTitle") && educacion.GetValue<string>("seoTitle", Cultura) != "Educación")
        {
            educacion.SetValue("seoTitle", "Educación", Cultura);
            _contentService.SaveAndPublish(educacion, new[] { Cultura });
            cambioEducacion = true;
        }
        return $"apps:{(cambio ? 1 : 0)};educacion:{(cambioEducacion ? 1 : 0)}";
    }

    /// <summary>Las páginas Cursos (Educación) y Ofertas (Tienda), por su Key de uSync.</summary>
    private static readonly Guid PaginaCursos = Guid.Parse("21100675-769e-42d3-95c0-2fa903d2c4d9");
    private static readonly Guid PaginaOfertas = Guid.Parse("8166a0f0-8f18-41c1-8d23-d9ba36289749");

    /// <summary>
    /// La descripción de las páginas del hub que no tenían ninguna (016, 4.ª vuelta, C9), por su Key de uSync. La de la
    /// raíz es también la frase de su pie. Las de las fichas de Apps son las que el editor dejó en el lanzador de
    /// Soluciones: copy ya revisado, que no promete lo que la app no hace.
    /// </summary>
    internal static readonly (Guid Pagina, string Descripcion)[] DescripcionesDelHub =
    [
        (Synergos, "Un motor, muchas apps reales: tienda, reservas, eventos, cursos y más, sobre la misma base."),
        (Casos, "Negocios de verdad sobre SynergosLabs: qué necesitaban, qué montaron y cómo les va."),
        (Guid.Parse("08b80e68-6afb-4790-90e3-6ddf0d23a1e5"), "De la idea a producción en cuatro pasos: modelas el contenido, compones las páginas, conectas tus servicios y publicas."),
        (Guid.Parse("1aa79edd-65be-4931-8056-be6df9c2726f"), "Planes de SynergosLabs que crecen con tu negocio: qué incluye cada uno y cuándo conviene pasar al siguiente."),
        (Guid.Parse("ba82f156-7b18-4410-8815-37687056f789"), "Un motor, muchos productos: los componentes y las apps que puedes combinar para armar el tuyo."),
        (Guid.Parse("c052e893-16d9-4299-a9a4-9de162775522"), "Una solución para cada tipo de negocio: tienda, reservas, eventos, educación, inmobiliaria y más, listas para arrancar."),
        (Guid.Parse("48b2f5fb-91de-4b4d-ba2a-d92a714b616b"), "Del catálogo al checkout: e-commerce completo con precios es-CO reales."),
        (Guid.Parse("64c93cd1-d6c2-4588-b54f-84679efe7bc6"), "Reservas y citas en línea sobre el motor transaccional: cada solicitud queda registrada."),
        (Guid.Parse("62c68173-3e43-42fe-8390-6e76799e0059"), "Cartelera, tickets con retención de cupo y agenda viva del evento."),
        (Guid.Parse("a9f996c2-5937-4598-a0ff-76516171c974"), "Portal inmobiliario premium: fichas inmersivas y visitas sin paso de pago."),
        (Guid.Parse("dcd02568-677d-4ebe-bc37-b7c677095b63"), "Una academia online: catálogo, temario interactivo e inscripciones."),
        (Guid.Parse("89a622e1-d8c4-4933-9815-635821f0abc8"), "La cara social del motor: feed editorial, autores y comentarios en vivo."),
        (Guid.Parse("35c999e4-7ef5-49d0-bc08-3650b7c0807e"), "Historia clínica, agenda sin sobrecupos y acceso auditado por consentimiento."),
        (Guid.Parse("eb0c7007-5722-4e34-83ef-dbae580d0bc5"), "Alojamiento de punta a punta: catálogo, tarifas y asistente de reserva."),
        (Guid.Parse("7a299138-4e0b-4e9c-968f-a1e92be084f0"), "Trámites en lenguaje claro con carpeta ciudadana y seguimiento por etapas."),
        (Guid.Parse("29612282-8a76-4d33-a3aa-aacb8ddfc40b"), "Equipos por días con la cotización por tramos y la garantía retenida, no cobrada."),
    ];

    /// <summary>
    /// C8 y C9 de la 4.ª vuelta (016): Educación se llama con tilde donde el sitio dice su nombre (pie, vuelta a la
    /// plataforma, og:site_name y el sufijo de sus títulos), Ofertas lleva el sufijo de su sitio, y las páginas del hub
    /// sin descripción reciben la suya. Una descripción que el editor ya escribió NO se pisa. Idempotente.
    /// </summary>
    private string RecetaTitulosYDescripciones()
    {
        var cambios = new List<string>();

        void Poner(Guid clave, string alias, string valor, bool soloSiVacio, string etiqueta)
        {
            var pagina = _contentService.GetById(clave);
            if (pagina is null || !pagina.HasProperty(alias)) { cambios.Add($"{etiqueta}:sin-pagina"); return; }
            var cultura = pagina.Properties[alias]!.PropertyType.VariesByCulture() ? Cultura : null;
            var actual = pagina.GetValue<string>(alias, cultura);
            if (actual == valor || (soloSiVacio && !string.IsNullOrWhiteSpace(actual))) { return; }
            pagina.SetValue(alias, valor, cultura);
            var r = _contentService.SaveAndPublish(pagina, new[] { cultura ?? Cultura });
            cambios.Add(r.Success ? etiqueta : $"{etiqueta}:fallo:{r.Result}");
        }

        Poner(Educacion, "siteDisplayName", "Educación", soloSiVacio: false, "educacion");
        Poner(PaginaCursos, "seoTitle", "Cursos — Educación", soloSiVacio: false, "cursos");
        Poner(PaginaOfertas, "seoTitle", "Ofertas — Tienda", soloSiVacio: false, "ofertas");
        foreach (var (pagina, descripcion) in DescripcionesDelHub)
        {
            Poner(pagina, "seoDescription", descripcion, soloSiVacio: true, $"desc:{pagina.ToString()[..8]}");
        }
        return cambios.Count == 0 ? "nada-que-hacer" : string.Join(";", cambios);
    }

    /// <summary>«{categoría} — {sitio} SynergosLabs», con el nombre que el sitio muestra (siteDisplayName).</summary>
    private string? TituloDeCategoria(IContent categoria)
    {
        var sitio = _contentService.GetById(categoria.ParentId);
        var nombreDelSitio = sitio is not null && sitio.HasProperty("siteDisplayName")
            ? sitio.GetValue<string>("siteDisplayName", sitio.Properties["siteDisplayName"]!.PropertyType.VariesByCulture() ? Cultura : null)
            : null;
        var nombre = categoria.HasProperty("categoryName") ? categoria.GetValue<string>("categoryName", Cultura) : null;
        nombre = string.IsNullOrWhiteSpace(nombre) ? categoria.GetCultureName(Cultura) ?? categoria.Name : nombre;
        return string.IsNullOrWhiteSpace(nombreDelSitio) || string.IsNullOrWhiteSpace(nombre)
            ? null
            : $"{nombre} — {nombreDelSitio.Trim()} SynergosLabs";
    }

    private string RecetaEntradasDeCategorias()
    {
        int cambiadas = 0, faltan = 0;
        foreach (var (key, entrada) in EntradasDeCategorias)
        {
            var pagina = _contentService.GetById(key);
            if (pagina is null || !pagina.HasProperty("categoryDescription")) { faltan++; continue; }
            var cambio = false;
            if (pagina.GetValue<string>("categoryDescription", Cultura) != entrada)
            {
                pagina.SetValue("categoryDescription", entrada, Cultura);
                cambio = true;
            }
            // El título nombra SU sitio (016, V7): el sembrador puso «Casas — Tienda SynergosLabs» a todas.
            if (TituloDeCategoria(pagina) is { } titulo && pagina.HasProperty("seoTitle")
                && pagina.GetValue<string>("seoTitle", Cultura) != titulo)
            {
                pagina.SetValue("seoTitle", titulo, Cultura);
                cambio = true;
            }
            if (!cambio) { continue; }
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

    // ───────────────────────────── Alquiler de equipos (4.ª vuelta, C7) ─────────────────────────────

    /// <summary>El platformRoot del que cuelgan las raíces de las verticales.</summary>
    private static readonly Guid RaizDeLaPlataforma = Guid.Parse("0461c64c-63a9-4bd1-ad59-a247253eb107");

    private const string NombreDeAlquiler = "Alquiler";

    /// <summary>Un equipo del catálogo de Alquiler, tal como lo autora el editor (<c>equipmentPage</c>).</summary>
    private sealed record EquipoAutorado(
        string Slug, string Nombre, string Categoria, string Resumen, string Descripcion, int Unidades,
        int Diaria, int Garantia, int MinDias, int MaxDias, string[] Incluye, string[] Requiere,
        (string Rotulo, string Valor)[] Ficha, (string Codigo, string Rotulo, int DesdeDias, int PorDia, string Texto)[] Tarifas);

    /// <summary>
    /// Ocho equipos para obra, eventos y oficina, con precios en pesos colombianos. Las tres primeras fichas son las de
    /// la semilla, ahora autoradas; el catálogo del sitio las lee del CMS (<c>Synergos:Catalog:Sources:Alquiler = cms</c>).
    /// </summary>
    private static readonly EquipoAutorado[] EquiposDeAlquiler =
    [
        new("andamio-multidireccional-2m", "Andamio multidireccional 2 m", "Andamios",
            "Cuerpo de andamio para trabajo en altura hasta 2 metros.",
            "Estructura modular de acero galvanizado con plataforma antideslizante y barandas. Se entrega armada y se recoge desarmada.",
            12, 38_000, 250_000, 1, 30,
            ["Plataforma", "Dos barandas", "Rodachines con freno"], ["Arnés propio", "Cédula del responsable"],
            [("Altura útil", "2,0 m"), ("Carga máxima", "250 kg")],
            [("semana", "Semana completa", 7, 30_000, "A partir de 7 días."), ("mes", "Mes", 28, 24_000, "A partir de 28 días.")]),
        new("escalera-tijera-3m", "Escalera de tijera de 3 m", "Andamios",
            "Escalera de aluminio de doble acceso para trabajos de mantenimiento.",
            "Escalera de tijera en aluminio con peldaños antideslizantes y tensor de seguridad. Liviana para moverla entre pisos.",
            8, 18_000, 120_000, 1, 30,
            ["Tensor de seguridad"], ["Cédula del responsable"],
            [("Altura", "3,0 m"), ("Carga máxima", "150 kg"), ("Peso", "9 kg")],
            [("semana", "Semana completa", 7, 14_000, "A partir de 7 días.")]),
        new("planta-electrica-5kva", "Planta eléctrica 5 kVA", "Energía",
            "Generador a gasolina para obra o evento, arranque manual.",
            "Planta monofásica de 5 kVA con tanque de 25 litros y autonomía aproximada de ocho horas al 60 % de carga.",
            3, 145_000, 900_000, 1, 15,
            ["Cable de 10 m", "Embudo de carga"], ["Devolver con el tanque lleno"],
            [("Potencia", "5 kVA"), ("Combustible", "Gasolina corriente"), ("Peso", "78 kg")],
            [("semana", "Semana completa", 7, 115_000, string.Empty)]),
        new("reflector-led-200w", "Reflector LED de 200 W con trípode", "Energía",
            "Iluminación de obra o de evento al aire libre, con trípode regulable.",
            "Reflector LED resistente al agua con trípode de hasta 1,8 m. Se conecta a una toma corriente o a una planta.",
            6, 35_000, 150_000, 1, 30,
            ["Trípode", "Cable de 5 m"], [],
            [("Potencia", "200 W"), ("Protección", "IP65"), ("Altura del trípode", "Hasta 1,8 m")],
            [("semana", "Semana completa", 7, 28_000, "A partir de 7 días.")]),
        new("kit-sonido-500w", "Kit de sonido 500 W", "Audiovisual",
            "Dos cabinas activas, consola de cuatro canales y dos micrófonos.",
            "Equipo para reuniones y eventos de hasta 150 personas en interior.",
            5, 220_000, 600_000, 1, 10,
            ["Dos trípodes", "Cableado", "Dos micrófonos alámbricos"], [],
            [("Potencia total", "500 W"), ("Canales de la consola", "4")],
            []),
        new("video-beam-4000-lumenes", "Video beam de 4.000 lúmenes con pantalla", "Audiovisual",
            "Proyector y pantalla de trípode para presentaciones y capacitaciones.",
            "Proyector de 4.000 lúmenes con entrada HDMI y pantalla de trípode de 2 metros. Para salas con luz ambiente.",
            4, 120_000, 500_000, 1, 10,
            ["Pantalla de trípode de 2 m", "Cable HDMI de 5 m", "Control remoto"], ["Cédula del responsable"],
            [("Brillo", "4.000 lúmenes"), ("Entradas", "HDMI y VGA"), ("Pantalla", "2,0 m de ancho")],
            []),
        new("rotomartillo-800w", "Rotomartillo de 800 W", "Herramienta",
            "Para perforar concreto y ladrillo, con función de cincelado.",
            "Rotomartillo con portabrocas SDS Plus y tres modos: taladro, percusión y cincel. Se entrega con su maletín.",
            6, 45_000, 300_000, 1, 30,
            ["Maletín", "Tres brocas para concreto", "Un cincel plano"], ["Gafas de seguridad propias"],
            [("Potencia", "800 W"), ("Portabrocas", "SDS Plus"), ("Energía de impacto", "2,8 J")],
            [("semana", "Semana completa", 7, 36_000, "A partir de 7 días.")]),
        new("mezcladora-concreto-1-bulto", "Mezcladora de concreto de un bulto", "Construcción",
            "Trompo eléctrico para mezclas de un bulto de cemento por tanda.",
            "Mezcladora eléctrica con tambor de 350 litros sobre ruedas. Mezcla un bulto de cemento por tanda.",
            3, 95_000, 700_000, 1, 30,
            [], ["Toma de 110 V a menos de 20 m", "Devolverla lavada"],
            [("Tambor", "350 L"), ("Motor", "1,5 HP"), ("Capacidad", "Un bulto por tanda")],
            [("semana", "Semana completa", 7, 80_000, "A partir de 7 días."), ("mes", "Mes", 28, 65_000, "A partir de 28 días.")]),
    ];

    /// <summary>
    /// La raíz «Alquiler» compuesta como las demás (C7): su tema, su cabecera y su pie, y una portada con el bloque de
    /// la app, cómo funciona, preguntas y un cierre. Los equipos son <c>equipmentPage</c> autorados bajo la raíz, si el
    /// esquema los admite ahí; si no, la receta lo dice y no los crea saltándose la estructura.
    /// </summary>
    private string RecetaAlquiler()
    {
        var partes = new List<string>();
        var plataforma = _contentService.GetById(RaizDeLaPlataforma) ?? throw new InvalidOperationException("No existe el platformRoot.");
        var raiz = _contentService.GetPagedChildren(plataforma.Id, 0, 100, out _)
            .FirstOrDefault(c => c.ContentType.Alias == "siteRoot" && string.Equals(c.GetCultureName(Cultura) ?? c.Name, NombreDeAlquiler, StringComparison.Ordinal));
        var nueva = raiz is null;
        raiz ??= _contentService.Create(NombreDeAlquiler, plataforma.Id, "siteRoot");
        raiz.SetCultureName(NombreDeAlquiler, Cultura);
        var cambio = nueva;
        foreach (var (alias, valor) in new (string, object)[]
                 {
                     ("siteDisplayName", NombreDeAlquiler),
                     ("brandKey", "alquiler"),
                     ("brandDisplayName", "SynergosLabs Alquiler"),
                     ("pageThemeVariant", "[\"dark\"]"),
                     ("canonicalHostname", "alquiler.synergos.local"),
                     ("seoDescription", "Andamios, energía, sonido y herramienta por días, con la garantía retenida y no cobrada."),
                 })
        {
            cambio |= Poner(raiz, alias, valor);
        }
        if (nueva)
        {
            raiz.SetValue(Cuerpo, PortadaDeAlquiler(), Cultura);
        }
        if (cambio)
        {
            var publicada = _contentService.SaveAndPublish(raiz, new[] { Cultura });
            if (!publicada.Success)
            {
                return $"raiz-sin-publicar:{publicada.Result};invalidas:{string.Join(",", publicada.InvalidProperties?.Select(p => p.Alias) ?? [])}";
            }
        }
        partes.Add(nueva ? "raiz:creada" : cambio ? "raiz:actualizada" : "raiz:igual");

        var admite = _contentTypeService.Get("siteRoot")?.AllowedContentTypes?
            .Any(t => string.Equals(t.Alias, "equipmentPage", StringComparison.Ordinal)) == true;
        if (!admite)
        {
            partes.Add("equipos:esquema-no-los-admite-bajo-siteRoot");
            return string.Join(";", partes);
        }

        int creados = 0, cambiados = 0;
        var existentes = _contentService.GetPagedChildren(raiz.Id, 0, 200, out _).Where(c => c.ContentType.Alias == "equipmentPage").ToList();
        foreach (var e in EquiposDeAlquiler)
        {
            var pagina = existentes.FirstOrDefault(c => string.Equals(c.GetValue<string>("equipmentSlug"), e.Slug, StringComparison.Ordinal));
            var esNueva = pagina is null;
            pagina ??= _contentService.Create(e.Nombre, raiz.Id, "equipmentPage");
            if (pagina.ContentType.VariesByCulture()) { pagina.SetCultureName(e.Nombre, Cultura); }
            var c = esNueva;
            c |= Poner(pagina, "equipmentSlug", e.Slug);
            c |= Poner(pagina, "equipmentName", e.Nombre);
            c |= Poner(pagina, "equipmentCategory", e.Categoria);
            c |= Poner(pagina, "equipmentSummary", e.Resumen);
            c |= Poner(pagina, "equipmentDescription", e.Descripcion);
            c |= Poner(pagina, "equipmentUnits", e.Unidades);
            c |= Poner(pagina, "equipmentDailyRate", e.Diaria);
            c |= Poner(pagina, "equipmentDepositAmount", e.Garantia);
            c |= Poner(pagina, "equipmentMinDays", e.MinDias);
            c |= Poner(pagina, "equipmentMaxDays", e.MaxDias);
            c |= Poner(pagina, "equipmentIncludes", string.Join("\n", e.Incluye));
            c |= Poner(pagina, "equipmentRequirements", string.Join("\n", e.Requiere));
            c |= Poner(pagina, "hideFromMainMenu", 1);
            if (esNueva)
            {
                var spec = KeyOf("elementEquipmentSpec");
                var ficha = new BlockListJsonBuilder();
                foreach (var (rotulo, valor) in e.Ficha)
                {
                    ficha.AddBlock(spec).Set("specLabel", rotulo).Set("specValue", valor).ApplyDefaults(_defaults.DefaultsFor(spec));
                }
                Poner(pagina, "equipmentSpecs", ficha.Build());
                var rate = KeyOf("elementEquipmentRate");
                var tarifas = new BlockListJsonBuilder();
                foreach (var (codigo, rotulo, desde, porDia, texto) in e.Tarifas)
                {
                    tarifas.AddBlock(rate).Set("rateCode", codigo).Set("rateLabel", rotulo).Set("rateMinDays", desde)
                        .Set("rateAmount", porDia).Set("rateDescription", texto).ApplyDefaults(_defaults.DefaultsFor(rate));
                }
                Poner(pagina, "equipmentRates", tarifas.Build());
            }
            if (!c) { continue; }
            var r = _contentService.SaveAndPublish(pagina, pagina.ContentType.VariesByCulture() ? new[] { Cultura } : Array.Empty<string>());
            if (!r.Success)
            {
                partes.Add($"equipo-sin-publicar:{e.Slug}:{r.Result}:{string.Join(",", r.InvalidProperties?.Select(p => p.Alias) ?? [])}");
                continue;
            }
            if (esNueva) { creados++; } else { cambiados++; }
        }
        partes.Add($"equipos-creados:{creados};equipos-cambiados:{cambiados}");
        return string.Join(";", partes);
    }

    /// <summary>Pone un valor respetando si la propiedad varía por cultura. Devuelve si cambió.</summary>
    private static bool Poner(IContent nodo, string alias, object valor)
    {
        if (!nodo.HasProperty(alias)) { return false; }
        var porCultura = nodo.Properties[alias]!.PropertyType.VariesByCulture();
        var actual = porCultura ? nodo.GetValue(alias, Cultura) : nodo.GetValue(alias);
        // Una lista vacía se guarda como nulo: «nada» y «""» son lo mismo, o la receta cambiaría en cada pasada.
        if (string.Equals(actual?.ToString() ?? string.Empty, valor.ToString() ?? string.Empty, StringComparison.Ordinal)) { return false; }
        // Un decimal vuelve con otra escala («3800000.0» frente a 3800000): se compara el valor, no el texto (016, F1).
        if (valor is decimal nuevo && actual is not null
            && decimal.TryParse(Convert.ToString(actual, System.Globalization.CultureInfo.InvariantCulture), System.Globalization.NumberStyles.Number,
                System.Globalization.CultureInfo.InvariantCulture, out var guardado) && guardado == nuevo) { return false; }
        if (porCultura) { nodo.SetValue(alias, valor, Cultura); } else { nodo.SetValue(alias, valor); }
        return true;
    }

    /// <summary>La portada de Alquiler: el bloque de la app, cómo funciona, preguntas y un cierre.</summary>
    private string PortadaDeAlquiler()
    {
        var b = new BlockGridJsonBuilder();
        var app = KeyOf("elementSynAlquiler");
        Seccion(b, null).AddChild(AreaDeLaSeccion, app, a => a
            .Set("heading", "Alquiler de equipos")
            .Set("subheading", "Andamios, energía, sonido y herramienta por días. La garantía se retiene, no se cobra.")
            .ApplyDefaults(_defaults.DefaultsFor(app)));
        Rejilla(b, "surface", "Cómo funciona", 4,
            ("Elige y calcula", "Escoge el equipo, las fechas y las unidades, y ve el valor antes de reservar."),
            ("Reserva y paga", "Apartamos tus unidades unos minutos; al confirmar pagas el alquiler y se retiene la garantía."),
            ("Tu contrato", "En «Mis alquileres» queda tu contrato sellado, con la fecha hasta la que puedes cancelar."),
            ("Devuelve", "Al entregar el equipo declaras la devolución; el personal la revisa y libera la garantía."));
        Preguntas(b, "Preguntas frecuentes",
            ("¿La garantía se cobra?", "No. Se retiene en tu medio de pago y se libera cuando el personal confirma la devolución. Si el equipo vuelve con daño, el valor del daño se toma de la garantía."),
            ("¿Necesito una cuenta?", "Sí, para alquilar: tus contratos van atados a tu cuenta, no a este navegador."),
            ("¿Por cuántos días puedo alquilar?", "Cada equipo dice su mínimo y su máximo. Hay un tope general porque la retención de la garantía tiene una vigencia limitada."),
            ("¿Puedo cancelar?", "Sí, hasta la fecha que indica tu contrato en «Mis alquileres»."));
        Cierre(b, "¿Ya sabes qué necesitas?", "Elige el equipo y calcula el valor de tus fechas.", "Ver equipos", "/alquiler/");
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
        new("dev-clean-architecture", "Arquitectura Limpia en .NET", "Desarrollo · Intermedio · $ 89.000",
            "Las capas, el grafo de dependencias y los seams stub-first, con sus pruebas.", "clean-architecture.jpg",
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
        new("trm-cedula", "Duplicado de cédula de ciudadanía", "Ventanilla de Identificación (ejemplo) · tarifa de ejemplo $ 65.500 · unos 15 días hábiles",
            "Solicite el duplicado de su cédula por deterioro, pérdida o corrección de datos."),
        new("trm-pasaporte", "Expedición de pasaporte", "Ventanilla de Pasaportes (ejemplo) · tarifa de ejemplo $ 189.000 · unos 8 días hábiles",
            "Solicite la expedición de su pasaporte para viajar al exterior."),
        new("trm-licencia-conduccion", "Renovación de licencia de conducción", "Ventanilla de Tránsito (ejemplo) · tarifa de ejemplo $ 95.000 · unos 5 días hábiles",
            "Renueve su licencia vigente o vencida. Necesita el examen médico aprobado."),
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

        Rejilla(b, "surface", "Ventanillas de ejemplo que atienden aquí", 4,
            ("Ventanilla de Identificación (ejemplo)", "Duplicado de la cédula de ciudadanía."),
            ("Ventanilla de Pasaportes (ejemplo)", "Expedición del pasaporte."),
            ("Ventanilla de Tránsito (ejemplo)", "Renovación de la licencia de conducción."),
            ("Ventanilla de Salud (ejemplo)", "Afiliación al régimen subsidiado de salud."),
            ("Ventanilla de Atención al Ciudadano (ejemplo)", "Certificado de residencia."),
            ("Ventanilla de Empresas (ejemplo)", "Registro mercantil de su empresa."),
            ("Ventanilla de Vivienda (ejemplo)", "Solicitud del subsidio de vivienda."));

        Preguntas(b, "Preguntas frecuentes",
            ("¿Qué es el número de radicado?", "Es el código que recibe su solicitud al enviarla. Con él la encuentra en «Mis solicitudes» y la identifica la entidad."),
            ("¿Cuánto tarda un trámite?", "Cada trámite dice su plazo en días hábiles. Se cuentan desde que radica; los fines de semana y festivos no cuentan."),
            ("¿Cómo pago la tasa?", "Si el trámite tiene tasa, la paga en línea al radicar; en esta demostración es una tarifa de ejemplo. Los trámites gratuitos lo dicen en su ficha."),
            ("¿Qué pasa si falta un documento?", "La entidad le pide subsanar y le avisamos en «Notificaciones». Completa lo que falta y el trámite sigue."),
            ("¿Se realizan cobros reales?", "No. Este es un sitio de demostración: las entidades son de ejemplo, y las solicitudes y los pagos no tienen efecto ante ninguna entidad real."));

        Cierre(b, "¿Ya radicó?", "Consulte en qué etapa va su expediente y lo que le ha pedido la entidad.",
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
    /// 4.ª vuelta, tanda 3 (016): Booking y Eventos prometían lo que la app no hace, medido contra la app.
    /// <list type="bullet">
    /// <item>Booking: «Confirmación al instante», y su propia portada dice que el negocio te contacta para confirmar; y
    /// «crear cuenta te deja gestionar tus reservas», sin una vista de reservas en el sitio.</item>
    /// <item>Booking, D-016-C12: «Planes para tu negocio» y «Gestión centralizada… para tu equipo» le hablaban al negocio
    /// en una portada para quien pide una cita; se desconectan (SUPERSET). Y, aprobado después, «Tu agenda abierta a toda
    /// hora» y «Empieza a recibir reservas hoy» pasan a la voz de quien reserva, y los testimonios de cargos de negocio
    /// salen de la página.</item>
    /// <item>Educación, D-016-C15: «Planes de acceso» (curso suelto a $89.000, membresía con cohortes en vivo) no es lo
    /// que cobra ni ofrece la app; se desconecta, y la FAQ deja de prometer cohortes.</item>
    /// <item>Educación y Propiedades (tanda 8): «cada curso termina en un proyecto», «rutas de aprendizaje», «instructores
    /// de la industria» y «fotos reales» pasan a lo que la ficha sí trae.</item>
    /// <item>Eventos: «recibe tu acceso por correo al instante»: ningún correo sale con la entrada; la compra pide
    /// sesión y las entradas, con su QR firmado, quedan en «Mis tickets», desde donde se transfieren.</item>
    /// </list>
    /// Idempotente.
    /// </summary>
    private string RecetaPromesasDeLasVerticales()
    {
        var partes = new List<string>();

        void Corregir(Guid raiz, string etiqueta, Func<CuerpoEditable, int> cambiar)
        {
            var pagina = _contentService.GetById(raiz);
            if (pagina is null) { partes.Add($"{etiqueta}:sin-pagina"); return; }
            var cuerpo = new CuerpoEditable(pagina.GetValue<string>(Cuerpo, Cultura));
            cambiar(cuerpo);
            if (!cuerpo.Cambio) { return; }
            pagina.SetValue(Cuerpo, cuerpo.Json, Cultura);
            var r = _contentService.SaveAndPublish(pagina, new[] { Cultura });
            partes.Add(r.Success ? etiqueta : $"{etiqueta}:fallo:{r.Result}");
        }

        var rejilla = KeyOf("elementSynFeatureGrid");
        var preguntas = KeyOf("elementSynFaqSection");

        Corregir(Booking, "booking", cuerpo =>
            CambiarEnLista(cuerpo, rejilla, "itemsJson", "heading", "Confirmación al instante",
                ("heading", "Tu solicitud, al instante"),
                ("body", "Sin llamadas — Tu solicitud le llega al negocio en cuanto la envías y te contactan para confirmarla."))
            + CambiarEnLista(cuerpo, preguntas, "itemsJson", "question", "¿Necesito crear una cuenta para reservar?",
                ("answer", "No. Eliges el servicio, la fecha y la hora, dejas tus datos y el negocio te escribe para confirmar."))
            // D-016-C12: la portada habla a quien pide una cita, no al negocio. «Planes para tu negocio» sale de la página
            // (desconectada: la tabla y su tipo siguen en el esquema) y «Gestión centralizada… para tu equipo» sale de la
            // rejilla, que queda en dos columnas para no dejar un hueco.
            + cuerpo.DesconectarSeccionesCon(KeyOf("elementPricingTable"), CuerpoEditable.Donde("tableHeading", "Planes para tu negocio"))
            + cuerpo.TransformarLista(rejilla, "itemsJson",
                lista => new JsonArray(lista
                    .Where(i => i?["heading"]?.GetValue<string>() != "Gestión centralizada")
                    .Select(i => i!.DeepClone()).ToArray()),
                CuerpoEditable.Donde("headingText", "Por qué reservar con nosotros"))
            + cuerpo.Reemplazar(rejilla, "columns", "2", CuerpoEditable.Donde("headingText", "Por qué reservar con nosotros"))
            // Lo aprobado tras D-016-C12: lo que quedaba en voz de negocio pasa a la de quien reserva, y los testimonios
            // —todos de cargos de un negocio («Gerente de operaciones»), ninguno de alguien que reservó— salen de la
            // página: la sección se desconecta y el tipo sigue en el esquema (SUPERSET), como en la Tienda.
            + CambiarEnLista(cuerpo, rejilla, "itemsJson", "heading", "Disponibilidad 24/7",
                ("heading", "Pide a cualquier hora"),
                ("body", "Cuando quieras — Envías tu solicitud de día o de noche, sin depender del horario de atención."))
            + cuerpo.DesconectarSeccionesCon(KeyOf("elementSynTestimonialCarousel"))
            + cuerpo.Reemplazar(KeyOf("elementCompCtaBanner"), "headingSubtitle",
                "Elige el servicio en el catálogo y pide el día y la hora que te sirven.",
                CuerpoEditable.Donde("headingTitle", "Empieza a recibir reservas hoy"))
            + cuerpo.Reemplazar(KeyOf("elementCompCtaBanner"), "headingTitle", "Reserva tu primera cita",
                CuerpoEditable.Donde("headingTitle", "Empieza a recibir reservas hoy")));

        Corregir(Eventos, "eventos", cuerpo =>
            CambiarEnLista(cuerpo, preguntas, "itemsJson", "question", "¿Cómo consigo mi entrada?",
                ("answer", "Elige el evento, inicia sesión y completa la compra: tus entradas, con su código QR, quedan en «Mis tickets»."))
            + CambiarEnLista(cuerpo, preguntas, "itemsJson", "question", "¿Puedo transferir mi entrada?",
                ("answer", "Sí. En «Mis tickets» eliges la entrada y la transfieres a otra persona con su correo.")));

        // 4.ª vuelta, tanda 9: la portada de Viajes prometía «vuelos, hoteles y paquetes»; la app ofrece estadías, vuelos y
        // autos en un solo carrito, y no tiene paquetes.
        Corregir(Viajes, "viajes", cuerpo =>
            cuerpo.Reemplazar(KeyOf("elementSynTravelShell"), "subheading",
                "Estadías, vuelos y autos en un solo carrito. Busca, compara y reserva en minutos.",
                CuerpoEditable.Donde("subheading", "Vuelos, hoteles y paquetes en un solo lugar. Busca, compara y reserva en minutos.")));

        // D-016-C15: «Planes de acceso» vendía un «Curso suelto» a $89.000 (el catálogo cobra cada curso a su precio) y una
        // membresía con cohortes en vivo que la app no tiene: la sección se desconecta (SUPERSET), y la FAQ deja de
        // prometer cohortes.
        Corregir(Educacion, "educacion", cuerpo =>
            cuerpo.DesconectarSeccionesCon(KeyOf("elementPricingTable"), CuerpoEditable.Donde("tableHeading", "Planes de acceso"))
            + CambiarEnLista(cuerpo, preguntas, "itemsJson", "question", "¿Puedo estudiar a mi ritmo?",
                ("answer", "Sí. El contenido queda disponible y avanzas cuando puedas."))
            // 4.ª vuelta, tanda 8: ningún curso termina en un proyecto (sus lecciones son de contenido), no hay rutas de
            // aprendizaje (la API las devuelve vacías) y los instructores del catálogo son personajes de demostración. Se
            // dice lo que la ficha sí trae: lo que sabrás hacer, quién enseña, el nivel y el certificado de cada curso.
            + CambiarEnLista(cuerpo, rejilla, "itemsJson", "heading", "Proyectos reales",
                ("heading", "Lo que sabrás hacer"),
                ("body", "Claro desde el principio — Cada curso dice qué sabrás hacer al terminar, y sus lecciones van a eso."))
            + CambiarEnLista(cuerpo, rejilla, "itemsJson", "heading", "Instructores expertos",
                ("heading", "Cada curso, su instructor"),
                ("body", "Antes de inscribirte — La ficha te dice quién lo enseña, su nivel y cuánto dura."))
            + CambiarEnLista(cuerpo, rejilla, "itemsJson", "body", "Avala tu progreso — Obtén un certificado verificable al completar cada ruta de aprendizaje.",
                ("body", "Avala tu progreso — Obtén un certificado verificable al completar cada curso."))
            + CambiarEnLista(cuerpo, preguntas, "itemsJson", "question", "¿Necesito conocimientos previos?",
                ("answer", "Cada curso indica su nivel: hay cursos para empezar desde cero y cursos avanzados.")));

        // 4.ª vuelta, tanda 8: «fotos reales» no lo son (son fotos de stock con su crédito) y la revisión no se ve en la app.
        // Se dice lo que cada ficha sí trae.
        Corregir(Propiedades, "propiedades", cuerpo =>
            CambiarEnLista(cuerpo, rejilla, "itemsJson", "heading", "Listado curado",
                ("heading", "Fichas completas"),
                ("body", "Todo a la vista — Precio, área, habitaciones, baños, estrato y ubicación en el mapa, en cada inmueble.")));

        return partes.Count == 0 ? "nada-que-hacer" : string.Join(";", partes);
    }

    /// <summary>Las categorías de Propiedades (<c>productCategoryPage</c>), por su Key de uSync.</summary>
    private static readonly Guid CategoriaApartamentos = Guid.Parse("10195c7f-440a-46c1-82b1-284ccc890206");
    private static readonly Guid CategoriaCasas = Guid.Parse("6e68d98e-f370-4d81-bc08-ea7b47a685f6");
    private static readonly Guid CategoriaComercial = Guid.Parse("abb93d6b-414f-44b7-91e0-669b93c90143");

    /// <summary>
    /// Un inmueble del inventario del CMS. Es la ÚNICA fuente de sus datos (D-016-C20/C22): de aquí salen el
    /// <c>propertyListing</c> que leen la app, el mapa y el listado, y la ficha técnica de su <c>productPage</c> (si la
    /// tiene), así que los dos no pueden decir cosas distintas.
    /// </summary>
    internal sealed record InmuebleDelCms(
        string Slug, string Titulo, string Operacion, string Tipo, decimal Precio, Guid Categoria, Guid? Ficha,
        string Ciudad, string Barrio, int Habitaciones, int Banos, int AreaM2, int Parqueaderos, int Estrato,
        decimal Lat, decimal Lng, Guid Foto, string Descripcion, string[] Amenidades);

    /// <summary>
    /// Los inmuebles del CMS: las 6 fichas de venta que ya existían como <c>productPage</c> (mismos nombres, precios y
    /// fotos) y 3 arriendos con fotos ya acreditadas. Contenido de demostración; sin agente con nombre.
    /// </summary>
    internal static readonly InmuebleDelCms[] InmueblesDelCms =
    [
        new("apto-lujo-chico-bogota", "Apartamento de lujo en Chicó", "venta", "apartamento", 1_250_000_000m, CategoriaApartamentos,
            Guid.Parse("07a38c3d-5ca0-4588-878d-2c60709da0d3"), "Bogotá", "Chicó", 3, 2, 120, 2, 6, 4.6760m, -74.0480m,
            Guid.Empty, "Apartamento de 120 m² con acabados premium, tres habitaciones, balcón panorámico y dos parqueaderos. En el corazón de Chicó.",
            ["Balcón con vista panorámica", "Acabados premium"]),
        new("apto-familiar-cedritos-bogota", "Apartamento familiar en Cedritos", "venta", "apartamento", 620_000_000m, CategoriaApartamentos,
            Guid.Parse("bf5075a8-0f24-430b-825d-d697e0070a39"), "Bogotá", "Cedritos", 2, 2, 78, 1, 4, 4.7230m, -74.0410m,
            Guid.Empty, "Acogedor apartamento de 78 m² con dos habitaciones, cocina integral y zona social iluminada. Conjunto con piscina y gimnasio.",
            ["Cocina integral", "Piscina", "Gimnasio"]),
        new("casa-campestre-los-lagos-la-calera", "Casa campestre en Los Lagos", "venta", "casa", 1_480_000_000m, CategoriaCasas,
            Guid.Parse("89f2d378-5ff8-4597-b094-2fd9675d1c22"), "La Calera", "Los Lagos", 3, 3, 180, 2, 5, 4.7210m, -73.9690m,
            Guid.Empty, "Casa campestre de 180 m² con tres habitaciones, terraza, BBQ y amplio lote arborizado. Ideal para descanso y naturaleza.",
            ["Terraza", "BBQ", "Lote arborizado"]),
        new("casa-jardin-usaquen-bogota", "Casa con jardín en Usaquén", "venta", "casa", 2_100_000_000m, CategoriaCasas,
            Guid.Parse("836a3901-642e-4a48-ab3c-dec46e38490e"), "Bogotá", "Usaquén", 4, 4, 240, 2, 6, 4.6950m, -74.0310m,
            Guid.Empty, "Casa de 240 m² en dos niveles, cuatro habitaciones, estudio, jardín privado y garaje doble. Sector residencial exclusivo.",
            ["Estudio", "Jardín privado", "Garaje doble"]),
        new("local-zona-t-bogota", "Local comercial en Zona T", "venta", "local", 1_650_000_000m, CategoriaComercial,
            Guid.Parse("1254bad8-3ee6-490c-a8bd-675cb30f70a0"), "Bogotá", "Zona T", 0, 1, 140, 0, 0, 4.6670m, -74.0540m,
            Guid.Empty, "Local de 140 m² a pie de calle en la Zona T, con vitrina amplia y alto flujo peatonal. Ideal para retail o gastronomía.",
            ["A pie de calle", "Vitrina amplia", "Alto flujo peatonal"]),
        new("oficina-centro-internacional-bogota", "Oficina en torre empresarial", "venta", "oficina", 890_000_000m, CategoriaComercial,
            Guid.Parse("20e1de52-c2c6-4b1d-b7c4-d7218f4dd1a8"), "Bogotá", "Centro Internacional", 0, 2, 95, 2, 0, 4.6150m, -74.0690m,
            Guid.Empty, "Oficina de 95 m² en torre AAA con recepción, dos salas de reunión y vista a la ciudad. Excelente conectividad y parqueaderos.",
            ["Torre AAA", "Recepción", "2 salas de reunión", "Vista a la ciudad"]),
        new("apto-arriendo-poblado-medellin", "Apartamento en El Poblado", "arriendo", "apartamento", 3_800_000m, CategoriaApartamentos,
            null, "Medellín", "El Poblado", 2, 2, 85, 1, 6, 6.2086m, -75.5660m,
            Guid.Parse("9bfd8dc2-16ba-4fca-854f-a9b38b35987c"), "Apartamento moderno amoblado, ideal para ejecutivos, con vista panorámica y a pasos del Parque Lleras y la zona financiera.",
            ["Amoblado", "Gimnasio", "Piscina", "Coworking", "Pet friendly"]),
        new("casa-arriendo-cedritos-bogota", "Casa en Cedritos", "arriendo", "casa", 6_500_000m, CategoriaCasas,
            null, "Bogotá", "Cedritos", 4, 3, 210, 2, 4, 4.7240m, -74.0320m,
            Guid.Parse("bb54e982-ccea-4e05-ba6c-1eeeb0371007"), "Casa espaciosa con antejardín y zona verde, en un sector residencial tranquilo y bien conectado del norte de Bogotá.",
            ["Antejardín", "Chimenea", "Cuarto de servicio", "Bodega", "Garaje doble"]),
        new("oficina-santa-monica-cali", "Oficina en Santa Mónica", "arriendo", "oficina", 5_200_000m, CategoriaComercial,
            null, "Cali", "Santa Mónica", 0, 2, 130, 4, 5, 3.4699m, -76.5290m,
            Guid.Parse("c141182f-be33-4f78-8401-b2b993b6d69d"), "Oficina diáfana lista para adecuar, con recepción, sala de juntas y excelente conectividad de transporte en el norte de Cali.",
            ["Recepción", "Sala de juntas", "Aire acondicionado", "Planta eléctrica", "Fibra óptica"]),
    ];

    /// <summary>
    /// D-016-C20/C22, F1b: el inventario de Propiedades vive en el CMS. Por cada <see cref="InmueblesDelCms"/> hay un
    /// <c>propertyListing</c> bajo su categoría (lo que lee <c>UmbracoPropertyCatalogSource</c>). La foto de una venta
    /// es la de su ficha (<c>productImages</c>); la de un arriendo, la que ya está acreditada. Idempotente.
    /// </summary>
    private string RecetaInmuebles()
    {
        var listado = _contentTypeService.Get("propertyListing");
        var admite = listado is not null && _contentTypeService.Get("productCategoryPage")?.AllowedContentTypes?
            .Any(t => t.Id.Value == listado.Id || string.Equals(t.Alias, "propertyListing", StringComparison.Ordinal)) == true;
        if (!admite) { return "esquema:productCategoryPage-no-admite-propertyListing"; }

        int creados = 0, cambiados = 0;
        var partes = new List<string>();
        foreach (var i in InmueblesDelCms)
        {
            var categoria = _contentService.GetById(i.Categoria);
            if (categoria is null) { partes.Add($"sin-categoria:{i.Slug}"); continue; }

            var foto = FotoDelInmueble(i);

            var existentes = _contentService.GetPagedChildren(categoria.Id, 0, 200, out _).Where(c => c.ContentType.Alias == "propertyListing");
            var nodo = existentes.FirstOrDefault(c => string.Equals(c.GetValue<string>("propertySlug"), i.Slug, StringComparison.Ordinal));
            var esNuevo = nodo is null;
            // El nodo se llama distinto de su ficha hermana (productPage): en el árbol se ve qué es cada uno.
            var nombre = $"{i.Titulo} · inventario";
            nodo ??= _contentService.Create(nombre, categoria.Id, "propertyListing");

            var c = esNuevo;
            if (nodo.ContentType.VariesByCulture() && nodo.GetCultureName(Cultura) != nombre) { nodo.SetCultureName(nombre, Cultura); c = true; }
            c |= Poner(nodo, "propertySlug", i.Slug);
            c |= Poner(nodo, "propertyTitle", i.Titulo);
            c |= Poner(nodo, "propertyOperation", $"[\"{i.Operacion}\"]");
            c |= Poner(nodo, "propertyType", $"[\"{i.Tipo}\"]");
            c |= Poner(nodo, "propertyPrice", i.Precio);
            c |= Poner(nodo, "propertyDescription", i.Descripcion);
            c |= Poner(nodo, "propertyAmenities", string.Join("\n", i.Amenidades));
            c |= Poner(nodo, "propertyBeds", i.Habitaciones);
            c |= Poner(nodo, "propertyBaths", i.Banos);
            c |= Poner(nodo, "propertyAreaM2", i.AreaM2);
            c |= Poner(nodo, "propertyParking", i.Parqueaderos);
            c |= Poner(nodo, "propertyStratum", i.Estrato);
            c |= Poner(nodo, "propertyCity", i.Ciudad);
            c |= Poner(nodo, "propertyNeighborhood", i.Barrio);
            c |= Poner(nodo, "propertyLat", i.Lat);
            c |= Poner(nodo, "propertyLng", i.Lng);
            if (foto != Guid.Empty)
            {
                // La clave del elemento sale del slug y la foto: repetir la receta no la cambia.
                var clave = new Guid(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(i.Slug + foto)).AsSpan(0, 16));
                c |= Poner(nodo, "propertyGallery", $"[{{\"key\":\"{clave}\",\"mediaKey\":\"{foto}\",\"crops\":[],\"focalPoint\":null}}]");
            }
            if (!c) { continue; }
            var r = _contentService.SaveAndPublish(nodo, nodo.ContentType.VariesByCulture() ? new[] { Cultura } : Array.Empty<string>());
            if (!r.Success)
            {
                partes.Add($"sin-publicar:{i.Slug}:{r.Result}:{string.Join(",", r.InvalidProperties?.Select(x => x.Alias) ?? [])}");
                continue;
            }
            if (esNuevo) { creados++; } else { cambiados++; }
        }
        partes.Insert(0, $"inmuebles:{InmueblesDelCms.Length};creados:{creados};cambiados:{cambiados}");

        // F1d: la ficha (productPage) de cada venta dice lo mismo que su inmueble —la ficha técnica sale de la misma fila— y
        // su «Agendar visita» lleva al inmueble en la app, donde vive el flujo de la visita.
        var rejilla = KeyOf("elementSynFeatureGrid");
        var cierre = KeyOf("elementCompCtaBanner");
        var fichas = 0;
        foreach (var i in InmueblesDelCms.Where(x => x.Ficha is not null))
        {
            var ficha = _contentService.GetById(i.Ficha!.Value);
            if (ficha is null) { partes.Add($"sin-ficha:{i.Slug}"); continue; }
            var cuerpo = new CuerpoEditable(ficha.GetValue<string>(Cuerpo, Cultura));
            cuerpo.TransformarLista(rejilla, "itemsJson", lista => FichaTecnica(lista, i), CuerpoEditable.Donde("headingText", "Ficha técnica"));
            cuerpo.Reemplazar(cierre, "ctaLink", JsonNode.Parse(Enlace("Agendar visita", FichaEnLaApp(i)))!);
            if (!cuerpo.Cambio) { continue; }
            ficha.SetValue(Cuerpo, cuerpo.Json, Cultura);
            var r = _contentService.SaveAndPublish(ficha, new[] { Cultura });
            if (r.Success) { fichas++; } else { partes.Add($"ficha-sin-publicar:{i.Slug}:{r.Result}"); }
        }
        partes.Insert(1, $"fichas:{fichas}");

        // «Ver una propiedad» del listado llevaba a la página de demostración (oculta en F4): ahora, a una ficha real.
        var paginaListado = _contentService.GetById(PaginaListadoDePropiedades);
        if (paginaListado is not null)
        {
            var cuerpo = new CuerpoEditable(paginaListado.GetValue<string>(Cuerpo, Cultura));
            cuerpo.CambiarBotones(KeyOf("elementCompHero"), "ctaItems",
                [("Ver una propiedad", Enlace("Ver una propiedad", FichaEnLaApp(InmueblesDelCms[0]))), ("Hablar con un asesor", Enlace("Hablar con un asesor", "/synergos/contacto"))]);
            if (cuerpo.Cambio)
            {
                paginaListado.SetValue(Cuerpo, cuerpo.Json, Cultura);
                var r = _contentService.SaveAndPublish(paginaListado, new[] { Cultura });
                partes.Add(r.Success ? "listado:ver-una-propiedad" : $"listado-sin-publicar:{r.Result}");
            }
        }
        return string.Join(";", partes);
    }

    /// <summary>La página «Listado» de Propiedades, por su Key de uSync.</summary>
    private static readonly Guid PaginaListadoDePropiedades = Guid.Parse("ce0d4262-5cc3-4e3c-b478-d4d0e1adcdd9");

    /// <summary>La ficha de un inmueble en la app del portal, con su visita.</summary>
    internal static string FichaEnLaApp(InmuebleDelCms i) => $"/propiedades/#/realty/inmueble/{i.Slug}";

    /// <summary>Los datos de la ficha técnica que salen de la fila del inmueble, en este orden (lo que vale 0 no se pone).</summary>
    internal static IReadOnlyList<(string Rotulo, string Texto, int? Numero)> DatosDeLaFicha(InmuebleDelCms i)
    {
        var datos = new List<(string, string, int?)> { ("Operación", i.Operacion == "arriendo" ? "Arriendo" : "Venta", null) };
        if (i.AreaM2 > 0) { datos.Add(("Área", $"{i.AreaM2} m²", i.AreaM2)); }
        if (i.Habitaciones > 0) { datos.Add(("Habitaciones", $"{i.Habitaciones}", i.Habitaciones)); }
        if (i.Banos > 0) { datos.Add(("Baños", $"{i.Banos}", i.Banos)); }
        if (i.Parqueaderos > 0) { datos.Add(("Parqueaderos", $"{i.Parqueaderos}", i.Parqueaderos)); }
        if (i.Estrato > 0) { datos.Add(("Estrato", $"{i.Estrato}", i.Estrato)); }
        return datos;
    }

    /// <summary>El número con que empieza un texto («240 m² en dos niveles» → 240), o nulo.</summary>
    internal static int? NumeroDe(string? texto)
    {
        var m = System.Text.RegularExpressions.Regex.Match(texto ?? string.Empty, "^\\s*(\\d+)");
        return m.Success ? int.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) : null;
    }

    /// <summary>
    /// La ficha técnica de una ficha de Propiedades con los datos de su fila primero: un ítem que ya existe y dice el mismo
    /// número conserva su texto («240 m² en dos niveles»); si no, toma el de la fila. Los demás ítems siguen detrás.
    /// </summary>
    private static JsonArray FichaTecnica(JsonArray lista, InmuebleDelCms i)
    {
        var items = lista.OfType<JsonObject>().ToList();
        string? Texto(JsonObject o, string campo) => o[campo] is JsonValue v && v.TryGetValue<string>(out var t) ? t : null;
        var nueva = new JsonArray();
        var usados = new HashSet<JsonObject>();
        foreach (var (rotulo, texto, numero) in DatosDeLaFicha(i))
        {
            var actual = items.FirstOrDefault(o => Texto(o, "heading") == rotulo);
            var cuerpo = actual is not null && (numero is null ? Texto(actual, "body") == texto : NumeroDe(Texto(actual, "body")) == numero)
                ? Texto(actual, "body")!
                : texto;
            if (actual is not null) { usados.Add(actual); }
            nueva.Add(new JsonObject { ["heading"] = rotulo, ["body"] = cuerpo });
        }
        foreach (var o in items.Where(o => !usados.Contains(o))) { nueva.Add(o.DeepClone()); }
        return nueva;
    }

    /// <summary>La foto de un inmueble: la suya si la tiene; si no, la primera de su ficha (<c>productImages</c>).</summary>
    private Guid FotoDelInmueble(InmuebleDelCms i)
    {
        if (i.Foto != Guid.Empty) { return i.Foto; }
        if (i.Ficha is not { } fichaKey || _contentService.GetById(fichaKey) is not { } ficha || !ficha.HasProperty("productImages")) { return Guid.Empty; }
        var imagenes = ficha.GetValue<string>("productImages", ficha.Properties["productImages"]!.PropertyType.VariesByCulture() ? Cultura : null);
        var m = System.Text.RegularExpressions.Regex.Match(imagenes ?? string.Empty, "\"mediaKey\"\\s*:\\s*\"([0-9a-fA-F-]{36})\"");
        return m.Success ? Guid.Parse(m.Groups[1].Value) : Guid.Empty;
    }

    /// <summary>La página de demostración «Propiedad» de Propiedades, por su Key de uSync.</summary>
    private static readonly Guid PaginaPropiedad = Guid.Parse("c4e874b0-b607-4313-a76b-b35ae1ba7dff");

    /// <summary>
    /// D-016-C20, punto 4: la página de demostración «Propiedad» repetía una ficha (con recuadros sin foto) y estaba en el
    /// menú y en el pie. Sale de los dos (<c>hideFromMainMenu</c> y <c>hideFromFooter</c>); sigue publicada. Idempotente.
    /// </summary>
    private string OcultarDeLaNavegacion(Guid clave)
    {
        var pagina = _contentService.GetById(clave);
        if (pagina is null) { return "sin-pagina"; }
        var cambio = false;
        foreach (var oculto in new[] { "hideFromMainMenu", "hideFromFooter" })
        {
            if (pagina.HasProperty(oculto) && !pagina.GetValue<bool>(oculto))
            {
                pagina.SetValue(oculto, true);
                cambio = true;
            }
        }
        if (!cambio) { return "nada-que-hacer"; }
        var r = _contentService.SaveAndPublish(pagina, new[] { Cultura });
        return r.Success ? "oculta-del-menu-y-del-pie" : $"fallo:{r.Result}";
    }

    /// <summary>
    /// D-016-C14: Casos contaba historias de negocios que no existen («Retail Norte», «Grupo Andino») con sus cifras. Salen
    /// de la página «Casos de éxito», las tres cifras y «Cómo lo lograron» (SUPERSET: se desconectan; el tipo, los textos
    /// y las fotos siguen en el esquema y en el repo). Sin contenido real, la página sale del menú y del pie
    /// (<c>hideFromMainMenu</c> y <c>hideFromFooter</c>): sigue publicada, y nada más la enlaza. Idempotente.
    /// </summary>
    private string RecetaCasosSinHistoriasInventadas()
    {
        var casos = _contentService.GetById(Casos);
        if (casos is null) { return "sin-pagina"; }
        var cuerpo = new CuerpoEditable(casos.GetValue<string>(Cuerpo, Cultura));
        var fuera = cuerpo.DesconectarSeccionesCon(KeyOf("elementSynFeatureGrid"), CuerpoEditable.Donde("headingText", "Casos de éxito"))
            + cuerpo.DesconectarSeccionesCon(KeyOf("elementSynStatTicker"))
            + cuerpo.DesconectarSeccionesCon(KeyOf("elementSynMediaText"), CuerpoEditable.Donde("headingText", "Cómo lo lograron"));
        var cambio = cuerpo.Cambio;
        if (cambio) { casos.SetValue(Cuerpo, cuerpo.Json, Cultura); }
        // El menú y el pie se ocultan cada uno con su propiedad (NavegacionDelSitio): las dos.
        foreach (var oculto in new[] { "hideFromMainMenu", "hideFromFooter" })
        {
            if (casos.HasProperty(oculto) && !casos.GetValue<bool>(oculto))
            {
                casos.SetValue(oculto, true);
                cambio = true;
            }
        }
        if (!cambio) { return "nada-que-hacer"; }
        var r = _contentService.SaveAndPublish(casos, new[] { Cultura });
        return r.Success ? $"secciones:{fuera};oculta-del-menu" : $"fallo:{r.Result}";
    }

    /// <summary>
    /// Las fichas de Booking y la opción del campo «Servicio» del formulario de reserva (<c>reserva-cita</c>) que cada una
    /// elige al reservar (016, D-016-C24).
    /// </summary>
    internal static readonly (Guid Ficha, string Servicio)[] ServiciosDeBooking =
    [
        (Guid.Parse("ac898180-e618-44ba-9fc1-29cf0a2a71e6"), "Asesoría express"),
        (Guid.Parse("4f0092ca-bf9e-468e-abf0-47bc5483103e"), "Consultoría estratégica"),
        (Guid.Parse("5b622e11-1191-4c2a-b805-9f888bc5f87a"), "Sala de reuniones"),
        (Guid.Parse("156beede-bf2b-447d-95a2-e89ff207a0a1"), "Auditorio"),
        (Guid.Parse("51e3c7f8-96f8-4458-a650-5adec6126174"), "Masaje terapéutico"),
        (Guid.Parse("678082a0-3387-4adb-9dad-2c8911387830"), "Plan nutricional"),
    ];

    /// <summary>El enlace de «Reservar ahora» de una ficha de Booking: el formulario con su servicio ya elegido.</summary>
    internal static string ReservarElServicio(string servicio) => $"/booking/reservar?servicio={Uri.EscapeDataString(servicio)}";

    /// <summary>
    /// D-016-C24: «Reservar ahora» de cada ficha de Booking lleva al formulario con su servicio ya elegido. Es el cierre
    /// de la ficha (<c>elementCompCtaBanner</c>), que la vista repite junto al precio. Idempotente.
    /// </summary>
    private string RecetaReservarDesdeLaFicha()
    {
        var cierre = KeyOf("elementCompCtaBanner");
        var partes = new List<string>();
        foreach (var (key, servicio) in ServiciosDeBooking)
        {
            var ficha = _contentService.GetById(key);
            if (ficha is null) { partes.Add($"sin-ficha:{key}"); continue; }
            var cuerpo = new CuerpoEditable(ficha.GetValue<string>(Cuerpo, Cultura));
            cuerpo.Reemplazar(cierre, "ctaLink", JsonNode.Parse(Enlace("Reservar ahora", ReservarElServicio(servicio)))!);
            if (!cuerpo.Cambio) { continue; }
            ficha.SetValue(Cuerpo, cuerpo.Json, Cultura);
            var r = _contentService.SaveAndPublish(ficha, new[] { Cultura });
            partes.Add(r.Success ? $"{servicio}:ok" : $"{servicio}:fallo:{r.Result}");
        }
        return partes.Count == 0 ? "nada-que-hacer" : string.Join(";", partes);
    }

    /// <summary>
    /// D-016-C13: un testimonio inventado lo es con nombre o con rol y ciudad, y unos logos de clientes que no lo son
    /// serían una afirmación falsa en producción (corrige la decisión del 2026-11-13). Salen de su página los carruseles
    /// de testimonios de Educación, Eventos, Hoteles, Propiedades, la portada de Synergos, Casos, la raíz de Blogs
    /// («Lo que dicen nuestros lectores») y la de Healthcare («Lo que dicen las clínicas», D-003-V3), y las dos nubes de
    /// logos del hub. Cada sección es sólo su encabezado y el bloque, así que no deja un título suelto. SUPERSET: se desconecta
    /// la sección; el tipo, los textos y las fotos siguen en el esquema y en el repo. Idempotente.
    /// </summary>
    private string RecetaSinTestimoniosInventados()
    {
        var partes = new List<string>();
        var carrusel = KeyOf("elementSynTestimonialCarousel");
        var logos = KeyOf("elementMediaLogoCloud");
        foreach (var (pagina, etiqueta, conLogos) in new[]
                 {
                     (Educacion, "educacion", false), (Eventos, "eventos", false), (Hoteles, "hoteles", false),
                     (Propiedades, "propiedades", false), (Synergos, "synergos", true), (Casos, "casos", true),
                     (Blogs, "blogs", false), (Healthcare, "healthcare", false),
                 })
        {
            var contenido = _contentService.GetById(pagina);
            if (contenido is null) { partes.Add($"{etiqueta}:sin-pagina"); continue; }
            var cuerpo = new CuerpoEditable(contenido.GetValue<string>(Cuerpo, Cultura));
            var fuera = cuerpo.DesconectarSeccionesCon(carrusel) + (conLogos ? cuerpo.DesconectarSeccionesCon(logos) : 0);
            if (!cuerpo.Cambio) { continue; }
            contenido.SetValue(Cuerpo, cuerpo.Json, Cultura);
            var r = _contentService.SaveAndPublish(contenido, new[] { Cultura });
            partes.Add(r.Success ? $"{etiqueta}:{fuera}" : $"{etiqueta}:fallo:{r.Result}");
        }
        return partes.Count == 0 ? "nada-que-hacer" : string.Join(";", partes);
    }

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
                "<p>Completa tu reserva en pocos pasos: fechas, huéspedes, habitación y datos de contacto. Antes de confirmar ves el total y si la tarifa es reembolsable; al confirmar, tu comprobante con el número de la reserva.</p>");
            cuerpo.Reemplazar(KeyOf("elementCorpMissionBlock"), "textBody",
                "<p>El asistente te guía paso a paso y te muestra las habitaciones libres para tus fechas. Antes de confirmar ves el total y si la tarifa es reembolsable.</p>",
                CuerpoEditable.Donde("headingTitle", "Completa tu reserva"));
            // El asistente quedaba a ~1.000 px, bajo la portada y un bloque que la repetía (016, 4.ª vuelta): el bloque sale
            // de la página (desconectado; el tipo sigue) y su única frase nueva pasa a la portada.
            cuerpo.DesconectarSeccionesCon(KeyOf("elementCorpMissionBlock"), CuerpoEditable.Donde("headingTitle", "Completa tu reserva"));
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
    /// Desde D-016-C13 esos carruseles salen de su página (<see cref="RecetaSinTestimoniosInventados"/>): esta tabla
    /// sólo cambia un carrusel que siga conectado.
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
        ["gobierno"] = new(Gobierno, "Trámites de ejemplo en lenguaje claro: radique, pague la tasa si aplica y siga su expediente.", []),
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

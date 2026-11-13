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
        };

    /// <summary>
    /// Aplica a <paramref name="vertical"/> su receta (si tiene) y su atribución (si tiene): la descripción del sitio y
    /// los testimonios por rol y ciudad. Idempotente.
    /// </summary>
    public Resultado Componer(string vertical)
    {
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
                cuerpo.TransformarLista(galeria, "imagesJson", lista =>
                {
                    var nueva = new JsonArray();
                    foreach (var item in lista)
                    {
                        var copia = (JsonObject)item!.DeepClone();
                        var url = copia["fullUrl"]?.GetValue<string>() ?? string.Empty;
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
                if (!encontrado) { faltan++; }
            }

            var tarjeta = grupo.FirstOrDefault(f => f.Ambiente == "exterior") ?? grupo.FirstOrDefault(f => f.Ambiente == "sala");
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

    /// <summary>Las Keys de los medios que referencia una página (cualquier MediaPicker3 de sus propiedades o bloques).</summary>
    private static IReadOnlyList<Guid> ClavesDeMedios(IContentBase pagina) =>
        pagina.Properties
            .SelectMany(p => p.Values)
            .Select(v => v.EditedValue?.ToString())
            .Where(v => v is not null && v.Contains("mediaKey", StringComparison.Ordinal))
            .SelectMany(v => System.Text.RegularExpressions.Regex.Matches(v!, @"""mediaKey""\s*:\s*""([0-9a-fA-F-]{36})""")
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
                         .Set("textBody", $"<p>{System.Net.WebUtility.HtmlEncode(t.Texto)}</p>")
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

    private static string Enlace(string nombre, string url) =>
        new JsonArray(new JsonObject
        {
            ["name"] = nombre, ["url"] = url, ["target"] = "", ["udi"] = null, ["icon"] = null, ["queryString"] = null,
        }).ToJsonString();

    private Guid KeyOf(string alias) => _contentTypeService.Get(alias)?.Key
        ?? throw new InvalidOperationException($"ContentType '{alias}' no existe — corré el import de uSync.");

    public sealed record Resultado(bool Ok, string Vertical, string Detalle, int SeccionesNuevas);
}

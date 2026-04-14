using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;

namespace Synergos.CMS.Schema.Initializers;

/// <summary>
/// Phase 11 — Languages and Dictionary Items.
///
/// Ensures two cultures:
///   es-CO  (Spanish Colombia — default / mandatory)
///   en-US  (English United States)
///
/// Creates all dictionary items with translations for both cultures.
/// Idempotent: safe to run on every schema bump.
///
/// ─────────────────────────────────────────────────────────
/// NAMING CONVENTION
/// ─────────────────────────────────────────────────────────
/// Pattern  : {Group}.{SubGroup}.{Leaf}   — PascalCase, dot-separated, max 3 levels
/// Examples : Common.Actions.ReadMore
///            Form.Validation.Required
///            Nav.SkipToContent
///            Accordion.ExpandAll
///
/// ─────────────────────────────────────────────────────────
/// WHEN TO USE A DICTIONARY ITEM (vs. a property)
/// ─────────────────────────────────────────────────────────
/// USE dictionary for:
///   - UI chrome text that is the SAME across all instances (aria labels,
///     loading states, error messages, empty states, button labels,
///     placeholder text, accessibility helpers, pagination labels)
///
/// USE an editable property for:
///   - Titles, body copy, CTA labels, images — anything an editor
///     configures per-instance in the backoffice
///
/// ─────────────────────────────────────────────────────────
/// GROUPS (root items — tree organisers in backoffice)
/// ─────────────────────────────────────────────────────────
/// Common     — global reusable actions, buttons, states, dates, aria
/// Nav        — navigation chrome
/// Header     — site header chrome
/// Footer     — site footer chrome
/// Form       — validation messages, placeholders, form actions
/// Blog       — blog list / article chrome
/// Pagination — paginator labels
/// Search     — search block chrome
/// Cookie     — cookie consent banner
/// Error      — 404 / 500 page copy
/// Accordion  — expand / collapse chrome
/// Slider     — carousel navigation chrome
/// Tab        — tab group accessibility chrome
/// Gallery    — lightbox/gallery chrome
/// Video      — video player chrome
/// Newsletter — subscription feedback messages
/// Social     — social share labels
/// Map        — map embed chrome
/// Testimonial— testimonial accessibility
/// Pricing    — pricing card chrome
/// Alert      — alert bar / notification chrome
/// Stats      — stat block auxiliary text
/// </summary>
internal sealed class DictionaryInitializer : ISchemaInitializer
{
    private readonly ILocalizationService _loc;

    public DictionaryInitializer(ILocalizationService loc) => _loc = loc;

    public void Initialize()
    {
        // ── 1. Ensure cultures ───────────────────────────────────────────────
        // Languages are created by CultureInitializer (Phase 0) — just look them up here.
        var es = _loc.GetLanguageByIsoCode("es-CO")!;
        var en = _loc.GetLanguageByIsoCode("en-US")!;

        // ── 2. Common ────────────────────────────────────────────────────────
        var gCommon = EnsureGroup("Common");

        //   Common.Actions — reusable verb labels
        Item(gCommon, "Common.Actions.ReadMore",        en, "Read more",              es, "Leer más");
        Item(gCommon, "Common.Actions.ViewAll",          en, "View all",               es, "Ver todo");
        Item(gCommon, "Common.Actions.LearnMore",        en, "Learn more",             es, "Más información");
        Item(gCommon, "Common.Actions.GetStarted",       en, "Get started",            es, "Comenzar");
        Item(gCommon, "Common.Actions.Download",         en, "Download",               es, "Descargar");
        Item(gCommon, "Common.Actions.Share",            en, "Share",                  es, "Compartir");
        Item(gCommon, "Common.Actions.Back",             en, "Back",                   es, "Volver");
        Item(gCommon, "Common.Actions.Next",             en, "Next",                   es, "Siguiente");
        Item(gCommon, "Common.Actions.Previous",         en, "Previous",               es, "Anterior");
        Item(gCommon, "Common.Actions.Close",            en, "Close",                  es, "Cerrar");
        Item(gCommon, "Common.Actions.Open",             en, "Open",                   es, "Abrir");
        Item(gCommon, "Common.Actions.Expand",           en, "Expand",                 es, "Expandir");
        Item(gCommon, "Common.Actions.Collapse",         en, "Collapse",               es, "Colapsar");
        Item(gCommon, "Common.Actions.ContactUs",        en, "Contact us",             es, "Contáctanos");
        Item(gCommon, "Common.Actions.SeeMore",          en, "See more",               es, "Ver más");

        //   Common.Buttons — imperative form for button labels
        Item(gCommon, "Common.Buttons.Submit",           en, "Submit",                 es, "Enviar");
        Item(gCommon, "Common.Buttons.Cancel",           en, "Cancel",                 es, "Cancelar");
        Item(gCommon, "Common.Buttons.Save",             en, "Save",                   es, "Guardar");
        Item(gCommon, "Common.Buttons.Delete",           en, "Delete",                 es, "Eliminar");
        Item(gCommon, "Common.Buttons.Edit",             en, "Edit",                   es, "Editar");
        Item(gCommon, "Common.Buttons.Search",           en, "Search",                 es, "Buscar");
        Item(gCommon, "Common.Buttons.Filter",           en, "Filter",                 es, "Filtrar");
        Item(gCommon, "Common.Buttons.Reset",            en, "Reset",                  es, "Restablecer");
        Item(gCommon, "Common.Buttons.Confirm",          en, "Confirm",                es, "Confirmar");
        Item(gCommon, "Common.Buttons.Continue",         en, "Continue",               es, "Continuar");
        Item(gCommon, "Common.Buttons.Apply",            en, "Apply",                  es, "Aplicar");

        //   Common.States — UI state labels
        Item(gCommon, "Common.States.Loading",           en, "Loading…",               es, "Cargando…");
        Item(gCommon, "Common.States.NoResults",         en, "No results found.",       es, "Sin resultados.");
        Item(gCommon, "Common.States.Error",             en, "Something went wrong.",   es, "Algo salió mal.");
        Item(gCommon, "Common.States.Success",           en, "Done!",                   es, "¡Listo!");
        Item(gCommon, "Common.States.Required",          en, "Required",                es, "Requerido");
        Item(gCommon, "Common.States.Optional",          en, "Optional",                es, "Opcional");
        Item(gCommon, "Common.States.New",               en, "New",                     es, "Nuevo");
        Item(gCommon, "Common.States.ComingSoon",        en, "Coming soon",             es, "Próximamente");
        Item(gCommon, "Common.States.NotAvailable",      en, "Not available",           es, "No disponible");

        //   Common.Date — date/time helpers
        Item(gCommon, "Common.Date.Today",               en, "Today",                   es, "Hoy");
        Item(gCommon, "Common.Date.PublishedOn",         en, "Published on",            es, "Publicado el");
        Item(gCommon, "Common.Date.UpdatedOn",           en, "Updated on",              es, "Actualizado el");
        Item(gCommon, "Common.Date.By",                  en, "by",                      es, "por");
        Item(gCommon, "Common.Date.At",                  en, "at",                      es, "a las");
        Item(gCommon, "Common.Date.MinRead",             en, "{n} min read",            es, "{n} min de lectura");

        //   Common.Aria — accessibility chrome
        Item(gCommon, "Common.Aria.CloseMenu",           en, "Close menu",              es, "Cerrar menú");
        Item(gCommon, "Common.Aria.OpenMenu",            en, "Open menu",               es, "Abrir menú");
        Item(gCommon, "Common.Aria.ToggleNav",           en, "Toggle navigation",       es, "Alternar navegación");
        Item(gCommon, "Common.Aria.ExternalLink",        en, "Opens in a new tab",      es, "Se abre en nueva pestaña");
        Item(gCommon, "Common.Aria.SkipToContent",       en, "Skip to main content",    es, "Ir al contenido principal");
        Item(gCommon, "Common.Aria.Loading",             en, "Loading, please wait",    es, "Cargando, por favor espera");
        Item(gCommon, "Common.Aria.Image",               en, "Decorative image",        es, "Imagen decorativa");
        Item(gCommon, "Common.Aria.EmbeddedContent",     en, "Embedded content",        es, "Contenido embebido");

        // ── 3. Nav ───────────────────────────────────────────────────────────
        var gNav = EnsureGroup("Nav");
        Item(gNav, "Nav.SkipToContent",                  en, "Skip to content",         es, "Saltar al contenido");
        Item(gNav, "Nav.BackToHome",                     en, "Back to home",            es, "Volver al inicio");
        Item(gNav, "Nav.BreadcrumbHome",                 en, "Home",                    es, "Inicio");
        Item(gNav, "Nav.OpenMenu",                       en, "Open menu",               es, "Abrir menú");
        Item(gNav, "Nav.CloseMenu",                      en, "Close menu",              es, "Cerrar menú");
        Item(gNav, "Nav.CurrentPage",                    en, "Current page",            es, "Página actual");
        Item(gNav, "Nav.Submenu",                        en, "Submenu",                 es, "Submenú");
        Item(gNav, "Nav.MainNavLabel",                   en, "Main navigation",         es, "Navegación principal");
        Item(gNav, "Nav.MobileNavLabel",                 en, "Mobile navigation",       es, "Navegación móvil");
        Item(gNav, "Nav.LanguageSelector",               en, "Language selector",       es, "Selector de idioma");
        Item(gNav, "Nav.FooterNavLabel",                 en, "Footer navigation",       es, "Navegación del pie de página");
        Item(gNav, "Nav.OpenSubmenu",                    en, "Open submenu",            es, "Abrir submenú");
        Item(gNav, "Nav.Breadcrumb",                     en, "Breadcrumb",              es, "Ruta de navegación");

        // ── 4. Header ────────────────────────────────────────────────────────
        var gHeader = EnsureGroup("Header");
        Item(gHeader, "Header.SearchPlaceholder",        en, "Search…",                 es, "Buscar…");
        Item(gHeader, "Header.SearchButton",             en, "Search",                  es, "Buscar");
        Item(gHeader, "Header.LanguageSwitcher",         en, "Change language",         es, "Cambiar idioma");
        Item(gHeader, "Header.Aria.Logo",                en, "Go to homepage",          es, "Ir a la página de inicio");

        // ── 5. Footer ────────────────────────────────────────────────────────
        var gFooter = EnsureGroup("Footer");
        Item(gFooter, "Footer.AllRightsReserved",        en, "All rights reserved.",    es, "Todos los derechos reservados.");
        Item(gFooter, "Footer.PrivacyPolicy",            en, "Privacy Policy",          es, "Política de privacidad");
        Item(gFooter, "Footer.TermsOfService",           en, "Terms of Service",        es, "Términos de servicio");
        Item(gFooter, "Footer.CookiePolicy",             en, "Cookie Policy",           es, "Política de cookies");
        Item(gFooter, "Footer.BackToTop",                en, "Back to top",             es, "Volver arriba");
        Item(gFooter, "Footer.FollowUs",                 en, "Follow us",               es, "Síguenos");
        Item(gFooter, "Footer.NewsletterTitle",          en, "Stay updated",            es, "Mantente informado");
        Item(gFooter, "Footer.NewsletterDesc",           en, "Digital architecture insights. No spam.", es, "Insights sobre arquitectura digital. Sin spam.");
        Item(gFooter, "Footer.NavigationTitle",          en, "Navigation",              es, "Navegación");
        Item(gFooter, "Footer.Accessibility",            en, "Accessibility",           es, "Accesibilidad");

        // ── 6. Form ──────────────────────────────────────────────────────────
        var gForm = EnsureGroup("Form");

        //   Validation messages
        Item(gForm, "Form.Validation.Required",          en, "This field is required.",
                                                         es, "Este campo es obligatorio.");
        Item(gForm, "Form.Validation.InvalidEmail",      en, "Please enter a valid email address.",
                                                         es, "Introduce un correo electrónico válido.");
        Item(gForm, "Form.Validation.InvalidPhone",      en, "Please enter a valid phone number.",
                                                         es, "Introduce un número de teléfono válido.");
        Item(gForm, "Form.Validation.TooShort",          en, "This field is too short.",
                                                         es, "Este campo es demasiado corto.");
        Item(gForm, "Form.Validation.TooLong",           en, "This field is too long.",
                                                         es, "Este campo es demasiado largo.");
        Item(gForm, "Form.Validation.PasswordMismatch",  en, "Passwords do not match.",
                                                         es, "Las contraseñas no coinciden.");
        Item(gForm, "Form.Validation.AcceptTerms",       en, "You must accept the terms to continue.",
                                                         es, "Debes aceptar los términos para continuar.");
        Item(gForm, "Form.Validation.RequiredSummary",   en, "Required fields:",
                                                         es, "Campos requeridos:");

        //   Placeholders
        Item(gForm, "Form.Placeholders.Name",            en, "Your name",               es, "Tu nombre");
        Item(gForm, "Form.Placeholders.FirstName",       en, "First name",              es, "Nombre");
        Item(gForm, "Form.Placeholders.LastName",        en, "Last name",               es, "Apellidos");
        Item(gForm, "Form.Placeholders.Email",           en, "your@email.com",          es, "tu@correo.com");
        Item(gForm, "Form.Placeholders.Phone",           en, "+1 (000) 000-0000",       es, "+34 000 000 000");
        Item(gForm, "Form.Placeholders.Company",         en, "Company name",            es, "Nombre de empresa");
        Item(gForm, "Form.Placeholders.Message",         en, "Write your message…",     es, "Escribe tu mensaje…");
        Item(gForm, "Form.Placeholders.Search",          en, "Search…",                 es, "Buscar…");
        Item(gForm, "Form.Placeholders.SelectOption",    en, "Select an option",        es, "Selecciona una opción");

        //   Feedback messages
        Item(gForm, "Form.Messages.Success",             en, "Thank you! Your message has been sent.",
                                                         es, "¡Gracias! Tu mensaje ha sido enviado.");
        Item(gForm, "Form.Messages.Error",               en, "An error occurred. Please try again.",
                                                         es, "Ocurrió un error. Por favor inténtalo de nuevo.");
        Item(gForm, "Form.Messages.Sending",             en, "Sending…",                es, "Enviando…");
        Item(gForm, "Form.Messages.NetworkError",        en, "Connection error. Please try again.",
                                                         es, "Error de conexión. Inténtalo de nuevo.");
        Item(gForm, "Form.Messages.NotFound",            en, "Form \"{0}\" not found.",
                                                         es, "Formulario \"{0}\" no encontrado.");

        //   Labels and actions
        Item(gForm, "Form.Labels.RequiredFields",        en, "* Required fields",       es, "* Campos obligatorios");
        Item(gForm, "Form.Actions.Subscribe",            en, "Subscribe",               es, "Suscribirme");
        Item(gForm, "Form.Actions.Send",                 en, "Send message",            es, "Enviar mensaje");
        Item(gForm, "Form.Actions.Apply",                en, "Apply",                   es, "Solicitar");
        Item(gForm, "Form.Actions.RequestDemo",          en, "Request a demo",          es, "Solicitar demo");
        Item(gForm, "Form.Actions.Register",             en, "Register",                es, "Registrarme");

        // ── 7. Blog ──────────────────────────────────────────────────────────
        var gBlog = EnsureGroup("Blog");
        Item(gBlog, "Blog.PublishedBy",                  en, "By {author}",             es, "Por {autor}");
        Item(gBlog, "Blog.RelatedPosts",                 en, "Related posts",           es, "Artículos relacionados");
        Item(gBlog, "Blog.BackToList",                   en, "Back to blog",            es, "Volver al blog");
        Item(gBlog, "Blog.NoPosts",                      en, "No posts available.",     es, "No hay publicaciones disponibles.");
        Item(gBlog, "Blog.NoArticles",                   en, "No articles selected.",   es, "No hay artículos seleccionados.");
        Item(gBlog, "Blog.Tags",                         en, "Tags",                    es, "Etiquetas");
        Item(gBlog, "Blog.Categories",                   en, "Categories",              es, "Categorías");
        Item(gBlog, "Blog.Share",                        en, "Share this article",      es, "Compartir este artículo");
        Item(gBlog, "Blog.ContinueReading",              en, "Continue reading",        es, "Seguir leyendo");
        Item(gBlog, "Blog.AllPosts",                     en, "All posts",               es, "Todas las publicaciones");
        Item(gBlog, "Blog.DefaultLabel",                 en, "Blog",                    es, "Blog");
        Item(gBlog, "Blog.MaybeInterest",                en, "You may also like…",      es, "Quizás también te interese…");
        Item(gBlog, "Blog.SeeAll",                       en, "See all",                 es, "Ver todos");
        Item(gBlog, "Blog.Featured",                     en, "Featured",                es, "Destacado");
        Item(gBlog, "Blog.RecentPosts",                  en, "Recent posts",            es, "Artículos recientes");
        Item(gBlog, "Blog.Prev",                         en, "Previous",                es, "Anterior");
        Item(gBlog, "Blog.Next",                         en, "Next",                    es, "Siguiente");
        Item(gBlog, "Blog.BackToBlog",                   en, "Back to blog",            es, "Volver al Blog");
        Item(gBlog, "Blog.ArticleCount",                 en, "articles",                es, "artículos");
        Item(gBlog, "Blog.OtherTopics",                  en, "Other topics",            es, "Otros temas");
        Item(gBlog, "Blog.AuthorSocial",                 en, "Author social profiles",  es, "Redes sociales del autor");
        Item(gBlog, "Blog.ReadMore",                     en, "Read more",               es, "Leer más");
        Item(gBlog, "Blog.ReadArticle",                  en, "Read article",            es, "Leer artículo");

        // ── 8. Pagination ────────────────────────────────────────────────────
        var gPagination = EnsureGroup("Pagination");
        Item(gPagination, "Pagination.Previous",         en, "Previous",                es, "Anterior");
        Item(gPagination, "Pagination.Next",             en, "Next",                    es, "Siguiente");
        Item(gPagination, "Pagination.First",            en, "First",                   es, "Primera");
        Item(gPagination, "Pagination.Last",             en, "Last",                    es, "Última");
        Item(gPagination, "Pagination.Page",             en, "Page {n}",                es, "Página {n}");
        Item(gPagination, "Pagination.Of",               en, "of",                      es, "de");
        Item(gPagination, "Pagination.ItemsPerPage",     en, "Items per page",          es, "Elementos por página");
        Item(gPagination, "Pagination.GoToPage",         en, "Go to page",              es, "Ir a la página");

        // ── 9. Search ────────────────────────────────────────────────────────
        var gSearch = EnsureGroup("Search");
        Item(gSearch, "Search.Placeholder",              en, "Search…",                 es, "Buscar…");
        Item(gSearch, "Search.Button",                   en, "Search",                  es, "Buscar");
        Item(gSearch, "Search.ResultsFor",               en, "Results for \"{query}\"", es, "Resultados para \"{query}\"");
        Item(gSearch, "Search.NoResults",                en, "No results found for \"{query}\".",
                                                         es, "Sin resultados para \"{query}\".");
        Item(gSearch, "Search.Suggestions",              en, "Suggestions",             es, "Sugerencias");
        Item(gSearch, "Search.ClearSearch",              en, "Clear search",            es, "Limpiar búsqueda");
        Item(gSearch, "Search.Searching",                en, "Searching…",              es, "Buscando…");
        Item(gSearch, "Search.ResultCount",              en, "{n} results",             es, "{n} resultados");

        // ── 10. Cookie Consent ───────────────────────────────────────────────
        var gCookie = EnsureGroup("Cookie");
        Item(gCookie, "Cookie.BannerMessage",            en, "We use cookies to improve your experience.",
                                                         es, "Usamos cookies para mejorar tu experiencia.");
        Item(gCookie, "Cookie.AcceptAll",                en, "Accept all",              es, "Aceptar todo");
        Item(gCookie, "Cookie.RejectAll",                en, "Reject all",              es, "Rechazar todo");
        Item(gCookie, "Cookie.Customize",                en, "Customize",               es, "Personalizar");
        Item(gCookie, "Cookie.MoreInfo",                 en, "More information",        es, "Más información");
        Item(gCookie, "Cookie.Accepted",                 en, "Preferences saved.",      es, "Preferencias guardadas.");
        Item(gCookie, "Cookie.NecessaryOnly",            en, "Necessary only",          es, "Solo necesarias");

        // ── 11. Error Pages ──────────────────────────────────────────────────
        var gError = EnsureGroup("Error");
        Item(gError, "Error.NotFound.Title",             en, "Page not found",
                                                         es, "Página no encontrada");
        Item(gError, "Error.NotFound.Description",       en, "The page you're looking for doesn't exist.",
                                                         es, "La página que buscas no existe.");
        Item(gError, "Error.NotFound.Action",            en, "Go back home",            es, "Volver al inicio");
        Item(gError, "Error.Server.Title",               en, "Something went wrong",    es, "Algo salió mal");
        Item(gError, "Error.Server.Description",         en, "We're working on fixing this. Please try again later.",
                                                         es, "Estamos trabajando en solucionarlo. Inténtalo más tarde.");
        Item(gError, "Error.Forbidden.Title",            en, "Access denied",           es, "Acceso denegado");
        Item(gError, "Error.Forbidden.Description",      en, "You don't have permission to view this page.",
                                                         es, "No tienes permiso para ver esta página.");

        // ── 12. Accordion ────────────────────────────────────────────────────
        var gAccordion = EnsureGroup("Accordion");
        Item(gAccordion, "Accordion.Expand",             en, "Expand",                  es, "Expandir");
        Item(gAccordion, "Accordion.Collapse",           en, "Collapse",                es, "Colapsar");
        Item(gAccordion, "Accordion.ExpandAll",          en, "Expand all",              es, "Expandir todo");
        Item(gAccordion, "Accordion.CollapseAll",        en, "Collapse all",            es, "Colapsar todo");
        Item(gAccordion, "Accordion.Aria.Region",        en, "Accordion section",       es, "Sección de acordeón");

        // ── 13. Slider / Banner Slider ───────────────────────────────────────
        var gSlider = EnsureGroup("Slider");
        Item(gSlider, "Slider.Previous",                 en, "Previous slide",          es, "Diapositiva anterior");
        Item(gSlider, "Slider.Next",                     en, "Next slide",              es, "Siguiente diapositiva");
        Item(gSlider, "Slider.GoToSlide",                en, "Go to slide {n}",         es, "Ir a diapositiva {n}");
        Item(gSlider, "Slider.Current",                  en, "Current slide",           es, "Diapositiva actual");
        Item(gSlider, "Slider.Pause",                    en, "Pause slideshow",         es, "Pausar presentación");
        Item(gSlider, "Slider.Play",                     en, "Play slideshow",          es, "Reproducir presentación");
        Item(gSlider, "Slider.SlideOf",                  en, "Slide {n} of {total}",    es, "Diapositiva {n} de {total}");

        // ── 14. Tabs ─────────────────────────────────────────────────────────
        var gTab = EnsureGroup("Tab");
        Item(gTab, "Tab.Aria.TabList",                   en, "Tabs",                    es, "Pestañas");
        Item(gTab, "Tab.Aria.Panel",                     en, "Tab panel",               es, "Panel de pestaña");
        Item(gTab, "Tab.Aria.Selected",                  en, "Selected tab",            es, "Pestaña seleccionada");

        // ── 15. Gallery ──────────────────────────────────────────────────────
        var gGallery = EnsureGroup("Gallery");
        Item(gGallery, "Gallery.Previous",               en, "Previous image",          es, "Imagen anterior");
        Item(gGallery, "Gallery.Next",                   en, "Next image",              es, "Imagen siguiente");
        Item(gGallery, "Gallery.Close",                  en, "Close gallery",           es, "Cerrar galería");
        Item(gGallery, "Gallery.ImageOf",                en, "Image {n} of {total}",    es, "Imagen {n} de {total}");
        Item(gGallery, "Gallery.Zoom",                   en, "Zoom in",                 es, "Ampliar");
        Item(gGallery, "Gallery.Download",               en, "Download image",          es, "Descargar imagen");

        // ── 16. Video Player ─────────────────────────────────────────────────
        var gVideo = EnsureGroup("Video");
        Item(gVideo, "Video.Play",                       en, "Play video",              es, "Reproducir video");
        Item(gVideo, "Video.Pause",                      en, "Pause video",             es, "Pausar video");
        Item(gVideo, "Video.Mute",                       en, "Mute",                    es, "Silenciar");
        Item(gVideo, "Video.Unmute",                     en, "Unmute",                  es, "Activar sonido");
        Item(gVideo, "Video.Captions",                   en, "Toggle captions",         es, "Activar subtítulos");
        Item(gVideo, "Video.Fullscreen",                 en, "Fullscreen",              es, "Pantalla completa");
        Item(gVideo, "Video.ExitFullscreen",             en, "Exit fullscreen",         es, "Salir de pantalla completa");

        // ── 17. Newsletter ───────────────────────────────────────────────────
        var gNewsletter = EnsureGroup("Newsletter");
        Item(gNewsletter, "Newsletter.Success",          en, "Thanks for subscribing!", es, "¡Gracias por suscribirte!");
        Item(gNewsletter, "Newsletter.ErrorDuplicate",   en, "You're already subscribed.", es, "Ya estás suscrito.");
        Item(gNewsletter, "Newsletter.ErrorInvalid",     en, "Please enter a valid email.", es, "Introduce un correo válido.");
        Item(gNewsletter, "Newsletter.Placeholder",      en, "Your email address",      es, "Tu correo electrónico");
        Item(gNewsletter, "Newsletter.ConsentLabel",     en, "I agree to receive marketing emails.",
                                                         es, "Acepto recibir correos de marketing.");
        Item(gNewsletter, "Newsletter.SubmitButton",     en, "Subscribe",               es, "Suscribirme");

        // ── 18. Social Share ─────────────────────────────────────────────────
        var gSocial = EnsureGroup("Social");
        Item(gSocial, "Social.Share.Facebook",           en, "Share on Facebook",       es, "Compartir en Facebook");
        Item(gSocial, "Social.Share.X",                  en, "Share on X",              es, "Compartir en X");
        Item(gSocial, "Social.Share.LinkedIn",           en, "Share on LinkedIn",       es, "Compartir en LinkedIn");
        Item(gSocial, "Social.Share.WhatsApp",           en, "Share on WhatsApp",       es, "Compartir en WhatsApp");
        Item(gSocial, "Social.Share.CopyLink",           en, "Copy link",               es, "Copiar enlace");
        Item(gSocial, "Social.Share.LinkCopied",         en, "Link copied!",            es, "¡Enlace copiado!");
        Item(gSocial, "Social.Follow.Label",             en, "Follow us on",            es, "Síguenos en");

        // ── 19. Map ──────────────────────────────────────────────────────────
        var gMap = EnsureGroup("Map");
        Item(gMap, "Map.GetDirections",                  en, "Get directions",          es, "Cómo llegar");
        Item(gMap, "Map.Loading",                        en, "Loading map…",            es, "Cargando mapa…");
        Item(gMap, "Map.ViewLarger",                     en, "View larger map",         es, "Ver mapa ampliado");
        Item(gMap, "Map.Error",                          en, "Map unavailable.",        es, "Mapa no disponible.");

        // ── 20. Testimonial ──────────────────────────────────────────────────
        var gTestimonial = EnsureGroup("Testimonial");
        Item(gTestimonial, "Testimonial.Rating.Aria",    en, "{n} out of 5 stars",      es, "{n} de 5 estrellas");
        Item(gTestimonial, "Testimonial.VerifiedBuyer",  en, "Verified buyer",          es, "Comprador verificado");

        // ── 21. Pricing ──────────────────────────────────────────────────────
        var gPricing = EnsureGroup("Pricing");
        Item(gPricing, "Pricing.PerMonth",               en, "/ month",                 es, "/ mes");
        Item(gPricing, "Pricing.PerYear",                en, "/ year",                  es, "/ año");
        Item(gPricing, "Pricing.ContactUs",              en, "Contact us for pricing",  es, "Contáctanos para conocer el precio");
        Item(gPricing, "Pricing.Popular",                en, "Most popular",            es, "Más popular");
        Item(gPricing, "Pricing.Featured",               en, "Recommended",             es, "Recomendado");
        Item(gPricing, "Pricing.IncludesTax",            en, "Prices include applicable taxes.",
                                                         es, "Los precios incluyen impuestos aplicables.");
        Item(gPricing, "Pricing.BilledAnnually",         en, "Billed annually",         es, "Facturado anualmente");
        Item(gPricing, "Pricing.Free",                   en, "Free",                    es, "Gratis");

        // ── 22. Alert / Notifications ────────────────────────────────────────
        var gAlert = EnsureGroup("Alert");
        Item(gAlert, "Alert.Dismiss",                    en, "Dismiss",                 es, "Cerrar");
        Item(gAlert, "Alert.Info",                       en, "Information",             es, "Información");
        Item(gAlert, "Alert.Success",                    en, "Success",                 es, "Éxito");
        Item(gAlert, "Alert.Warning",                    en, "Warning",                 es, "Advertencia");
        Item(gAlert, "Alert.Error",                      en, "Error",                   es, "Error");

        // ── 23. Stats ────────────────────────────────────────────────────────
        var gStats = EnsureGroup("Stats");
        Item(gStats, "Stats.Source",                     en, "Source:",                 es, "Fuente:");
        Item(gStats, "Stats.AsOf",                       en, "As of",                   es, "A partir de");

        // ── 24. Quiz Flow ────────────────────────────────────────────────────
        var gQuiz = EnsureGroup("Quiz");
        Item(gQuiz, "Quiz.Start",            en, "Start quiz",              es, "Iniciar quiz");
        Item(gQuiz, "Quiz.Submit",           en, "Submit answers",          es, "Enviar respuestas");
        Item(gQuiz, "Quiz.Restart",          en, "Try again",               es, "Intentar de nuevo");
        Item(gQuiz, "Quiz.Next",             en, "Next question",           es, "Siguiente pregunta");
        Item(gQuiz, "Quiz.Progress",         en, "Question {current} of {total}", es, "Pregunta {current} de {total}");
        Item(gQuiz, "Quiz.Correct",          en, "Correct!",                es, "¡Correcto!");
        Item(gQuiz, "Quiz.Incorrect",        en, "Incorrect",               es, "Incorrecto");
        Item(gQuiz, "Quiz.Results",          en, "Your results",            es, "Tus resultados");
        Item(gQuiz, "Quiz.Score",            en, "{score} of {total} correct", es, "{score} de {total} correctas");

        // ── 25. Filter Board ─────────────────────────────────────────────────
        var gFilterBoard = EnsureGroup("FilterBoard");
        Item(gFilterBoard, "FilterBoard.ApplyFilters",   en, "Apply filters",          es, "Aplicar filtros");
        Item(gFilterBoard, "FilterBoard.ClearFilters",   en, "Clear filters",          es, "Limpiar filtros");
        Item(gFilterBoard, "FilterBoard.ActiveFilters",  en, "{n} active",             es, "{n} activos");
        Item(gFilterBoard, "FilterBoard.NoResults",      en, "No results match your filters.", es, "Ningún resultado coincide con tus filtros.");
        Item(gFilterBoard, "FilterBoard.ShowingResults", en, "Showing {n} results",   es, "Mostrando {n} resultados");
        Item(gFilterBoard, "FilterBoard.SortBy",         en, "Sort by",               es, "Ordenar por");
        Item(gFilterBoard, "FilterBoard.FilterBy",       en, "Filter by",             es, "Filtrar por");

        // ── 26. Rating Widget ────────────────────────────────────────────────
        var gRating = EnsureGroup("Rating");
        Item(gRating, "Rating.Stars.Aria",       en, "{n} out of {max} stars",  es, "{n} de {max} estrellas");
        Item(gRating, "Rating.Submit",           en, "Submit rating",           es, "Enviar valoración");
        Item(gRating, "Rating.Success",          en, "Thanks for your rating!", es, "¡Gracias por tu valoración!");
        Item(gRating, "Rating.AlreadyRated",     en, "You've already rated this.", es, "Ya has valorado esto.");
        Item(gRating, "Rating.Average",          en, "{avg} average ({count} reviews)", es, "{avg} promedio ({count} reseñas)");
        Item(gRating, "Rating.SelectStars",      en, "Select a star rating",    es, "Selecciona una valoración");

        // ── 27. Countdown Clock ──────────────────────────────────────────────
        var gCountdown = EnsureGroup("Countdown");
        Item(gCountdown, "Countdown.Days",       en, "Days",                    es, "Días");
        Item(gCountdown, "Countdown.Hours",      en, "Hours",                   es, "Horas");
        Item(gCountdown, "Countdown.Minutes",    en, "Minutes",                 es, "Minutos");
        Item(gCountdown, "Countdown.Seconds",    en, "Seconds",                 es, "Segundos");
        Item(gCountdown, "Countdown.Expired",    en, "Offer expired",           es, "Oferta expirada");
        Item(gCountdown, "Countdown.Label",      en, "Countdown to",            es, "Cuenta regresiva hacia");
        Item(gCountdown, "Countdown.Aria.Timer", en, "Time remaining: {time}",  es, "Tiempo restante: {time}");

        // ── 28. Notification Stack ───────────────────────────────────────────
        var gNotification = EnsureGroup("Notification");
        Item(gNotification, "Notification.Dismiss",      en, "Dismiss",               es, "Cerrar");
        Item(gNotification, "Notification.DismissAll",   en, "Dismiss all",           es, "Cerrar todas");
        Item(gNotification, "Notification.MarkRead",     en, "Mark as read",          es, "Marcar como leída");
        Item(gNotification, "Notification.MarkAllRead",  en, "Mark all as read",      es, "Marcar todas como leídas");
        Item(gNotification, "Notification.Empty",        en, "No notifications",      es, "Sin notificaciones");
        Item(gNotification, "Notification.New",          en, "{n} new",               es, "{n} nuevas");
        Item(gNotification, "Notification.Aria.List",    en, "Notifications",         es, "Notificaciones");

        // ── Shop ─────────────────────────────────────────────────────────────
        var gShop = EnsureGroup("Shop");

        var gShopCart = EnsureGroup("Shop.Cart");
        Item(gShopCart, "Shop.Cart.Title",               en, "Shopping cart",           es, "Carrito de compras");
        Item(gShopCart, "Shop.Cart.Empty",               en, "Your cart is empty",      es, "Tu carrito está vacío");
        Item(gShopCart, "Shop.Cart.ContinueShopping",    en, "Continue shopping",       es, "Seguir comprando");
        Item(gShopCart, "Shop.Cart.Checkout",            en, "Proceed to checkout",     es, "Proceder al pago");
        Item(gShopCart, "Shop.Cart.ItemsCount",          en, "{count} item(s)",         es, "{count} artículo(s)");
        Item(gShopCart, "Shop.Cart.Subtotal",            en, "Subtotal",                es, "Subtotal");
        Item(gShopCart, "Shop.Cart.Shipping",            en, "Shipping",                es, "Envío");
        Item(gShopCart, "Shop.Cart.Tax",                 en, "Tax",                     es, "Impuesto");
        Item(gShopCart, "Shop.Cart.Total",               en, "Total",                   es, "Total");
        Item(gShopCart, "Shop.Cart.Coupon",              en, "Discount code",           es, "Código de descuento");
        Item(gShopCart, "Shop.Cart.ApplyCoupon",         en, "Apply",                   es, "Aplicar");
        Item(gShopCart, "Shop.Cart.Remove",              en, "Remove",                  es, "Eliminar");
        Item(gShopCart, "Shop.Cart.ViewCart",            en, "View cart",               es, "Ver carrito");
        Item(gShopCart, "Shop.Cart.Free",                en, "Free",                    es, "Gratis");

        var gShopProduct = EnsureGroup("Shop.Product");
        Item(gShopProduct, "Shop.Product.AddToCart",     en, "Add to cart",             es, "Agregar al carrito");
        Item(gShopProduct, "Shop.Product.BuyNow",        en, "Buy now",                 es, "Comprar ahora");
        Item(gShopProduct, "Shop.Product.InStock",       en, "In stock",                es, "Disponible");
        Item(gShopProduct, "Shop.Product.OutOfStock",    en, "Out of stock",            es, "Agotado");
        Item(gShopProduct, "Shop.Product.LowStock",      en, "Only {n} left",           es, "Solo quedan {n}");
        Item(gShopProduct, "Shop.Product.SelectVariant", en, "Select an option",        es, "Selecciona una opción");
        Item(gShopProduct, "Shop.Product.Quantity",      en, "Quantity",                es, "Cantidad");
        Item(gShopProduct, "Shop.Product.Sku",           en, "SKU",                     es, "Referencia");
        Item(gShopProduct, "Shop.Product.Rating",        en, "Reviews",                 es, "Valoraciones");
        Item(gShopProduct, "Shop.Product.Share",         en, "Share",                   es, "Compartir");
        Item(gShopProduct, "Shop.Product.Wishlist",      en, "Save",                    es, "Guardar");
        Item(gShopProduct, "Shop.Product.AddedToCart",   en, "Added to cart",           es, "Agregado al carrito");

        var gShopOrder = EnsureGroup("Shop.Order");
        Item(gShopOrder, "Shop.Order.Confirmation",      en, "Order confirmed",         es, "Pedido confirmado");
        Item(gShopOrder, "Shop.Order.Number",            en, "Order #{number}",         es, "Pedido #{number}");
        Item(gShopOrder, "Shop.Order.Status",            en, "Order status",            es, "Estado del pedido");
        Item(gShopOrder, "Shop.Order.PlaceOrder",        en, "Place order",             es, "Finalizar compra");
        Item(gShopOrder, "Shop.Order.BackToShop",        en, "Continue shopping",       es, "Seguir comprando");

        var gShopFilter = EnsureGroup("Shop.Filter");
        Item(gShopFilter, "Shop.Filter.Clear",           en, "Clear filters",           es, "Limpiar filtros");
        Item(gShopFilter, "Shop.Filter.Apply",           en, "Apply filters",           es, "Aplicar filtros");
        Item(gShopFilter, "Shop.Filter.NoResults",       en, "No products found.",      es, "No se encontraron productos.");
        Item(gShopFilter, "Shop.Filter.SortBy",          en, "Sort by",                 es, "Ordenar por");
        Item(gShopFilter, "Shop.Filter.Relevance",       en, "Relevance",               es, "Relevancia");
        Item(gShopFilter, "Shop.Filter.Newest",          en, "Newest",                  es, "Más recientes");
        Item(gShopFilter, "Shop.Filter.PriceLow",        en, "Price: low to high",      es, "Precio: menor a mayor");
        Item(gShopFilter, "Shop.Filter.PriceHigh",       en, "Price: high to low",      es, "Precio: mayor a menor");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Ensures a language exists. Creates it if missing; promotes to default if needed.
    /// Returns the (possibly new) ILanguage.
    /// </summary>
    private ILanguage EnsureLanguage(string isoCode, string cultureName, bool isDefault)
    {
        var existing = _loc.GetLanguageByIsoCode(isoCode);
        if (existing is not null)
        {
            if (isDefault && !existing.IsDefault)
            {
                existing.IsDefault   = true;
                existing.IsMandatory = true;
                _loc.Save(existing);
            }
            return existing;
        }

        var lang = new Language(isoCode, cultureName)
        {
            IsDefault   = isDefault,
            IsMandatory = isDefault
        };
        _loc.Save(lang);

        // Re-fetch so we have the persisted entity (with Id assigned)
        return _loc.GetLanguageByIsoCode(isoCode) ?? lang;
    }

    /// <summary>
    /// Ensures a root-level group dictionary item exists.
    /// Returns its Key (GUID) for use as parentId when creating children.
    /// </summary>
    private Guid? EnsureGroup(string alias)
    {
        var existing = _loc.GetDictionaryItemByKey(alias);
        if (existing is not null) return existing.Key;

        var item = new DictionaryItem(alias);
        _loc.Save(item);
        return item.Key;
    }

    /// <summary>
    /// Creates or updates a dictionary item with English and Spanish translations.
    /// The <paramref name="alias"/> is the full dotted key (e.g. "Common.Actions.ReadMore").
    /// </summary>
    private void Item(
        Guid?     parentKey,
        string    alias,
        ILanguage en, string enValue,
        ILanguage es, string esValue)
    {
        var existing = _loc.GetDictionaryItemByKey(alias);
        if (existing is null)
        {
            existing = new DictionaryItem(parentKey, alias);
            _loc.Save(existing);
        }

        _loc.AddOrUpdateDictionaryValue(existing, en, enValue);
        _loc.AddOrUpdateDictionaryValue(existing, es, esValue);
        _loc.Save(existing);
    }
}

using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Application.Proxies.Impl;
using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Notifications;
using Synergos.CMS.Web.Services;
using Synergos.CMS.Web.Services.Catalog;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Cms.Core.Web;

namespace Synergos.CMS.Web.Composers;

public sealed partial class SeamComposer
{
    private void ComposeAcademy(IUmbracoBuilder builder)
    {
        var services = builder.Services;

        // OLA 4 Educación — LMS (doc educacion-app-spec). Dos seams stub-first,
        // aditivos (no tocan Booking/Travel/Shop/Blogs). ADR 0002 (Application pura,
        // sin Umbraco) + ADR 0075 (seam con tests canónicos).
        //   - ICourseCatalogProvider: catálogo buscable (texto/categoría/nivel →
        //     cursos) + detalle del curso (PDP: módulos → lecciones + instructor +
        //     planes de precio/cuotas). Stub: catálogo sembrado (3 categorías ×
        //     cursos × módulos × lecciones). Adapter real: Examine sobre coursePage.
        //     POLIMORFISMO Blogs: el contenido editorial de cada lección NO se
        //     duplica — se SIEMBRA en el IContentStream EXISTENTE con Kind=lesson y
        //     se referencia por id (DIP; depende de la abstracción de feed, no del
        //     módulo Blogs ni de su schema; no instancia Blogs). Por eso el catálogo
        //     recibe el IContentStream ya registrado arriba.
        //   - IEnrollmentService: motor transaccional de matrícula + progreso,
        //     calcando StubShopOrderService/StubReservationService. Resuelve el
        //     precio real desde el catálogo (anti-tampering), abre UNA sesión de
        //     pago (IPaymentProvider) si el curso es de pago o activa la matrícula
        //     de inmediato si es gratis; Confirm captura y activa. Añade lo propio
        //     del LMS: progreso por lección (idempotente) + certificado al 100%.
        // OLA 5 Educación (doc 21 §2.4) — app completa: certificado verificable
        // público (seam DEDICADO ICertificateService), panel del instructor
        // (GetForInstructorAsync con métricas + PublishCourseAsync al catálogo) y
        // tracking del ciclo de aprendizaje (enrolled→in-progress→completed). El
        // motor de matrícula ahora ALIMENTA su timeline de aprendizaje (instancia
        // PROPIA del seam genérico IOrderTrackingService con AcademyPipeline — NO
        // reusa el singleton de Tienda, cuyo pipeline es de envío) y expone la cara
        // de lectura IEnrollmentMetrics (alumnos/ingresos por curso) que el catálogo
        // COMPONE para el panel (DIP, sin duplicar el estado de matrículas). El
        // catálogo recibe ese seam por property injection DESPUÉS de construir ambos
        // singletons, para no crear un ciclo catálogo↔matrícula en el ctor.
        //
        // Singletons — el estado (matrículas + progreso + lecciones sembradas +
        // cursos publicados + timelines) vive en el proceso, igual que el resto de
        // stubs del motor.
        services.AddSingleton<StubCourseCatalogProvider>(sp =>
            new StubCourseCatalogProvider(sp.GetRequiredService<IContentStream>()));
        services.AddSingleton<ICourseCatalogProvider>(sp => sp.GetRequiredService<StubCourseCatalogProvider>());
        services.AddSingleton<StubEnrollmentService>(sp =>
        {
            // Durabilidad (doc 25): el estado de matrículas + progreso vive tras el store
            // genérico (resourceTypes "enrollments"/"course-progress") → sobrevive un reinicio.
            var enrollment = new StubEnrollmentService(
                sp.GetRequiredService<ICourseCatalogProvider>(),
                sp.GetRequiredService<IPaymentProvider>(),
                Tracking(sp, StubEnrollmentService.AcademyPipeline, "tracking-academy", "academy"),
                sp.GetRequiredService<IJsonEntityStore>(),
                null,
                notifier: sp.GetRequiredService<ITransactionalNotifier>());
            // Enchufa la cara de lectura en el catálogo (DIP) para el panel del
            // instructor — se resuelve tras construir ambos singletons.
            sp.GetRequiredService<StubCourseCatalogProvider>().EnrollmentMetrics = enrollment;
            return enrollment;
        });
        services.AddSingleton<IEnrollmentService>(sp => sp.GetRequiredService<StubEnrollmentService>());
        services.AddSingleton<IEnrollmentMetrics>(sp => sp.GetRequiredService<StubEnrollmentService>());
        // Certificado verificable — seam dedicado, compone catálogo + motor.
        //
        // ADR 0124: el id lo FIRMA ICertificateIdSigner (HMAC-SHA256 con llave del servidor,
        // persistida cifrada por CertificateSigningKeyProvider, calcando el firmante de los
        // e-tickets). Antes era un FNV-1a de 31 bits SIN secreto sobre "curso|alumno": quien
        // supiera un id de curso —que es público— y adivinara un correo calculaba el id del
        // certificado de esa persona. Con la verificación abierta al público, eso no habría
        // sido una credencial sino un padrón consultable de quién estudió qué.
        //
        // El índice de emitidos pasa además a IJsonEntityStore (familia "certificates") para
        // que un QR impreso en un diploma siga verificando después de un reinicio.
        services.AddSingleton<CertificateSigningKeyProvider>();
        services.AddSingleton<ICertificateIdSigner, LazyCertificateIdSigner>();
        services.AddSingleton<ICertificateService>(sp =>
            new StubCertificateService(
                sp.GetRequiredService<ICourseCatalogProvider>(),
                sp.GetRequiredService<IEnrollmentService>(),
                sp.GetRequiredService<ICertificateIdSigner>(),
                null,
                sp.GetRequiredService<IJsonEntityStore>()));

    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Services;
using Xunit;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// Cubre la propiedad que ADR 0124 introduce: <b>el id de un certificado no se puede
/// calcular sin la llave del servidor</b>, y el índice de emitidos sobrevive un reinicio.
/// </summary>
/// <remarks>
/// <para>El id era <c>"cert-" + FNV-1a-32(curso|alumno)</c>: hash no criptográfico, sin
/// secreto, enmascarado a 31 bits. Con el id del curso (público, sale del catálogo) y el
/// correo de alguien, cualquiera reproducía su id de certificado en cinco líneas. Nada
/// fallaba porque <c>VerifyAsync</c> no tenía ninguna ruta que lo expusiera; el endpoint
/// público que este trabajo añade habría convertido eso en un padrón consultable de quién
/// estudió qué (<c>Certificate.StudentName</c> viaja en la respuesta).</para>
///
/// <para>El proxy de reinicio es el del repo: <b>construir un servicio nuevo sobre el
/// MISMO store</b> (ver <c>TrackingAndReturnsDurabilityTests</c>). Si el certificado
/// sobrevive ese cambio, sobrevive un reinicio del proceso — que es lo que hace falta para
/// que el QR impreso en un diploma siga sirviendo.</para>
/// </remarks>
public sealed class CertificateCredentialTests
{
    private const string FreeCourse = "dev-git-fundamentals"; // 0 COP, ins-elena
    private const string Juan = "juan@synergos.co";

    private static ICertificateIdSigner Signer(string key = "llave-del-servidor")
        => new HmacCertificateIdSigner(Encoding.UTF8.GetBytes(key));

    private static StubContentStream ContentStream()
    {
        var clock = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc);
        return new StubContentStream(new StubSocialGraphService(), new StubReactionService(), () => clock);
    }

    /// <summary>
    /// Motor real (catálogo + matrícula) y un certificado montado encima, como el composer.
    /// El store se recibe para poder "reiniciar" construyendo otro servicio sobre él.
    /// </summary>
    private static (StubCourseCatalogProvider Catalog, StubEnrollmentService Enroll) Engine()
    {
        var catalog = new StubCourseCatalogProvider(ContentStream());
        var enroll = new StubEnrollmentService(
            catalog,
            new StubPaymentProvider(),
            new StubOrderTrackingService(StubEnrollmentService.AcademyPipeline, null),
            null);
        catalog.EnrollmentMetrics = enroll;
        return (catalog, enroll);
    }

    private static async Task CompleteAsync(StubEnrollmentService enroll, ICourseCatalogProvider catalog, string student)
    {
        await enroll.EnrollAsync(FreeCourse, new Student("Juan Pérez", student));
        var detail = await catalog.GetCourseAsync(FreeCourse);
        foreach (var lesson in detail!.Modules.SelectMany(m => m.Lessons))
        {
            await enroll.MarkLessonAsync(FreeCourse, lesson.Id, student);
        }
    }

    // ── El id ya no es derivable ─────────────────────────────────────────

    [Fact] // EL QUE MUERDE: sin la llave no hay forma de llegar al id
    public async Task El_id_NO_se_puede_derivar_sin_la_llave()
    {
        var (catalog, enroll) = Engine();
        await CompleteAsync(enroll, catalog, Juan);

        var real = await new StubCertificateService(catalog, enroll, Signer("la-buena")).GetAsync(Juan, FreeCourse);
        Assert.NotNull(real);

        // 1. El esquema VIEJO (FNV-1a de 31 bits sobre "curso|alumno", sin secreto) no
        //    llega al id nuevo. Este es literalmente el cálculo que cualquiera podía hacer.
        Assert.NotEqual("cert-" + FnvViejo($"{FreeCourse}|{Juan}"), real!.Id);

        // 2. Y conocer TODO el algoritmo nuevo tampoco alcanza: con otra llave sale otro id.
        var conOtraLlave = new HmacCertificateIdSigner(Encoding.UTF8.GetBytes("la-que-adivinó-el-atacante"))
            .Sign(new CertificateSubject(FreeCourse, Juan));
        Assert.NotEqual(conOtraLlave, real.Id);
    }

    [Fact] // el id es opaco: no lleva dentro a quién acredita
    public async Task El_id_NO_contiene_el_identificador_del_alumno()
    {
        var (catalog, enroll) = Engine();
        await CompleteAsync(enroll, catalog, Juan);

        var cert = await new StubCertificateService(catalog, enroll, Signer()).GetAsync(Juan, FreeCourse);

        Assert.DoesNotContain("@", cert!.Id, StringComparison.Ordinal);
        Assert.DoesNotContain("juan", cert.Id, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(FreeCourse, cert.Id, StringComparison.OrdinalIgnoreCase);
    }

    [Fact] // dos alumnos distintos JAMÁS comparten credencial (el viejo tenía 31 bits)
    public void Dos_alumnos_distintos_nunca_colisionan()
    {
        var signer = Signer();
        var ids = new HashSet<string>(StringComparer.Ordinal);

        for (var i = 0; i < 2_000; i++)
        {
            Assert.True(ids.Add(signer.Sign(new CertificateSubject(FreeCourse, $"alumno{i}@synergos.co"))));
        }
        // Y el mismo alumno en dos cursos tampoco.
        Assert.NotEqual(
            signer.Sign(new CertificateSubject("curso-a", Juan)),
            signer.Sign(new CertificateSubject("curso-b", Juan)));
    }

    [Fact] // el mismo alumno escrito distinto es el MISMO alumno (idempotencia real)
    public void El_id_normaliza_espacios_y_mayusculas()
    {
        var signer = Signer();

        Assert.Equal(
            signer.Sign(new CertificateSubject(FreeCourse, Juan)),
            signer.Sign(new CertificateSubject("  " + FreeCourse.ToUpperInvariant() + " ", "  Juan@Synergos.CO ")));
    }

    // ── Emisión: idempotente ─────────────────────────────────────────────

    [Fact] // idempotent: re-emitir devuelve el MISMO id y la MISMA fecha
    public async Task Reemitir_es_idempotente_incluida_la_fecha()
    {
        var (catalog, enroll) = Engine();
        await CompleteAsync(enroll, catalog, Juan);

        // Reloj que avanza: si la emisión no fuera idempotente de verdad, la segunda
        // llamada movería la fecha impresa en un diploma que ya está colgado.
        var tick = new DateTimeOffset(2026, 8, 1, 12, 0, 0, TimeSpan.Zero);
        var sut = new StubCertificateService(
            catalog, enroll, Signer(), () => tick = tick.AddDays(1), new InMemoryJsonEntityStore());

        var first = await sut.GetAsync(Juan, FreeCourse);
        var second = await sut.GetAsync(Juan, FreeCourse);

        Assert.Equal(first!.Id, second!.Id);
        Assert.Equal(first.IssuedAt, second.IssuedAt);
    }

    [Fact] // empty: sin completar no hay credencial (ni registro que verificar)
    public async Task Sin_completar_el_curso_no_hay_certificado()
    {
        var (catalog, enroll) = Engine();
        await enroll.EnrollAsync(FreeCourse, new Student("Juan Pérez", Juan));

        var sut = new StubCertificateService(catalog, enroll, Signer());

        Assert.Null(await sut.GetAsync(Juan, FreeCourse));
        Assert.Null(await sut.GetAsync("", FreeCourse));
        Assert.Null(await sut.GetAsync(Juan, "curso-que-no-existe"));
    }

    // ── Verificación ─────────────────────────────────────────────────────

    [Fact] // happy: la credencial emitida verifica por su id, sin sesión
    public async Task La_credencial_emitida_verifica_por_su_id()
    {
        var (catalog, enroll) = Engine();
        await CompleteAsync(enroll, catalog, Juan);
        var sut = new StubCertificateService(catalog, enroll, Signer());

        var issued = await sut.GetAsync(Juan, FreeCourse);
        var verified = await sut.VerifyAsync(issued!.Id);

        Assert.NotNull(verified);
        Assert.Equal(issued.Id, verified!.Id);
        Assert.Equal("Juan Pérez", verified.StudentName);
        Assert.Equal($"/academy/verify/{issued.Id}", issued.VerifyUrl);
    }

    [Fact] // EL QUE MUERDE: un id manipulado no verifica
    public async Task Un_id_MANIPULADO_no_verifica()
    {
        var (catalog, enroll) = Engine();
        await CompleteAsync(enroll, catalog, Juan);
        var sut = new StubCertificateService(catalog, enroll, Signer());

        var issued = await sut.GetAsync(Juan, FreeCourse);

        // Un solo carácter cambiado.
        var chars = issued!.Id.ToCharArray();
        chars[^1] = chars[^1] == 'a' ? 'b' : 'a';
        Assert.Null(await sut.VerifyAsync(new string(chars)));

        // Y las formas obvias de tantear.
        Assert.Null(await sut.VerifyAsync("cert-noexiste"));
        Assert.Null(await sut.VerifyAsync(issued.Id.ToUpperInvariant()));
        Assert.Null(await sut.VerifyAsync("   "));
        Assert.Null(await sut.VerifyAsync("../../keys/certificate-signing-v1"));
    }

    [Fact] // EL QUE MUERDE: el ÍNDICE no es la autoridad — la llave sí
    public async Task Un_registro_FABRICADO_en_el_store_no_verifica()
    {
        // Quien consiga escribir en App_Data podría inventar un certificado con el id que
        // quiera y el nombre de quien quiera. Al verificar se recalcula el id desde el
        // sujeto que el propio registro afirma, y no cuadra.
        var (catalog, enroll) = Engine();
        await CompleteAsync(enroll, catalog, Juan);

        var store = new InMemoryJsonEntityStore();
        const string forjado = "cert-00000000000000000000000000000000";
        await store.WriteAsync("certificates", forjado, JsonSerializer.Serialize(
            new PersistedCertificate(forjado, FreeCourse, Juan, "Juan Pérez", DateTimeOffset.UtcNow)));

        var sut = new StubCertificateService(catalog, enroll, Signer(), null, store);

        Assert.Null(await sut.VerifyAsync(forjado));
    }

    [Theory] // un id con otra forma NI SIQUIERA llega al almacén (no se convierte en una clave)
    [InlineData("../../keys/certificate-signing-v1")]
    [InlineData("cert-NOESHEX0000000000000000000000")]
    [InlineData("cert-abc")]
    [InlineData("")]
    public async Task Un_id_con_otra_forma_no_llega_al_almacen(string id)
    {
        var (catalog, enroll) = Engine();
        var store = Substitute.For<IJsonEntityStore>();

        var sut = new StubCertificateService(catalog, enroll, Signer(), null, store);

        Assert.Null(await sut.VerifyAsync(id));
        await store.DidNotReceive().ReadAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact] // filter: un registro corrupto no tumba la verificación, solo no vale
    public async Task Un_registro_corrupto_se_ignora_en_vez_de_reventar()
    {
        var (catalog, enroll) = Engine();
        var store = new InMemoryJsonEntityStore();
        var id = Signer().Sign(new CertificateSubject(FreeCourse, Juan));
        await store.WriteAsync("certificates", id, "{ esto no es json válido");

        var sut = new StubCertificateService(catalog, enroll, Signer(), null, store);

        Assert.Null(await sut.VerifyAsync(id));
    }

    [Fact] // la credencial refleja el estado ACTUAL: sin 100%, no vale
    public async Task Una_credencial_de_quien_ya_no_esta_al_100_no_verifica()
    {
        // Mismo store (el certificado sigue emitido) pero un motor donde ese alumno no
        // completó nada: la credencial no es un sello mudo.
        var (catalog, enroll) = Engine();
        await CompleteAsync(enroll, catalog, Juan);
        var store = new InMemoryJsonEntityStore();

        var issued = await new StubCertificateService(catalog, enroll, Signer(), null, store).GetAsync(Juan, FreeCourse);

        var (otroCatalogo, otroMotor) = Engine(); // sin progreso de nadie
        var sut = new StubCertificateService(otroCatalogo, otroMotor, Signer(), null, store);

        Assert.Null(await sut.VerifyAsync(issued!.Id));
    }

    // ── Durabilidad (ADR 0105) ───────────────────────────────────────────

    [Fact] // EL QUE MUERDE: el diploma impreso sigue verificando tras el reinicio
    public async Task Un_certificado_emitido_SOBREVIVE_a_un_servicio_nuevo_sobre_el_mismo_store()
    {
        var (catalog, enroll) = Engine();
        await CompleteAsync(enroll, catalog, Juan);
        var store = new InMemoryJsonEntityStore();

        var before = new StubCertificateService(catalog, enroll, Signer(), null, store);
        var issued = await before.GetAsync(Juan, FreeCourse);

        var after = new StubCertificateService(catalog, enroll, Signer(), null, store); // "reinicio"
        var verified = await after.VerifyAsync(issued!.Id);

        Assert.NotNull(verified);
        Assert.Equal(issued.Id, verified!.Id);
        Assert.Equal(issued.IssuedAt, verified.IssuedAt); // la fecha de emisión no se reinventa
    }

    [Fact] // …y con el store por defecto (en memoria) NO sobrevive: por eso el composer inyecta el real
    public async Task Sin_store_compartido_el_certificado_NO_sobrevive()
    {
        var (catalog, enroll) = Engine();
        await CompleteAsync(enroll, catalog, Juan);

        var issued = await new StubCertificateService(catalog, enroll, Signer()).GetAsync(Juan, FreeCourse);
        var otro = new StubCertificateService(catalog, enroll, Signer());

        Assert.Null(await otro.VerifyAsync(issued!.Id));
    }

    // ── Sin llave ────────────────────────────────────────────────────────

    [Fact] // fail-closed: un firmante sin llave no se construye
    public void Sin_llave_no_hay_firmante()
    {
        Assert.Throws<ArgumentException>(() => new HmacCertificateIdSigner(Array.Empty<byte>()));
        Assert.Throws<ArgumentException>(() => new HmacCertificateIdSigner(null!));
    }

    [Fact] // fail-closed: y no hay ctor del seam que prescinda del firmante
    public void Sin_firmante_no_hay_servicio_de_certificados()
    {
        var (catalog, enroll) = Engine();

        Assert.Throws<ArgumentNullException>(() => new StubCertificateService(catalog, enroll, null!));
    }

    // El esquema ANTERIOR, reproducido aquí para demostrar que ya no llega al id.
    private static string FnvViejo(string value)
    {
        const uint fnvOffset = 2166136261;
        const uint fnvPrime = 16777619;
        var hash = fnvOffset;
        foreach (var ch in value)
        {
            hash ^= ch;
            hash *= fnvPrime;
        }
        return (hash & 0x7FFFFFFF).ToString("x8");
    }
}

/// <summary>
/// Cubre <see cref="CertificateSigningKeyProvider"/> — de dónde sale la llave que hace
/// infalsificable el id (ADR 0124).
/// </summary>
/// <remarks>
/// Lo que estos tests fijan es la propiedad que hunde a todo lo demás si se pierde: <b>dos
/// proveedores distintos sobre el mismo store devuelven la MISMA llave</b>. Sin eso, cada
/// reinicio cambiaría el id de cada certificado y el QR de un diploma dejaría de servir —
/// el mismo bug que T9 corrigió para las entradas, con la diferencia de que una entrada
/// vale una noche y un diploma se presenta años después.
/// </remarks>
public sealed class CertificateSigningKeyProviderTests : IDisposable
{
    private readonly string _root;
    private readonly IDataProtectionProvider _protection;

    public CertificateSigningKeyProviderTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "syn-cert-keys-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _protection = DataProtectionProvider.Create(new DirectoryInfo(Path.Combine(_root, "dp")));
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { /* best-effort */ }
    }

    private CertificateSigningKeyProvider Make(IJsonEntityStore store, string configured = "") =>
        new(store,
            _protection,
            Options.Create(new AcademySettings { CertificateSigningSecret = configured }),
            NullLogger<CertificateSigningKeyProvider>.Instance);

    [Fact] // sin secreto configurado la llave se genera UNA vez y sobrevive el reinicio
    public async Task La_llave_generada_SOBREVIVE_un_reinicio()
    {
        var store = new InMemoryJsonEntityStore();

        var before = await Make(store).GetKeyAsync();
        var after = await Make(store).GetKeyAsync(); // "reinicio": instancia nueva

        Assert.NotEmpty(before);
        Assert.Equal(before, after);
    }

    [Fact] // …y por tanto el id del certificado también
    public async Task El_id_del_certificado_SOBREVIVE_un_reinicio()
    {
        var store = new InMemoryJsonEntityStore();
        var subject = new CertificateSubject("dev-git-fundamentals", "juan@synergos.co");

        var antes = new HmacCertificateIdSigner(await Make(store).GetKeyAsync()).Sign(subject);
        var despues = new HmacCertificateIdSigner(await Make(store).GetKeyAsync());

        Assert.Equal(antes, despues.Sign(subject));
        Assert.True(despues.Matches(antes, subject)); // el QR impreso ayer verifica hoy
    }

    [Fact] // el secreto configurado MANDA (es la vía de producción: rotable y compartible)
    public async Task El_secreto_configurado_manda()
    {
        var store = new InMemoryJsonEntityStore();

        var key = await Make(store, configured: "secreto-de-produccion").GetKeyAsync();

        Assert.Equal(Encoding.UTF8.GetBytes("secreto-de-produccion"), key);
        Assert.Empty(await store.ListAsync("keys")); // no se generó ni guardó nada
    }

    [Fact] // la llave generada NO se guarda en claro (el store de JSON no cifra)
    public async Task La_llave_generada_se_guarda_cifrada()
    {
        var store = new InMemoryJsonEntityStore();

        var key = await Make(store).GetKeyAsync();

        var raw = await store.ReadAsync("keys", "certificate-signing-v1");
        Assert.False(string.IsNullOrWhiteSpace(raw));
        Assert.DoesNotContain(Convert.ToBase64String(key), raw!, StringComparison.Ordinal);
    }

    [Fact] // EL QUE MUERDE por diseño: rotar el secreto de EVENTOS no toca los diplomas
    public async Task La_llave_de_certificados_NO_es_la_de_tickets()
    {
        var store = new InMemoryJsonEntityStore(); // el MISMO store para los dos

        var certKey = await Make(store).GetKeyAsync();
        var ticketKey = await new TicketSigningKeyProvider(
            store,
            _protection,
            Options.Create(new EventsSettings()),
            NullLogger<TicketSigningKeyProvider>.Instance).GetKeyAsync();

        Assert.NotEqual(certKey, ticketKey);
    }

    [Fact] // idempotent: pedir la llave dos veces a la MISMA instancia no la regenera
    public async Task Pedir_la_llave_dos_veces_no_la_regenera()
    {
        var provider = Make(new InMemoryJsonEntityStore());

        Assert.Equal(await provider.GetKeyAsync(), await provider.GetKeyAsync());
    }
}

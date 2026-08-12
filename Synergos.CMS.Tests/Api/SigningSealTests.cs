using Synergos.Api.Signing.Domain;
using Synergos.Api.Signing.Storage;
using Synergos.Core;
using Synergos.Shared;

namespace Synergos.CMS.Tests.Api;

/// <summary>
/// Cubre el <b>sello</b> de <c>Api.Signing</c> — la operación determinista, sin vencimiento y sin
/// payload recuperable que pide un identificador permanente (hallazgo #45).
/// </summary>
/// <remarks>
/// <para><b>Lo que vigila este fichero es que el sello NO se parezca a la firma.</b> Las tres
/// propiedades que hacen correcto a <c>/v1/signatures</c> —vence, mete el vencimiento dentro
/// de lo firmado, y deja leer el payload sin llave— son exactamente las tres que lo hacen
/// inservible para identificar un diploma. Cada una tiene acá su test, y cada test es el que se
/// pone en rojo si alguien "unifica" las dos operaciones en una.</para>
///
/// <para>La cuarta es la que más se olvida y va aparte: <b>se verifica el sello CONTRA el
/// sujeto</b>. Un <c>verify</c> que solo mire el sello dejaría al índice de quien consume ser la
/// autoridad otra vez.</para>
/// </remarks>
public sealed class SigningSealTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 3, 2, 10, 0, 0, TimeSpan.Zero);

    private sealed class RelojFalso : TimeProvider
    {
        private DateTimeOffset _now;
        public RelojFalso(DateTimeOffset inicio) => _now = inicio;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Avanzar(TimeSpan d) => _now += d;
    }

    private sealed class MemoriaStore : ISigningKeyStore, IIdempotencyLedger
    {
        private readonly List<SigningKey> _keys = new();
        private readonly Dictionary<string, string> _idem = new(StringComparer.Ordinal);

        public SigningKey? Find(string id) => _keys.FirstOrDefault(k => k.Id == id);

        public SigningKey? FindActive(string purpose)
            => _keys.Where(k => string.Equals(k.Purpose, purpose, StringComparison.OrdinalIgnoreCase) && k.CanSign)
                .OrderByDescending(k => k.CreatedAtUtc)
                .ThenBy(k => k.Id, StringComparer.Ordinal)
                .FirstOrDefault();

        public IReadOnlyList<SigningKey> ForPurpose(string purpose)
            => _keys.Where(k => string.Equals(k.Purpose, purpose, StringComparison.OrdinalIgnoreCase)).ToList();

        public void Put(SigningKey key)
        {
            _keys.RemoveAll(k => k.Id == key.Id);
            _keys.Add(key);
        }

        public string? Find(string scope, IdempotencyKey key) => _idem.GetValueOrDefault($"{scope}|{key.Value}");
        public void Remember(string scope, IdempotencyKey key, string resultId) => _idem[$"{scope}|{key.Value}"] = resultId;
    }

    private const string Proposito = "academy.certificado";

    private static SigningKey Llave(string id = "k1", DateTimeOffset? creada = null, DateTimeOffset? retirada = null)
        => new(id, Proposito, SigningRules.NewSecret(), creada ?? Ahora.AddDays(-30), retirada);

    private static (SigningService Svc, MemoriaStore Store, RelojFalso Reloj) Armar(params SigningKey[] llaves)
    {
        var store = new MemoriaStore();
        foreach (var l in llaves) store.Put(l);
        var reloj = new RelojFalso(Ahora);
        return (new SigningService(store, store, reloj), store, reloj);
    }

    // ── Las tres propiedades que la firma NO tiene ───────────────────────────

    [Fact]
    public void El_sello_NO_vence_aunque_pase_mas_que_la_vida_maxima_de_una_firma()
    {
        // Es la razón número uno por la que el certificado no podía ir a /v1/signatures: allí la
        // vigencia es obligatoria y tope 365 días, así que el diploma dejaría de verificar
        // dentro de un año con el QR ya impreso.
        var (svc, _, reloj) = Armar(Llave());

        var recien = svc.Seal(Proposito, "curso-7|alumno-3");
        reloj.Avanzar(SigningRules.MaxLifetime + TimeSpan.FromDays(400));
        var mucho_despues = svc.Seal(Proposito, "curso-7|alumno-3");

        Assert.True(recien.IsOk);
        Assert.True(mucho_despues.IsOk);
        Assert.Equal(recien.Value.Seal, mucho_despues.Value.Seal);

        // Y sigue cuadrando, que es lo que de verdad importa el día del reclamo.
        Assert.True(svc.VerifySeal(Proposito, "curso-7|alumno-3", recien.Value.Seal).IsOk);
    }

    [Fact]
    public void El_sello_es_DETERMINISTA_y_de_eso_vive_la_re_emision()
    {
        // Un token de firma cambia entre llamadas porque lleva el instante dentro. Si el id del
        // certificado hiciera lo mismo, re-emitir daría un id distinto y GetAsync dejaría de ser
        // idempotente: el mismo alumno tendría dos credenciales del mismo curso.
        var key = Llave();

        var a = SigningRules.Seal(key, "curso-7|alumno-3");
        var b = SigningRules.Seal(key, "curso-7|alumno-3");

        Assert.Equal(a, b);
    }

    [Fact]
    public void El_sello_NO_publica_su_contenido()
    {
        // La tercera: SigningRules.TryRead decodifica el payload de un token SIN llave —es
        // base64url, no cifrado—. Con (curso, ALUMNO) dentro, el identificador impreso en el
        // diploma publicaría a su titular en cada verificación pública.
        const string sujeto = "curso-7|alumno-3";
        var sello = SigningRules.Seal(Llave(), sujeto);

        Assert.DoesNotContain("alumno-3", sello, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("curso-7", sello, StringComparison.OrdinalIgnoreCase);

        // Y no se deja leer como token: no tiene sus partes.
        Assert.False(SigningRules.TryRead(sello, out _, out _, out _));

        // Ni decodificándolo a mano sale nada del sujeto — es un MAC, no un envoltorio.
        var crudo = Convert.FromBase64String(
            sello.Replace('-', '+').Replace('_', '/').PadRight(sello.Length + (4 - sello.Length % 4) % 4, '='));
        Assert.DoesNotContain(sujeto, System.Text.Encoding.UTF8.GetString(crudo), StringComparison.OrdinalIgnoreCase);
    }

    // ── La cuarta: se verifica contra el sujeto ──────────────────────────────

    [Fact]
    public void Un_sello_valido_de_OTRO_sujeto_no_cuadra()
    {
        // Lo que impide que el índice sea la autoridad: quien consiga escribir en el almacén de
        // certificados podría poner un id real junto al nombre que quiera.
        var (svc, _, _) = Armar(Llave());
        var deTercero = svc.Seal(Proposito, "curso-7|alumno-3").Value.Seal;

        var ajeno = svc.VerifySeal(Proposito, "curso-7|alumno-9", deTercero);

        Assert.False(ajeno.IsOk);
        Assert.Equal("signing.seal_mismatch", ajeno.Rejection!.Code);
    }

    [Fact]
    public void Verificar_pide_el_contenido_y_no_solo_el_sello()
    {
        // Si esto dejara de ser cierto, el sello se estaría verificando solo — que es verificar
        // una firma, no comprobar una credencial.
        var (svc, _, _) = Armar(Llave());
        var sello = svc.Seal(Proposito, "curso-7|alumno-3").Value.Seal;

        Assert.Equal("signing.empty_payload", svc.VerifySeal(Proposito, null, sello).Rejection!.Code);
        Assert.Equal("signing.seal_required", svc.VerifySeal(Proposito, "curso-7|alumno-3", "  ").Rejection!.Code);
    }

    // ── Rotación: lo que hace que no haga falta migrar ───────────────────────

    [Fact]
    public void Rotar_la_llave_no_invalida_lo_ya_sellado()
    {
        // El punto delicado del hallazgo #45: con otra llave, cada id emitido cambia y todo QR
        // impreso dejaría de verificar. Se evita probando TODAS las llaves del propósito, no
        // solo la vigente — que es lo que "retirar deja de emitir pero sigue verificando"
        // significa aplicado al sello.
        var vieja = Llave("k-vieja", creada: Ahora.AddDays(-30));
        var (svc, store, _) = Armar(vieja);

        var emitido = svc.Seal(Proposito, "curso-7|alumno-3").Value;
        Assert.Equal("k-vieja", emitido.KeyId);

        store.Put(vieja with { RetiredAtUtc = Ahora });
        store.Put(Llave("k-nueva", creada: Ahora));

        // Lo nuevo se emite con la nueva…
        Assert.Equal("k-nueva", svc.Seal(Proposito, "curso-7|alumno-4").Value.KeyId);

        // …y lo viejo sigue cuadrando, diciendo con cuál cuadró.
        var comprobado = svc.VerifySeal(Proposito, "curso-7|alumno-3", emitido.Seal);
        Assert.True(comprobado.IsOk);
        Assert.Equal("k-vieja", comprobado.Value);
    }

    [Fact]
    public void Sellar_exige_llave_VIGENTE_y_verificar_no()
    {
        // Con todas retiradas ya no se emite —sería sellar con algo que se decidió no usar— pero
        // sí se comprueba, porque lo emitido ayer sigue en manos de su titular.
        var retirada = Llave("k1", retirada: Ahora.AddDays(-1));
        var (svc, _, _) = Armar(retirada);

        var sello = SigningRules.Seal(retirada, "curso-7|alumno-3");

        Assert.Equal("signing.no_active_key", svc.Seal(Proposito, "curso-7|alumno-3").Rejection!.Code);
        Assert.True(svc.VerifySeal(Proposito, "curso-7|alumno-3", sello).IsOk);
    }

    [Fact]
    public void Sin_ninguna_llave_lo_dice_en_vez_de_parecer_un_sello_falso()
    {
        // Un despliegue a medio configurar tiene que verse como tal. Confundirlo con "este sello
        // no cuadra" manda a buscar un ataque donde falta un paso de despliegue.
        var (svc, _, _) = Armar();

        var sinLlaves = svc.VerifySeal(Proposito, "curso-7|alumno-3", "loquesea");

        Assert.Equal("signing.no_keys_for_purpose", sinLlaves.Rejection!.Code);
    }

    // ── Separación de dominio ────────────────────────────────────────────────

    /// <remarks>
    /// <b>Esto fija una característica; no es un gate, y conviene no confundirlo.</b> La etiqueta
    /// de dominio que <c>Seal</c> mete dentro del MAC no es observable desde fuera —el sello es
    /// opaco por definición—, así que ningún test de comportamiento puede ponerse rojo si alguien
    /// la quita: los dos valores seguirían siendo distintos por la forma de sus cuerpos. Lo que
    /// sí queda clavado acá es que un sello nunca vale como token ni al revés, que es la
    /// consecuencia que se notaría. La etiqueta se defiende con el comentario de
    /// <c>SigningRules.SealDomain</c> y con la revisión, no con este test.
    /// </remarks>
    [Fact]
    public void Un_sello_no_es_un_token_ni_se_puede_usar_como_tal()
    {
        var key = Llave();

        var sello = SigningRules.Seal(key, "curso-7|alumno-3");
        var token = SigningRules.Sign(key, "curso-7|alumno-3", Ahora.AddDays(1));

        Assert.NotEqual(sello, token);
        Assert.DoesNotContain(sello, token, StringComparison.Ordinal);

        // Y presentar un sello donde se espera un token no "casi funciona": no tiene la forma.
        Assert.Equal("signing.malformed_token", SigningRules.Verify(key, sello, Ahora)?.Code);
    }

    [Fact]
    public void Dos_llaves_distintas_sellan_distinto()
    {
        // Si no, el sello no dependería de la llave y "sin la llave no se puede calcular" sería
        // falso — que es la propiedad entera.
        var a = Llave("k1");
        var b = Llave("k2");

        Assert.NotEqual(SigningRules.Seal(a, "curso-7|alumno-3"), SigningRules.Seal(b, "curso-7|alumno-3"));
        Assert.False(SigningRules.SealMatches(b, "curso-7|alumno-3", SigningRules.Seal(a, "curso-7|alumno-3")));
    }

    [Fact]
    public void El_sello_sobrevive_a_un_contenido_con_el_separador()
    {
        // El sujeto real ES "curso|alumno". Si el separador rompiera el armado, (a, b|c) y
        // (a|b, c) podrían colapsar en el mismo sello — dos alumnos con la misma credencial.
        var key = Llave();

        var uno = SigningRules.Seal(key, "a.b|c");
        var otro = SigningRules.Seal(key, "a|b.c");

        Assert.NotEqual(uno, otro);
        Assert.True(SigningRules.SealMatches(key, "a.b|c", uno));
        Assert.False(SigningRules.SealMatches(key, "a|b.c", uno));
    }
}

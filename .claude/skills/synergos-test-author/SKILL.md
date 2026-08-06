---
name: synergos-test-author
description: Escribe tests xUnit para seams de Synergos CMS siguiendo los 4 casos canónicos del proyecto — empty, happy, filter, idempotent (ADR 0075). Conoce los frameworks (xUnit + NSubstitute + FluentAssertions), las trampas de NSubstitute en Umbraco, y los patrones de mock para IBundleRegistryClient, ISynHostEmitter, IBrandingProvider e IAuditTrailWriter. Invocar cuando se crea un nuevo seam o se modifica uno existente.
model: claude-opus-4-8
---

# SYNERGOS Test Author — escribir tests xUnit para seams

Cada nuevo seam en Synergos debe llegar con tests. El gate fue levantado en la Ola 190 (post-migración completa) pero la expectativa sigue: ADR 0075 establece que cada seam nuevo tiene al menos los 4 casos canónicos.

---

## 0. Ubicación y estructura del proyecto de tests

```
Synergos.CMS/
└── Synergos.CMS.Tests/
    ├── Synergos.CMS.Tests.csproj
    ├── Application/
    │   ├── BundleRegistry/       ← tests de IBundleRegistryClient
    │   ├── Analytics/            ← tests de IAnalyticsTracker
    │   └── Audit/                ← tests de IAuditTrailWriter
    ├── Infrastructure/
    │   ├── SynHost/              ← tests de ISynHostEmitter
    │   └── Branding/             ← tests de IBrandingProvider
    └── Web/
        └── Composers/            ← tests de composers si aplica
```

---

## 1. Frameworks y versiones

```xml
<!-- Del .csproj — no cambiar versiones sin verificar compatibilidad con Umbraco 13 -->
<PackageReference Include="xunit" Version="2.x.x" />
<PackageReference Include="NSubstitute" Version="5.x.x" />
<PackageReference Include="FluentAssertions" Version="6.x.x" />
<PackageReference Include="Microsoft.NET.Test.Sdk" />
```

---

## 2. Los 4 casos canónicos (ADR 0075)

Cada seam nuevo debe tener al menos:

| Caso | Test name suffix | Descripción |
|------|-----------------|-------------|
| `empty` | `_WhenEmpty_Returns…` | Input vacío / null / lista vacía → resultado vacío/default válido |
| `happy` | `_WhenValid_Returns…` | Input completo y correcto → resultado esperado |
| `filter` | `_WhenFiltered_Returns…` | Input parcial (algunos nulos/inválidos) → solo los válidos pasan |
| `idempotent` | `_IsIdempotent` | Llamar 2+ veces con el mismo input → mismo resultado sin side effects |

---

## 3. Template base de clase de test

```csharp
using FluentAssertions;
using NSubstitute;
using Xunit;
using Synergos.CMS.Interfaces;
// ... otros using según el seam

namespace Synergos.CMS.Tests.Application.{Seam};

public class {SeamName}Tests
{
    private readonly {ISeamInterface} _sut;
    // Dependencias mockeadas
    private readonly {IDependency} _{dep};

    public {SeamName}Tests()
    {
        _{dep} = Substitute.For<{IDependency}>();
        _sut   = new {ConcreteImplementation}(_{dep});
    }

    // ── empty ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task {Method}_WhenEmpty_Returns{Expected}()
    {
        // Arrange — input vacío / null
        var input = {EmptyInput};

        // Act
        var result = await _sut.{Method}(input);

        // Assert
        result.Should().Be{ExpectedEmpty}();
    }

    // ── happy ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task {Method}_WhenValid_Returns{Expected}()
    {
        // Arrange
        var input   = {ValidInput};
        var expected = {ExpectedOutput};
        _{dep}.{DepMethod}(Arg.Any<{T}>()).Returns(expected);

        // Act
        var result = await _sut.{Method}(input);

        // Assert
        result.Should().Be(expected);
        await _{dep}.Received(1).{DepMethod}(Arg.Is<{T}>(x => x.{Prop} == {Value}));
    }

    // ── filter ────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task {Method}_WhenInvalidInput_Returns{EmptyOrDefault}(string? input)
    {
        // Act
        var result = await _sut.{Method}(input);

        // Assert
        result.Should().Be{EmptyOrDefault}();
        await _{dep}.DidNotReceive().{DepMethod}(Arg.Any<{T}>());
    }

    // ── idempotent ────────────────────────────────────────────────────────────

    [Fact]
    public async Task {Method}_IsIdempotent()
    {
        // Arrange
        var input   = {ValidInput};
        _{dep}.{DepMethod}(Arg.Any<{T}>()).Returns({ExpectedOutput});

        // Act — llamar 2 veces
        var result1 = await _sut.{Method}(input);
        var result2 = await _sut.{Method}(input);

        // Assert — mismo resultado, sin side effects extra
        result1.Should().BeEquivalentTo(result2);
        await _{dep}.Received(2).{DepMethod}(Arg.Any<{T}>());  // exactamente 2, no más
    }
}
```

---

## 4. Mocks de seams canónicos de Synergos

### IBundleRegistryClient

```csharp
var bundleRegistry = Substitute.For<IBundleRegistryClient>();

// Happy: bundle encontrado
bundleRegistry
    .GetBundleAsync("accordion", Arg.Any<CancellationToken>())
    .Returns(Task.FromResult<BundleEntry?>(new BundleEntry
    {
        Name    = "accordion",
        Tag     = "synergos-accordion",
        MainUrl = "/cdn-bundles/synergos/accordion/angular/latest/main.js",
        Integrity = "sha384-abc123"
    }));

// Empty: bundle no encontrado
bundleRegistry
    .GetBundleAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
    .Returns(Task.FromResult<BundleEntry?>(null));
```

### ISynHostEmitter

```csharp
var emitter = Substitute.For<ISynHostEmitter>();

// Happy: emitir HTML
var fakeResult = new SynHostEmitResult(
    ScriptHtml: "<script src=\"/cdn-bundles/synergos/accordion/angular/latest/main.js\"></script>",
    ElementHtml: "<synergos-accordion heading=\"Test\"></synergos-accordion>"
);
emitter
    .EmitAsync(Arg.Is<SynHostEmitRequest>(r => r.BlockAlias == "accordion"))
    .Returns(Task.FromResult(fakeResult));

// Stub/fallback
emitter
    .EmitAsync(Arg.Any<SynHostEmitRequest>())
    .Returns(Task.FromResult(SynHostEmitResult.Empty));
```

### IBrandingProvider

```csharp
var brandingProvider = Substitute.For<IBrandingProvider>();
brandingProvider.GetBranding().Returns(new BrandingContext
{
    SiteKey   = Guid.NewGuid(),
    SiteName  = "Test Site",
    ThemeColor = "#0F58A7"
});
```

### IAuditTrailWriter (append-only — verificar que no muta)

```csharp
var auditWriter = Substitute.For<IAuditTrailWriter>();

// Verificar que se llamó exactamente una vez por operación
await auditWriter.Received(1).WriteAsync(
    Arg.Is<AuditEntry>(e =>
        e.EventType == "ContentPublished" &&
        e.EntityKey  == expectedKey));

// Verificar que NO se llamó (operación read-only no debe auditar)
await auditWriter.DidNotReceive().WriteAsync(Arg.Any<AuditEntry>());
```

### IAnalyticsTracker (fire-and-forget — verificar disparo no bloqueo)

```csharp
var analyticsTracker = Substitute.For<IAnalyticsTracker>();

// Verificar que se disparó el evento
analyticsTracker.Received(1).Track(
    Arg.Is<AnalyticsEvent>(e =>
        e.Name == "page_view" &&
        e.Properties.ContainsKey("url")));
```

---

## 5. TRAMPA NSubstitute — Returns dentro de Returns

**Patrón prohibido:**

```csharp
// MAL — configura el substitute DENTRO del argumento de otro Returns
// NSubstitute confunde el "last call" tracker y el mock queda mal configurado
var sut = new MyService(
    bundleRegistry.GetBundleAsync("x").Returns(  // ← esto interfiere con el tracker externo
        someOtherSub.Method().Returns(value)));   // ← never do this
```

**Patrón correcto:**

```csharp
// BIEN — construir el substitute por separado ANTES de pasarlo como argumento
var innerResult = new BundleEntry { Name = "accordion" };
bundleRegistry
    .GetBundleAsync("accordion", Arg.Any<CancellationToken>())
    .Returns(Task.FromResult<BundleEntry?>(innerResult));

var sut = new MyService(bundleRegistry);
```

---

## 6. Tests para seams con cultura (Variations=Culture)

Cuando el seam maneja propiedades culture-variant, usar `CultureInfo` en los tests:

```csharp
[Theory]
[InlineData("es-co")]
[InlineData("en-us")]
public async Task Render_ReturnsLocalizedContent_ForCulture(string culture)
{
    // Arrange
    var cultureInfo = new CultureInfo(culture);
    Thread.CurrentThread.CurrentUICulture = cultureInfo;

    // El request lleva la cultura
    var request = new SynHostEmitRequest(
        BlockAlias:           "hero-banner",
        Props:                new Dictionary<string, object?> { ["heading"] = $"Heading in {culture}" },
        ConfigOverrideJson:   null,
        Culture:              cultureInfo);

    // Act + Assert
    var result = await _sut.EmitAsync(request);
    result.ElementHtml.Should().Contain(culture);
}
```

---

## 7. Tests para IOptionsMonitor (Polly hot-reload)

Para seams que usan `IOptionsMonitor<T>`:

```csharp
var optionsMonitor = Substitute.For<IOptionsMonitor<BundleRegistrySettings>>();
optionsMonitor.CurrentValue.Returns(new BundleRegistrySettings
{
    Mode        = "FileSystem",
    LocalPath   = @"C:\LOCAL_CDN",
    RegistryFileName = "registry.json"
});

// Simular cambio de configuración en caliente
optionsMonitor.OnChange(Arg.Invoke(new BundleRegistrySettings { Mode = "Stub" }));
```

---

## 8. Convenciones de naming de tests

```
{SeamName}Tests.cs
  · {Method}_WhenEmpty_ReturnsEmpty
  · {Method}_WhenNull_ThrowsArgumentNullException   (si aplica)
  · {Method}_WhenValid_ReturnsExpected
  · {Method}_WhenOneItemNull_ReturnsOnlyValid        (filter case)
  · {Method}_IsIdempotent
  · {Method}_WhenDependencyFails_Returns{Fallback}   (resilience)
  · {Method}_WhenCancelled_ThrowsOperationCanceledException
```

---

## 9. Ejecutar los tests

```powershell
$testProject = "Synergos.CMS\Synergos.CMS.Tests\Synergos.CMS.Tests.csproj"

# Todos
dotnet test $testProject --logger "console;verbosity=normal"

# Solo un seam específico
dotnet test $testProject --filter "FullyQualifiedName~BundleRegistry" --logger "console;verbosity=normal"

# Con cobertura
dotnet test $testProject --collect:"XPlat Code Coverage" --results-directory coverage/

# Ver resumen
dotnet test $testProject --logger "trx;LogFileName=results.trx"
```

**Resultado esperado:** `Passed: 111+` (la suite completa pre-Ola). Si algún test falla después de los cambios, no cerrar la Ola (`synergos-ola-close` lo verifica).

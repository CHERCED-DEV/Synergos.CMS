---
name: synergos-smoke-test
description: Prueba de humo post-deploy de Synergos — verifica que el sitio público renderiza, los custom elements hidratan correctamente (no quedan como HTML comments), los CDN bundles cargan con Content-Type y Cache-Control correctos, el SEO metadata está presente, y la API de Management responde. Ejecutar después de synergos-cdn-build o cualquier cambio de infraestructura.
model: claude-opus-4-8
---

# SYNERGOS Smoke Test — verificación post-deploy

Smoke test enfocado en comportamiento observable de extremo a extremo — no en tests unitarios. Verifica que lo que el usuario final ve en el browser funciona.

---

## 0. Prerequisitos

El CMS debe estar corriendo (`synergos-run-dev`). Si no está corriendo, esta skill no puede ejecutarse.

```powershell
$base = "http://synergos.local:5000"
try {
    Invoke-WebRequest "$base/umbraco/api/keepalive/ping" -UseBasicParsing -TimeoutSec 5 | Out-Null
    Write-Output "CMS OK — iniciando smoke test"
} catch {
    Write-Error "CMS no responde. Ejecutar synergos-run-dev primero."
    exit 1
}
```

---

## 1. Autenticación Management API

```powershell
$auth = Invoke-RestMethod "$base/umbraco/management/api/v1/security/back-office/token" `
    -Method POST -ContentType "application/x-www-form-urlencoded" `
    -Body "grant_type=password&client_id=umbraco-back-office&username=admin%40synergos.local&password=Synergos2026%21"
$token   = $auth.access_token
$headers = @{ "Authorization" = "Bearer $token" }
```

---

## 2. Sitio público — render y estructura HTML

```powershell
$issues = [System.Collections.Generic.List[string]]::new()

try {
    $pub  = Invoke-WebRequest "$base/" -UseBasicParsing -TimeoutSec 10
    $html = $pub.Content

    # 2A. HTTP 200
    if ($pub.StatusCode -ne 200) {
        $issues.Add("Sitio público: HTTP $($pub.StatusCode) (esperado 200)")
    } else {
        Write-Output "✓ Sitio público: HTTP 200"
    }

    # 2B. DOCTYPE y HTML básico
    if ($html -notmatch '<!DOCTYPE html') {
        $issues.Add("HTML: falta DOCTYPE — puede ser error de template Razor")
    }

    # 2C. Custom elements NO son HTML comments placeholder
    # El StubBundleRegistryClient deja: <!-- synergos-{name} placeholder -->
    $placeholders = [regex]::Matches($html, '<!--\s*synergos-[\w-]+\s*(?:placeholder|not found)')
    if ($placeholders.Count -gt 0) {
        $placeholders | ForEach-Object {
            $issues.Add("PLACEHOLDER en HTML: $($_.Value.Trim()) — bundle no resuelto por ISynHostEmitter")
        }
    } else {
        Write-Output "✓ Custom elements: sin placeholders en HTML raíz"
    }

    # 2D. Verificar que hay al menos un custom element synergos-* en el HTML
    $ceMatches = [regex]::Matches($html, '<synergos-[\w-]+')
    Write-Output "  Custom elements encontrados: $($ceMatches.Count)"
    $ceMatches | ForEach-Object { Write-Output "    · $($_.Value)>" }

} catch {
    $issues.Add("Sitio público: no accesible — $($_.Exception.Message)")
}
```

---

## 3. SEO metadata

```powershell
if ($html) {
    # 3A. Title
    $titleMatch = [regex]::Match($html, '<title>(.+?)</title>')
    if ($titleMatch.Success -and $titleMatch.Groups[1].Value -notmatch 'umbraco|error|404') {
        Write-Output "✓ <title>: $($titleMatch.Groups[1].Value)"
    } else {
        $issues.Add("SEO: <title> ausente, vacío o genérico")
    }

    # 3B. og:title
    $ogTitle = [regex]::Match($html, 'property=["\']og:title["\'] content=["\'](.+?)["\']')
    if ($ogTitle.Success) {
        Write-Output "✓ og:title: $($ogTitle.Groups[1].Value)"
    } else {
        $issues.Add("SEO: og:title ausente — compSeo no configurado o no aplicado")
    }

    # 3C. og:description
    $ogDesc = [regex]::Match($html, 'property=["\']og:description["\'] content=["\'](.+?)["\']')
    if ($ogDesc.Success) {
        Write-Output "✓ og:description presente"
    } else {
        Write-Output "  WARN og:description ausente (puede ser intencional)"
    }

    # 3D. Canonical
    $canonical = [regex]::Match($html, '<link rel=["\']canonical["\'] href=["\'](.+?)["\']')
    if ($canonical.Success) {
        Write-Output "✓ canonical: $($canonical.Groups[1].Value)"
    } else {
        Write-Output "  WARN canonical ausente"
    }

    # 3E. lang attribute
    $langAttr = [regex]::Match($html, '<html[^>]+lang=["\']([^"\']+)["\']')
    if ($langAttr.Success) {
        Write-Output "✓ lang: $($langAttr.Groups[1].Value)"
    } else {
        $issues.Add("HTML: falta atributo lang en <html> — compSeo debería setearlo")
    }
}
```

---

## 4. CDN bundles — HTTP, Content-Type, Cache-Control

```powershell
try {
    $reg = Get-Content "C:\LOCAL_CDN\synergos\registry.json" | ConvertFrom-Json
} catch {
    $issues.Add("registry.json no legible — CDN smoke test saltado")
    $reg = $null
}

if ($reg) {
    foreach ($el in @($reg.elements)) {
        $latestVer = $el.implementations.angular.latest
        $latestUrl = "$base/cdn-bundles/synergos/$($el.name)/angular/latest/main.js"
        $versionUrl = "$base/cdn-bundles/synergos/$($el.name)/angular/$latestVer/main.js"

        # 4A. URL /latest/ — debe ser no-cache
        try {
            $r = Invoke-WebRequest $latestUrl -UseBasicParsing -TimeoutSec 8
            $ct = $r.Headers["Content-Type"]
            $cc = $r.Headers["Cache-Control"]
            $kb = [Math]::Round($r.RawContentLength / 1024, 1)

            # Content-Type debe ser application/javascript
            if ($ct -notmatch 'javascript') {
                $issues.Add("$($el.name)/latest: Content-Type '$ct' — esperado application/javascript")
            }
            # Cache-Control en /latest/ debe ser no-cache o must-revalidate
            if ($cc -match 'immutable') {
                $issues.Add("$($el.name)/latest: Cache-Control '$cc' — /latest/ no debe ser immutable")
            }

            Write-Output "✓ $($el.name) ($latestVer): HTTP 200, $kb KB, CC=$cc"
        } catch {
            $issues.Add("$($el.name)/latest: 404 o error — $($_.Exception.Message)")
        }

        # 4B. URL /0.x.x/ — debe ser immutable
        try {
            $rv = Invoke-WebRequest $versionUrl -UseBasicParsing -TimeoutSec 8
            $ccv = $rv.Headers["Cache-Control"]
            if ($ccv -notmatch 'immutable' -and $ccv -notmatch 'max-age=31536000') {
                Write-Output "  WARN $($el.name)/$latestVer: Cache-Control '$ccv' — URL versionada debería ser immutable"
            }
        } catch {
            $issues.Add("$($el.name)/$latestVer: 404 — verificar synergos-cdn-build §5")
        }
    }
}
```

---

## 5. Verificar hidratación de custom elements (page-level)

Si hay content publicado, verificar que los elementos hidratan y no quedan como comentarios:

```powershell
# Obtener la lista de páginas publicadas vía API
try {
    $pages = Invoke-RestMethod "$base/umbraco/management/api/v1/document?skip=0&take=10" `
        -Headers $headers
    $publishedPages = @($pages.items) | Where-Object { $_.variants[0].state -eq "Published" }
    Write-Output "Páginas publicadas: $($publishedPages.Count)"

    # Verificar la primera página publicada
    if ($publishedPages.Count -gt 0) {
        $firstPage = $publishedPages[0]
        # El URL del sitio se construye a partir del slug
        $pageUrl = "$base/"  # ajustar según la estructura del sitio
        try {
            $pageHtml = (Invoke-WebRequest $pageUrl -UseBasicParsing -TimeoutSec 10).Content
            $phpCount = ([regex]::Matches($pageHtml, '<!--\s*synergos-')).Count
            if ($phpCount -gt 0) {
                $issues.Add("HIDRATACIÓN: $phpCount placeholder(s) en $pageUrl — ISynHostEmitter no resolvió bundles")
            } else {
                Write-Output "✓ Hidratación OK en $pageUrl — sin placeholders"
            }
        } catch {
            Write-Output "  WARN no se pudo verificar la URL del sitio"
        }
    }
} catch {
    Write-Output "  WARN no se pudieron obtener páginas publicadas vía API"
}
```

---

## 6. Scripts y assets en HTML — no 404s

```powershell
if ($html) {
    # Buscar todas las URLs de scripts en el HTML
    $scriptSrcs = [regex]::Matches($html, '<script[^>]+src=["\']([^"\']+)["\']') |
        ForEach-Object { $_.Groups[1].Value } |
        Where-Object { $_ -match '^/' -or $_ -match "^$base" }

    $scriptErrors = 0
    foreach ($src in $scriptSrcs | Select-Object -First 20) {
        $url = if ($src -match '^/') { "$base$src" } else { $src }
        try {
            $rs = Invoke-WebRequest $url -UseBasicParsing -TimeoutSec 5
            if ($rs.StatusCode -ne 200) {
                $issues.Add("Script 404: $url")
                $scriptErrors++
            }
        } catch {
            $issues.Add("Script error: $url — $($_.Exception.Message)")
            $scriptErrors++
        }
    }

    if ($scriptErrors -eq 0) {
        Write-Output "✓ Scripts: $($scriptSrcs.Count) encontrados, 0 errores (checked first 20)"
    }
}
```

---

## 7. Reporte final

```powershell
Write-Output ""
Write-Output "═══════════════════════════════════════════════════════════"
Write-Output "  SYNERGOS Smoke Test — $(Get-Date -Format 'yyyy-MM-dd HH:mm')"
Write-Output "═══════════════════════════════════════════════════════════"

if ($issues.Count -eq 0) {
    Write-Output "  Estado: PASS — todos los checks superados"
} else {
    Write-Output "  Estado: FAIL — $($issues.Count) issue(s) encontrados"
    $issues | ForEach-Object { Write-Output "  ✗ $_" }
}

Write-Output ""
Write-Output "  Checks ejecutados:"
Write-Output "    · Sitio público render"
Write-Output "    · Custom elements hydration"
Write-Output "    · SEO metadata (title, og:title, canonical, lang)"
Write-Output "    · CDN bundles (HTTP 200, Content-Type, Cache-Control)"
Write-Output "    · Scripts 404 check"
Write-Output "═══════════════════════════════════════════════════════════"
```

---

## 8. Issues frecuentes y soluciones

| Síntoma | Causa | Solución |
|---------|-------|----------|
| `<!-- synergos-* placeholder -->` en HTML | Bundle no en registry.json o FileSystemBundleRegistryClient en Stub mode | Verificar `BundleRegistry:Mode=FileSystem` + ejecutar synergos-cdn-build |
| Content-Type `text/plain` en bundles | Static files middleware no configurado para `.js` | Revisar `Program.cs` — verificar `UseStaticFiles()` con FileExtensionContentTypeProvider |
| Cache-Control `immutable` en `/latest/` | Bug en el middleware de static files | Verificar la config de `OnPrepareResponse` en el hosting del CDN local |
| `<title>` genérico o vacío | compSeo no está en la composition del DocType | Agregar compSeo como composition en el PageType correspondiente |
| Script 404 | Bundle referenciado en HTML pero no publicado en LOCAL_CDN | Ejecutar synergos-cdn-build para el elemento faltante |
| HTTP 500 en sitio | Excepción en Razor (modelo nulo, alias mal escrito) | Revisar logs de dotnet run; buscar `throw` / `NullReferenceException` |

---

## 9. Alcance de esta skill vs `synergos-app-verify` (complementarias, no duplicadas)

Esta skill (`synergos-smoke-test`) opera a **nivel HTTP / placeholder** desde PowerShell: inspecciona el HTML crudo del servidor, headers de los bundles CDN, metadata SEO y ausencia de comentarios placeholder (`<!-- synergos-* placeholder -->`). **No ejecuta JavaScript ni monta un DOM real** — un custom element puede pasar el smoke test (aparece como `<synergos-*>` sin placeholder) y aun así **no hidratar** en el navegador (export faltante en `sg-shared.js`, `customElements.get()` undefined, fetch en constructor leyendo input default, etc.).

Para la verificación **a nivel navegador / DOM** usar la skill **`synergos-app-verify`**, que cubre lo que el smoke test HTTP no puede ver:

- **Hidratación real** vía `customElements.get('<tag>')` + import forzado del bundle — confirma que el elemento definió su clase, no solo que el tag existe en el HTML.
- **Leak-scan del DOM renderizado** — busca `undefined` / `NaN` / `[object Object]` / claves crudas filtradas en el texto ya hidratado (bugs de shape backend↔UI que el HTML de servidor no muestra).
- **Responsive a 375px** — layout móvil real, no solo markup.
- **7 temas por-siteRoot** (dark / eventsNight / silverGold / scholar / terraLux / meridian / light) — contraste y tokens de tema que solo se rompen en el navegador con el CSS aplicado.

**Regla práctica:** correr `synergos-smoke-test` primero (rápido, HTTP, atrapa infra/placeholder/CDN/SEO); si pasa y el cambio toca UI hidratada o temas, correr `synergos-app-verify` para confirmar el comportamiento real en el DOM. Un PASS aquí **no** garantiza hidratación ni congruencia visual — para eso está `synergos-app-verify`.

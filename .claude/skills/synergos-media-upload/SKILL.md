---
name: synergos-media-upload
description: Genera una imagen PNG y la sube a la biblioteca de medios de Umbraco CMS vía Management API. Usar cuando se necesita crear una imagen (hero, og:image, thumbnail, avatar, logo placeholder) y registrarla en Umbraco para que esté disponible en MediaPicker3. Retorna el mediaKey GUID y el UDI. Requiere que el CMS esté corriendo en http://synergos.local:5000.
model: claude-opus-4-8
---

# SYNERGOS Media Upload — generación y upload de imágenes a Umbraco

Esta skill crea una imagen PNG desde cero usando PowerShell + GDI+ y la registra en la biblioteca de medios de Umbraco 13 vía Management API. Al finalizar retorna el `mediaKey` (GUID) y el UDI `umb://media/{key}` listos para usar en cualquier campo `MediaPicker3` o `ImageCropper`.

> ## ⚠️ AVISO (ADR 0093) — El upload vía Management API NO funciona en Umbraco 13
> Umbraco 13 **no tiene Management API** (`/umbraco/management/api/v1/media` → 404; el paquete `Umbraco.Cms.Api.Management` empieza en v14). El flujo de upload de las §3-§5 (token + multipart a `/v1/media`) **no funciona en este stack**. La generación de la imagen PNG (GDI+) sigue siendo útil; pero el registro en Umbraco debe hacerse **server-side con `IMediaService`** (crear nodo Media, set `umbracoFile` + `altDefault`, `Save`, devolver el JSON del MediaPicker3). Ese seam **aún no existe** — es el incremento pendiente del path de autoría (ver `synergos-content-fill` + ADR 0093). Hasta entonces, subir media manualmente por el backoffice clásico (`/umbraco` → Media).

## 0. Parámetros de entrada

Cuando se activa esta skill, extrae o infiere del mensaje del usuario:

| Parámetro | Descripción | Default |
|-----------|-------------|---------|
| `$title` | Texto que aparecerá en la imagen generada | Nombre del contenido |
| `$subtitle` | Texto secundario (opcional) | — |
| `$width` | Ancho en píxeles | 1200 |
| `$height` | Alto en píxeles | 630 |
| `$bgColorHex` | Color de fondo en hex | `#0F58A7` |
| `$altText` | Alt text para accesibilidad | Igual que `$title` |
| `$folderPath` | Ruta de carpeta en Media Library | Raíz |
| `$fileName` | Nombre del archivo (sin extensión) | slug del título |

## 1. Pre-flight — verificar que el CMS está corriendo

Antes de cualquier llamada API, verifica que Umbraco responde:

```powershell
try {
    $ping = Invoke-WebRequest -Uri "http://synergos.local:5000/umbraco/api/keepalive/ping" `
        -Method GET -UseBasicParsing -TimeoutSec 5
    Write-Output "CMS OK — status $($ping.StatusCode)"
} catch {
    Write-Error "CMS no responde en http://synergos.local:5000. Arrancar la app primero con 'dotnet run' desde Synergos.CMS.Web."
    exit 1
}
```

Si el CMS no responde: detener y avisar al usuario que debe arrancar el server. No continuar.

## 2. Generación de la imagen PNG

Usa PowerShell con `System.Drawing` (GDI+, disponible en Windows 11 / PowerShell 5.1):

```powershell
Add-Type -AssemblyName System.Drawing

function New-SynImage {
    param(
        [string]$Title,
        [string]$Subtitle = "",
        [int]$Width = 1200,
        [int]$Height = 630,
        [string]$BgColorHex = "#0F58A7",
        [string]$OutputPath
    )

    $bmp = New-Object System.Drawing.Bitmap($Width, $Height)
    $g   = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode      = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.TextRenderingHint  = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
    $g.InterpolationMode  = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic

    # Fondo sólido
    $bg = [System.Drawing.ColorTranslator]::FromHtml($BgColorHex)
    $g.Clear($bg)

    # Overlay sutil (cuarto inferior oscuro para legibilidad)
    $overlayBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(60, 0, 0, 0))
    $g.FillRectangle($overlayBrush, 0, ($Height * 0.65), $Width, ($Height * 0.35))
    $overlayBrush.Dispose()

    # Título principal
    $titleSize  = [Math]::Max(36, [Math]::Min(64, $Width / 18))
    $fontTitle  = New-Object System.Drawing.Font("Segoe UI", $titleSize, [System.Drawing.FontStyle]::Bold)
    $sfCenter   = New-Object System.Drawing.StringFormat
    $sfCenter.Alignment     = [System.Drawing.StringAlignment]::Center
    $sfCenter.LineAlignment = [System.Drawing.StringAlignment]::Center

    $titleRect = if ($Subtitle) {
        New-Object System.Drawing.RectangleF(60, 60, ($Width - 120), ($Height * 0.55))
    } else {
        New-Object System.Drawing.RectangleF(60, 60, ($Width - 120), ($Height - 120))
    }
    $g.DrawString($Title, $fontTitle, [System.Drawing.Brushes]::White, $titleRect, $sfCenter)
    $fontTitle.Dispose()

    # Subtítulo (si existe)
    if ($Subtitle) {
        $subtitleSize = [Math]::Max(20, $titleSize * 0.55)
        $fontSub  = New-Object System.Drawing.Font("Segoe UI", $subtitleSize, [System.Drawing.FontStyle]::Regular)
        $subRect  = New-Object System.Drawing.RectangleF(60, ($Height * 0.62), ($Width - 120), ($Height * 0.3))
        $subBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(220, 255, 255, 255))
        $g.DrawString($Subtitle, $fontSub, $subBrush, $subRect, $sfCenter)
        $fontSub.Dispose()
        $subBrush.Dispose()
    }

    # Logo mark: franja de acento inferior izquierda
    $accentBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 255, 255, 255))
    $g.FillRectangle($accentBrush, 60, ($Height - 20), 80, 8)
    $accentBrush.Dispose()

    $g.Flush()
    $g.Dispose()
    $bmp.Save($OutputPath, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    Write-Output "Imagen generada: $OutputPath"
}

# Ejecutar
$slug     = ($Title -replace '[^a-zA-Z0-9]', '-').ToLower() -replace '-+', '-'
$tmpPath  = [System.IO.Path]::Combine($env:TEMP, "syn-$slug-$([System.Guid]::NewGuid().ToString('N').Substring(0,8)).png")
New-SynImage -Title $title -Subtitle $subtitle -Width $width -Height $height `
             -BgColorHex $bgColorHex -OutputPath $tmpPath
```

**Si `System.Drawing` no carga** (raro en Win11 PS5.1 pero posible si GDI+ está deshabilitado):
- Fallback: generar SVG como texto plano y subirlo al MediaType `synImage` (SVG es aceptado).
- SVG fallback:
```powershell
$svgContent = @"
<svg xmlns="http://www.w3.org/2000/svg" width="$width" height="$height">
  <rect width="$width" height="$height" fill="$bgColorHex"/>
  <rect x="0" y="$([int]($height*0.65))" width="$width" height="$([int]($height*0.35))" fill="rgba(0,0,0,0.35)"/>
  <text x="$([int]($width/2))" y="$([int]($height/2))" font-family="Segoe UI,Arial,sans-serif"
        font-size="60" font-weight="bold" fill="white" text-anchor="middle" dominant-baseline="middle">$title</text>
</svg>
"@
$tmpPath = [System.IO.Path]::Combine($env:TEMP, "syn-$slug.svg")
[System.IO.File]::WriteAllText($tmpPath, $svgContent, [System.Text.Encoding]::UTF8)
```

## 3. Autenticación con Umbraco Management API

```powershell
$baseUrl  = "http://synergos.local:5000"
$authUri  = "$baseUrl/umbraco/management/api/v1/security/back-office/token"

$authBody = "grant_type=password&client_id=umbraco-back-office" +
            "&username=admin%40synergos.local&password=Synergos2026%21"

try {
    $authResp = Invoke-RestMethod -Uri $authUri -Method POST `
        -ContentType "application/x-www-form-urlencoded" `
        -Body $authBody
    $token = $authResp.access_token
    Write-Output "Auth OK — token expira en $($authResp.expires_in)s"
} catch {
    Write-Error "Auth fallida: $($_.Exception.Message). Verificar que el CMS está corriendo y las credenciales en appsettings.Development.json."
    exit 1
}

$headers = @{
    "Authorization" = "Bearer $token"
    "Accept"        = "application/json"
}
```

## 4. Crear nodo media en Umbraco

El MediaType canónico para imágenes es `synImage` con Key `bcc6d08c-509e-4ab6-8d8b-c00c6199253f`.

```powershell
$mediaPayload = @{
    contentTypeKey = "bcc6d08c-509e-4ab6-8d8b-c00c6199253f"
    parentKey      = $null   # null = raíz de Media Library; reemplazar con GUID si va en carpeta
    values         = @(
        @{ alias = "altDefault"; value = $altText; culture = $null; segment = $null }
    )
} | ConvertTo-Json -Depth 5 -Compress

try {
    $mediaNode = Invoke-RestMethod -Uri "$baseUrl/umbraco/management/api/v1/media" `
        -Method POST `
        -Headers $headers `
        -ContentType "application/json; charset=utf-8" `
        -Body ([System.Text.Encoding]::UTF8.GetBytes($mediaPayload))

    $mediaKey = $mediaNode.id   # GUID del nodo media recién creado
    Write-Output "Nodo media creado: $mediaKey"
} catch {
    Write-Error "Error creando nodo media: $($_.Exception.Message)"
    # Intentar leer el response body para más detalles
    if ($_.Exception.Response) {
        $reader = [System.IO.StreamReader]::new($_.Exception.Response.GetResponseStream())
        Write-Error "Response body: $($reader.ReadToEnd())"
    }
    exit 1
}
```

**Nota sobre `parentKey`:** Si el usuario especificó una carpeta en Media Library, primero busca la carpeta por nombre:
```powershell
$folders = Invoke-RestMethod -Uri "$baseUrl/umbraco/management/api/v1/media?skip=0&take=100" `
    -Headers $headers
$folder = $folders.items | Where-Object { $_.variants[0].name -eq $folderName }
$parentKey = $folder?.id  # null si no se encontró (va a raíz)
```

## 5. Subir el archivo de imagen al nodo media

Umbraco espera un `multipart/form-data` con el campo `file`. Usa `System.Net.Http`:

```powershell
Add-Type -AssemblyName System.Net.Http

$httpClient = [System.Net.Http.HttpClient]::new()
$httpClient.DefaultRequestHeaders.Add("Authorization", "Bearer $token")

$multipart  = [System.Net.Http.MultipartFormDataContent]::new()
$fileBytes  = [System.IO.File]::ReadAllBytes($tmpPath)
$byteContent = [System.Net.Http.ByteArrayContent]::new($fileBytes)

$ext = [System.IO.Path]::GetExtension($tmpPath).TrimStart('.')
$mime = if ($ext -eq "svg") { "image/svg+xml" } else { "image/png" }
$byteContent.Headers.ContentType = [System.Net.Http.Headers.MediaTypeHeaderValue]::new($mime)

$multipart.Add($byteContent, "file", [System.IO.Path]::GetFileName($tmpPath))

$uploadUri  = "$baseUrl/umbraco/management/api/v1/media/$mediaKey/file"
$uploadTask = $httpClient.PostAsync($uploadUri, $multipart)
$uploadResp = $uploadTask.GetAwaiter().GetResult()

if ($uploadResp.IsSuccessStatusCode) {
    Write-Output "Imagen subida correctamente — status $([int]$uploadResp.StatusCode)"
} else {
    $body = $uploadResp.Content.ReadAsStringAsync().GetAwaiter().GetResult()
    Write-Error "Upload fallido ($([int]$uploadResp.StatusCode)): $body"
    $httpClient.Dispose()
    exit 1
}

$httpClient.Dispose()
```

## 6. Limpiar archivo temporal

```powershell
if (Test-Path $tmpPath) {
    Remove-Item $tmpPath -Force
    Write-Output "Archivo temporal eliminado: $tmpPath"
}
```

## 7. Retorno — mediaKey y UDI

Al finalizar, reportar:

```
Media creado exitosamente:
  mediaKey : <GUID>
  UDI      : umb://media/<GUID>
  Alt text : <altText>
  Tamaño   : <width>x<height>px
  Nombre   : <fileName>

Para usar en un campo MediaPicker3, el valor JSON del property value es:
  [{"key": "<GUID>", "mediaKey": "<GUID>", "focalPoint": null, "crops": []}]
```

Guardar el `mediaKey` para pasarlo a `synergos-cms-author` cuando se necesite.

## 8. Troubleshooting

| Error | Causa probable | Solución |
|-------|---------------|----------|
| `404 /security/back-office/token` | URL Management API incorrecta en esta build | Verificar que Umbraco 13.13.1 está activo; probar con swagger en `/umbraco/swagger` |
| `401 Unauthorized` | Credenciales incorrectas o sesión expirada | Regenerar token; verificar `UnattendedUserPassword` en appsettings.Development.json |
| `400 Bad Request` en POST /media | `contentTypeKey` incorrecto o payload malformado | Verificar GUID de synImage (`bcc6d08c-509e-4ab6-8d8b-c00c6199253f`) en uSync |
| `System.Drawing` no carga | GDI+ no disponible (raro) | Usar fallback SVG (sección 2) |
| Upload 422 Unprocessable Entity | Archivo corrupto o MIME incorrecto | Verificar que el PNG se generó correctamente con `Test-Path $tmpPath` |
| CMS no responde (pre-flight) | App no arrancada | Ejecutar `dotnet run` en `Synergos.CMS.Web/` |

## 9. Variantes de tamaño comunes

| Uso | Width | Height | Color sugerido |
|-----|-------|--------|---------------|
| OG image / hero social | 1200 | 630 | `#0F58A7` |
| Card thumbnail | 800 | 450 | `#1A3A5C` |
| Avatar / profile | 400 | 400 | `#2C7BE5` |
| Logo placeholder | 300 | 100 | `#FFFFFF` (con texto oscuro — cambiar brush) |
| Favicon base | 512 | 512 | `#0F58A7` |
| Banner wide | 1600 | 400 | `#0A2540` |

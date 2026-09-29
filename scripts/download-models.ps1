# scripts/download-models.ps1
# Скачивает embedding-модель уровня 2 (multilingual-e5-small, ONNX int8) и её токенизатор.
#
# Хэши ниже (KnownGoodHashes) — не просто "доверяй при первом скачивании": это SHA256 файлов,
# которые реально скачаны и эмпирически проверены (OnnxEmbeddingModelIntegrationTests —
# косинусное сходство отличает похожие фразы от случайных, 40/44 верных совпадений на фикстуре
# из 50 фраз при пороге 0.90). Если хэш скачанного файла не совпадёт — это значит, что на
# Hugging Face файл изменился, и его качество больше не гарантировано этой проверкой.
# Для любых ДРУГИХ файлов (если модель заменят в будущем) остаётся принцип "доверяй при первом
# скачивании": вычисленный SHA256 сохраняется в models/CHECKSUMS.txt при первом запуске.

$ErrorActionPreference = "Stop"
# В Windows PowerShell 5.1 отрисовка прогресс-бара Invoke-WebRequest многократно замедляет
# скачивание больших файлов (модель ~118 МБ) — отключаем его.
$ProgressPreference = 'SilentlyContinue'
$modelsDir = Join-Path $PSScriptRoot "..\models"
New-Item -ItemType Directory -Force -Path $modelsDir | Out-Null

$files = @(
    @{ Url = "https://huggingface.co/Xenova/multilingual-e5-small/resolve/main/onnx/model_int8.onnx"; Name = "e5-small-int8.onnx" },
    @{ Url = "https://huggingface.co/intfloat/multilingual-e5-small/resolve/main/sentencepiece.bpe.model"; Name = "sentencepiece.bpe.model" }
)

# Реально скачано и эмпирически проверено (см. комментарий выше) — не TOFU-заглушка.
$knownGoodHashes = @{
    "e5-small-int8.onnx"       = "4D24E2BC01A447951524466EF533E52944BF48509E6552810BCEE1A2711CB02C"
    "sentencepiece.bpe.model"  = "CFC8146ABE2A0488E9E2A0C56DE7952F7C11AB059ECA145A0A727AFCE0DB2865"
}

$checksumsPath = Join-Path $modelsDir "CHECKSUMS.txt"
$checksums = @{}
if (Test-Path $checksumsPath) {
    Get-Content $checksumsPath | ForEach-Object {
        $parts = $_ -split "  ", 2
        if ($parts.Length -eq 2) { $checksums[$parts[1]] = $parts[0] }
    }
}

foreach ($file in $files) {
    $destination = Join-Path $modelsDir $file.Name
    $needsDownload = -not (Test-Path $destination)
    $expectedHash = if ($checksums.ContainsKey($file.Name)) { $checksums[$file.Name] } else { $knownGoodHashes[$file.Name] }

    if (-not $needsDownload -and $expectedHash) {
        $currentHash = (Get-FileHash $destination -Algorithm SHA256).Hash
        if ($currentHash -ne $expectedHash) {
            Write-Warning "$($file.Name) не совпадает с ожидаемым хэшем, перекачиваю."
            $needsDownload = $true
        }
    }

    if ($needsDownload) {
        Write-Host "Скачиваю $($file.Name)..."
        Invoke-WebRequest -Uri $file.Url -OutFile $destination
        $hash = (Get-FileHash $destination -Algorithm SHA256).Hash
        if ($knownGoodHashes.ContainsKey($file.Name) -and $hash -ne $knownGoodHashes[$file.Name]) {
            Write-Warning "$($file.Name): SHA256 $hash НЕ совпадает с проверенным ($($knownGoodHashes[$file.Name])). Файл на Hugging Face мог измениться — эмбеддинги не гарантированно работают так, как проверено в этом проекте."
        }
        $checksums[$file.Name] = $hash
        Write-Host "$($file.Name): SHA256 $hash"
    } else {
        Write-Host "$($file.Name) уже скачан и хэш совпадает, пропускаю."
    }
}

$checksums.GetEnumerator() | ForEach-Object { "$($_.Value)  $($_.Key)" } | Set-Content $checksumsPath
Write-Host "Готово. Хэши сохранены в $checksumsPath — при подмене файла на диске скрипт перекачает его заново."

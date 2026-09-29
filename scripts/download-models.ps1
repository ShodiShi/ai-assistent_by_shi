# scripts/download-models.ps1
# Скачивает embedding-модель уровня 2 (multilingual-e5-small, ONNX int8) и её токенизатор.
# Хэши проверяются по принципу "доверяй при первом скачивании": при первом запуске
# вычисленный SHA256 сохраняется в models/CHECKSUMS.txt; при повторных запусках файл
# перекачивается только если существующий локальный файл не совпадает с сохранённым хэшем.

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

    if (-not $needsDownload -and $checksums.ContainsKey($file.Name)) {
        $currentHash = (Get-FileHash $destination -Algorithm SHA256).Hash
        if ($currentHash -ne $checksums[$file.Name]) {
            Write-Warning "$($file.Name) не совпадает с сохранённым хэшем, перекачиваю."
            $needsDownload = $true
        }
    }

    if ($needsDownload) {
        Write-Host "Скачиваю $($file.Name)..."
        Invoke-WebRequest -Uri $file.Url -OutFile $destination
        $hash = (Get-FileHash $destination -Algorithm SHA256).Hash
        $checksums[$file.Name] = $hash
        Write-Host "$($file.Name): SHA256 $hash"
    } else {
        Write-Host "$($file.Name) уже скачан и хэш совпадает, пропускаю."
    }
}

$checksums.GetEnumerator() | ForEach-Object { "$($_.Value)  $($_.Key)" } | Set-Content $checksumsPath
Write-Host "Готово. Хэши сохранены в $checksumsPath — при подмене файла на диске скрипт перекачает его заново."

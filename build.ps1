# Сборка Wi-Fi Флешки встроенным компилятором .NET Framework (без установки SDK)
$ErrorActionPreference = 'Stop'
try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch { }

$root    = $PSScriptRoot
$srcDir  = Join-Path $root 'src'
$outDir  = Join-Path $root 'bin'
$exe     = Join-Path $outDir 'PhoneDrive.exe'

$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $csc)) {
    $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
if (-not (Test-Path -LiteralPath $csc)) {
    throw 'Не найден компилятор C# (.NET Framework 4). Он входит в состав Windows 10/11.'
}

New-Item -ItemType Directory -Path $outDir -Force | Out-Null

# Если предыдущая версия ещё запущена, её exe занят и компилятор не сможет его
# перезаписать. В этом случае откладываем старый файл в PhoneDrive.old.exe —
# он удалится при следующей сборке, а новый exe займёт штатное место.
$oldExe = Join-Path $outDir 'PhoneDrive.old.exe'
if (Test-Path -LiteralPath $oldExe) { Remove-Item -LiteralPath $oldExe -Force -ErrorAction SilentlyContinue }

$appRunning = $false
if (Test-Path -LiteralPath $exe) {
    try {
        $probe = [System.IO.File]::Open($exe, [System.IO.FileMode]::Open, [System.IO.FileAccess]::ReadWrite, [System.IO.FileShare]::None)
        $probe.Close()
    } catch {
        Move-Item -LiteralPath $exe -Destination $oldExe -Force
        $appRunning = $true
        Write-Host 'Программа запущена: старый exe отложен, новый встанет на его место.' -ForegroundColor Yellow
        Write-Host 'Закройте старое окно и запустите программу заново.' -ForegroundColor Yellow
    }
}

$sources = @(Get-ChildItem -LiteralPath $srcDir -Filter '*.cs' | ForEach-Object { $_.FullName })
if ($sources.Count -eq 0) { throw "В папке src нет исходников: $srcDir" }

$compilerArgs = @(
    '/nologo'
    '/target:winexe'
    '/platform:anycpu'
    '/optimize+'
    '/codepage:65001'
    ('/out:' + $exe)
    ('/win32manifest:' + (Join-Path $srcDir 'app.manifest'))
    ('/win32icon:' + (Join-Path $srcDir 'app.ico'))
    '/reference:System.dll'
    '/reference:System.Core.dll'
    '/reference:System.Drawing.dll'
    '/reference:System.Windows.Forms.dll'
)

Write-Host 'Компиляция...' -ForegroundColor Cyan
& $csc $compilerArgs $sources
if ($LASTEXITCODE -ne 0) { throw "Сборка не удалась (код $LASTEXITCODE)." }

# скрипты должны лежать рядом с exe
$scriptsTarget = Join-Path $outDir 'scripts'
if (-not (Test-Path -LiteralPath $scriptsTarget)) {
    New-Item -ItemType Directory -Path $scriptsTarget -Force | Out-Null
} elseif (-not $appRunning) {
    # при запущенной старой копии не удаляем папку: она ещё может понадобиться ей для очистки
    Remove-Item -LiteralPath $scriptsTarget -Recurse -Force
    New-Item -ItemType Directory -Path $scriptsTarget -Force | Out-Null
}
Copy-Item -Path (Join-Path $root 'scripts\*') -Destination $scriptsTarget -Force

Write-Host ('Готово: ' + $exe) -ForegroundColor Green

# ----------------------------------------------------------------------------
#  PhoneDrive - локальная точка доступа без интернета (Wi-Fi Direct, legacy AP)
#  Работает, даже когда компьютер ни к какой сети не подключён: Windows сама
#  назначает адаптеру 192.168.137.1 и выдаёт телефонам адреса 192.168.137.x.
#  Параметры через переменные окружения:
#     PD_ACTION = start | stop
#     PD_SSID   = имя сети
#     PD_PASS   = пароль сети (8..63 символа)
#     PD_STATUS = файл состояния (по умолчанию %TEMP%\PhoneDrive.wfd.status)
#     PD_STOP   = файл-флаг остановки (по умолчанию %TEMP%\PhoneDrive.wfd.stop)
#  Результат пишется в файл состояния: OK=1 / OK=0 + ERROR=...
# ----------------------------------------------------------------------------

$ErrorActionPreference = 'Stop'
try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch { }

$status = "$env:PD_STATUS"
if ([string]::IsNullOrWhiteSpace($status)) { $status = Join-Path $env:TEMP 'PhoneDrive.wfd.status' }
$stopFile = "$env:PD_STOP"
if ([string]::IsNullOrWhiteSpace($stopFile)) { $stopFile = Join-Path $env:TEMP 'PhoneDrive.wfd.stop' }
$action = "$env:PD_ACTION"
if ([string]::IsNullOrWhiteSpace($action)) { $action = 'start' }

function Write-Status([string]$text) {
    $tmp = $status + '.tmp'
    [System.IO.File]::WriteAllText($tmp, $text, (New-Object System.Text.UTF8Encoding($false)))
    Move-Item -LiteralPath $tmp -Destination $status -Force
}

function Write-Failure([string]$message) {
    Write-Status ('OK=0' + "`n" + 'ERROR=' + (($message -replace "`r", ' ' -replace "`n", ' ').Trim()))
    exit 1
}

# любая неперехваченная ошибка должна вернуться приложению понятным текстом
trap {
    Write-Failure $_.Exception.Message
}

if ($action -eq 'stop') {
    Set-Content -LiteralPath $stopFile -Value '1' -Encoding ASCII
    'OK=1'
    exit 0
}

Write-Status 'STARTING'

try {
    Add-Type -AssemblyName System.Runtime.WindowsRuntime | Out-Null
    $null = [Windows.Devices.WiFiDirect.WiFiDirectAdvertisementPublisher, Windows.Devices.WiFiDirect, ContentType = WindowsRuntime]
    $null = [Windows.Devices.WiFiDirect.WiFiDirectAdvertisement, Windows.Devices.WiFiDirect, ContentType = WindowsRuntime]
    $null = [Windows.Devices.WiFiDirect.WiFiDirectLegacySettings, Windows.Devices.WiFiDirect, ContentType = WindowsRuntime]
    $null = [Windows.Security.Credentials.PasswordCredential, Windows.Security.Credentials, ContentType = WindowsRuntime]

    $ssid = "$env:PD_SSID"
    $pass = "$env:PD_PASS"
    if ([string]::IsNullOrWhiteSpace($ssid)) { Write-Failure 'Не задано имя Wi-Fi сети.' }
    if ($ssid.Length -gt 32) { $ssid = $ssid.Substring(0, 32) }
    if ($pass.Length -lt 8) { Write-Failure 'Пароль Wi-Fi должен быть не короче 8 символов.' }

    $pub = [Windows.Devices.WiFiDirect.WiFiDirectAdvertisementPublisher]::new()
    $pub.Advertisement.IsAutonomousGroupOwnerEnabled = $true
    $pub.Advertisement.LegacySettings.IsEnabled = $true
    $pub.Advertisement.LegacySettings.Ssid = $ssid
    $cred = [Windows.Security.Credentials.PasswordCredential]::new()
    $cred.Password = $pass
    $pub.Advertisement.LegacySettings.Passphrase = $cred
    $pub.Start()
}
catch {
    Write-Failure ('Не удалось создать точку доступа: ' + $_.Exception.Message)
}

# ждём, пока у виртуального адаптера появится адрес: пока его нет,
# телефон не получит IP
$ip = ''
for ($i = 0; $i -lt 80; $i++) {
    $found = @(Get-NetIPAddress -AddressFamily IPv4 -ErrorAction SilentlyContinue |
        Where-Object { $_.IPAddress -like '192.168.137.*' }) | Select-Object -First 1
    if ($found) { $ip = $found.IPAddress; break }
    Start-Sleep -Milliseconds 250
}
if ([string]::IsNullOrEmpty($ip)) { $ip = '192.168.137.1' }

# Windows должна поднять на этом адресе DHCP: без него телефон подключится,
# не получит IP и через несколько секунд отвалится.
$dhcp = $false
for ($i = 0; $i -lt 40; $i++) {
    $listen = @(Get-NetUDPEndpoint -LocalPort 67 -ErrorAction SilentlyContinue |
        Where-Object { $_.LocalAddress -eq $ip })
    if ($listen.Count -gt 0) { $dhcp = $true; break }
    Start-Sleep -Milliseconds 250
}
$dhcpText = '0'
if ($dhcp) { $dhcpText = '1' }

$statusLines = @(
    'OK=1'
    'STATE=On'
    ('SSID=' + $ssid)
    ('HOSTIP=' + $ip)
    ('MODE=wfd')
    ('DHCP=' + $dhcpText)
)
if (-not $dhcp) {
    $statusLines += 'WARN=Windows не запустила DHCP: телефон может не получить адрес.'
}
Write-Status ($statusLines -join "`n")

# Держим точку доступа, пока приложение не попросит остановиться: объект
# объявления живёт только внутри этого процесса. Заодно следим за процессом
# приложения: если оно закрылось аварийно, точка доступа не должна висеть.
$parentPid = 0
if (-not [int]::TryParse("$env:PD_PARENT", [ref]$parentPid)) { $parentPid = 0 }

for ($i = 0; $i -lt 43200; $i++) {
    if (Test-Path -LiteralPath $stopFile) { break }
    if ($parentPid -gt 0) {
        $alive = $true
        try { $null = [System.Diagnostics.Process]::GetProcessById($parentPid) }
        catch { $alive = $false }
        if (-not $alive) { break }
    }
    Start-Sleep -Seconds 1
}

try { $pub.Stop() } catch { }
Remove-Item -LiteralPath $status -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $stopFile -ErrorAction SilentlyContinue
exit 0

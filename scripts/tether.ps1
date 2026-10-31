# ----------------------------------------------------------------------------
#  PhoneDrive - управление точкой доступа Wi-Fi (Windows Mobile Hotspot API)
#  Параметры передаются через переменные окружения:
#     PD_ACTION = on | off | status
#     PD_SSID   = имя сети
#     PD_PASS   = пароль сети (WPA2, 8..63 символа)
#     PD_BAND   = auto | 2.4 | 5  (по умолчанию 2.4: iPhone подключается быстрее)
#  Результат: строки KEY=value (OK=1 / OK=0 + ERROR=...)
# ----------------------------------------------------------------------------

$ErrorActionPreference = 'Stop'
try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch { }

function Write-Failure([string]$message) {
    'OK=0'
    'ERROR=' + (($message -replace "`r", ' ' -replace "`n", ' ').Trim())
    exit 1
}

# любая неперехваченная ошибка должна вернуться приложению понятным текстом
trap {
    'OK=0'
    'ERROR=' + (($_.Exception.Message -replace "`r", ' ' -replace "`n", ' ').Trim())
    exit 1
}

$action = "$env:PD_ACTION"
if ([string]::IsNullOrWhiteSpace($action)) { $action = 'status' }

# --- загрузка WinRT-типов ---------------------------------------------------
try {
    Add-Type -AssemblyName System.Runtime.WindowsRuntime | Out-Null
    $null = [Windows.Networking.Connectivity.NetworkInformation, Windows.Networking.Connectivity, ContentType = WindowsRuntime]
    $null = [Windows.Networking.NetworkOperators.NetworkOperatorTetheringManager, Windows.Networking.NetworkOperators, ContentType = WindowsRuntime]
    $null = [Windows.Networking.NetworkOperators.NetworkOperatorTetheringAccessPointConfiguration, Windows.Networking.NetworkOperators, ContentType = WindowsRuntime]
    $null = [Windows.Networking.NetworkOperators.NetworkOperatorTetheringOperationResult, Windows.Networking.NetworkOperators, ContentType = WindowsRuntime]
    $null = [Windows.Networking.NetworkOperators.TetheringWiFiBand, Windows.Networking.NetworkOperators, ContentType = WindowsRuntime]
}
catch {
    Write-Failure "Не удалось загрузить Windows Runtime: $($_.Exception.Message)"
}

$script:asTaskOperation = $null
$script:asTaskAction = $null
foreach ($m in [System.WindowsRuntimeSystemExtensions].GetMethods()) {
    if ($m.Name -ne 'AsTask') { continue }
    if ($m.GetParameters().Count -ne 1) { continue }
    $pname = $m.GetParameters()[0].ParameterType.Name
    if ($pname -eq 'IAsyncOperation`1' -and $null -eq $script:asTaskOperation) { $script:asTaskOperation = $m }
    if ($pname -eq 'IAsyncAction' -and $null -eq $script:asTaskAction) { $script:asTaskAction = $m }
}

function Await-Operation($operation, [Type]$resultType) {
    $task = $script:asTaskOperation.MakeGenericMethod($resultType).Invoke($null, @($operation))
    $task.Wait(-1) | Out-Null
    return $task.Result
}

function Await-Action($action) {
    $task = $script:asTaskAction.Invoke($null, @($action))
    $task.Wait(-1) | Out-Null
}

# --- выбор профиля подключения, через который будет работать точка доступа --
$profile = $null
try { $profile = [Windows.Networking.Connectivity.NetworkInformation]::GetInternetConnectionProfile() } catch { $profile = $null }

if ($null -eq $profile) {
    try {
        foreach ($p in [Windows.Networking.Connectivity.NetworkInformation]::GetConnectionProfiles()) {
            if ($p.GetNetworkConnectivityLevel().ToString() -ne 'None') { $profile = $p; break }
        }
    } catch { }
}

if ($null -eq $profile) {
    Write-Failure 'Нет активного сетевого подключения. Подключите компьютер к сети (Wi-Fi или кабель) и повторите.'
}

try {
    $mgr = [Windows.Networking.NetworkOperators.NetworkOperatorTetheringManager]::CreateFromConnectionProfile($profile)
}
catch {
    Write-Failure "Windows не смог создать точку доступа: $($_.Exception.Message)"
}

if ($null -eq $mgr) {
    Write-Failure 'Точка доступа недоступна: адаптер Wi-Fi не поддерживает режим Mobile Hotspot.'
}

$resultType = [Windows.Networking.NetworkOperators.NetworkOperatorTetheringOperationResult]

# --- диапазон Wi-Fi ---------------------------------------------------------
# 2.4 ГГц: iPhone находит сеть быстрее, сигнал дальше бьёт, подключение стабильнее.
# 5 ГГц: выше скорость, но короче радиус и дольше поиск сети.
$bandWanted = $null
switch ("$env:PD_BAND".Trim().ToLowerInvariant()) {
    '2.4'    { $bandWanted = 'TwoPointFourGigahertz' }
    '2.4ghz' { $bandWanted = 'TwoPointFourGigahertz' }
    '2g'     { $bandWanted = 'TwoPointFourGigahertz' }
    '5'      { $bandWanted = 'FiveGigahertz' }
    '5ghz'   { $bandWanted = 'FiveGigahertz' }
    '5g'     { $bandWanted = 'FiveGigahertz' }
    'auto'   { $bandWanted = 'Auto' }
}
$bandValue = $null
if ($null -ne $bandWanted) {
    try { $bandValue = [Windows.Networking.NetworkOperators.TetheringWiFiBand]::$bandWanted }
    catch { $bandValue = $null }
}

switch ($action) {

    'on' {
        $state = $mgr.TetheringOperationalState.ToString()

        $ssid = "$env:PD_SSID"
        $pass = "$env:PD_PASS"

        # если запрошен другой диапазон, чем сейчас — точку доступа надо перезапустить,
        # иначе Windows продолжит вещать на прежней частоте
        if ($state -eq 'On' -and $null -ne $bandValue) {
            $currentBand = $null
            try { $currentBand = $mgr.GetCurrentAccessPointConfiguration().Band } catch { }
            if ($null -ne $currentBand -and $currentBand.ToString() -ne $bandValue.ToString()) {
                Await-Operation $mgr.StopTetheringAsync() $resultType | Out-Null
                $state = 'Off'
            }
        }

        if (-not [string]::IsNullOrWhiteSpace($ssid)) {
            if ($ssid.Length -gt 32) { $ssid = $ssid.Substring(0, 32) }
            if ($pass.Length -lt 8) { Write-Failure 'Пароль Wi-Fi должен быть не короче 8 символов.' }
            try {
                $ap = [Windows.Networking.NetworkOperators.NetworkOperatorTetheringAccessPointConfiguration]::new()
                $ap.Ssid = $ssid
                $ap.Passphrase = $pass
                if ($null -ne $bandValue) {
                    try { $ap.Band = $bandValue }
                    catch {
                        'WARN=Сменить диапазон Wi-Fi не удалось: ' + (($_.Exception.Message -replace "`r", ' ' -replace "`n", ' ').Trim())
                    }
                }
                Await-Action $mgr.ConfigureAccessPointAsync($ap)
            }
            catch {
                # не критично: ниже включим с уже сохранёнными настройками Windows
                'WARN=' + (($_.Exception.Message -replace "`r", ' ' -replace "`n", ' ').Trim())
            }
        }

        if ($mgr.TetheringOperationalState.ToString() -ne 'On') {
            $res = Await-Operation $mgr.StartTetheringAsync() $resultType
            if ($res.Status.ToString() -ne 'Success') {
                $extra = ''
                if ($res.AdditionalErrorMessage) { $extra = ' ' + $res.AdditionalErrorMessage }
                Write-Failure ("Не удалось включить точку доступа: " + $res.Status + "." + $extra)
            }
        }

        # ждём, пока у виртуального адаптера появится адрес: пока его нет,
        # телефон не получит IP и будет висеть на «Подключение…»
        $ip = $null
        for ($i = 0; $i -lt 60; $i++) {
            $candidates = @(Get-NetIPAddress -AddressFamily IPv4 -ErrorAction SilentlyContinue |
                Where-Object { $_.IPAddress -notlike '127.*' -and $_.IPAddress -notlike '169.254.*' -and $_.IPAddress -ne '0.0.0.0' })

            $ip = ($candidates | Where-Object { $_.IPAddress -like '192.168.137.*' } | Select-Object -First 1).IPAddress
            if (-not $ip) {
                $ip = ($candidates | Where-Object { $_.InterfaceAlias -like 'Local Area Connection*' } | Select-Object -First 1).IPAddress
            }
            if ($ip) { break }
            Start-Sleep -Milliseconds 250
        }
        if (-not $ip) { $ip = '192.168.137.1' }

        $currentSsid = "$env:PD_SSID"
        try { $currentSsid = $mgr.GetCurrentAccessPointConfiguration().Ssid } catch { }
        if ([string]::IsNullOrWhiteSpace($currentSsid)) { $currentSsid = "$env:PD_SSID" }

        $bandNow = ''
        try { $bandNow = $mgr.GetCurrentAccessPointConfiguration().Band.ToString() } catch { }

        'OK=1'
        'STATE=' + $mgr.TetheringOperationalState.ToString()
        'SSID=' + $currentSsid
        'HOSTIP=' + $ip
        'BAND=' + $bandNow
    }

    'off' {
        $state = $mgr.TetheringOperationalState.ToString()
        if ($state -eq 'On' -or $state -eq 'InTransition') {
            $res = Await-Operation $mgr.StopTetheringAsync() $resultType
            if ($res.Status.ToString() -ne 'Success') {
                $extra = ''
                if ($res.AdditionalErrorMessage) { $extra = ' ' + $res.AdditionalErrorMessage }
                Write-Failure ("Не удалось выключить точку доступа: " + $res.Status + "." + $extra)
            }
        }
        'OK=1'
        'STATE=' + $mgr.TetheringOperationalState.ToString()
    }

    'status' {
        'OK=1'
        'STATE=' + $mgr.TetheringOperationalState.ToString()
        try { 'SSID=' + $mgr.GetCurrentAccessPointConfiguration().Ssid } catch { 'SSID=' }
        try { 'BAND=' + $mgr.GetCurrentAccessPointConfiguration().Band.ToString() } catch { 'BAND=' }
    }

    default {
        Write-Failure "Неизвестное действие: $action"
    }
}

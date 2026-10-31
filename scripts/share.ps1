# ----------------------------------------------------------------------------
#  PhoneDrive - общий доступ к папке по SMB (порт 445) для iPhone
#  Параметры через переменные окружения:
#     PD_ACTION = on | off | cleanup
#     PD_FOLDER   - папка, которую отдаём
#     PD_SHARE    - имя сетевой папки
#     PD_USER     - локальный пользователь для входа с телефона
#     PD_PASSWORD - его пароль
#     PD_SUBNET   - подсеть точки доступа (например 192.168.137.0/24)
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
if ([string]::IsNullOrWhiteSpace($action)) { $action = 'on' }

$folder   = "$env:PD_FOLDER"
$share    = "$env:PD_SHARE"
$user     = "$env:PD_USER"
$password = "$env:PD_PASSWORD"
$subnet   = "$env:PD_SUBNET"
$webPort  = "$env:PD_WEBPORT"

if ([string]::IsNullOrWhiteSpace($share)) { $share = 'PhoneDrive' }
if ([string]::IsNullOrWhiteSpace($user))  { $user  = 'PhoneDrive' }

$ruleName = 'PhoneDrive - SMB (TCP 445)'
$account  = "$env:COMPUTERNAME\$user"

$script:netOutput = ''

# Запуск net.exe с корректным экранированием аргументов (Start-Process склеивает
# массив в строку без кавычек, из-за чего аргументы с пробелами ломают команду)
# и с запоминанием ответа Windows для понятных сообщений об ошибке.
function Invoke-Net([string[]]$arguments) {
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = "$env:WINDIR\System32\net.exe"
    $psi.UseShellExecute = $false
    $psi.CreateNoWindow = $true
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true

    # net.exe пишет в кодировке консоли (OEM), а не в UTF-8 — иначе русский текст ломается
    try {
        $oem = [System.Text.Encoding]::GetEncoding([System.Globalization.CultureInfo]::CurrentCulture.TextInfo.OEMCodePage)
        $psi.StandardOutputEncoding = $oem
        $psi.StandardErrorEncoding = $oem
    } catch { }

    $quoted = @()
    foreach ($a in $arguments) {
        if ($a -match '[\s"]') { $quoted += '"' + ($a -replace '"', '\"') + '"' } else { $quoted += $a }
    }
    $psi.Arguments = $quoted -join ' '

    $p = [System.Diagnostics.Process]::Start($psi)
    $stdout = $p.StandardOutput.ReadToEnd()
    $stderr = $p.StandardError.ReadToEnd()
    $p.WaitForExit()

    $text = ($stdout + ' ' + $stderr).Trim()
    $script:netOutput = (($text -replace "\s+", ' ').Trim())
    if ($script:netOutput.Length -gt 300) { $script:netOutput = $script:netOutput.Substring(0, 300) + '…' }
    return $p.ExitCode
}

function Remove-FirewallRule {
    try {
        # удаляем все правила приложения: и SMB, и веб-доступ
        $rules = @(Get-NetFirewallRule -Group 'PhoneDrive' -ErrorAction SilentlyContinue)
        foreach ($r in $rules) { Remove-NetFirewallRule -Name $r.Name -ErrorAction SilentlyContinue }
    } catch { }
}

function Remove-ShareIfExists {
    param([string]$Name)
    try {
        if (Get-SmbShare -Name $Name -ErrorAction SilentlyContinue) {
            Remove-SmbShare -Name $Name -Force -ErrorAction Stop
        }
    } catch { }
}

function Set-FolderAcl([bool]$grant) {
    try {
        $acl = Get-Acl -LiteralPath $folder
        $rights = [System.Security.AccessControl.FileSystemRights]::Modify
        $inheritance = [System.Security.AccessControl.InheritanceFlags]'ContainerInherit, ObjectInherit'
        $propagation = [System.Security.AccessControl.PropagationFlags]::None
        $allow = [System.Security.AccessControl.AccessControlType]::Allow

        $rule = New-Object System.Security.AccessControl.FileSystemAccessRule(
            $account, $rights, $inheritance, $propagation, $allow)

        if ($grant) {
            $acl.SetAccessRule($rule)
        } else {
            $acl.RemoveAccessRuleAll($rule)
        }
        Set-Acl -LiteralPath $folder -AclObject $acl
    } catch {
        'WARN=Права NTFS на папку изменить не удалось: ' + (($_.Exception.Message -replace "`r", ' ' -replace "`n", ' ').Trim())
    }
}

switch ($action) {

    'on' {
        if ([string]::IsNullOrWhiteSpace($folder)) { Write-Failure 'Не выбрана папка для обмена.' }
        if ([string]::IsNullOrWhiteSpace($password)) { Write-Failure 'Не задан пароль для входа с телефона.' }

        $folder = $folder.TrimEnd('\')
        if (-not (Test-Path -LiteralPath $folder)) {
            New-Item -ItemType Directory -Path $folder -Force | Out-Null
        }
        if (-not (Test-Path -LiteralPath $folder -PathType Container)) {
            Write-Failure "Папка не найдена: $folder"
        }

        # 1. служба SMB-сервера
        try {
            $lanman = Get-Service -Name LanmanServer -ErrorAction SilentlyContinue
            if ($lanman -and $lanman.Status -ne 'Running') { Start-Service -Name LanmanServer -ErrorAction Stop }
        } catch {
            Write-Failure "Служба общего доступа к файлам не запускается: $($_.Exception.Message)"
        }

        # 2. служебный локальный пользователь
        $exists = (Invoke-Net @('user', $user)) -eq 0
        if (-not $exists) {
            if ((Invoke-Net @('user', $user, $password, '/add', '/comment:PhoneDrive', '/expires:never', '/passwordchg:no')) -ne 0) {
                Write-Failure "Не удалось создать пользователя ${user}: $script:netOutput (нужны права администратора или другое имя пользователя)"
            }
        } else {
            if ((Invoke-Net @('user', $user, $password)) -ne 0) {
                Write-Failure "Не удалось задать пароль пользователю ${user}: $script:netOutput"
            }
        }
        if ((Invoke-Net @('user', $user, '/active:yes')) -ne 0) {
            Write-Failure "Не удалось включить пользователя ${user}: $script:netOutput"
        }

        # пароль служебной учётной записи не должен истекать (по умолчанию он живёт 42 дня)
        try {
            Set-LocalUser -Name $user -PasswordNeverExpires $true -ErrorAction Stop
        } catch { }

        # 3. права NTFS
        Set-FolderAcl $true

        # 4. сетевая папка
        Remove-ShareIfExists $share
        try {
            New-SmbShare -Name $share -Path $folder -FullAccess $account -Description 'PhoneDrive (Wi-Fi)' -ErrorAction Stop | Out-Null
        } catch {
            Write-Failure "Не удалось открыть общий доступ к папке: $($_.Exception.Message)"
        }

        # 5. брандмауэр: только из подсети точки доступа / домашней сети
        if (-not [string]::IsNullOrWhiteSpace($subnet)) {
            Remove-FirewallRule
            try {
                New-NetFirewallRule -DisplayName $ruleName -Direction Inbound -Action Allow -Protocol TCP `
                    -LocalPort 445 -RemoteAddress $subnet -Profile Any -Group 'PhoneDrive' -ErrorAction Stop | Out-Null
            } catch {
                'WARN=Не удалось добавить правило брандмауэра: ' + (($_.Exception.Message -replace "`r", ' ' -replace "`n", ' ').Trim())
            }

            # отдельное правило для веб-доступа: на «общедоступной» сети Windows
            # иначе закрывает порт и Safari с телефона ничего не откроет
            if (-not [string]::IsNullOrWhiteSpace($webPort)) {
                $port = 0
                if ([int]::TryParse($webPort, [ref]$port) -and $port -gt 0) {
                    try {
                        New-NetFirewallRule -DisplayName ('PhoneDrive - Web (TCP ' + $port + ')') `
                            -Direction Inbound -Action Allow -Protocol TCP `
                            -LocalPort $port -RemoteAddress $subnet -Profile Any -Group 'PhoneDrive' -ErrorAction Stop | Out-Null
                    } catch {
                        'WARN=Не удалось открыть порт веб-доступа: ' + (($_.Exception.Message -replace "`r", ' ' -replace "`n", ' ').Trim())
                    }
                }
            }
        }

        'OK=1'
        'SHARE=' + $share
        'USER=' + $user
        'FOLDER=' + $folder
        'COMPUTER=' + $env:COMPUTERNAME
    }

    'off' {
        Remove-ShareIfExists $share
        Remove-FirewallRule
        if (-not [string]::IsNullOrWhiteSpace($folder)) { Set-FolderAcl $false }
        Invoke-Net @('user', $user, '/active:no') | Out-Null
        'OK=1'
    }

    'cleanup' {
        Remove-ShareIfExists $share
        Remove-FirewallRule
        if (-not [string]::IsNullOrWhiteSpace($folder)) { Set-FolderAcl $false }
        Invoke-Net @('user', $user, '/active:no') | Out-Null
        Invoke-Net @('user', $user, '/delete') | Out-Null
        'OK=1'
    }

    default {
        Write-Failure "Неизвестное действие: $action"
    }
}

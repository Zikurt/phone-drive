# PhoneDrive

Turn a folder on your Windows PC into a network drive for your iPhone — wirelessly,
over Wi-Fi, right inside the **Files** app. No cable, no iTunes, no iCloud, no cloud.

The files stay on your PC: nothing is uploaded, copied or synced.

```
┌──────────────┐        Wi-Fi (hotspot / home network)      ┌───────────────┐
│     PC       │  ───────────────────────────────────────▶  │    iPhone     │
│   folder     │      SMB:   smb://192.168.137.1            │  Files app →  │
│  PhoneDrive  │      HTTP:  http://192.168.137.1:8080      │  Browse → …   │
└──────────────┘                                            └───────────────┘
```

> **A note about "like a USB drive".** iOS does not mount network shares the way
> Windows mounts a USB stick — there is no "USB over Wi-Fi" driver. The standard,
> native equivalent is **SMB**: once connected, the folder appears in
> *Files → Browse* under **Shared** as a device. You can open documents, save
> photos and videos from the phone, and copy files in both directions.

---

## Key features

- **Two modes**
  - **Hotspot** — the PC creates its own Wi-Fi network. Works **even with no internet
    connection**: if the standard Windows Mobile Hotspot is unavailable or fails to
    hand out IP addresses, PhoneDrive falls back to a local Wi-Fi Direct access point.
  - **Home network** — the PC and iPhone are already on the same router; the folder
    is shared over your existing Wi-Fi, no hotspot needed.
- **Two access protocols**
  - **SMB** (`smb://…`) — native in the iOS **Files** app.
  - **HTTP** (`http://…:8080`) — optional built-in web UI for Safari (list, download,
    upload, delete, create folders).
- **Everything in a single file.** All scripts are embedded into `PhoneDrive.exe`
  as resources — you can copy the exe to another PC without carrying the `scripts`
  folder along (they are unpacked to `%TEMP%` on launch and removed on exit).
- **Works even without PowerShell.** On Windows 10/11 PowerShell is always present,
  but on rare machines it may be missing — in that case PhoneDrive automatically
  runs the `.cmd` fallbacks (`share.bat`, `hotspot.bat`) built on plain
  `net.exe` / `icacls` / `netsh`. The Wi-Fi Direct hotspot (offline mode) is the
  only thing that requires PowerShell.
- **Compact window.** The *Instruction* and *Journal* panels are toggled by buttons
  in the top bar; the window resizes itself accordingly. The Instruction starts
  collapsed so the app stays compact.
- **Editable server address.** When the auto-detected IP is wrong (e.g. several
  network adapters), you can type the server IP in the left panel next to
  `Folder` — leave it empty to detect it automatically again.
- **Safe and reversible.** A dedicated Windows user is created for phone logins, a
  firewall rule is opened only for the hotspot/home subnet, and everything is removed
  with a single "cleanup" button. Passwords are generated randomly on first run.

---

## Requirements

- Windows 10 or 11 (tested on Windows 11 Pro).
- A Wi-Fi adapter with **Mobile Hotspot** support (almost all Intel / Realtek /
  Qualcomm adapters).
- **Internet is NOT required** — if there is no network, PhoneDrive creates a local
  Wi-Fi Direct access point and the phone connects anyway.
- Administrator rights — the app asks for them (UAC) when you start sharing.

---

## Quick start

1. Double-click **`Запустить.cmd`** (or `bin\PhoneDrive.exe`).
2. Pick the folder you want to share (default `C:\Users\<you>\PhoneDrive`).
3. Click **«Включить»** (Start) and approve the UAC prompt.
4. In the top bar you can open **«Инструкция»** (guide) and **«Журнал»** (log);
   they open and close together with the window. The guide starts collapsed to
   keep the window compact; once sharing is running it shows the iPhone steps
   plus the server address. You can also type the server IP manually in the
   **«Адрес сервера»** field (leave it empty for automatic detection, the
   **«Авто»** button clears it).
5. The right panel shows the network name, Wi-Fi password and
   server address. Passwords in the fields on the left are hidden — press
   **«показать»** (show) next to a field to reveal it.

> **If the PC and iPhone are already on the same home network**, tick
> **«Через домашнюю сеть (без точки доступа)»** ("via home network"). No hotspot is
> created, the phone keeps its internet, and the server address looks like
> `smb://192.168.0.152` — the app shows the exact address.

To stop: click **«Выключить»** (Stop) or just close the window — the hotspot and the
share are torn down automatically.

---

## Connecting from the iPhone

1. **Settings → Wi-Fi** → pick the network shown in the app (e.g. `PhoneDrive`) and
   enter the Wi-Fi password (also shown in the app).
2. Open **Files** → **Browse** → the **`…`** button in the top-right →
   **Connect to Server**.
3. Enter the address exactly as shown, e.g. `smb://192.168.137.1` (server address only —
   no path, no space).
4. Choose **Registered User** and enter the login and **access** password (not the
   Wi-Fi password!) shown in the app. Use the **Copy** buttons next to them.
5. Done — the server appears under **Shared**. Tap it and enable "Remember" to skip
   the credentials next time.

Saving files from the iPhone: long-press a photo/file → **Save to Files** → pick the
server and folder. The other direction: tap a file on the server → **Share/Save**.

### Fallback — Safari web UI

If SMB is not available (e.g. company policies), tick **«Веб-доступ через Safari»**.
Open an address like `http://192.168.137.1:8080` on the phone to list, download, upload
and delete files and create folders. No login — access is protected by the Wi-Fi
password.

---

## What the app does to your system

Everything is reversible and can be removed with the
**«Удалить служебного пользователя Windows»** (cleanup) button.

| What | Details |
|---|---|
| Wi-Fi hotspot | Tries the standard Windows Mobile Hotspot (2.4 GHz) first. If it fails or doesn't hand out addresses, PhoneDrive switches to a local Wi-Fi Direct access point that works **without internet**. The PC gets `192.168.137.1` either way. In home-network mode no hotspot is created at all. |
| Local user | `phonedrive` (configurable) — used only for phone logins. Created on first start, **disabled** on stop, removed by the cleanup button. |
| SMB share | `\\192.168.137.1\PhoneDrive` points at the folder you chose. |
| NTFS permissions | The `phonedrive` user gets "Modify" only on the chosen folder. Revoked on stop. |
| Firewall rule | Inbound TCP 445 (and TCP 8080 for the web UI) **only from the hotspot/home subnet**. Removed on stop. |
| Wi-Fi band | Choose **2.4 GHz** (recommended — iPhone finds the network faster) or **5 GHz** in the left panel; applies to the hotspot. In home-network mode the journal shows the actual band of your Wi-Fi. |
| Settings | `%APPDATA%\PhoneDrive\settings.ini` (folder, passwords, web port). |

Passwords are generated randomly on first run and can be changed in the app window.

---

## Troubleshooting

- **"Couldn't enable hotspot"** — make sure Wi-Fi is on. Try enabling "Mobile hotspot"
  manually (Settings → Network & Internet → Mobile hotspot). If Windows also fails
  there, it's the adapter driver, not PhoneDrive.
- **iPhone joins the Wi-Fi but the server isn't visible** — check the address starts
  with `smb://` and matches the IP in the app window. Third-party antivirus
  (Kaspersky, ESET, Avast) can block port 445 — trust the `192.168.137.0/24` subnet.
- **"Wrong username or password"** — the login has no `PC\` prefix and no `@`, e.g.
  `phonedrive`. Use the access password, not the Wi-Fi password.
- **"Couldn't create user …"** — usually the password is too weak for Windows policy
  (it may require uppercase letters and digits); use something like `PhoneDrive7`.
- **iPhone says "No Internet Connection"** — normal when the PC has no network: the
  local Wi-Fi Direct hotspot works without internet. The share doesn't depend on it.
- **iPhone says "URL not supported"** — the address is mistyped. Use `smb://` (not
  `smd://`), a colon and two forward slashes, then the IP from the app window.
- **"Unreliable connection / phone drops"** — "forget" the network on the iPhone and
  reconnect; use the 2.4 GHz band; disable Wi-Fi power saving on the PC.

See the Russian section below for a more detailed checklist.

---

## Building from source

```
Собрать.cmd
```

or manually:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1
```

The script uses `csc.exe` from the .NET Framework 4 already present in Windows — no
Visual Studio or .NET SDK needed.

### Project structure

```
PhoneDrive/
├─ bin/                  ready build: a single PhoneDrive.exe (scripts are embedded)
├─ scripts/              same scripts as separate files (for review/development)
│  ├─ tether.ps1         hotspot (Windows Mobile Hotspot API)
│  ├─ wfd.ps1            local hotspot without internet (Wi-Fi Direct)
│  ├─ share.ps1          SMB share, service user, NTFS rights, firewall
│  ├─ share.bat          cmd fallback for share
│  └─ hotspot.bat        cmd fallback for the hotspot
├─ src/
│  ├─ Program.cs         entry point, single-instance
│  ├─ MainForm.cs        UI: compact window, Instruction/Journal toggles
│  ├─ Config.cs          settings and password generation
│  ├─ ShareService.cs    script wrappers
│  ├─ WfdHotspot.cs      local hotspot start/stop
│  ├─ ScriptRunner.cs    extracts embedded scripts, picks PowerShell or cmd
│  ├─ FileWebServer.cs   web access (Safari): list, download, upload
│  └─ app.manifest       DPI, Windows 10/11 compatibility
├─ build.ps1
├─ Запустить.cmd
└─ Собрать.cmd
```

---

## Limitations

- One hotspot per PC: if it's already enabled by another program or in Windows
  Settings, PhoneDrive uses it as-is (the actual network name is shown in the guide).
- Several devices can connect at once — access is shared between them.
- While the app is on, the PC shares its internet connection. If there is none,
  PhoneDrive creates a local hotspot — the phone connects to Wi-Fi but has no internet.
- SMB is intended for trusted home networks (Wi-Fi password + a separate user
  password). Don't enable this on public networks.

---

## License

MIT — see [LICENSE](LICENSE).

---

# PhoneDrive (русский)

Папка на компьютере появляется на iPhone как сетевой диск в приложении **«Файлы»** —
по Wi-Fi, без кабеля, iTunes, iCloud и облаков. Файлы физически лежат на компьютере,
ничего не копируется и не синхронизируется.

## Что нужно

- Windows 10 или 11 (проверено на Windows 11 Pro).
- Wi-Fi адаптер с поддержкой **Mobile Hotspot** (почти все современные
  Intel/Realtek/Qualcomm).
- **Интернет не обязателен**: без сети программа создаёт локальную точку доступа
  (Wi-Fi Direct), телефон подключается и без выхода в интернет.
- Права администратора — приложение запросит их само (UAC) при включении.

## Запуск

1. Двойной щелчок по **`Запустить.cmd`** (или по `bin\PhoneDrive.exe`).
2. Выберите папку (по умолчанию `C:\Users\<вы>\PhoneDrive`).
3. Нажмите **«Включить»** и разрешите запрос UAC.
4. В верхней графе есть кнопки **«Инструкция»** и **«Журнал»** — панели открываются
   и закрываются по щелчку, а окно само подстраивается под них. При старте
   инструкция свёрнута, чтобы окно оставалось компактным; когда доступ уже включён,
   в ней видны шаги для iPhone и адрес сервера. Адрес сервера можно задать вручную
   в поле **«Адрес сервера»** (пусто — определяется автоматически, кнопка «Авто»).
5. Пароли в полях слева скрыты — нажмите **«показать»** рядом с нужным полем.

> **Если компьютер и iPhone уже в одной домашней сети**, поставьте галочку
> **«Через домашнюю сеть (без точки доступа)»** — точка доступа не создаётся, телефон
> остаётся в своей сети с интернетом, а адрес сервера будет вида `smb://192.168.0.152`.

Выключить: кнопка **«Выключить»** или закрытие окна — точка доступа и общий доступ
закрываются автоматически.

## Подключение iPhone

1. **Настройки → Wi-Fi** → сеть из окна программы (например, `PhoneDrive`), введите
   пароль Wi-Fi.
2. **«Файлы» → «Обзор» → «…»** (вверху справа) → **«Подключиться к серверу»**.
3. Введите адрес **ровно как в окне**: `smb://192.168.137.1` (только адрес — без
   папки, пробела и слова PhoneDrive).
4. **«Зарегистрированный пользователь»** — логин и **пароль доступа** (не пароль
   Wi-Fi!), кнопки «Копировать» рядом.
5. Сервер появится в «Общем доступе». Включите «Запомнить», чтобы не вводить данные
   каждый раз.

Файлы с iPhone: долгое нажатие → «Сохранить в Файлы» → сервер и папка. Обратно — тап
по файлу на сервере → «Поделиться»/«Сохранить».

### Запасной вариант — через Safari

Если SMB не подходит, включите галочку **«Веб-доступ через Safari»**. На телефоне
откройте `http://192.168.137.1:8080` — можно смотреть, скачивать, загружать и удалять
файлы, создавать папки. Логин не нужен, доступ закрыт паролем Wi-Fi.

## Что приложение делает в системе

Всё обратимо, всё удаляется кнопкой **«Удалить служебного пользователя Windows»**.

| Что | Подробности |
|---|---|
| Точка доступа | Сначала пробуется штатный Windows Mobile Hotspot (2,4 ГГц). Если недоступен или не выдаёт адреса — программа переключается на локальную точку доступа (Wi-Fi Direct), которая работает **и без интернета**. Компьютер получает `192.168.137.1`. В режиме домашней сети точка доступа не создаётся. |
| Локальный пользователь | `phonedrive` (можно изменить) — только для входа с телефона. Создаётся при включении, **отключается** при выключении, удаляется кнопкой очистки. |
| Сетевая папка (SMB) | `\\192.168.137.1\PhoneDrive` указывает на выбранную папку. |
| Права NTFS | Пользователю `phonedrive` выдаётся «Изменение» только на выбранную папку. При выключении снимается. |
| Брандмауэр | Входящий TCP 445 (и 8080 для веба) **только из подсети точки доступа/домашней сети**. Удаляется при выключении. |
| Диапазон | В левой панели выбираете **2,4 ГГц (рекомендуется)** или **5 ГГц** — применяется к точке доступа. В режиме домашней сети журнал показывает фактическую частоту вашего Wi-Fi. |
| Настройки | `%APPDATA%\PhoneDrive\settings.ini` (папка, пароли, порт). |

Пароли генерируются случайно при первом запуске, меняются в окне программы.

## Если что-то не работает

- **«Не удалось включить точку доступа»** — включите Wi-Fi, попробуйте «Мобильный
  хот-спот» вручную (Параметры → Сеть и Интернет). Если Windows ругается там же —
  дело в драйвере адаптера.
- **Телефон в Wi-Fi, но сервера нет** — адрес должен начинаться с `smb://` и совпадать
  с IP из окна. Антивирусы (Kaspersky/ESET/Avast) блокируют 445 порт — добавьте
  `192.168.137.0/24` в доверенные.
- **«Неверное имя пользователя или пароль»** — логин без `PC\` и без `@`, например
  `phonedrive`; пароль — из блока «Пароль» (не Wi-Fi).
- **«Не удалось создать пользователя…»** — пароль слишком простой для политики Windows:
  задайте вроде `PhoneDrive7` или смените логин.
- **iPhone пишет «Нет доступа к интернету»** — это нормально, когда у компьютера нет
  сети: локальная точка доступа работает без интернета, папка от него не зависит.
- **iPhone пишет «URL не поддерживается»** — схема `smb` (не `smd`), двоеточие, две
  прямые черты и IP из окна.
- **Телефон отваливается** — «Забыть эту сеть» на iPhone и подключиться заново;
  диапазон 2,4 ГГц; отключить энергосбережение Wi-Fi адаптера на ПК.

## Сборка из исходников

```
Собрать.cmd
```

или `powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1` — используется
`csc.exe` из .NET Framework 4, Visual Studio и .NET SDK не нужны.

## Лицензия

MIT — см. [LICENSE](LICENSE).

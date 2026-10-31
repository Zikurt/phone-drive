using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Principal;
using System.Threading;
using System.Windows.Forms;

namespace PhoneDrive
{
    internal sealed class MainForm : Form
    {
        // --- палитра ---------------------------------------------------------
        private static readonly Color Bg = Color.FromArgb(13, 17, 23);
        private static readonly Color PanelBg = Color.FromArgb(22, 27, 34);
        private static readonly Color PanelBg2 = Color.FromArgb(33, 38, 45);
        private static readonly Color BorderColor = Color.FromArgb(48, 54, 61);
        private static readonly Color TextColor = Color.FromArgb(230, 237, 243);
        private static readonly Color Muted = Color.FromArgb(139, 148, 158);
        private static readonly Color AccentGreen = Color.FromArgb(35, 134, 54);
        private static readonly Color AccentRed = Color.FromArgb(179, 43, 43);
        private static readonly Color AccentLink = Color.FromArgb(88, 166, 255);
        private static readonly Color AccentYellow = Color.FromArgb(210, 153, 34);
        private static readonly Color AccentOk = Color.FromArgb(63, 185, 80);

        private const string ShareName = "PhoneDrive";

        private readonly AppConfig _cfg;
        private readonly ShareService _svc = new ShareService();
        private readonly bool _autoStart;

        private FileWebServer _web;
        private readonly WfdHotspot _wfd = new WfdHotspot();

        /// <summary>Точка доступа поднята в локальном режиме (Wi-Fi Direct, без интернета).</summary>
        private volatile bool _wfdMode;

        private volatile bool _running;
        private volatile bool _busy;
        private bool _closing;

        private string _activeIp = "";
        private string _activeSsid = "";
        private string _activeShare = ShareName;
        private string _activeUser = "";

        // --- элементы управления ---------------------------------------------
        private TextBox _txtFolder;
        private TextBox _txtSsid;
        private TextBox _txtWifiPass;
        private TextBox _txtLogin;
        private TextBox _txtLoginPass;
        private CheckBox _chkShowWifi;
        private CheckBox _chkWeb;
        private CheckBox _chkLan;
        private bool _logTrimmed;
        private Button _btnToggle;
        private Button _btnBrowse;
        private Button _btnOpen;
        private Button _btnCleanup;
        private Label _lblStatus;
        private Panel _guide;
        private TextBox _txtLog;
        private int _gy;

        public MainForm(bool autoStart)
        {
            _autoStart = autoStart;
            _cfg = AppConfig.Load();
            _activeShare = ShareName;
            _activeUser = _cfg.Login;

            try
            {
                if (!string.IsNullOrWhiteSpace(_cfg.Folder) && !Directory.Exists(_cfg.Folder))
                {
                    Directory.CreateDirectory(_cfg.Folder);
                }
            }
            catch
            {
                // если не получилось - папку создаст share.ps1 при включении
            }

            BuildUi();
            LoadIntoUi();
            SetUiState();

            // при самом первом запуске сразу сохраняем настройки: иначе пароли
            // генерировались бы заново при каждом открытии окна
            if (!File.Exists(AppConfig.ConfigPath))
            {
                _cfg.Save();
            }

            Log("Готово к работе. Папка: " + _cfg.Folder);
        }

        // =====================================================================
        //  Построение интерфейса
        // =====================================================================

        private void BuildUi()
        {
            Text = "Wi-Fi Флешка";
            BackColor = Bg;
            ForeColor = TextColor;
            Font = new Font("Segoe UI", 9f);
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96f, 96f);
            ClientSize = new Size(1180, 700);
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(1100, 660);
            DoubleBuffered = true;

            try
            {
                Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            }
            catch
            {
                // остаётся значок по умолчанию
            }

            Label title = new Label();
            title.Text = "Wi-Fi Флешка";
            title.Font = new Font("Segoe UI", 17f, FontStyle.Bold);
            title.ForeColor = TextColor;
            title.AutoSize = true;
            title.Location = new Point(24, 16);
            Controls.Add(title);

            Label subtitle = new Label();
            subtitle.Text = "Папка компьютера становится сетевым диском на iPhone — без кабеля и iTunes";
            subtitle.Font = new Font("Segoe UI", 9.5f);
            subtitle.ForeColor = Muted;
            subtitle.AutoSize = true;
            subtitle.Location = new Point(26, 52);
            Controls.Add(subtitle);

            // ---- левая панель: настройки ------------------------------------
            Panel left = new Panel();
            left.BackColor = PanelBg;
            left.Location = new Point(24, 92);
            left.Size = new Size(400, 440);
            left.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Bottom;
            Controls.Add(left);

            left.Controls.Add(MakeCaption("ПАПКА ДЛЯ ОБМЕНА", 18, 14));
            _txtFolder = MakeInput(18, 32, 196);
            _txtFolder.ReadOnly = true;
            left.Controls.Add(_txtFolder);

            _btnBrowse = MakeFlatButton("Выбрать", 220, 31, 78, 28);
            _btnBrowse.Click += delegate { BrowseFolder(); };
            left.Controls.Add(_btnBrowse);

            _btnOpen = MakeFlatButton("Открыть", 304, 31, 78, 28);
            _btnOpen.Click += delegate { OpenFolder(); };
            left.Controls.Add(_btnOpen);

            left.Controls.Add(MakeCaption("ИМЯ WI-FI СЕТИ", 18, 66));
            _txtSsid = MakeInput(18, 84, 364);
            left.Controls.Add(_txtSsid);

            left.Controls.Add(MakeCaption("ПАРОЛЬ WI-FI (МИНИМУМ 8 СИМВОЛОВ)", 18, 118));
            _txtWifiPass = MakeInput(18, 136, 268);
            _txtWifiPass.PasswordChar = '\u25CF';
            left.Controls.Add(_txtWifiPass);

            _chkShowWifi = new CheckBox();
            _chkShowWifi.Text = "показать";
            _chkShowWifi.Font = new Font("Segoe UI", 8.5f);
            _chkShowWifi.ForeColor = Muted;
            _chkShowWifi.FlatStyle = FlatStyle.System;
            _chkShowWifi.AutoSize = true;
            _chkShowWifi.Location = new Point(294, 139);
            _chkShowWifi.CheckedChanged += delegate
            {
                _txtWifiPass.PasswordChar = _chkShowWifi.Checked ? '\0' : '\u25CF';
            };
            left.Controls.Add(_chkShowWifi);

            left.Controls.Add(MakeCaption("ЛОГИН НА ТЕЛЕФОНЕ", 18, 170));
            _txtLogin = MakeInput(18, 188, 364);
            left.Controls.Add(_txtLogin);

            left.Controls.Add(MakeCaption("ПАРОЛЬ НА ТЕЛЕФОНЕ", 18, 222));
            _txtLoginPass = MakeInput(18, 240, 364);
            _txtLoginPass.PasswordChar = '\u25CF';
            left.Controls.Add(_txtLoginPass);

            _chkWeb = new CheckBox();
            _chkWeb.Text = "Веб-доступ через Safari (запасной вариант)";
            _chkWeb.Font = new Font("Segoe UI", 9f);
            _chkWeb.ForeColor = TextColor;
            _chkWeb.AutoSize = true;
            _chkWeb.FlatStyle = FlatStyle.System;
            _chkWeb.Location = new Point(18, 276);
            left.Controls.Add(_chkWeb);

            _chkLan = new CheckBox();
            _chkLan.Text = "Через домашнюю сеть (без точки доступа)";
            _chkLan.Font = new Font("Segoe UI", 9f);
            _chkLan.ForeColor = TextColor;
            _chkLan.AutoSize = true;
            _chkLan.FlatStyle = FlatStyle.System;
            _chkLan.Location = new Point(18, 298);
            _chkLan.CheckedChanged += delegate
            {
                _cfg.UseHomeNetwork = _chkLan.Checked;
                SetUiState();
            };
            left.Controls.Add(_chkLan);

            _btnToggle = new Button();
            _btnToggle.Text = "Включить";
            _btnToggle.Font = new Font("Segoe UI", 11.5f, FontStyle.Bold);
            _btnToggle.Location = new Point(18, 328);
            _btnToggle.Size = new Size(364, 46);
            _btnToggle.FlatStyle = FlatStyle.Flat;
            _btnToggle.FlatAppearance.BorderSize = 0;
            _btnToggle.FlatAppearance.MouseOverBackColor = Color.FromArgb(46, 160, 67);
            _btnToggle.BackColor = AccentGreen;
            _btnToggle.ForeColor = Color.White;
            _btnToggle.Cursor = Cursors.Hand;
            _btnToggle.UseVisualStyleBackColor = false;
            _btnToggle.Click += delegate { Toggle(); };
            left.Controls.Add(_btnToggle);

            _lblStatus = new Label();
            _lblStatus.Text = "\u25CF  Выключено";
            _lblStatus.Font = new Font("Segoe UI", 9.5f);
            _lblStatus.ForeColor = Muted;
            _lblStatus.AutoSize = true;
            _lblStatus.Location = new Point(18, 382);
            left.Controls.Add(_lblStatus);

            _btnCleanup = new Button();
            _btnCleanup.Text = "Удалить служебного пользователя Windows";
            _btnCleanup.Font = new Font("Segoe UI", 8.5f);
            _btnCleanup.FlatStyle = FlatStyle.Flat;
            _btnCleanup.FlatAppearance.BorderSize = 0;
            _btnCleanup.BackColor = PanelBg;
            _btnCleanup.ForeColor = Muted;
            _btnCleanup.Size = new Size(364, 24);
            _btnCleanup.Location = new Point(18, 404);
            _btnCleanup.TextAlign = ContentAlignment.MiddleLeft;
            _btnCleanup.Cursor = Cursors.Hand;
            _btnCleanup.Click += delegate { CleanupAccount(); };
            left.Controls.Add(_btnCleanup);

            // ---- правая панель: инструкция ----------------------------------
            Panel right = new Panel();
            right.BackColor = PanelBg;
            right.Location = new Point(440, 92);
            right.Size = new Size(716, 440);
            right.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
            Controls.Add(right);

            Label guideCaption = new Label();
            guideCaption.Text = "КАК ПОДКЛЮЧИТЬСЯ С IPHONE";
            guideCaption.Font = new Font("Segoe UI", 8f, FontStyle.Bold);
            guideCaption.ForeColor = Muted;
            guideCaption.AutoSize = true;
            guideCaption.Location = new Point(18, 14);
            right.Controls.Add(guideCaption);

            _guide = new Panel();
            _guide.BackColor = PanelBg;
            _guide.Location = new Point(0, 38);
            _guide.Size = new Size(right.Width, right.Height - 38);
            _guide.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
            _guide.AutoScroll = true;
            right.Controls.Add(_guide);

            // ---- журнал ------------------------------------------------------
            Panel logPanel = new Panel();
            logPanel.BackColor = PanelBg;
            logPanel.Location = new Point(24, 548);
            logPanel.Size = new Size(1132, 132);
            logPanel.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            Controls.Add(logPanel);

            Label logCaption = new Label();
            logCaption.Text = "ЖУРНАЛ";
            logCaption.Font = new Font("Segoe UI", 8f, FontStyle.Bold);
            logCaption.ForeColor = Muted;
            logCaption.AutoSize = true;
            logCaption.Location = new Point(14, 8);
            logPanel.Controls.Add(logCaption);

            _txtLog = new TextBox();
            _txtLog.Multiline = true;
            _txtLog.ReadOnly = true;
            _txtLog.ScrollBars = ScrollBars.Vertical;
            _txtLog.BorderStyle = BorderStyle.None;
            _txtLog.BackColor = Color.FromArgb(9, 12, 16);
            _txtLog.ForeColor = Color.FromArgb(170, 178, 188);
            _txtLog.Font = new Font("Consolas", 8.75f);
            _txtLog.Location = new Point(14, 28);
            _txtLog.Size = new Size(logPanel.Width - 28, logPanel.Height - 38);
            _txtLog.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
            logPanel.Controls.Add(_txtLog);
        }

        private static Label MakeCaption(string text, int x, int y)
        {
            Label label = new Label();
            label.Text = text;
            label.Font = new Font("Segoe UI", 8f, FontStyle.Bold);
            label.ForeColor = Muted;
            label.AutoSize = true;
            label.Location = new Point(x, y);
            label.BackColor = PanelBg;
            return label;
        }

        private static TextBox MakeInput(int x, int y, int width)
        {
            TextBox box = new TextBox();
            box.Location = new Point(x, y);
            box.Width = width;
            box.Font = new Font("Segoe UI", 10.5f);
            box.BackColor = Color.FromArgb(13, 17, 23);
            box.ForeColor = TextColor;
            box.BorderStyle = BorderStyle.FixedSingle;
            return box;
        }

        private static Button MakeFlatButton(string text, int x, int y, int width, int height)
        {
            Button button = new Button();
            button.Text = text;
            button.Location = new Point(x, y);
            button.Size = new Size(width, height);
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderColor = BorderColor;
            button.FlatAppearance.BorderSize = 1;
            button.BackColor = PanelBg2;
            button.ForeColor = TextColor;
            button.Font = new Font("Segoe UI", 9f);
            button.Cursor = Cursors.Hand;
            button.UseVisualStyleBackColor = false;
            return button;
        }

        private Button MakeCopyButton(int x, int y, string text)
        {
            Button button = MakeFlatButton("Копировать", x, y, 88, 24);
            button.Font = new Font("Segoe UI", 8f);
            string payload = text;
            button.Click += delegate
            {
                try
                {
                    Clipboard.SetText(payload);
                    Log("Скопировано в буфер: " + payload);
                }
                catch
                {
                    Log("Не удалось скопировать в буфер обмена.");
                }
            };
            return button;
        }

        // =====================================================================
        //  Инструкция справа
        // =====================================================================

        private void RenderGuide()
        {
            if (_guide == null) { return; }

            _guide.SuspendLayout();
            _guide.Controls.Clear();
            _gy = 8;
            int width = _guide.Width;

            if (_busy)
            {
                GuideHeading(_running ? "Выключаю…" : "Включаю…", AccentYellow);
                GuideParagraph("Это занимает несколько секунд. Не закрывайте окно.");
            }
            else if (!_running)
            {
                GuideHeading("Готово к запуску", TextColor);
                GuideStep("1", "Выберите папку на компьютере");
                GuideParagraph("Именно её содержимое появится на iPhone. Файлы остаются на компьютере, ничего не копируется.");
                GuideStep("2", "Нажмите «Включить»");
                if (_cfg.UseHomeNetwork)
                {
                    GuideParagraph("Приложение откроет доступ к папке через ваш домашний роутер. Точка доступа создаваться не будет.");
                }
                else
                {
                    GuideParagraph("Приложение создаст Wi-Fi сеть и откроет к папке сетевой доступ. Windows один раз спросит права администратора.");
                }
                GuideStep("3", _cfg.UseHomeNetwork ? "Оставьте iPhone в домашней сети" : "Подключите iPhone к этой сети");
                GuideParagraph("Затем в приложении «Файлы»: «Обзор» → «…» в правом верхнем углу → «Подключиться к серверу». Адрес, логин и пароль появятся здесь после включения.");
            }
            else
            {
                bool lan = _cfg.UseHomeNetwork;
                GuideHeading(lan ? "\u25CF  Доступ через домашнюю сеть" : "\u25CF  Точка доступа работает", AccentOk);

                if (lan)
                {
                    GuideStep("1", "iPhone должен быть в той же сети Wi-Fi");
                    GuideParagraph("Компьютер: " + _activeIp + ". Оставьте телефон в домашней сети — точка доступа не нужна.");
                }
                else
                {
                    GuideStep("1", "Подключите iPhone к Wi-Fi");
                    GuideField("СЕТЬ", _activeSsid, _activeSsid, false);
                    GuideField("ПАРОЛЬ WI-FI", _cfg.WifiPassword, _cfg.WifiPassword, true);
                    if (_wfdMode)
                    {
                        GuideParagraph("Эта точка доступа работает и без интернета. "
                            + "iPhone может написать «Нет доступа к интернету» — это нормально, папка всё равно открывается.");
                    }
                }

                GuideStep("2", "Откройте «Файлы» → «Обзор» → «…» → «Подключиться к серверу»");
                string server = "smb://" + _activeIp;
                GuideField("СЕРВЕР", server, server, false);
                GuideField("ПОЛЬЗОВАТЕЛЬ", _activeUser, _activeUser, false);
                GuideField("ПАРОЛЬ", _cfg.LoginPassword, _cfg.LoginPassword, true);
                GuideParagraph("Только адрес — без пути и пробела. Папку «" + _activeShare + "» выберите внутри.");

                if (_web != null)
                {
                    GuideStep("3", "Или через Safari — без логина");
                    string url = "http://" + _activeIp + ":" + _web.Port + "/";
                    GuideField("АДРЕС В БРАУЗЕРЕ", url, url, false);
                }
            }

            _guide.ResumeLayout();
            _guide.PerformLayout();
        }

        private void GuideHeading(string text, Color color)
        {
            Label label = new Label();
            label.Text = text;
            label.Font = new Font("Segoe UI", 12.5f, FontStyle.Bold);
            label.ForeColor = color;
            label.AutoSize = true;
            label.BackColor = PanelBg;
            label.Location = new Point(18, _gy);
            _guide.Controls.Add(label);
            _gy += label.PreferredHeight + 8;
        }

        private void GuideStep(string number, string title)
        {
            _gy += 2;
            Label label = new Label();
            label.Text = number + "    " + title;
            label.Font = new Font("Segoe UI", 10f, FontStyle.Bold);
            label.ForeColor = AccentLink;
            label.AutoSize = true;
            label.MaximumSize = new Size(_guide.Width - 60, 0);
            label.BackColor = PanelBg;
            label.Location = new Point(18, _gy);
            _guide.Controls.Add(label);
            _gy += label.PreferredHeight + 5;
        }

        private void GuideParagraph(string text)
        {
            Label label = new Label();
            label.Text = text;
            label.Font = new Font("Segoe UI", 9.5f);
            label.ForeColor = Muted;
            label.AutoSize = true;
            label.MaximumSize = new Size(_guide.Width - 60, 0);
            label.BackColor = PanelBg;
            label.Location = new Point(18, _gy);
            _guide.Controls.Add(label);
            _gy += label.PreferredHeight + 4;
        }

        private void GuideField(string caption, string value, string copyValue, bool secret)
        {
            Label cap = new Label();
            cap.Text = caption;
            cap.Font = new Font("Segoe UI", 7.75f, FontStyle.Bold);
            cap.ForeColor = Muted;
            cap.AutoSize = true;
            cap.BackColor = PanelBg;
            cap.Location = new Point(18, _gy);
            _guide.Controls.Add(cap);
            _gy += 17;

            Label val = new Label();
            val.Text = value;
            val.Font = new Font("Consolas", 11f, FontStyle.Bold);
            val.ForeColor = secret ? AccentOk : TextColor;
            val.AutoSize = false;
            val.AutoEllipsis = true;
            val.BackColor = PanelBg;
            val.Location = new Point(18, _gy);
            val.Size = new Size(_guide.Width - 200, 22);
            _guide.Controls.Add(val);

            if (copyValue != null)
            {
                _guide.Controls.Add(MakeCopyButton(_guide.Width - 118, _gy - 2, copyValue));
            }

            _gy += 26;
        }

        // =====================================================================
        //  Состояние
        // =====================================================================

        private void LoadIntoUi()
        {
            _txtFolder.Text = _cfg.Folder;
            _txtSsid.Text = _cfg.Ssid;
            _txtWifiPass.Text = _cfg.WifiPassword;
            _txtLogin.Text = _cfg.Login;
            _txtLoginPass.Text = _cfg.LoginPassword;
            _chkWeb.Checked = _cfg.WebEnabled;
            _chkLan.Checked = _cfg.UseHomeNetwork;
        }

        private void SaveFromUi()
        {
            _cfg.Folder = _txtFolder.Text.Trim();
            _cfg.Ssid = _txtSsid.Text.Trim();
            _cfg.WifiPassword = _txtWifiPass.Text;
            _cfg.Login = _txtLogin.Text.Trim();
            _cfg.LoginPassword = _txtLoginPass.Text;
            _cfg.WebEnabled = _chkWeb.Checked;
            _cfg.UseHomeNetwork = _chkLan.Checked;
            _cfg.Save();
        }

        /// <summary>Сохраняет только корректные значения — используется при закрытии окна.</summary>
        private void SaveQuietly()
        {
            string folder = _txtFolder.Text.Trim();
            if (folder.Length > 0 && Directory.Exists(folder)) { _cfg.Folder = folder; }

            string ssid = _txtSsid.Text.Trim();
            if (ssid.Length > 0) { _cfg.Ssid = ssid; }

            string login = _txtLogin.Text.Trim();
            if (login.Length > 0) { _cfg.Login = login; }

            if (_txtWifiPass.Text.Length >= 8) { _cfg.WifiPassword = _txtWifiPass.Text; }
            if (_txtLoginPass.Text.Length >= 8) { _cfg.LoginPassword = _txtLoginPass.Text; }

            _cfg.WebEnabled = _chkWeb.Checked;
            _cfg.UseHomeNetwork = _chkLan.Checked;
            _cfg.Save();
        }

        private void SetUiState()
        {
            bool editable = !_busy && !_running;
            bool wifiEditable = editable && !_cfg.UseHomeNetwork;

            _txtFolder.Enabled = editable;
            _btnBrowse.Enabled = editable;
            _btnOpen.Enabled = true;
            _txtSsid.Enabled = wifiEditable;
            _txtWifiPass.Enabled = wifiEditable;
            _chkShowWifi.Enabled = wifiEditable;
            _txtLogin.Enabled = editable;
            _txtLoginPass.Enabled = editable;
            _chkWeb.Enabled = editable;
            _chkLan.Enabled = editable;
            _btnCleanup.Enabled = editable;

            _btnToggle.Enabled = !_busy;
            if (_running)
            {
                _btnToggle.Text = _busy ? "Выключение…" : "Выключить";
                _btnToggle.BackColor = AccentRed;
                _btnToggle.FlatAppearance.MouseOverBackColor = Color.FromArgb(210, 60, 60);
            }
            else
            {
                _btnToggle.Text = _busy ? "Включение…" : "Включить";
                _btnToggle.BackColor = AccentGreen;
                _btnToggle.FlatAppearance.MouseOverBackColor = Color.FromArgb(46, 160, 67);
            }

            if (_busy)
            {
                _lblStatus.Text = "\u25CF  Подождите…";
                _lblStatus.ForeColor = AccentYellow;
            }
            else if (_running)
            {
                _lblStatus.Text = "\u25CF  Работает";
                _lblStatus.ForeColor = AccentOk;
            }
            else
            {
                _lblStatus.Text = "\u25CF  Выключено";
                _lblStatus.ForeColor = Muted;
            }

            RenderGuide();
        }

        // =====================================================================
        //  Действия
        // =====================================================================

        private void BrowseFolder()
        {
            using (FolderBrowserDialog dialog = new FolderBrowserDialog())
            {
                dialog.Description = "Выберите папку, которую хотите видеть на iPhone";
                dialog.ShowNewFolderButton = true;
                if (Directory.Exists(_txtFolder.Text)) { dialog.SelectedPath = _txtFolder.Text; }

                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    _txtFolder.Text = dialog.SelectedPath;
                    _cfg.Folder = dialog.SelectedPath;
                    _cfg.Save();
                    Log("Папка: " + dialog.SelectedPath);
                }
            }
        }

        private void OpenFolder()
        {
            string folder = _txtFolder.Text.Trim();
            try
            {
                if (!Directory.Exists(folder)) { Directory.CreateDirectory(folder); }
                Process.Start("explorer.exe", "\"" + folder + "\"");
            }
            catch (Exception ex)
            {
                Log("Не удалось открыть папку: " + ex.Message);
            }
        }

        private void Toggle()
        {
            if (_busy) { return; }
            if (_running) { StopAsync(); return; }
            StartAsync();
        }

        private void StartAsync()
        {
            if (!IsAdministrator())
            {
                SaveFromUi();
                DialogResult answer = MessageBox.Show(
                    this,
                    "Чтобы создать точку доступа Wi-Fi и сетевую папку, нужны права администратора.\r\n\r\n" +
                    "Перезапустить приложение с повышенными правами?",
                    "Wi-Fi Флешка",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);

                if (answer == DialogResult.Yes)
                {
                    RelaunchElevated(true);
                }
                return;
            }

            string problem = ValidateSettings();
            if (problem != null)
            {
                MessageBox.Show(this, problem, "Wi-Fi Флешка", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (_cfg.UseHomeNetwork)
            {
                string ip = FindLocalAddress();
                if (string.IsNullOrEmpty(ip))
                {
                    MessageBox.Show(this,
                        "Не удалось определить адрес компьютера в домашней сети.\r\n\r\n" +
                        "Проверьте, что компьютер и iPhone подключены к одному роутеру.",
                        "Wi-Fi Флешка", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                _activeIp = ip;
                _activeSsid = "";
                _activeUser = _cfg.Login;
                _activeShare = ShareName;
                Log("Режим без точки доступа: используем домашнюю сеть, адрес компьютера " + ip);
            }

            SaveFromUi();
            _busy = true;
            SetUiState();
            ThreadPool.QueueUserWorkItem(delegate { DoStart(); });
        }

        private void StopAsync()
        {
            _busy = true;
            SetUiState();
            ThreadPool.QueueUserWorkItem(delegate { DoStop(); });
        }

        private string ValidateSettings()
        {
            string folder = _txtFolder.Text.Trim();
            if (folder.Length == 0) { return "Выберите папку для обмена."; }
            if (!Directory.Exists(folder)) { return "Папка не существует:\r\n" + folder; }

            string ssid = _txtSsid.Text.Trim();
            string wifiPassword = _txtWifiPass.Text;

            if (!_cfg.UseHomeNetwork)
            {
                if (ssid.Length == 0) { return "Введите имя Wi-Fi сети."; }
                if (ssid.Length > 32) { return "Имя Wi-Fi сети не должно быть длиннее 32 символов."; }

                if (wifiPassword.Length < 8) { return "Пароль Wi-Fi должен содержать минимум 8 символов."; }
                if (wifiPassword.Length > 63) { return "Пароль Wi-Fi слишком длинный: максимум 63 символа."; }
            }

            string login = _txtLogin.Text.Trim();
            if (login.Length == 0) { return "Введите логин для входа с телефона."; }
            if (login.IndexOf(' ') >= 0) { return "Логин не должен содержать пробелы."; }
            if (login.IndexOfAny(new char[] { '\\', '/', ':', '"', '[', ']', '|', '<', '>', '+', '=', ';', ',', '?', '*', '@' }) >= 0)
            {
                return "В логине нельзя использовать символы \\ / : \" [ ] | < > + = ; , ? * @";
            }
            if (login.Length > 20) { return "Логин слишком длинный: максимум 20 символов."; }

            if (_txtLoginPass.Text.Length < 4) { return "Пароль для входа с телефона должен быть не короче 4 символов."; }

            return null;
        }

        private void DoStart()
        {
            try
            {
                string note = "";

                if (!_cfg.UseHomeNetwork)
                {
                    Log("Включаю точку доступа «" + _cfg.Ssid + "»…");
                    Dictionary<string, string> tether = _svc.TetherOn(_cfg.Ssid, _cfg.WifiPassword, _cfg.Band);

                    if (tether.TryGetValue("WARN", out note) && note.Length > 0) { Log("Замечание: " + note); }

                    bool local = false;

                    if (!ScriptRunner.IsOk(tether))
                    {
                        // Мобильный хот-спот требует подключение к сети: без Wi-Fi
                        // или Ethernet Windows его просто не включает.
                        Log("Обычная точка доступа не включилась: " + ScriptRunner.Message(tether));
                        Log("Включаю локальную точку доступа — она работает без интернета…");

                        tether = StartLocalHotspot();
                        if (tether == null) { return; }
                        local = true;
                    }
                    else if (!WaitForDhcp(12))
                    {
                        // Хот-спот ассоциирует телефон, но адрес не выдаёт:
                        // без DHCP телефон отваливается через несколько секунд.
                        Log("Windows подняла точку доступа, но не выдаёт адреса телефонам (порт 67 молчит).");
                        Log("Переключаюсь на локальную точку доступа — она работает без интернета…");

                        Dictionary<string, string> off = _svc.TetherOff();
                        if (!ScriptRunner.IsOk(off)) { Log("Замечание: " + ScriptRunner.Message(off)); }

                        tether = StartLocalHotspot();
                        if (tether == null) { return; }
                        local = true;
                    }

                    _wfdMode = local;
                    _activeIp = Value(tether, "HOSTIP", "192.168.137.1");
                    _activeSsid = Value(tether, "SSID", _cfg.Ssid);
                    _activeUser = _cfg.Login;
                    _activeShare = ShareName;

                    if (local)
                    {
                        Log("Локальная точка доступа работает. Сеть: «" + _activeSsid + "», адрес компьютера: "
                            + _activeIp + ". Интернет для неё не нужен.");
                    }
                    else
                    {
                        string band = Value(tether, "BAND", "");
                        string bandText = band == "FiveGigahertz" ? "5 ГГц" : (band == "TwoPointFourGigahertz" ? "2,4 ГГц" : "авто");
                        Log("Точка доступа работает. Сеть: «" + _activeSsid + "», диапазон: " + bandText
                            + ", адрес компьютера: " + _activeIp);
                    }
                }

                Log("Открываю доступ к папке «" + _cfg.Folder + "»…");
                Dictionary<string, string> share = _svc.ShareOn(
                    _cfg.Folder, ShareName, _cfg.Login, _cfg.LoginPassword, ShareService.SubnetOf(_activeIp),
                    _cfg.WebEnabled ? _cfg.WebPort : 0);

                if (share.TryGetValue("WARN", out note) && note.Length > 0) { Log("Замечание: " + note); }

                if (!ScriptRunner.IsOk(share))
                {
                    if (!_cfg.UseHomeNetwork)
                    {
                        Log("Откат: выключаю точку доступа.");
                        _svc.TetherOff();
                    }
                    Fail("Не удалось открыть общий доступ к папке", share);
                    return;
                }
                Log("Сетевая папка готова: \\\\" + _activeIp + "\\" + ShareName);
                VerifyShare();

                StartWebServer();

                _running = true;
                if (_cfg.UseHomeNetwork)
                {
                    Log("Готово! iPhone должен быть в той же сети Wi-Fi, что и компьютер.");
                }
                else
                {
                    Log("Готово! Подключите iPhone к сети «" + _activeSsid + "».");
                }
            }
            catch (Exception ex)
            {
                Log("Ошибка: " + ex.Message);
                _running = false;

                if (!_cfg.UseHomeNetwork)
                {
                    try { _wfd.Stop(0); } catch { }
                    try { _svc.TetherOff(); } catch { }
                }

                Ui(delegate
                {
                    MessageBox.Show(this, ex.Message, "Wi-Fi Флешка", MessageBoxButtons.OK, MessageBoxIcon.Error);
                });
            }
            finally
            {
                _busy = false;
                Ui(delegate { SetUiState(); });
            }
        }

        /// <summary>
        /// Точка доступа без интернета (Wi-Fi Direct, обычная сеть). Возвращает
        /// результат скрипта или null, если поднять не удалось — ошибка уже показана.
        /// </summary>
        private Dictionary<string, string> StartLocalHotspot()
        {
            Dictionary<string, string> wfd = _wfd.Start(_cfg.Ssid, _cfg.WifiPassword, 45000);

            string warn;
            if (wfd.TryGetValue("WARN", out warn) && warn.Length > 0) { Log("Замечание: " + warn); }

            if (!ScriptRunner.IsOk(wfd))
            {
                Fail("Не удалось включить точку доступа Wi-Fi", wfd);
                return null;
            }

            return wfd;
        }

        /// <summary>
        /// Ждёт, пока Windows запустит DHCP для точки доступа: без него телефон
        /// подключается, не получает адрес и через несколько секунд отваливается.
        /// </summary>
        private static bool WaitForDhcp(int seconds)
        {
            for (int i = 0; i < seconds * 2; i++)
            {
                if (WfdHotspot.DhcpListening()) { return true; }
                Thread.Sleep(500);
            }
            return false;
        }

        /// <summary>
        /// Проверяет, что папка действительно доступна по сети: слушается порт 445
        /// и общий ресурс зарегистрирован в Windows. Если нет — телефон увидит только ошибку.
        /// </summary>
        private void VerifyShare()
        {
            bool open = false;
            try
            {
                using (TcpClient client = new TcpClient())
                {
                    IAsyncResult wait = client.BeginConnect(_activeIp, 445, null, null);
                    open = wait.AsyncWaitHandle.WaitOne(2000) && client.Connected;
                }
            }
            catch { }

            if (!open)
            {
                Log("ВНИМАНИЕ: порт 445 не отвечает. Проверьте службу «Сервер» (LanmanServer).");
            }

            // Проверять доступ к \\\\ip\\share из этой же программы нельзя: ресурс выдан
            // пользователю iphone, а приложение работает под вашей учётной записью и
            // получит «нет доступа». Спрашиваем у Windows список общих ресурсов —
            // это доступно только с правами администратора, а они здесь уже есть.
            bool shared = true;
            if (IsAdministrator())
            {
                shared = false;
                try
                {
                    ProcessStartInfo psi = new ProcessStartInfo();
                    psi.FileName = Path.Combine(Environment.SystemDirectory, "net.exe");
                    psi.Arguments = "share \"" + _activeShare + "\"";
                    psi.UseShellExecute = false;
                    psi.CreateNoWindow = true;
                    psi.RedirectStandardOutput = true;
                    psi.RedirectStandardError = true;

                    using (Process p = Process.Start(psi))
                    {
                        p.StandardOutput.ReadToEnd();
                        p.StandardError.ReadToEnd();
                        p.WaitForExit(3000);
                        shared = p.HasExited && p.ExitCode == 0;
                    }
                }
                catch { }
            }

            if (!shared)
            {
                Log("ВНИМАНИЕ: общий ресурс «" + _activeShare + "» не найден в Windows.");
            }
        }

        private void StartWebServer()
        {
            if (!_cfg.WebEnabled)
            {
                Log("Веб-доступ отключён в настройках.");
                return;
            }

            try
            {
                _web = new FileWebServer(_cfg.Folder);
                _web.Start(_cfg.WebPort);
                Log("Веб-доступ: http://" + _activeIp + ":" + _web.Port + "/");
            }
            catch (Exception ex)
            {
                if (_web != null)
                {
                    _web.Dispose();
                    _web = null;
                }
                Log("Веб-доступ не запустился: " + ex.Message + " (на доступ через «Файлы» это не влияет)");
            }
        }

        private void DoStop()
        {
            try
            {
                if (_web != null)
                {
                    _web.Dispose();
                    _web = null;
                    Log("Веб-доступ остановлен.");
                }

                Log("Закрываю сетевую папку…");
                Dictionary<string, string> share = _svc.ShareOff(_cfg.Folder, _activeShare, _activeUser);
                if (!ScriptRunner.IsOk(share)) { Log("Замечание: " + ScriptRunner.Message(share)); }

                if (!_cfg.UseHomeNetwork)
                {
                    if (_wfd.IsRunning)
                    {
                        Log("Выключаю локальную точку доступа…");
                        _wfd.Stop(8000);
                    }
                    else if (!_wfdMode)
                    {
                        Log("Выключаю точку доступа…");
                        Dictionary<string, string> tether = _svc.TetherOff();
                        if (!ScriptRunner.IsOk(tether)) { Log("Замечание: " + ScriptRunner.Message(tether)); }
                    }

                    _wfdMode = false;
                }

                Log("Выключено. Папка и файлы остались на месте.");
            }
            catch (Exception ex)
            {
                Log("Ошибка при выключении: " + ex.Message);
            }
            finally
            {
                _running = false;
                _busy = false;
                Ui(delegate { SetUiState(); });
            }
        }

        private void CleanupAccount()
        {
            DialogResult answer = MessageBox.Show(
                this,
                "Удалить сетевую папку, правило брандмауэра и служебного пользователя Windows «" + _cfg.Login + "»?\r\n\r\n" +
                "Файлы в папке останутся на месте. Понадобятся права администратора.",
                "Wi-Fi Флешка",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (answer != DialogResult.Yes) { return; }

            if (!IsAdministrator())
            {
                SaveFromUi();
                RelaunchElevated(false);
                return;
            }

            _busy = true;
            SetUiState();
            ThreadPool.QueueUserWorkItem(delegate
            {
                // точка доступа больше не нужна: иначе она останется висеть
                // после удаления пользователя и папки
                if (_wfd.IsRunning)
                {
                    _wfd.Stop(4000);
                    _wfdMode = false;
                }

                Dictionary<string, string> result = _svc.Cleanup(_cfg.Folder, ShareName, _cfg.Login);
                if (ScriptRunner.IsOk(result))
                {
                    Log("Служебная учётная запись «" + _cfg.Login + "» удалена, доступ закрыт.");
                }
                else
                {
                    Log("Не удалось удалить: " + ScriptRunner.Message(result));
                }

                _busy = false;
                Ui(delegate { SetUiState(); });
            });
        }

        private void Fail(string prefix, Dictionary<string, string> result)
        {
            _running = false;
            string message = ScriptRunner.Message(result);
            Log(prefix + ": " + message);

            string log;
            if (result.TryGetValue("LOG", out log) && !string.IsNullOrWhiteSpace(log))
            {
                foreach (string line in log.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    Log("  " + line.Trim());
                }
            }

            Ui(delegate
            {
                MessageBox.Show(this, prefix + ".\r\n\r\n" + message, "Wi-Fi Флешка", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            });
        }

        // =====================================================================
        //  Служебное
        // =====================================================================

        /// <summary>
        /// Адрес компьютера в домашней сети. Сначала берём Wi-Fi с шлюзом,
        /// иначе — любой рабочий адаптер со шлюзом.
        /// </summary>
        private static string FindLocalAddress()
        {
            string fallback = null;
            try
            {
                foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up) { continue; }
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) { continue; }
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Tunnel) { continue; }

                    IPInterfaceProperties props = ni.GetIPProperties();
                    bool hasGateway = false;
                    foreach (GatewayIPAddressInformation gw in props.GatewayAddresses)
                    {
                        if (gw.Address != null && !gw.Address.Equals(IPAddress.Any)) { hasGateway = true; break; }
                    }
                    if (!hasGateway) { continue; }

                    foreach (UnicastIPAddressInformation ua in props.UnicastAddresses)
                    {
                        if (ua.Address.AddressFamily != AddressFamily.InterNetwork) { continue; }
                        string ip = ua.Address.ToString();
                        if (ip.StartsWith("127.") || ip.StartsWith("169.254.")) { continue; }

                        if (ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211) { return ip; }
                        if (fallback == null) { fallback = ip; }
                    }
                }
            }
            catch
            {
                return null;
            }
            return fallback;
        }

        private void Log(string message)
        {
            string line = "[" + DateTime.Now.ToString("HH:mm:ss") + "] " + message + Environment.NewLine;
            Ui(delegate
            {
                _txtLog.AppendText(line);
            });

            // дублируем в файл: окно закрывается, а разбираться с подключением
            // телефона приходится уже потом
            try
            {
                string dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PhoneDrive");
                if (!Directory.Exists(dir)) { Directory.CreateDirectory(dir); }

                string file = Path.Combine(dir, "app.log");
                if (!_logTrimmed && File.Exists(file) && new FileInfo(file).Length > 512 * 1024)
                {
                    File.Delete(file);
                    _logTrimmed = true;
                }
                File.AppendAllText(file,
                    "[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "] " + message + Environment.NewLine);
            }
            catch { }
        }

        private void Ui(Action action)
        {
            if (IsDisposed || Disposing) { return; }
            try
            {
                if (InvokeRequired) { BeginInvoke(action); }
                else { action(); }
            }
            catch (ObjectDisposedException) { }
            catch (InvalidOperationException) { }
        }

        private static string Value(Dictionary<string, string> data, string key, string fallback)
        {
            string value;
            if (data.TryGetValue(key, out value) && !string.IsNullOrWhiteSpace(value)) { return value.Trim(); }
            return fallback;
        }

        private static bool IsAdministrator()
        {
            try
            {
                using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
                {
                    return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
                }
            }
            catch
            {
                return false;
            }
        }

        private void RelaunchElevated(bool autoStart)
        {
            try
            {
                ProcessStartInfo info = new ProcessStartInfo();
                info.FileName = Application.ExecutablePath;
                info.Arguments = autoStart ? "--autostart" : "";
                info.UseShellExecute = true;
                info.Verb = "runas";
                info.WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory;
                Process.Start(info);
                Close();
            }
            catch (System.ComponentModel.Win32Exception)
            {
                Log("Запуск с правами администратора отменён.");
            }
            catch (Exception ex)
            {
                Log("Не удалось перезапуститься с правами администратора: " + ex.Message);
            }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);

            Log("Запуск. Режим: " + (_cfg.UseHomeNetwork ? "домашняя сеть (без точки доступа)" : "точка доступа Wi-Fi")
                + ". Папка: " + _cfg.Folder);

            if (_autoStart && !_running && !_busy)
            {
                StartAsync();
                return;
            }

            ThreadPool.QueueUserWorkItem(delegate
            {
                Dictionary<string, string> state = _svc.TetherStatus();
                if (ScriptRunner.IsOk(state) && Value(state, "STATE", "") == "On")
                {
                    Log("Точка доступа Wi-Fi уже включена в Windows. Приложение будет использовать её как есть — "
                        + "имя сети может отличаться от указанного выше.");
                }
            });
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (_busy)
            {
                e.Cancel = true;
                Log("Дождитесь завершения операции.");
                return;
            }

            if (_running && !_closing)
            {
                _closing = true;
                UseWaitCursor = true;
                Log("Закрываю доступ и выключаю точку доступа…");

                if (_web != null)
                {
                    _web.Dispose();
                    _web = null;
                }

                _svc.ShareOff(_cfg.Folder, _activeShare, _activeUser);

                if (!_cfg.UseHomeNetwork)
                {
                    if (_wfd.IsRunning) { _wfd.Stop(6000); }
                    else if (!_wfdMode) { _svc.TetherOff(); }
                    _wfdMode = false;
                }

                _running = false;
                UseWaitCursor = false;
            }

            SaveQuietly();
            base.OnFormClosing(e);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;

namespace PhoneDrive
{
    /// <summary>
    /// Небольшой файловый сервер для браузера Safari: просмотр папки, скачивание,
    /// загрузка файлов с телефона и создание папок. Работает без прав администратора
    /// на уровне протокола, но для префикса http://+:port/ нужен urlacl (добавляем сами).
    /// </summary>
    internal sealed class FileWebServer : IDisposable
    {
        private readonly string _root;
        private readonly string _prefix;
        private HttpListener _listener;
        private Thread _thread;
        private volatile bool _running;

        public int Port { get; private set; }
        public string Root { get { return _root; } }

        public FileWebServer(string root)
        {
            _root = Path.GetFullPath(root);
        }

        /// <summary>Позволяет подменить префикс (используется в автотестах).</summary>
        internal FileWebServer(string root, string prefix)
            : this(root)
        {
            _prefix = prefix;
        }

        public void Start(int port)
        {
            Port = port;

            _listener = new HttpListener();
            _listener.Prefixes.Add(_prefix ?? ("http://+:" + port + "/"));

            try
            {
                _listener.Start();
            }
            catch (HttpListenerException)
            {
                if (_prefix != null) { throw; }
                ReserveUrl(port);
                _listener.Start();
            }

            _running = true;
            _thread = new Thread(Loop);
            _thread.IsBackground = true;
            _thread.Name = "PhoneDrive web";
            _thread.Start();
        }

        private static void ReserveUrl(int port)
        {
            try
            {
                ProcessStartInfo info = new ProcessStartInfo();
                info.FileName = "netsh.exe";
                info.Arguments = "http add urlacl url=http://+:" + port + "/ user=Everyone";
                info.UseShellExecute = false;
                info.CreateNoWindow = true;
                info.RedirectStandardOutput = true;
                info.RedirectStandardError = true;
                using (Process p = Process.Start(info))
                {
                    p.StandardOutput.ReadToEnd();
                    p.StandardError.ReadToEnd();
                    p.WaitForExit(5000);
                }
            }
            catch
            {
                // если не получилось - HttpListener.Start() выбросит исключение выше
            }
        }

        private void Loop()
        {
            while (_running)
            {
                HttpListenerContext context;
                try
                {
                    context = _listener.GetContext();
                }
                catch
                {
                    break;
                }

                ThreadPool.QueueUserWorkItem(delegate(object state) { Handle((HttpListenerContext)state); }, context);
            }
        }

        public void Dispose()
        {
            _running = false;
            if (_listener != null)
            {
                try { _listener.Stop(); } catch { }
                try { _listener.Close(); } catch { }
                _listener = null;
            }
        }

        // --------------------------------------------------------------------

        private void Handle(HttpListenerContext context)
        {
            try
            {
                HttpListenerRequest request = context.Request;
                string path = request.Url.AbsolutePath;

                if (path == "/dl") { SendFile(context); return; }
                if (path == "/up" && request.HttpMethod == "POST") { ReceiveFile(context); return; }
                if (path == "/rm" && request.HttpMethod == "POST") { RemoveEntry(context); return; }
                if (path == "/mk" && request.HttpMethod == "POST") { MakeDirectory(context); return; }

                SendPage(context);
            }
            catch (Exception ex)
            {
                TryFail(context, ex.Message);
            }
            finally
            {
                try { context.Response.Close(); } catch { }
            }
        }

        private void SendPage(HttpListenerContext context)
        {
            string relative = NormalizeRelative(QueryValue(context, "d"));
            string directory = Resolve(relative);

            if (!Directory.Exists(directory))
            {
                TryFail(context, "Папка не найдена.");
                return;
            }

            string html = RenderPage(relative, directory);
            byte[] body = Encoding.UTF8.GetBytes(html);

            context.Response.StatusCode = 200;
            context.Response.ContentType = "text/html; charset=utf-8";
            context.Response.Headers["Cache-Control"] = "no-store";
            context.Response.ContentLength64 = body.Length;
            context.Response.OutputStream.Write(body, 0, body.Length);
        }

        private void SendFile(HttpListenerContext context)
        {
            string relative = NormalizeRelative(QueryValue(context, "p"));
            string file = Resolve(relative);

            if (!File.Exists(file))
            {
                TryFail(context, "Файл не найден.");
                return;
            }

            FileInfo info = new FileInfo(file);
            context.Response.StatusCode = 200;
            context.Response.ContentType = ContentTypeFor(info.Extension);
            context.Response.Headers["Content-Disposition"] =
                "attachment; filename*=UTF-8''" + Uri.EscapeDataString(info.Name);
            context.Response.ContentLength64 = info.Length;
            context.Response.Headers["Cache-Control"] = "no-store";

            using (FileStream stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                byte[] buffer = new byte[128 * 1024];
                int read;
                while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
                {
                    context.Response.OutputStream.Write(buffer, 0, read);
                }
            }
        }

        private void ReceiveFile(HttpListenerContext context)
        {
            string directory = Resolve(NormalizeRelative(QueryValue(context, "d")));
            string name = SafeName(QueryValue(context, "n"));

            if (name.Length == 0)
            {
                TryFail(context, "Не указано имя файла.");
                return;
            }
            if (!Directory.Exists(directory))
            {
                TryFail(context, "Папка не найдена.");
                return;
            }

            string target = UniquePath(Path.Combine(directory, name));
            long length = context.Request.ContentLength64;

            using (FileStream output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                byte[] buffer = new byte[128 * 1024];
                long total = 0;
                int read;
                while ((read = context.Request.InputStream.Read(buffer, 0, buffer.Length)) > 0)
                {
                    output.Write(buffer, 0, read);
                    total += read;
                    if (total > 32L * 1024 * 1024 * 1024) { throw new IOException("Файл слишком большой."); }
                }
            }

            Respond(context, "OK " + length.ToString(CultureInfo.InvariantCulture));
        }

        private void RemoveEntry(HttpListenerContext context)
        {
            string target = Resolve(NormalizeRelative(QueryValue(context, "p")));
            if (string.Equals(target, _root, StringComparison.OrdinalIgnoreCase))
            {
                TryFail(context, "Нельзя удалить корневую папку.");
                return;
            }

            if (Directory.Exists(target))
            {
                Directory.Delete(target, true);
            }
            else if (File.Exists(target))
            {
                File.Delete(target);
            }

            Respond(context, "OK");
        }

        private void MakeDirectory(HttpListenerContext context)
        {
            string parent = Resolve(NormalizeRelative(QueryValue(context, "d")));
            string name = SafeName(QueryValue(context, "n"));

            if (name.Length == 0)
            {
                TryFail(context, "Не указано имя папки.");
                return;
            }

            Directory.CreateDirectory(UniquePath(Path.Combine(parent, name)));
            Respond(context, "OK");
        }

        // --------------------------------------------------------------------

        private static void Respond(HttpListenerContext context, string text)
        {
            byte[] body = Encoding.UTF8.GetBytes(text);
            context.Response.StatusCode = 200;
            context.Response.ContentType = "text/plain; charset=utf-8";
            context.Response.ContentLength64 = body.Length;
            context.Response.OutputStream.Write(body, 0, body.Length);
        }

        private static void TryFail(HttpListenerContext context, string message)
        {
            try
            {
                byte[] body = Encoding.UTF8.GetBytes(message ?? "Ошибка");
                context.Response.StatusCode = 400;
                context.Response.ContentType = "text/plain; charset=utf-8";
                context.Response.ContentLength64 = body.Length;
                context.Response.OutputStream.Write(body, 0, body.Length);
            }
            catch
            {
                // клиент уже отключился
            }
        }

        /// <summary>
        /// HttpListener разбирает query-строку в кодировке ANSI, из-за чего кириллица
        /// в именах файлов превращается в мусор. Поэтому разбираем вручную как UTF-8.
        /// </summary>
        private static string QueryValue(HttpListenerContext context, string key)
        {
            string query = context.Request.Url == null ? null : context.Request.Url.Query;
            if (string.IsNullOrEmpty(query) || query.Length < 2) { return null; }

            foreach (string pair in query.Substring(1).Split('&'))
            {
                if (pair.Length == 0) { continue; }

                int split = pair.IndexOf('=');
                string name = split < 0 ? pair : pair.Substring(0, split);
                if (!string.Equals(Uri.UnescapeDataString(name), key, StringComparison.Ordinal)) { continue; }

                string value = split < 0 ? "" : pair.Substring(split + 1);
                try { return Uri.UnescapeDataString(value); }
                catch { return null; }
            }

            return null;
        }

        private static string NormalizeRelative(string value)
        {            if (string.IsNullOrEmpty(value)) { return ""; }
            string result = value.Replace('\\', '/').Trim();
            while (result.StartsWith("/")) { result = result.Substring(1); }
            return result.TrimEnd('/');
        }

        private string Resolve(string relative)
        {
            string combined = Path.Combine(_root, relative.Replace('/', Path.DirectorySeparatorChar));
            string full = Path.GetFullPath(combined);
            string rootWithSlash = _root.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
                ? _root
                : _root + Path.DirectorySeparatorChar;

            if (!string.Equals(full, _root, StringComparison.OrdinalIgnoreCase) &&
                !full.StartsWith(rootWithSlash, StringComparison.OrdinalIgnoreCase))
            {
                throw new UnauthorizedAccessException("Недопустимый путь.");
            }

            return full;
        }

        private static string SafeName(string name)
        {
            if (string.IsNullOrEmpty(name)) { return ""; }
            string result = name.Replace('/', ' ').Replace('\\', ' ').Trim();
            foreach (char bad in Path.GetInvalidFileNameChars())
            {
                result = result.Replace(bad, ' ');
            }
            result = result.Trim().Trim('.');
            return result.Length > 180 ? result.Substring(0, 180) : result;
        }

        private static string UniquePath(string path)
        {
            if (!File.Exists(path) && !Directory.Exists(path)) { return path; }

            string directory = Path.GetDirectoryName(path);
            string extension = Path.GetExtension(path);
            string stem = Path.GetFileNameWithoutExtension(path);

            for (int i = 2; i < 1000; i++)
            {
                string candidate = Path.Combine(directory, stem + " (" + i.ToString(CultureInfo.InvariantCulture) + ")" + extension);
                if (!File.Exists(candidate) && !Directory.Exists(candidate)) { return candidate; }
            }

            return Path.Combine(directory, stem + " (" + Guid.NewGuid().ToString("N").Substring(0, 6) + ")" + extension);
        }

        private static string ContentTypeFor(string extension)
        {
            switch ((extension ?? "").ToLowerInvariant())
            {
                case ".jpg": case ".jpeg": return "image/jpeg";
                case ".png": return "image/png";
                case ".gif": return "image/gif";
                case ".heic": return "image/heic";
                case ".webp": return "image/webp";
                case ".mp4": case ".m4v": return "video/mp4";
                case ".mov": return "video/quicktime";
                case ".mp3": return "audio/mpeg";
                case ".m4a": return "audio/mp4";
                case ".pdf": return "application/pdf";
                case ".txt": return "text/plain; charset=utf-8";
                case ".zip": return "application/zip";
                default: return "application/octet-stream";
            }
        }

        // --------------------------------------------------------------------

        private string RenderPage(string relative, string directory)
        {
            DirectoryInfo info = new DirectoryInfo(directory);
            string prefix = relative.Length == 0 ? "" : relative + "/";

            List<DirectoryInfo> folders = new List<DirectoryInfo>();
            List<FileInfo> files = new List<FileInfo>();

            try
            {
                foreach (DirectoryInfo child in info.GetDirectories())
                {
                    if ((child.Attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0) { continue; }
                    folders.Add(child);
                }
                foreach (FileInfo child in info.GetFiles())
                {
                    if ((child.Attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0) { continue; }
                    files.Add(child);
                }
            }
            catch (Exception ex)
            {
                return "<!DOCTYPE html><html><body style=\"font-family:sans-serif;padding:24px\">"
                    + "<h3>Не удалось прочитать папку</h3><p>" + Html(ex.Message) + "</p></body></html>";
            }

            folders.Sort(delegate(DirectoryInfo a, DirectoryInfo b)
            {
                return string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase);
            });
            files.Sort(delegate(FileInfo a, FileInfo b)
            {
                return string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase);
            });

            StringBuilder items = new StringBuilder();
            if (relative.Length > 0)
            {
                string parent = relative.Contains("/")
                    ? relative.Substring(0, relative.LastIndexOf('/'))
                    : "";
                items.Append("<a class=\"row up\" href=\"/?d=").Append(Uri.EscapeDataString(parent)).Append("\">");
                items.Append("<span class=\"ico\">&#8592;</span><span class=\"name\">Наверх</span></a>");
            }

            foreach (DirectoryInfo folder in folders)
            {
                string child = prefix + folder.Name;
                items.Append("<div class=\"row\">");
                items.Append("<a class=\"link\" href=\"/?d=").Append(Uri.EscapeDataString(child)).Append("\">");
                items.Append("<span class=\"ico\">&#128193;</span><span class=\"name\">").Append(Html(folder.Name)).Append("</span>");
                items.Append("<span class=\"meta\">папка</span></a>");
                items.Append("<button class=\"del\" data-p=\"").Append(Html(child)).Append("\" title=\"Удалить\">&#10005;</button>");
                items.Append("</div>");
            }

            foreach (FileInfo file in files)
            {
                string child = prefix + file.Name;
                items.Append("<div class=\"row\">");
                items.Append("<a class=\"link\" href=\"/dl?p=").Append(Uri.EscapeDataString(child)).Append("\">");
                items.Append("<span class=\"ico\">&#128196;</span><span class=\"name\">").Append(Html(file.Name)).Append("</span>");
                items.Append("<span class=\"meta\">").Append(Html(FormatSize(file.Length))).Append("</span></a>");
                items.Append("<button class=\"del\" data-p=\"").Append(Html(child)).Append("\" title=\"Удалить\">&#10005;</button>");
                items.Append("</div>");
            }

            if (folders.Count == 0 && files.Count == 0)
            {
                items.Append("<p class=\"empty\">Папка пуста. Загрузите файлы кнопкой ниже.</p>");
            }

            string html = PageTemplate;
            html = html.Replace("@@TITLE@@", Html(DisplayPath(relative)));
            html = html.Replace("@@CRUMBS@@", BuildCrumbs(relative));
            html = html.Replace("@@ITEMS@@", items.ToString());
            html = html.Replace("@@DIR@@", Uri.EscapeDataString(relative));
            html = html.Replace("@@COUNT@@", (folders.Count + files.Count).ToString(CultureInfo.InvariantCulture));
            return html;
        }

        private static string DisplayPath(string relative)
        {
            return relative.Length == 0 ? "Wi-Fi Флешка" : relative.Replace('/', ' ').Trim();
        }

        private static string BuildCrumbs(string relative)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("<a href=\"/\">Wi-Fi Флешка</a>");

            if (relative.Length > 0)
            {
                string[] parts = relative.Split('/');
                string accumulated = "";
                for (int i = 0; i < parts.Length; i++)
                {
                    if (parts[i].Length == 0) { continue; }
                    accumulated = accumulated.Length == 0 ? parts[i] : accumulated + "/" + parts[i];
                    sb.Append("<span class=\"sep\">/</span>");
                    sb.Append("<a href=\"/?d=").Append(Uri.EscapeDataString(accumulated)).Append("\">")
                      .Append(Html(parts[i])).Append("</a>");
                }
            }

            return sb.ToString();
        }

        private static string FormatSize(long bytes)
        {
            if (bytes < 1024) { return bytes.ToString(CultureInfo.InvariantCulture) + " Б"; }
            if (bytes < 1024 * 1024) { return (bytes / 1024.0).ToString("0.#", CultureInfo.CurrentCulture) + " КБ"; }
            if (bytes < 1024L * 1024 * 1024) { return (bytes / (1024.0 * 1024)).ToString("0.#", CultureInfo.CurrentCulture) + " МБ"; }
            return (bytes / (1024.0 * 1024 * 1024)).ToString("0.##", CultureInfo.CurrentCulture) + " ГБ";
        }

        private static string Html(string text)
        {
            if (text == null) { return ""; }
            return text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;")
                       .Replace("\"", "&quot;").Replace("'", "&#39;");
        }

        // --------------------------------------------------------------------

        private const string PageTemplate = @"<!DOCTYPE html>
<html lang=""ru"">
<head>
<meta charset=""utf-8"">
<meta name=""viewport"" content=""width=device-width, initial-scale=1, viewport-fit=cover"">
<meta name=""apple-mobile-web-app-capable"" content=""yes"">
<title>@@TITLE@@</title>
<style>
:root { color-scheme: dark; }
* { box-sizing: border-box; -webkit-tap-highlight-color: transparent; }
body {
  margin: 0; padding: 16px 14px 120px;
  font: 16px/1.45 -apple-system, BlinkMacSystemFont, ""Segoe UI"", Roboto, sans-serif;
  background: #0d1117; color: #e6edf3;
}
h1 { font-size: 20px; margin: 0 0 4px; font-weight: 600; }
.crumbs { font-size: 14px; color: #8b949e; margin-bottom: 14px; word-break: break-all; }
.crumbs a { color: #58a6ff; text-decoration: none; }
.crumbs .sep { margin: 0 6px; color: #484f58; }
.row { display: flex; align-items: center; gap: 4px; border-bottom: 1px solid #21262d; }
.row .link { flex: 1; display: flex; align-items: center; gap: 12px; padding: 14px 4px; color: inherit; text-decoration: none; min-width: 0; }
.row .ico { font-size: 20px; width: 26px; text-align: center; flex: none; }
.row .name { flex: 1; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.row .meta { color: #8b949e; font-size: 13px; flex: none; }
.row.up .name { color: #58a6ff; }
.del { background: transparent; border: 0; color: #6e7681; font-size: 17px; padding: 12px; cursor: pointer; }
.empty { color: #8b949e; text-align: center; margin: 40px 0; }
.bar {
  position: fixed; left: 0; right: 0; bottom: 0; padding: 12px 14px calc(12px + env(safe-area-inset-bottom));
  display: flex; gap: 10px; background: #161b22; border-top: 1px solid #30363d;
}
.btn {
  flex: 1; display: flex; align-items: center; justify-content: center; height: 50px;
  border: 1px solid #30363d; border-radius: 12px; background: #21262d; color: #e6edf3;
  font: 600 16px -apple-system, BlinkMacSystemFont, ""Segoe UI"", Roboto, sans-serif; cursor: pointer;
}
.btn.primary { background: #238636; border-color: #2ea043; color: #fff; }
.progress {
  position: fixed; left: 14px; right: 14px; bottom: 86px; padding: 10px 14px;
  background: #161b22; border: 1px solid #30363d; border-radius: 12px; display: none;
}
.progress .track { height: 6px; background: #21262d; border-radius: 3px; overflow: hidden; margin-top: 8px; }
.progress .fill { height: 100%; width: 0; background: #2ea043; transition: width .15s linear; }
.progress .text { font-size: 13px; color: #8b949e; }
</style>
</head>
<body>
<h1>@@TITLE@@</h1>
<div class=""crumbs"">@@CRUMBS@@ <span class=""sep"">·</span> @@COUNT@@ эл.</div>
<div>@@ITEMS@@</div>

<div class=""progress"" id=""progress"">
  <div class=""text"" id=""ptext"">Загрузка…</div>
  <div class=""track""><div class=""fill"" id=""pfill""></div></div>
</div>

<div class=""bar"">
  <label class=""btn primary"">Загрузить файлы<input type=""file"" id=""picker"" multiple hidden></label>
  <button class=""btn"" id=""mkdir"">Новая папка</button>
</div>

<script>
(function () {
  var dir = decodeURIComponent(""@@DIR@@"");
  var picker = document.getElementById('picker');
  var progress = document.getElementById('progress');
  var ptext = document.getElementById('ptext');
  var pfill = document.getElementById('pfill');

  function put(file, onProgress) {
    return new Promise(function (resolve, reject) {
      var xhr = new XMLHttpRequest();
      xhr.open('POST', '/up?d=' + encodeURIComponent(dir) + '&n=' + encodeURIComponent(file.name));
      xhr.upload.onprogress = function (e) { if (e.lengthComputable) { onProgress(e.loaded / e.total); } };
      xhr.onload = function () { (xhr.status === 200) ? resolve() : reject(new Error(xhr.responseText || 'Ошибка')); };
      xhr.onerror = function () { reject(new Error('Сеть недоступна')); };
      xhr.send(file);
    });
  }

  picker.addEventListener('change', function () {
    var files = Array.prototype.slice.call(picker.files || []);
    if (files.length === 0) { return; }
    progress.style.display = 'block';
    var index = 0;

    function next() {
      if (index >= files.length) {
        progress.style.display = 'none';
        picker.value = '';
        location.reload();
        return;
      }
      var file = files[index];
      ptext.textContent = 'Загрузка ' + (index + 1) + ' из ' + files.length + ': ' + file.name;
      put(file, function (ratio) {
        pfill.style.width = Math.round(ratio * 100) + '%';
      }).then(function () {
        index++;
        next();
      }).catch(function (error) {
        progress.style.display = 'none';
        picker.value = '';
        alert('Не удалось загрузить ' + file.name + ': ' + error.message);
        location.reload();
      });
    }

    next();
  });

  document.getElementById('mkdir').addEventListener('click', function () {
    var name = prompt('Имя новой папки');
    if (!name) { return; }
    fetch('/mk?d=' + encodeURIComponent(dir) + '&n=' + encodeURIComponent(name), { method: 'POST' })
      .then(function () { location.reload(); });
  });

  Array.prototype.forEach.call(document.querySelectorAll('.del'), function (button) {
    button.addEventListener('click', function () {
      var path = button.getAttribute('data-p');
      if (!confirm('Удалить «' + path + '»?')) { return; }
      fetch('/rm?p=' + encodeURIComponent(path), { method: 'POST' })
        .then(function () { location.reload(); });
    });
  });
})();
</script>
</body>
</html>";
    }
}

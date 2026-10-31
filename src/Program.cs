using System;
using System.Threading;
using System.Windows.Forms;

namespace PhoneDrive
{
    internal static class Program
    {
        private const string MutexName = "PhoneDrive_SingleInstance_v1";

        [STAThread]
        private static void Main(string[] args)
        {
            bool autoStart = false;
            if (args != null)
            {
                foreach (string arg in args)
                {
                    if (string.Equals(arg, "--autostart", StringComparison.OrdinalIgnoreCase)) { autoStart = true; }
                }
            }

            bool createdNew;
            using (Mutex mutex = new Mutex(true, MutexName, out createdNew))
            {
                if (!createdNew)
                {
                    // Возможен случай, когда старая копия ещё закрывается перед
                    // перезапуском с правами администратора - подождём её.
                    bool acquired = false;
                    try { acquired = mutex.WaitOne(8000); }
                    catch (AbandonedMutexException) { acquired = true; }

                    if (!acquired)
                    {
                        MessageBox.Show(
                            "Wi-Fi Флешка уже запущена.\r\n\r\nНайдите её окно на панели задач.",
                            "Wi-Fi Флешка",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Information);
                        return;
                    }
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                Application.ThreadException += delegate(object sender, ThreadExceptionEventArgs e)
                {
                    Report(e.Exception);
                };

                AppDomain.CurrentDomain.UnhandledException += delegate(object sender, UnhandledExceptionEventArgs e)
                {
                    Report(e.ExceptionObject as Exception);
                };

                Application.Run(new MainForm(autoStart));
            }
        }

        private static void Report(Exception exception)
        {
            string message = exception == null ? "неизвестная ошибка" : exception.Message;
            try
            {
                MessageBox.Show(
                    "Непредвиденная ошибка: " + message,
                    "Wi-Fi Флешка",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            catch
            {
                // окно сообщения уже недоступно
            }
        }
    }
}

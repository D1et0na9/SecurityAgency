using System;
using System.Windows.Forms;

namespace SecurityAgencysApp
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            // Современная инициализация WinForms в .NET 8
            ApplicationConfiguration.Initialize();

            // 1) Инициализируем строку подключения к БД самым первым делом.
            try
            {
                FirebirdConnection.Initialize(
                    dataSource: "localhost",
                    userId: "SYSDBA",
                    password: "masterkey",
                    database: @"C:\Base\Secure_Base.fdb",
                    port: 3050,
                    charset: "UTF8"
                );

                // Быстрая проверка — открыть/закрыть соединение
                using var conn = FirebirdConnection.CreateOpenConnection();
                conn.Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Не удалось подключиться к базе данных: {ex.Message}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            // 2) Показать форму авторизации модально
            using (var loginForm = new LoginForm())
            {
                var result = loginForm.ShowDialog();
                if (result == DialogResult.OK)
                {
                    // Пользователь авторизован — запускаем главное окно
                    Application.Run(new Form1());
                }
                else
                {
                    // Пользователь отменил вход — выходим
                    return;
                }
            }
        }
    }
}
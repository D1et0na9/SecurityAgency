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
                if (result != DialogResult.OK)
                {
                    // Пользователь отменил вход — выходим
                    return;
                }
            }

            // 3) Цикл запуска главной формы с поддержкой повторной авторизации
            bool shouldRestart = true;
            while (shouldRestart)
            {
                shouldRestart = false;
                
                using (var form = new Form1())
                {
                    // Запускаем главное окно
                    Application.Run(form);

                    // Если пользователь выбрал смену учётной записи
                    if (form.DialogResult == DialogResult.Retry)
                    {
                        // Показываем форму авторизации снова
                        using (var loginForm = new LoginForm())
                        {
                            var result = loginForm.ShowDialog();
                            if (result == DialogResult.OK)
                            {
                                // Пользователь успешно авторизовался — перезапускаем главное окно
                                shouldRestart = true;
                            }
                            // Если отменил, цикл завершится и приложение закроется
                        }
                    }
                }
            }
        }
    }
}
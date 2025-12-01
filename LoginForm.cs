using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows.Forms;
using FirebirdSql.Data.FirebirdClient;

namespace SecurityAgencysApp
{
    public partial class LoginForm : Form
    {
        // Включить для отладки причин неуспешного входа (не оставлять включённым в production)
        private const bool EnableAuthDebug = true;

        public AuthResult? AuthenticatedUser { get; private set; }

        public LoginForm()
        {
            InitializeComponent();

            AcceptButton = btnLogin;
            CancelButton = btnCancel;

        }

        private async void btnLogin_Click(object sender, EventArgs e)
        {
            btnLogin.Enabled = false;
            btnCancel.Enabled = false;

            try
            {
                var login = txtUser.Text.Trim();
                var password = txtPassword.Text ?? string.Empty;

                if (string.IsNullOrEmpty(login))
                {
                    MessageBox.Show(this, "Введите логин.", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                var result = await AuthenticateAsync(login, password);
                if (result.IsAuthenticated)
                {
                    AuthenticatedUser = result;
                    DialogResult = DialogResult.OK;
                    Close();
                    return;
                }
                else
                {
                    if (EnableAuthDebug)
                    {
                        // Включаем дополнительные проверки для отладки
                        var passwordHash = PasswordHelper.ComputeSha256Hash(password);
                        Debug.WriteLine($"[AuthDebug] Computed hash for '{login}': {passwordHash}");

                        var debugInfo = await DebugFetchStoredHashAsync(login);
                        if (debugInfo.Found)
                        {
                            var msg = $"Пользователь найден (колонка логина: {debugInfo.LoginColumn}).\n" +
                                      $"Колонка хеша: {debugInfo.HashColumn}\n" +
                                      $"Хеш в БД: {debugInfo.HashValue}\n" +
                                      $"Вычисленный хеш: {passwordHash}\n\n" +
                                      (string.Equals(debugInfo.HashValue, passwordHash, StringComparison.OrdinalIgnoreCase)
                                          ? "Хеши совпадают — проблема может быть в вызове процедуры AUTHENTICATE_USER или в дополнительных проверках."
                                          : "Хеши не совпадают — введён неверный пароль или в БД хранится другой формат/соль.");
                            MessageBox.Show(this, msg, "Отладка авторизации", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        }
                        else
                        {
                            MessageBox.Show(this, "Пользователь не найден в таблице APP_USERS (по проверяемым колонкам). Проверьте схему таблицы или имена колонок.", "Отладка авторизации", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        }
                    }
                    MessageBox.Show(this, "Неверный логин или пароль.", "Ошибка авторизации", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            catch (FbException fbEx)
            {
                MessageBox.Show(this, $"Ошибка БД: {fbEx.Message}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Ошибка при проверке/подключении: {ex.Message}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                btnLogin.Enabled = true;
                btnCancel.Enabled = true;
            }
        }

        private void btnCancel_Click(object sender, EventArgs e)
        {
            DialogResult = DialogResult.Cancel;
            Close();
        }

        private async Task<AuthResult> AuthenticateAsync(string login, string password)
        {
            var auth = new AuthResult();

            // Хэшируем пароль (соответствует вашей реализации)
            var passwordHash = PasswordHelper.ComputeSha256Hash(password);

            // Используем уже инициализированное подключение (инициализация в Program.Main)
            await using var conn = await FirebirdConnection.CreateOpenConnectionAsync();
            await using var cmd = conn.CreateCommand();

            cmd.CommandText = "AUTHENTICATE_USER";
            cmd.CommandType = System.Data.CommandType.StoredProcedure;

            // Параметры процедуры — имена соответствуют вашей процедуре
            cmd.Parameters.AddWithValue("USER_LOGIN", login);
            cmd.Parameters.AddWithValue("PASSWORD_HASH", passwordHash);

            await using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                // Чтение колонок, если они возвращаются процедурой
                var ordUserId = reader.GetOrdinal("USER_ID");
                if (ordUserId >= 0 && !reader.IsDBNull(ordUserId))
                    auth.UserId = reader.GetInt32(ordUserId);

                var ordFullName = reader.GetOrdinal("FULL_NAME");
                if (ordFullName >= 0 && !reader.IsDBNull(ordFullName))
                    auth.FullName = reader.GetString(ordFullName);

                var ordRole = reader.GetOrdinal("ROLE_NAME");
                if (ordRole >= 0 && !reader.IsDBNull(ordRole))
                    auth.Role = reader.GetString(ordRole);
            }

            return auth;
        }

        // --- Отладочная проверка: пытаемся найти пользователя и колонку с хешем в таблице APP_USERS ---
        // Вернёт информацию о найденной колонке и значении хеша, если нашли.
        private async Task<(bool Found, string? LoginColumn, string? HashColumn, string? HashValue)> DebugFetchStoredHashAsync(string login)
        {
            // Популярные варианты имён колонок
            string[] loginCols = { "LOGIN", "USER_LOGIN", "USERNAME" };
            string[] hashCols = { "PASSWORD_HASH", "PASSWORD", "PASSWORDHASH", "USER_PASSWORD", "PASS", "PWD", "HASH" };

            await using var conn = await FirebirdConnection.CreateOpenConnectionAsync();

            foreach (var lcol in loginCols)
            {
                foreach (var hcol in hashCols)
                {
                    var sql = $"SELECT {hcol} FROM APP_USERS WHERE UPPER({lcol}) = UPPER(@login)";
                    try
                    {
                        await using var cmd = conn.CreateCommand();
                        cmd.CommandText = sql;
                        cmd.Parameters.Clear();
                        cmd.Parameters.AddWithValue("login", login);

                        var result = await cmd.ExecuteScalarAsync();
                        if (result != null && result != DBNull.Value)
                        {
                            var hashValue = result.ToString();
                            Debug.WriteLine($"[AuthDebug] Found hash column '{hcol}' (login column '{lcol}'): {hashValue}");
                            return (true, lcol, hcol, hashValue);
                        }
                        else
                        {
                            // Если запрос с корректными именами вернул null, значит колонка есть, но значение NULL — считаем найденным, с пустым значением
                            // но чаще это значит не нашли строку
                            Debug.WriteLine($"[AuthDebug] Query returned null for {lcol}/{hcol}");
                        }
                    }
                    catch (FbException ex)
                    {
                        // Ошибка означает, вероятно, что такого столбца/колонки нет — пропускаем
                        Debug.WriteLine($"[AuthDebug] Query failed for {lcol}/{hcol}: {ex.Message}");
                        continue;
                    }
                }
            }

            return (false, null, null, null);
        }
    }
}
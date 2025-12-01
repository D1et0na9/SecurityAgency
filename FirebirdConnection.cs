using System;
using System.Threading.Tasks;
using FirebirdSql.Data.FirebirdClient;

/// <summary>
/// Утилита для формирования строки подключения и создания соединений Firebird.
/// Инициализируется вызовом Initialize(...). После инициализации можно
/// получать открытые соединения через CreateOpenConnection / CreateOpenConnectionAsync.
/// </summary>
public static class FirebirdConnection
{
    private static string? _connectionString;

    /// <summary>
    /// Текущая строка подключения. Бросает InvalidOperationException, если не инициализирована.
    /// </summary>
    public static string ConnectionString
    {
        get => _connectionString ?? throw new InvalidOperationException("Строка подключения не инициализирована. Вызовите FirebirdConnection.Initialize(...) перед использованием.");
        private set => _connectionString = value;
    }

    /// <summary>
    /// Формирует и сохраняет строку подключения на основе параметров.
    /// Бросает ArgumentException при ошибках формирования.
    /// </summary>
    public static void Initialize(string dataSource, string userId, string password, string database, int port = 3050, string charset = "UTF8")
    {
        try
        {
            var cs = new FbConnectionStringBuilder
            {
                DataSource = dataSource,
                UserID = userId,
                Password = password,
                Database = database,
                Port = port,
                Charset = charset,
                Pooling = true
            };

            ConnectionString = cs.ToString();
        }
        catch (Exception ex)
        {
            throw new ArgumentException($"Невозможно сформировать строку подключения: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Создаёт и открывает синхронное соединение. Caller ответственен за закрытие/утилизацию соединения.
    /// </summary>
    public static FbConnection CreateOpenConnection()
    {
        var conn = new FbConnection(ConnectionString);
        conn.Open();
        return conn;
    }

    /// <summary>
    /// Создаёт и открывает асинхронное соединение. Caller ответственен за закрытие/утилизацию соединения.
    /// </summary>
    public static async Task<FbConnection> CreateOpenConnectionAsync()
    {
        var conn = new FbConnection(ConnectionString);
        await conn.OpenAsync();
        return conn;
    }
}
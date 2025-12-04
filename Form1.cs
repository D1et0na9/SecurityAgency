using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using FirebirdSql.Data.FirebirdClient;

namespace SecurityAgencysApp
{
    public partial class Form1 : Form
    {
        private readonly Dictionary<string, UserControl> _views = new();
        private UserControl? _currentView;

        public Form1()
        {
            InitializeComponent();

            // Загружаем данные при старте формы
            Load += Form1_Load;
        }

        private async void Form1_Load(object? sender, EventArgs e)
        {
            await LoadEmployeesAsync();

            // Загружаем клиентов с подсчётом связанных объектов и заказов
            await LoadClientsWithCountsAsync();

            // Загружаем охраняемые объекты с приклеенной информацией о клиенте и вызовах (в один грид — dataGridView3)
            await LoadGuardedObjectsCombinedAsync();

            // Загружаем вызовы с приклеенной информацией по заказам/услугам (в один грид — dataGridView4)
            await LoadGuardCallsCombinedAsync();
        }

        /// <summary>
        /// Загрузить сотрудников (через хранимую процедуру GET_EMPLOYEES)
        /// и отобразить их в dataGridView1. Колонка ID скрывается/не добавляется.
        /// Таблица будет полностью только для просмотра.
        /// </summary>
        private async Task LoadEmployeesAsync()
        {
            try
            {
                // Настройки DataGridView — только просмотр
                dataGridView1.ReadOnly = true;
                dataGridView1.AllowUserToAddRows = false;
                dataGridView1.AllowUserToDeleteRows = false;
                dataGridView1.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
                dataGridView1.MultiSelect = false;
                dataGridView1.AutoGenerateColumns = true;
                dataGridView1.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.AllCells;

                var table = new DataTable();

                // Создаём колонки, кроме ID и без BLOB (PHOTO)
                table.Columns.Add("NAME", typeof(string)).Caption = "Имя";
                table.Columns.Add("SURNAME", typeof(string)).Caption = "Фамилия";
                table.Columns.Add("SECOND_NAME", typeof(string)).Caption = "Отчество";
                table.Columns.Add("HIRE_DATE", typeof(DateTime)).Caption = "Дата приёма";
                table.Columns.Add("SALARY", typeof(decimal)).Caption = "Оклад";
                table.Columns.Add("EDUCATION", typeof(string)).Caption = "Образование";
                table.Columns.Add("POSITION_NAME", typeof(string)).Caption = "Должность";

                await using var conn = await FirebirdConnection.CreateOpenConnectionAsync();
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = "GET_EMPLOYEES";
                cmd.CommandType = System.Data.CommandType.StoredProcedure;

                await using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    var row = table.NewRow();

                    if (ColumnExists(reader, "NAME") && !reader.IsDBNull(reader.GetOrdinal("NAME")))
                        row["NAME"] = reader.GetString(reader.GetOrdinal("NAME"));

                    if (ColumnExists(reader, "SURNAME") && !reader.IsDBNull(reader.GetOrdinal("SURNAME")))
                        row["SURNAME"] = reader.GetString(reader.GetOrdinal("SURNAME"));

                    if (ColumnExists(reader, "SECOND_NAME") && !reader.IsDBNull(reader.GetOrdinal("SECOND_NAME")))
                        row["SECOND_NAME"] = reader.GetString(reader.GetOrdinal("SECOND_NAME"));

                    if (ColumnExists(reader, "HIRE_DATE") && !reader.IsDBNull(reader.GetOrdinal("HIRE_DATE")))
                        row["HIRE_DATE"] = reader.GetDateTime(reader.GetOrdinal("HIRE_DATE"));

                    if (ColumnExists(reader, "SALARY") && !reader.IsDBNull(reader.GetOrdinal("SALARY")))
                        row["SALARY"] = reader.GetDecimal(reader.GetOrdinal("SALARY"));

                    if (ColumnExists(reader, "EDUCATION") && !reader.IsDBNull(reader.GetOrdinal("EDUCATION")))
                        row["EDUCATION"] = reader.GetString(reader.GetOrdinal("EDUCATION"));

                    if (ColumnExists(reader, "POSITION_NAME") && !reader.IsDBNull(reader.GetOrdinal("POSITION_NAME")))
                        row["POSITION_NAME"] = reader.GetString(reader.GetOrdinal("POSITION_NAME"));

                    table.Rows.Add(row);
                }

                dataGridView1.DataSource = table;

                foreach (DataGridViewColumn col in dataGridView1.Columns)
                {
                    if (table.Columns.Contains(col.Name) && !string.IsNullOrEmpty(table.Columns[col.Name].Caption))
                        col.HeaderText = table.Columns[col.Name].Caption;

                    if (string.Equals(col.Name, "ID", StringComparison.OrdinalIgnoreCase))
                        col.Visible = false;
                }
            }
            catch (FbException fbEx)
            {
                MessageBox.Show(this, $"Ошибка при загрузке сотрудников (БД): {fbEx.Message}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Ошибка при загрузке сотрудников: {ex.Message}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>
        /// Загружает таблицу CLIENTS (без показа ID) и добавляет справа два столбца:
        /// - количество охраняемых объектов (GUARDEDOBJECTS) у клиента
        /// - количество заказов (ORDERS) у клиента
        /// </summary>
        private async Task LoadClientsWithCountsAsync()
        {
            try
            {
                // Настройка grid'а
                dataGridView2.ReadOnly = true;
                dataGridView2.AllowUserToAddRows = false;
                dataGridView2.AllowUserToDeleteRows = false;
                dataGridView2.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
                dataGridView2.MultiSelect = false;
                dataGridView2.AutoGenerateColumns = true;
                dataGridView2.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
                dataGridView2.Visible = true;

                // Таблица для клиентов: добавляем скрытый столбец ID для внутреннего использования
                var clientsTable = new DataTable();
                clientsTable.Columns.Add("ID", typeof(int)); // скрытый
                clientsTable.Columns.Add("NAME", typeof(string)).Caption = "Имя";
                clientsTable.Columns.Add("SURNAME", typeof(string)).Caption = "Фамилия";
                clientsTable.Columns.Add("SECOND_NAME", typeof(string)).Caption = "Отчество";
                clientsTable.Columns.Add("ADDRESS", typeof(string)).Caption = "Адрес";
                clientsTable.Columns.Add("PHONE", typeof(string)).Caption = "Телефон";
                clientsTable.Columns.Add("ACCOUNT_NUMBER", typeof(string)).Caption = "Л/сч";
                clientsTable.Columns.Add("IS_ACTIVE", typeof(bool)).Caption = "Активен";

                // Через процедуры получим сначала клиентов, затем заказы и объекты — посчитаем по clientId
                await using var conn = await FirebirdConnection.CreateOpenConnectionAsync();

                // 1) GET_CLIENTS
                var clientIds = new List<int>();
                await using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "GET_CLIENTS";
                    cmd.CommandType = System.Data.CommandType.StoredProcedure;

                    await using var reader = await cmd.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                    {
                        var row = clientsTable.NewRow();

                        if (ColumnExists(reader, "ID") && !reader.IsDBNull(reader.GetOrdinal("ID")))
                        {
                            var id = reader.GetInt32(reader.GetOrdinal("ID"));
                            row["ID"] = id;
                            clientIds.Add(id);
                        }

                        if (ColumnExists(reader, "NAME") && !reader.IsDBNull(reader.GetOrdinal("NAME")))
                            row["NAME"] = reader.GetString(reader.GetOrdinal("NAME"));

                        if (ColumnExists(reader, "SURNAME") && !reader.IsDBNull(reader.GetOrdinal("SURNAME")))
                            row["SURNAME"] = reader.GetString(reader.GetOrdinal("SURNAME"));

                        if (ColumnExists(reader, "SECOND_NAME") && !reader.IsDBNull(reader.GetOrdinal("SECOND_NAME")))
                            row["SECOND_NAME"] = reader.GetString(reader.GetOrdinal("SECOND_NAME"));

                        if (ColumnExists(reader, "ADDRESS") && !reader.IsDBNull(reader.GetOrdinal("ADDRESS")))
                            row["ADDRESS"] = reader.GetString(reader.GetOrdinal("ADDRESS"));

                        if (ColumnExists(reader, "PHONE") && !reader.IsDBNull(reader.GetOrdinal("PHONE")))
                            row["PHONE"] = reader.GetString(reader.GetOrdinal("PHONE"));

                        if (ColumnExists(reader, "ACCOUNT_NUMBER") && !reader.IsDBNull(reader.GetOrdinal("ACCOUNT_NUMBER")))
                            row["ACCOUNT_NUMBER"] = reader.GetString(reader.GetOrdinal("ACCOUNT_NUMBER"));

                        if (ColumnExists(reader, "IS_ACTIVE") && !reader.IsDBNull(reader.GetOrdinal("IS_ACTIVE")))
                            row["IS_ACTIVE"] = reader.GetBoolean(reader.GetOrdinal("IS_ACTIVE"));

                        clientsTable.Rows.Add(row);
                    }
                }

                // Подготовим словари для подсчётов
                var ordersCountByClient = new Dictionary<int, int>();
                var objectsCountByClient = new Dictionary<int, int>();
                // И отображение orderId -> clientId (чтобы сопоставить guardedobjects -> client)
                var orderToClient = new Dictionary<int, int>();

                // Инициализация нулевых значений для всех клиентов
                foreach (var id in clientIds)
                {
                    ordersCountByClient[id] = 0;
                    objectsCountByClient[id] = 0;
                }

                // 2) GET_ORDERS — соберём mapping orderId -> clientId и посчитаем заказы на клиента
                await using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "GET_ORDERS";
                    cmd.CommandType = System.Data.CommandType.StoredProcedure;

                    await using var reader = await cmd.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                    {
                        int? orderId = null;
                        int? clientId = null;

                        if (ColumnExists(reader, "ID") && !reader.IsDBNull(reader.GetOrdinal("ID")))
                            orderId = reader.GetInt32(reader.GetOrdinal("ID"));

                        if (ColumnExists(reader, "CLIENT_ID") && !reader.IsDBNull(reader.GetOrdinal("CLIENT_ID")))
                            clientId = reader.GetInt32(reader.GetOrdinal("CLIENT_ID"));

                        if (orderId.HasValue && clientId.HasValue)
                        {
                            orderToClient[orderId.Value] = clientId.Value;
                            if (ordersCountByClient.ContainsKey(clientId.Value))
                                ordersCountByClient[clientId.Value]++;
                            else
                                ordersCountByClient[clientId.Value] = 1;
                        }
                    }
                }

                // 3) GET_GUARDEDOBJECTS — используем ORDER_ID чтобы узнать клиент и посчитать объекты
                await using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "GET_GUARDEDOBJECTS";
                    cmd.CommandType = System.Data.CommandType.StoredProcedure;

                    await using var reader = await cmd.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                    {
                        int? orderId = null;

                        if (ColumnExists(reader, "ORDER_ID") && !reader.IsDBNull(reader.GetOrdinal("ORDER_ID")))
                            orderId = reader.GetInt32(reader.GetOrdinal("ORDER_ID"));

                        if (orderId.HasValue && orderToClient.TryGetValue(orderId.Value, out var clientId))
                        {
                            if (objectsCountByClient.ContainsKey(clientId))
                                objectsCountByClient[clientId]++;
                            else
                                objectsCountByClient[clientId] = 1;
                        }
                        // Если orderId не найден в mapping — объект "без заказа" либо данные не сопоставимы; пропускаем
                    }
                }

                // Добавляем итоговые колонки (не показываем ID)
                clientsTable.Columns.Add("OBJECT_COUNT", typeof(int)).Caption = "Объектов";
                clientsTable.Columns.Add("ORDER_COUNT", typeof(int)).Caption = "Заказов";

                // Заполняем значения OBJECT_COUNT и ORDER_COUNT по clientId
                foreach (DataRow row in clientsTable.Rows)
                {
                    if (row["ID"] == DBNull.Value) continue;

                    var clientId = (int)row["ID"];
                    row["OBJECT_COUNT"] = objectsCountByClient.TryGetValue(clientId, out var oCnt) ? oCnt : 0;
                    row["ORDER_COUNT"] = ordersCountByClient.TryGetValue(clientId, out var ordCnt) ? ordCnt : 0;
                }

                // Привязка к гриду
                dataGridView2.DataSource = clientsTable;

                // Настройка отображения: скрыть столбец ID, установить заголовки
                foreach (DataGridViewColumn col in dataGridView2.Columns)
                {
                    if (clientsTable.Columns.Contains(col.Name) && !string.IsNullOrEmpty(clientsTable.Columns[col.Name].Caption))
                        col.HeaderText = clientsTable.Columns[col.Name].Caption;

                    if (string.Equals(col.Name, "ID", StringComparison.OrdinalIgnoreCase))
                        col.Visible = false;

                    // Делаем колонки с числами компактными
                    if (string.Equals(col.Name, "OBJECT_COUNT", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(col.Name, "ORDER_COUNT", StringComparison.OrdinalIgnoreCase))
                    {
                        col.FillWeight = 10;
                        col.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
                        col.AutoSizeMode = DataGridViewAutoSizeColumnMode.AllCells;
                    }
                }
            }
            catch (FbException fbEx)
            {
                MessageBox.Show(this, $"Ошибка при загрузке клиентов (БД): {fbEx.Message}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Ошибка при загрузке клиентов: {ex.Message}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>
        /// Загрузить GUARDEDOBJECTS и "приклеить" к каждому:
        /// - поля CLIENT_FIO (из GET_GUARDEDOBJECTS)
        /// - агрегированную информацию по GUARDCALLS: количество вызовов и данные последнего вызова
        /// Результат отображается в одном DataGridView — dataGridView3. ID не показывается.
        /// Все поля — только для чтения.
        /// </summary>
        private async Task LoadGuardedObjectsCombinedAsync()
        {
            try
            {
                // Настройки грида
                dataGridView3.ReadOnly = true;
                dataGridView3.AllowUserToAddRows = false;
                dataGridView3.AllowUserToDeleteRows = false;
                dataGridView3.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
                dataGridView3.MultiSelect = false;
                dataGridView3.AutoGenerateColumns = true;
                dataGridView3.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
                dataGridView3.Visible = true;

                var table = new DataTable();

                // Включаем скрытый ID (для внутренней корректной работы), но не показываем его пользователю
                table.Columns.Add("ID", typeof(int)); // ID guarded object (скрыт)
                table.Columns.Add("ORDER_ID", typeof(int)).Caption = "Номер заказа"; // можно показать при необходимости
                table.Columns.Add("CLIENT_FIO", typeof(string)).Caption = "Клиент";
                table.Columns.Add("OBJECT_ADDRESS", typeof(string)).Caption = "Адрес объекта";
                table.Columns.Add("DESCRIPTION", typeof(string)).Caption = "Описание";

                // Колонки с информацией по вызовам (агрегаты / последний вызов)
                table.Columns.Add("CALL_COUNT", typeof(int)).Caption = "Вызовов";
                table.Columns.Add("LAST_CALL_DATETIME", typeof(DateTime)).Caption = "Последний вызов";
                table.Columns.Add("LAST_CALL_EMPLOYEE", typeof(string)).Caption = "Сотрудник";
                table.Columns.Add("LAST_CALL_RESULT", typeof(string)).Caption = "Результат";

                // Словари для агрегации вызовов: ключ — адрес объекта
                var callsByAddress = new Dictionary<string, List<(DateTime dt, string emp, string result)>>(StringComparer.OrdinalIgnoreCase);

                await using var conn = await FirebirdConnection.CreateOpenConnectionAsync();

                // 1) Получаем все вызовы и группируем по OBJECT_ADDRESS
                await using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "GET_GUARDCALLS";
                    cmd.CommandType = System.Data.CommandType.StoredProcedure;

                    await using var reader = await cmd.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                    {
                        string? addr = null;
                        DateTime? dt = null;
                        string emp = string.Empty;
                        string res = string.Empty;

                        if (ColumnExists(reader, "OBJECT_ADDRESS") && !reader.IsDBNull(reader.GetOrdinal("OBJECT_ADDRESS")))
                            addr = reader.GetString(reader.GetOrdinal("OBJECT_ADDRESS"));

                        if (ColumnExists(reader, "CALL_DATETIME") && !reader.IsDBNull(reader.GetOrdinal("CALL_DATETIME")))
                            dt = reader.GetDateTime(reader.GetOrdinal("CALL_DATETIME"));

                        if (ColumnExists(reader, "EMPLOYEE_FIO") && !reader.IsDBNull(reader.GetOrdinal("EMPLOYEE_FIO")))
                            emp = reader.GetString(reader.GetOrdinal("EMPLOYEE_FIO"));

                        if (ColumnExists(reader, "RESULT") && !reader.IsDBNull(reader.GetOrdinal("RESULT")))
                            res = reader.GetString(reader.GetOrdinal("RESULT"));

                        if (!string.IsNullOrEmpty(addr))
                        {
                            if (!callsByAddress.TryGetValue(addr, out var list))
                            {
                                list = new List<(DateTime, string, string)>();
                                callsByAddress[addr] = list;
                            }

                            if (dt.HasValue)
                                list.Add((dt.Value, emp, res));
                        }
                    }
                }

                // 2) Получаем guarded objects и формируем строки
                await using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "GET_GUARDEDOBJECTS";
                    cmd.CommandType = System.Data.CommandType.StoredProcedure;

                    await using var reader = await cmd.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                    {
                        var row = table.NewRow();

                        if (ColumnExists(reader, "ID") && !reader.IsDBNull(reader.GetOrdinal("ID")))
                            row["ID"] = reader.GetInt32(reader.GetOrdinal("ID"));

                        if (ColumnExists(reader, "ORDER_ID") && !reader.IsDBNull(reader.GetOrdinal("ORDER_ID")))
                            row["ORDER_ID"] = reader.GetInt32(reader.GetOrdinal("ORDER_ID"));

                        if (ColumnExists(reader, "CLIENT_FIO") && !reader.IsDBNull(reader.GetOrdinal("CLIENT_FIO")))
                            row["CLIENT_FIO"] = reader.GetString(reader.GetOrdinal("CLIENT_FIO"));

                        string? addr = null;
                        if (ColumnExists(reader, "OBJECT_ADDRESS") && !reader.IsDBNull(reader.GetOrdinal("OBJECT_ADDRESS")))
                        {
                            addr = reader.GetString(reader.GetOrdinal("OBJECT_ADDRESS"));
                            row["OBJECT_ADDRESS"] = addr;
                        }

                        if (ColumnExists(reader, "DESCRIPTION") && !reader.IsDBNull(reader.GetOrdinal("DESCRIPTION")))
                            row["DESCRIPTION"] = reader.GetString(reader.GetOrdinal("DESCRIPTION"));

                        // По умолчанию 0 и null-значения
                        row["CALL_COUNT"] = 0;
                        row["LAST_CALL_EMPLOYEE"] = string.Empty;
                        row["LAST_CALL_RESULT"] = string.Empty;
                        row["LAST_CALL_DATETIME"] = DBNull.Value;

                        // Если по адресу есть вызовы — агрегируем
                        if (!string.IsNullOrEmpty(addr) && callsByAddress.TryGetValue(addr, out var calls) && calls.Count > 0)
                        {
                            row["CALL_COUNT"] = calls.Count;

                            // Найдём последний по дате
                            var last = calls.OrderByDescending(x => x.dt).First();
                            row["LAST_CALL_DATETIME"] = last.dt;
                            row["LAST_CALL_EMPLOYEE"] = last.emp;
                            row["LAST_CALL_RESULT"] = last.result;
                        }

                        table.Rows.Add(row);
                    }
                }

                // Привязка к гриду
                dataGridView3.DataSource = table;

                // Настройка отображения: скрыть столбец ID и настроить заголовки
                foreach (DataGridViewColumn col in dataGridView3.Columns)
                {
                    if (table.Columns.Contains(col.Name) && !string.IsNullOrEmpty(table.Columns[col.Name].Caption))
                        col.HeaderText = table.Columns[col.Name].Caption;

                    if (string.Equals(col.Name, "ID", StringComparison.OrdinalIgnoreCase))
                        col.Visible = false;
                }

                // Немного настроим отображение столбцов с датой/числом для удобства
                if (dataGridView3.Columns.Contains("LAST_CALL_DATETIME"))
                    dataGridView3.Columns["LAST_CALL_DATETIME"].DefaultCellStyle.Format = "g"; // краткий формат

                if (dataGridView3.Columns.Contains("CALL_COUNT"))
                {
                    dataGridView3.Columns["CALL_COUNT"].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
                    dataGridView3.Columns["CALL_COUNT"].AutoSizeMode = DataGridViewAutoSizeColumnMode.AllCells;
                }
            }
            catch (FbException fbEx)
            {
                MessageBox.Show(this, $"Ошибка при загрузке охраняемых объектов (БД): {fbEx.Message}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Ошибка при загрузке охраняемых объектов: {ex.Message}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>
        /// Загружает GUARDCALLS и "приклеивает" к каждому:
        /// - итоговую стоимость по заказу (из GET_ORDER_DETAILS / GET_ORDERS)
        /// - дату заказа (из GET_ORDERS)
        /// - названия услуг (агрегация из GET_ORDER_DETAILS)
        /// Всё отображается в одном DataGridView — dataGridView4. ID не показывается.
        /// Грид — только для чтения.
        /// </summary>
        private async Task LoadGuardCallsCombinedAsync()
        {
            try
            {
                dataGridView4.ReadOnly = true;
                dataGridView4.AllowUserToAddRows = false;
                dataGridView4.AllowUserToDeleteRows = false;
                dataGridView4.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
                dataGridView4.MultiSelect = false;
                dataGridView4.AutoGenerateColumns = true;
                dataGridView4.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
                dataGridView4.Visible = true;

                var table = new DataTable();

                // Основные поля вызова
                table.Columns.Add("CALL_ID", typeof(int)); // внутренний, скрываем
                table.Columns.Add("OBJECT_ADDRESS", typeof(string)).Caption = "Адрес объекта";
                table.Columns.Add("EMPLOYEE_FIO", typeof(string)).Caption = "Сотрудник";
                table.Columns.Add("CALL_DATETIME", typeof(DateTime)).Caption = "Дата/время вызова";
                table.Columns.Add("RESULT", typeof(string)).Caption = "Результат";

                // Поля из связанных таблиц/деталей заказа
                table.Columns.Add("ORDER_ID", typeof(int)).Caption = "Номер заказа"; // скрываем
                table.Columns.Add("ORDER_DATE", typeof(DateTime)).Caption = "Дата оказания";
                table.Columns.Add("ORDER_TOTAL", typeof(decimal)).Caption = "Итоговая сумма";
                table.Columns.Add("SERVICE_NAMES", typeof(string)).Caption = "Услуги";

                await using var conn = await FirebirdConnection.CreateOpenConnectionAsync();

                // 1) Получим mapping OBJECT_ADDRESS -> ORDER_ID (через GET_GUARDEDOBJECTS)
                var addrToOrder = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                await using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "GET_GUARDEDOBJECTS";
                    cmd.CommandType = System.Data.CommandType.StoredProcedure;

                    await using var rdr = await cmd.ExecuteReaderAsync();
                    while (await rdr.ReadAsync())
                    {
                        if (ColumnExists(rdr, "OBJECT_ADDRESS") && !rdr.IsDBNull(rdr.GetOrdinal("OBJECT_ADDRESS"))
                            && ColumnExists(rdr, "ORDER_ID") && !rdr.IsDBNull(rdr.GetOrdinal("ORDER_ID")))
                        {
                            var addr = rdr.GetString(rdr.GetOrdinal("OBJECT_ADDRESS"));
                            var orderId = rdr.GetInt32(rdr.GetOrdinal("ORDER_ID"));
                            addrToOrder[addr] = orderId;
                        }
                    }
                }

                // 2) Получим информацию по ORDERS: ORDER_ID -> (ORDER_DATE, TOTAL_REVENUE)
                var orderInfo = new Dictionary<int, (DateTime? orderDate, decimal total)>();
                await using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "GET_ORDERS";
                    cmd.CommandType = System.Data.CommandType.StoredProcedure;

                    await using var rdr = await cmd.ExecuteReaderAsync();
                    while (await rdr.ReadAsync())
                    {
                        if (ColumnExists(rdr, "ID") && !rdr.IsDBNull(rdr.GetOrdinal("ID")))
                        {
                            var id = rdr.GetInt32(rdr.GetOrdinal("ID"));
                            DateTime? orderDate = null;
                            decimal total = 0;

                            if (ColumnExists(rdr, "ORDER_DATE") && !rdr.IsDBNull(rdr.GetOrdinal("ORDER_DATE")))
                                orderDate = rdr.GetDateTime(rdr.GetOrdinal("ORDER_DATE"));

                            if (ColumnExists(rdr, "TOTAL_REVENUE") && !rdr.IsDBNull(rdr.GetOrdinal("TOTAL_REVENUE")))
                                total = rdr.GetDecimal(rdr.GetOrdinal("TOTAL_REVENUE"));

                            orderInfo[id] = (orderDate, total);
                        }
                    }
                }

                // 3) Получим детали заказов (GET_ORDER_DETAILS) и агрегируем названия услуг и итог по заказу
                var servicesByOrder = new Dictionary<int, List<string>>();
                var totalByOrder = new Dictionary<int, decimal>(); // если нужно, можно пересчитать
                await using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "GET_ORDER_DETAILS";
                    cmd.CommandType = System.Data.CommandType.StoredProcedure;

                    await using var rdr = await cmd.ExecuteReaderAsync();
                    while (await rdr.ReadAsync())
                    {
                        if (ColumnExists(rdr, "ORDER_ID") && !rdr.IsDBNull(rdr.GetOrdinal("ORDER_ID")))
                        {
                            var orderId = rdr.GetInt32(rdr.GetOrdinal("ORDER_ID"));

                            string servName = string.Empty;
                            if (ColumnExists(rdr, "SERVICENAME") && !rdr.IsDBNull(rdr.GetOrdinal("SERVICENAME")))
                                servName = rdr.GetString(rdr.GetOrdinal("SERVICENAME"));

                            decimal lineTotal = 0;
                            if (ColumnExists(rdr, "TOTAL_LINE") && !rdr.IsDBNull(rdr.GetOrdinal("TOTAL_LINE")))
                                lineTotal = rdr.GetDecimal(rdr.GetOrdinal("TOTAL_LINE"));
                            else if (ColumnExists(rdr, "SERVICE_AMOUNT") && !rdr.IsDBNull(rdr.GetOrdinal("SERVICE_AMOUNT"))
                                     && ColumnExists(rdr, "QUANTITY") && !rdr.IsDBNull(rdr.GetOrdinal("QUANTITY")))
                                lineTotal = rdr.GetDecimal(rdr.GetOrdinal("SERVICE_AMOUNT")) * rdr.GetInt32(rdr.GetOrdinal("QUANTITY"));

                            if (!servicesByOrder.TryGetValue(orderId, out var list))
                            {
                                list = new List<string>();
                                servicesByOrder[orderId] = list;
                            }

                            if (!string.IsNullOrEmpty(servName) && !list.Contains(servName))
                                list.Add(servName);

                            if (totalByOrder.ContainsKey(orderId))
                                totalByOrder[orderId] += lineTotal;
                            else
                                totalByOrder[orderId] = lineTotal;
                        }
                    }
                }

                // 4) Получаем вызовы (GET_GUARDCALLS) и собираем итоговую строку
                await using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "GET_GUARDCALLS";
                    cmd.CommandType = System.Data.CommandType.StoredProcedure;

                    await using var rdr = await cmd.ExecuteReaderAsync();
                    while (await rdr.ReadAsync())
                    {
                        var row = table.NewRow();

                        if (ColumnExists(rdr, "ID") && !rdr.IsDBNull(rdr.GetOrdinal("ID")))
                            row["CALL_ID"] = rdr.GetInt32(rdr.GetOrdinal("ID"));

                        string? addr = null;
                        if (ColumnExists(rdr, "OBJECT_ADDRESS") && !rdr.IsDBNull(rdr.GetOrdinal("OBJECT_ADDRESS")))
                        {
                            addr = rdr.GetString(rdr.GetOrdinal("OBJECT_ADDRESS"));
                            row["OBJECT_ADDRESS"] = addr;
                        }

                        if (ColumnExists(rdr, "EMPLOYEE_FIO") && !rdr.IsDBNull(rdr.GetOrdinal("EMPLOYEE_FIO")))
                            row["EMPLOYEE_FIO"] = rdr.GetString(rdr.GetOrdinal("EMPLOYEE_FIO"));

                        if (ColumnExists(rdr, "CALL_DATETIME") && !rdr.IsDBNull(rdr.GetOrdinal("CALL_DATETIME")))
                            row["CALL_DATETIME"] = rdr.GetDateTime(rdr.GetOrdinal("CALL_DATETIME"));

                        if (ColumnExists(rdr, "RESULT") && !rdr.IsDBNull(rdr.GetOrdinal("RESULT")))
                            row["RESULT"] = rdr.GetString(rdr.GetOrdinal("RESULT"));

                        // По адресу ищем ORDER_ID -> затем доп.инфу
                        if (!string.IsNullOrEmpty(addr) && addrToOrder.TryGetValue(addr, out var orderId))
                        {
                            row["ORDER_ID"] = orderId;

                            if (orderInfo.TryGetValue(orderId, out var info))
                            {
                                if (info.orderDate.HasValue)
                                    row["ORDER_DATE"] = info.orderDate.Value;
                                row["ORDER_TOTAL"] = info.total;
                            }

                            // Если в GET_ORDER_DETAILS собрали totalByOrder — используем его как более точный итог
                            if (totalByOrder.TryGetValue(orderId, out var recalculated))
                                row["ORDER_TOTAL"] = recalculated;

                            if (servicesByOrder.TryGetValue(orderId, out var servs) && servs.Count > 0)
                            {
                                row["SERVICE_NAMES"] = string.Join(", ", servs);
                            }
                            else
                            {
                                row["SERVICE_NAMES"] = string.Empty;
                            }
                        }
                        else
                        {
                            row["ORDER_ID"] = DBNull.Value;
                            row["ORDER_DATE"] = DBNull.Value;
                            row["ORDER_TOTAL"] = 0m;
                            row["SERVICE_NAMES"] = string.Empty;
                        }

                        table.Rows.Add(row);
                    }
                }

                // Привязка к гриду
                dataGridView4.DataSource = table;

                // Настройка отображения и скрытие ID-полей
                foreach (DataGridViewColumn col in dataGridView4.Columns)
                {
                    if (table.Columns.Contains(col.Name) && !string.IsNullOrEmpty(table.Columns[col.Name].Caption))
                        col.HeaderText = table.Columns[col.Name].Caption;

                    if (string.Equals(col.Name, "CALL_ID", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(col.Name, "ORDER_ID", StringComparison.OrdinalIgnoreCase))
                    {
                        col.Visible = false;
                    }
                }

                // Форматирование колонок
                if (dataGridView4.Columns.Contains("CALL_DATETIME"))
                    dataGridView4.Columns["CALL_DATETIME"].DefaultCellStyle.Format = "g";

                if (dataGridView4.Columns.Contains("ORDER_DATE"))
                    dataGridView4.Columns["ORDER_DATE"].DefaultCellStyle.Format = "d";

                if (dataGridView4.Columns.Contains("ORDER_TOTAL"))
                {
                    dataGridView4.Columns["ORDER_TOTAL"].DefaultCellStyle.Format = "N2";
                    dataGridView4.Columns["ORDER_TOTAL"].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
                    dataGridView4.Columns["ORDER_TOTAL"].AutoSizeMode = DataGridViewAutoSizeColumnMode.AllCells;
                }

                if (dataGridView4.Columns.Contains("SERVICE_NAMES"))
                {
                    dataGridView4.Columns["SERVICE_NAMES"].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
                }
            }
            catch (FbException fbEx)
            {
                MessageBox.Show(this, $"Ошибка при загрузке вызовов (БД): {fbEx.Message}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Ошибка при загрузке вызовов: {ex.Message}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private static bool ColumnExists(FbDataReader reader, string columnName)
        {
            try
            {
                return reader.GetOrdinal(columnName) >= 0;
            }
            catch (IndexOutOfRangeException)
            {
                return false;
            }
        }

        private void pictureBox1_Click(object sender, EventArgs e)
        {

        }

        private void button25_Click(object sender, EventArgs e)
        {

        }

        private void button24_Click(object sender, EventArgs e)
        {

        }

        private void button23_Click(object sender, EventArgs e)
        {

        }

        private void dataGridView1_CellContentClick(object sender, DataGridViewCellEventArgs e)
        {

        }

        private void textBox1_TextChanged(object sender, EventArgs e)
        {

        }

        private async void button1_Click(object sender, EventArgs e)
        {
            // Сохранение в CSV — оставлено без изменений пока
        }

        private async void button2_Click(object sender, EventArgs e)
        {
            // Кнопка "Обновить" для списка сотрудников — перезагрузим данные
            await LoadEmployeesAsync();
        }

        private void button5_Click(object sender, EventArgs e)
        {

        }

        private void checkBox1_CheckedChanged(object sender, EventArgs e)
        {

        }

        // Кнопка "Обновить" на вкладке Объекты (designer: button9) — перезагружает dataGridView3
        private async void button9_Click(object sender, EventArgs e)
        {
            await LoadGuardedObjectsCombinedAsync();
        }

        private void button6_Click(object sender, EventArgs e)
        {
            // Обновить представление "Клиенты" с подсчётами (при необходимости)
            _ = LoadClientsWithCountsAsync();
        }

        private void button7_Click(object sender, EventArgs e)
        {

        }

        private void checkBox2_CheckedChanged(object sender, EventArgs e)
        {

        }

        private void button20_Click(object sender, EventArgs e)
        {

        }

        private void button21_Click(object sender, EventArgs e)
        {

        }

        private void button22_Click(object sender, EventArgs e)
        {

        }

        private void dataGridView2_CellContentClick(object sender, DataGridViewCellEventArgs e)
        {

        }

        private void textBox2_TextChanged(object sender, EventArgs e)
        {

        }

        private void button4_Click(object sender, EventArgs e)
        {

        }

        private void button17_Click(object sender, EventArgs e)
        {

        }

        private void button18_Click(object sender, EventArgs e)
        {

        }

        private void button19_Click(object sender, EventArgs e)
        {

        }

        private void dataGridView3_CellContentClick(object sender, DataGridViewCellEventArgs e)
        {

        }

        private void textBox3_TextChanged(object sender, EventArgs e)
        {

        }

        private void button8_Click(object sender, EventArgs e)
        {

        }

        private void button10_Click(object sender, EventArgs e)
        {

        }

        private void checkBox3_CheckedChanged(object sender, EventArgs e)
        {

        }

        private void pictureBox2_Click(object sender, EventArgs e)
        {

        }

        private void button14_Click(object sender, EventArgs e)
        {

        }

        private void button15_Click(object sender, EventArgs e)
        {

        }

        private void button16_Click(object sender, EventArgs e)
        {

        }

        private void dataGridView4_CellContentClick(object sender, DataGridViewCellEventArgs e)
        {

        }

        private void textBox4_TextChanged(object sender, EventArgs e)
        {

        }

        private void button11_Click(object sender, EventArgs e)
        {

        }

        private void button12_Click(object sender, EventArgs e)
        {

        }

        private void button13_Click(object sender, EventArgs e)
        {

        }

        private void checkBox4_CheckedChanged(object sender, EventArgs e)
        {

        }

        private void dataGridView5_CellContentClick(object sender, DataGridViewCellEventArgs e)
        {

        }

        private void dataGridView6_CellContentClick(object sender, DataGridViewCellEventArgs e)
        {

        }

        private void comboBox1_SelectedIndexChanged(object sender, EventArgs e)
        {

        }

        private void comboBox2_SelectedIndexChanged(object sender, EventArgs e)
        {

        }

        private void dateTimePicker1_ValueChanged(object sender, EventArgs e)
        {

        }

        private void dateTimePicker2_ValueChanged(object sender, EventArgs e)
        {

        }

        private void button3_Click(object sender, EventArgs e)
        {

        }
    }
}

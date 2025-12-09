using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using FirebirdSql.Data.FirebirdClient;
using System.IO;
using System.Drawing;

namespace SecurityAgencysApp
{
    public partial class Form1 : Form
    {
        private readonly Dictionary<string, UserControl> _views = new();
        private UserControl? _currentView;

        // Добавлены поля в класс Form1
        private bool _isEditMode = false;
        private enum EditEntityType { Employee, Client, Position }
        private EditEntityType _editEntity = EditEntityType.Employee;
        private int _editingId = -1;
        // Текущее фото, загруженное через pictureBox1 (или взятое из грида)
        private byte[]? _currentPhoto;

        // Добавлены BindingSource поля в класс Form1 (в секции полей класса)
        private BindingSource _employeesBinding = new();
        private BindingSource _clientsBinding = new();
        private BindingSource _objectsBinding = new();
        private BindingSource _callsBinding = new();
        private BindingSource _servicesBinding = new();

        public Form1()
        {
            InitializeComponent();

            // Подписываемся на Click кнопки сохранения (добавлено программно — чтобы не трогать Designer)
            button26.Click += button26_Click;
            //button23.Click += button23_Click;

            // Подписываемся на кнопку удаления сотрудника
            button37.Click += button37_Click;

            // Загружаем данные при старте формы
            Load += Form1_Load;

            // В конструкторе — после InitializeComponent() и текущих подписок
            dataGridView1.CellClick += dataGridView1_CellClick;
            // Запрет редактирования richTextBox1
            richTextBox1.ReadOnly = true;

            // Подписываемся на DoubleClick для загрузки фото (активируется, когда panel2 и pictureBox1 включены)
            pictureBox1.DoubleClick += pictureBox1_DoubleClick;
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

            // Загружаем типы услуг (в грид — dataGridView7)
            await LoadServiceTypesAsync();

            // Загружаем справочник должностей для формы Добавления сотрудника
            await LoadPositionsAsync();

            // По умолчанию делаем panel8 недоступной для ввода (если нужно)
            if (panel2 != null)
                panel2.Enabled = false;
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

                // Добавляем скрытую колонку ID для корректного редактирования
                table.Columns.Add("ID", typeof(int));

                // Создаём колонки, включая PHOTO для хранения байтов
                table.Columns.Add("SURNAME", typeof(string)).Caption = "Фамилия";
                table.Columns.Add("NAME", typeof(string)).Caption = "Имя";
                table.Columns.Add("SECOND_NAME", typeof(string)).Caption = "Отчество";
                table.Columns.Add("HIRE_DATE", typeof(DateTime)).Caption = "Дата приёма";
                table.Columns.Add("SALARY", typeof(decimal)).Caption = "Оклад";
                table.Columns.Add("EDUCATION", typeof(string)).Caption = "Образование";
                table.Columns.Add("POSITION_NAME", typeof(string)).Caption = "Должность";
                table.Columns.Add("PHOTO", typeof(byte[])); // хранит BLOB, но не показываем в гриде

                // Колонка для быстрого поиска (нижний регистр). Скрытая в гриде.
                table.Columns.Add("SEARCH", typeof(string));

                await using var conn = await FirebirdConnection.CreateOpenConnectionAsync();
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = "GET_EMPLOYEES";
                cmd.CommandType = System.Data.CommandType.StoredProcedure;

                await using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    var row = table.NewRow();

                    string name = string.Empty;
                    string surname = string.Empty;
                    string secondName = string.Empty;
                    string positionName = string.Empty;
                    string education = string.Empty;

                    if (ColumnExists(reader, "ID") && !reader.IsDBNull(reader.GetOrdinal("ID")))
                        row["ID"] = reader.GetInt32(reader.GetOrdinal("ID"));

                    if (ColumnExists(reader, "NAME") && !reader.IsDBNull(reader.GetOrdinal("NAME")))
                    {
                        name = reader.GetString(reader.GetOrdinal("NAME"));
                        row["NAME"] = name;
                    }

                    if (ColumnExists(reader, "SURNAME") && !reader.IsDBNull(reader.GetOrdinal("SURNAME")))
                    {
                        surname = reader.GetString(reader.GetOrdinal("SURNAME"));
                        row["SURNAME"] = surname;
                    }

                    if (ColumnExists(reader, "SECOND_NAME") && !reader.IsDBNull(reader.GetOrdinal("SECOND_NAME")))
                    {
                        secondName = reader.GetString(reader.GetOrdinal("SECOND_NAME"));
                        row["SECOND_NAME"] = secondName;
                    }

                    if (ColumnExists(reader, "HIRE_DATE") && !reader.IsDBNull(reader.GetOrdinal("HIRE_DATE")))
                        row["HIRE_DATE"] = reader.GetDateTime(reader.GetOrdinal("HIRE_DATE"));

                    if (ColumnExists(reader, "SALARY") && !reader.IsDBNull(reader.GetOrdinal("SALARY")))
                        row["SALARY"] = reader.GetDecimal(reader.GetOrdinal("SALARY"));

                    if (ColumnExists(reader, "EDUCATION") && !reader.IsDBNull(reader.GetOrdinal("EDUCATION")))
                    {
                        education = reader.GetString(reader.GetOrdinal("EDUCATION"));
                        row["EDUCATION"] = education;
                    }

                    if (ColumnExists(reader, "POSITION_NAME") && !reader.IsDBNull(reader.GetOrdinal("POSITION_NAME")))
                    {
                        positionName = reader.GetString(reader.GetOrdinal("POSITION_NAME"));
                        row["POSITION_NAME"] = positionName;
                    }

                    // Чтение PHOTO (BLOB) — сохраняем как byte[] в таблицу (не показываем в гриде)
                    if (ColumnExists(reader, "PHOTO") && !reader.IsDBNull(reader.GetOrdinal("PHOTO")))
                    {
                        var ord = reader.GetOrdinal("PHOTO");
                        long blobLength = reader.GetBytes(ord, 0, null, 0, 0);
                        var buf = new byte[blobLength];
                        long bytesRead = 0;
                        int offset = 0;
                        const int chunk = 65536;
                        while (bytesRead < blobLength)
                        {
                            int toRead = (int)Math.Min(chunk, blobLength - bytesRead);
                            var read = (int)reader.GetBytes(ord, bytesRead, buf, offset, toRead);
                            bytesRead += read;
                            offset += read;
                            if (read == 0) break;
                        }
                        row["PHOTO"] = buf;
                    }
                    else
                    {
                        row["PHOTO"] = DBNull.Value;
                    }

                    // Заполним колонку SEARCH (нижний регистр) — используется для фильтрации
                    var searchValue = string.Join(" ", new[] { surname, name, secondName, positionName, education }
                                                    .Where(s => !string.IsNullOrEmpty(s)))
                                      .ToLowerInvariant();
                    row["SEARCH"] = searchValue;

                    table.Rows.Add(row);
                }

                // Привязываем через BindingSource — это позволит удобно фильтровать
                _employeesBinding.DataSource = table;
                dataGridView1.DataSource = _employeesBinding;

                foreach (DataGridViewColumn col in dataGridView1.Columns)
                {
                    var colName = col?.Name;
                    if (!string.IsNullOrEmpty(colName) && table.Columns.Contains(colName) && !string.IsNullOrEmpty(table.Columns[colName].Caption))
                        col.HeaderText = table.Columns[colName].Caption;

                    // Скрываем служебные колонки ID, PHOTO и SEARCH
                    if (!string.IsNullOrEmpty(colName) &&
                        (string.Equals(colName, "ID", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(colName, "PHOTO", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(colName, "SEARCH", StringComparison.OrdinalIgnoreCase)))
                    {
                        col.Visible = false;
                    }
                }

                // Сброс фильтра поиска после загрузки
                _employeesBinding.RemoveFilter();
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
        /// Загружает должности в comboBox3 (GET_POSITIONS)
        /// </summary>
        private async Task LoadPositionsAsync()
        {
            try
            {
                var table = new DataTable();
                await using var conn = await FirebirdConnection.CreateOpenConnectionAsync();
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = "GET_POSITIONS";
                cmd.CommandType = CommandType.StoredProcedure;

                await using var rdr = await cmd.ExecuteReaderAsync();
                table.Load(rdr);

                // Если SP вернула поля ID и NAME — привязываем
                if (table.Columns.Contains("ID") && table.Columns.Contains("NAME"))
                {
                    comboBox3.DisplayMember = "NAME";
                    comboBox3.ValueMember = "ID";
                    comboBox3.DataSource = table;
                }
                else
                {
                    // В случае неожиданного результата — очистим источник
                    comboBox3.DataSource = null;
                }
            }
            catch (FbException fbEx)
            {
                MessageBox.Show(this, $"Ошибка при загрузке должностей (БД): {fbEx.Message}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Ошибка при загрузке должностей: {ex.Message}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
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

                // Колонка для быстрого поиска
                clientsTable.Columns.Add("SEARCH", typeof(string));

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

                        string name = string.Empty, surname = string.Empty, second = string.Empty, address = string.Empty, phone = string.Empty, account = string.Empty;

                        if (ColumnExists(reader, "ID") && !reader.IsDBNull(reader.GetOrdinal("ID")))
                        {
                            var id = reader.GetInt32(reader.GetOrdinal("ID"));
                            row["ID"] = id;
                            clientIds.Add(id);
                        }

                        if (ColumnExists(reader, "NAME") && !reader.IsDBNull(reader.GetOrdinal("NAME")))
                        {
                            name = reader.GetString(reader.GetOrdinal("NAME"));
                            row["NAME"] = name;
                        }

                        if (ColumnExists(reader, "SURNAME") && !reader.IsDBNull(reader.GetOrdinal("SURNAME")))
                        {
                            surname = reader.GetString(reader.GetOrdinal("SURNAME"));
                            row["SURNAME"] = surname;
                        }

                        if (ColumnExists(reader, "SECOND_NAME") && !reader.IsDBNull(reader.GetOrdinal("SECOND_NAME")))
                        {
                            second = reader.GetString(reader.GetOrdinal("SECOND_NAME"));
                            row["SECOND_NAME"] = second;
                        }

                        if (ColumnExists(reader, "ADDRESS") && !reader.IsDBNull(reader.GetOrdinal("ADDRESS")))
                        {
                            address = reader.GetString(reader.GetOrdinal("ADDRESS"));
                            row["ADDRESS"] = address;
                        }

                        if (ColumnExists(reader, "PHONE") && !reader.IsDBNull(reader.GetOrdinal("PHONE")))
                        {
                            phone = reader.GetString(reader.GetOrdinal("PHONE"));
                            row["PHONE"] = phone;
                        }

                        if (ColumnExists(reader, "ACCOUNT_NUMBER") && !reader.IsDBNull(reader.GetOrdinal("ACCOUNT_NUMBER")))
                        {
                            account = reader.GetString(reader.GetOrdinal("ACCOUNT_NUMBER"));
                            row["ACCOUNT_NUMBER"] = account;
                        }

                        if (ColumnExists(reader, "IS_ACTIVE") && !reader.IsDBNull(reader.GetOrdinal("IS_ACTIVE")))
                            row["IS_ACTIVE"] = reader.GetBoolean(reader.GetOrdinal("IS_ACTIVE"));

                        // SEARCH = объединение ключевых полей в нижнем регистре
                        row["SEARCH"] = string.Join(" ", new[] { name, surname, second, address, phone, account }.Where(s => !string.IsNullOrEmpty(s))).ToLowerInvariant();

                        clientsTable.Rows.Add(row);
                    }
                }

                // Остальная логика подсчётов заказов/объектов остаётся прежней (GET_ORDERS, GET_GUARDEDOBJECTS)
                var ordersCountByClient = new Dictionary<int, int>();
                var objectsCountByClient = new Dictionary<int, int>();
                var orderToClient = new Dictionary<int, int>();

                foreach (var id in clientIds)
                {
                    ordersCountByClient[id] = 0;
                    objectsCountByClient[id] = 0;
                }

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

                await using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "GET_GUARDEDOBJECTS";
                    cmd.CommandType = System.Data.CommandType.StoredProcedure;

                    await using var reader = await cmd.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                    {
                        int? orderId = null;

                        if (ColumnExists(reader, "ORDER_ID") && !reader.IsDBNull(reader.GetOrdinal("ORDER_ID"))) ;
                        orderId = reader.GetInt32(reader.GetOrdinal("ORDER_ID"));

                        if (orderId.HasValue && orderToClient.TryGetValue(orderId.Value, out var clientId))
                        {
                            if (objectsCountByClient.ContainsKey(clientId))
                                objectsCountByClient[clientId]++;
                            else
                                objectsCountByClient[clientId] = 1;
                        }
                    }
                }

                clientsTable.Columns.Add("OBJECT_COUNT", typeof(int)).Caption = "Объектов";
                clientsTable.Columns.Add("ORDER_COUNT", typeof(int)).Caption = "Заказов";

                foreach (DataRow row in clientsTable.Rows)
                {
                    if (row["ID"] == DBNull.Value) continue;
                    var clientId = (int)row["ID"];
                    row["OBJECT_COUNT"] = objectsCountByClient.TryGetValue(clientId, out var oCnt) ? oCnt : 0;
                    row["ORDER_COUNT"] = ordersCountByClient.TryGetValue(clientId, out var ordCnt) ? ordCnt : 0;
                }

                // Привязка через BindingSource
                _clientsBinding.DataSource = clientsTable;
                dataGridView2.DataSource = _clientsBinding;

                // Скрываем служебные колонки и назначаем заголовки
                foreach (DataGridViewColumn col in dataGridView2.Columns)
                {
                    var colName = col?.Name;
                    if (!string.IsNullOrEmpty(colName) && clientsTable.Columns.Contains(colName) && !string.IsNullOrEmpty(clientsTable.Columns[colName].Caption))
                        col.HeaderText = clientsTable.Columns[colName].Caption;

                    if (!string.IsNullOrEmpty(colName) && string.Equals(colName, "ID", StringComparison.OrdinalIgnoreCase))
                        col.Visible = false;

                    if (!string.IsNullOrEmpty(colName) &&
                        (string.Equals(colName, "OBJECT_COUNT", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(colName, "ORDER_COUNT", StringComparison.OrdinalIgnoreCase)))
                    {
                        col.FillWeight = 10;
                        col.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
                        col.AutoSizeMode = DataGridViewAutoSizeColumnMode.AllCells;
                    }

                    if (!string.IsNullOrEmpty(colName) && string.Equals(colName, "SEARCH", StringComparison.OrdinalIgnoreCase))
                        col.Visible = false;
                }

                _clientsBinding.RemoveFilter();
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
        /// -aggregированную информацию по GUARDCALLS: количество вызовов и данные последнего вызова
        /// Результат отображается в одном DataGridView — dataGridView3. ID не показывается.
        /// Все поля — только для редактирования.
        /// </summary>
        private async Task LoadGuardedObjectsCombinedAsync()
        {
            try
            {
                dataGridView3.ReadOnly = true;
                dataGridView3.AllowUserToAddRows = false;
                dataGridView3.AllowUserToDeleteRows = false;
                dataGridView3.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
                dataGridView3.MultiSelect = false;
                dataGridView3.AutoGenerateColumns = true;
                dataGridView3.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
                dataGridView3.Visible = true;

                var table = new DataTable();

                table.Columns.Add("ID", typeof(int));
                table.Columns.Add("ORDER_ID", typeof(int)).Caption = "Номер заказа";
                table.Columns.Add("CLIENT_FIO", typeof(string)).Caption = "Клиент";
                table.Columns.Add("OBJECT_ADDRESS", typeof(string)).Caption = "Адрес объекта";
                table.Columns.Add("DESCRIPTION", typeof(string)).Caption = "Описание";
                table.Columns.Add("CALL_COUNT", typeof(int)).Caption = "Вызовов";
                table.Columns.Add("LAST_CALL_DATETIME", typeof(DateTime)).Caption = "Последний вызов";
                table.Columns.Add("LAST_CALL_EMPLOYEE", typeof(string)).Caption = "Сотрудник";
                table.Columns.Add("LAST_CALL_RESULT", typeof(string)).Caption = "Результат";

                // Колонка поиска
                table.Columns.Add("SEARCH", typeof(string));

                var callsByAddress = new Dictionary<string, List<(DateTime dt, string emp, string result)>>(StringComparer.OrdinalIgnoreCase);

                await using var conn = await FirebirdConnection.CreateOpenConnectionAsync();

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

                await using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "GET_GUARDEDOBJECTS";
                    cmd.CommandType = System.Data.CommandType.StoredProcedure;

                    await using var reader = await cmd.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                    {
                        var row = table.NewRow();

                        string? addr = null;
                        string clientFio = string.Empty;
                        string desc = string.Empty;

                        if (ColumnExists(reader, "ID") && !reader.IsDBNull(reader.GetOrdinal("ID")))
                            row["ID"] = reader.GetInt32(reader.GetOrdinal("ID"));

                        if (ColumnExists(reader, "ORDER_ID") && !reader.IsDBNull(reader.GetOrdinal("ORDER_ID")))
                            row["ORDER_ID"] = reader.GetInt32(reader.GetOrdinal("ORDER_ID"));

                        if (ColumnExists(reader, "CLIENT_FIO") && !reader.IsDBNull(reader.GetOrdinal("CLIENT_FIO")))
                        {
                            clientFio = reader.GetString(reader.GetOrdinal("CLIENT_FIO"));
                            row["CLIENT_FIO"] = clientFio;
                        }

                        if (ColumnExists(reader, "OBJECT_ADDRESS") && !reader.IsDBNull(reader.GetOrdinal("OBJECT_ADDRESS")))
                        {
                            addr = reader.GetString(reader.GetOrdinal("OBJECT_ADDRESS"));
                            row["OBJECT_ADDRESS"] = addr;
                        }

                        if (ColumnExists(reader, "DESCRIPTION") && !reader.IsDBNull(reader.GetOrdinal("DESCRIPTION")))
                        {
                            desc = reader.GetString(reader.GetOrdinal("DESCRIPTION"));
                            row["DESCRIPTION"] = desc;
                        }

                        row["CALL_COUNT"] = 0;
                        row["LAST_CALL_EMPLOYEE"] = string.Empty;
                        row["LAST_CALL_RESULT"] = string.Empty;
                        row["LAST_CALL_DATETIME"] = DBNull.Value;

                        if (!string.IsNullOrEmpty(addr) && callsByAddress.TryGetValue(addr, out var calls) && calls.Count > 0)
                        {
                            row["CALL_COUNT"] = calls.Count;
                            var last = calls.OrderByDescending(x => x.dt).First();
                            row["LAST_CALL_DATETIME"] = last.dt;
                            row["LAST_CALL_EMPLOYEE"] = last.emp;
                            row["LAST_CALL_RESULT"] = last.result;
                        }

                        // SEARCH: клиент, адрес, описание, последнее рез-ть и сотрудник
                        row["SEARCH"] = string.Join(" ", new[] {
                    clientFio, addr ?? string.Empty, desc,
                    row["LAST_CALL_EMPLOYEE"]?.ToString() ?? string.Empty,
                    row["LAST_CALL_RESULT"]?.ToString() ?? string.Empty
                }.Where(s => !string.IsNullOrEmpty(s))).ToLowerInvariant();

                        table.Rows.Add(row);
                    }
                }

                // Привязка через BindingSource
                _objectsBinding.DataSource = table;
                dataGridView3.DataSource = _objectsBinding;

                foreach (DataGridViewColumn col in dataGridView3.Columns)
                {
                    var colName = col?.Name;
                    if (!string.IsNullOrEmpty(colName) && table.Columns.Contains(colName) && !string.IsNullOrEmpty(table.Columns[colName].Caption))
                        col.HeaderText = table.Columns[colName].Caption;

                    if (!string.IsNullOrEmpty(colName) && string.Equals(colName, "ID", StringComparison.OrdinalIgnoreCase))
                        col.Visible = false;

                    if (!string.IsNullOrEmpty(colName) && string.Equals(colName, "SEARCH", StringComparison.OrdinalIgnoreCase))
                        col.Visible = false;
                }

                if (dataGridView3.Columns.Contains("LAST_CALL_DATETIME"))
                    dataGridView3.Columns["LAST_CALL_DATETIME"].DefaultCellStyle.Format = "g";

                if (dataGridView3.Columns.Contains("CALL_COUNT"))
                {
                    dataGridView3.Columns["CALL_COUNT"].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
                    dataGridView3.Columns["CALL_COUNT"].AutoSizeMode = DataGridViewAutoSizeColumnMode.AllCells;
                }

                _objectsBinding.RemoveFilter();
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

                table.Columns.Add("CALL_ID", typeof(int));
                table.Columns.Add("OBJECT_ADDRESS", typeof(string)).Caption = "Адрес объекта";
                table.Columns.Add("EMPLOYEE_FIO", typeof(string)).Caption = "Сотрудник";
                table.Columns.Add("CALL_DATETIME", typeof(DateTime)).Caption = "Дата/время вызова";
                table.Columns.Add("RESULT", typeof(string)).Caption = "Результат";
                table.Columns.Add("ORDER_ID", typeof(int)).Caption = "Номер заказа";
                table.Columns.Add("ORDER_DATE", typeof(DateTime)).Caption = "Дата оказания";
                table.Columns.Add("ORDER_TOTAL", typeof(decimal)).Caption = "Итоговая сумма";
                table.Columns.Add("SERVICE_NAMES", typeof(string)).Caption = "Услуги";

                // Поисковая колонка
                table.Columns.Add("SEARCH", typeof(string));

                await using var conn = await FirebirdConnection.CreateOpenConnectionAsync();

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
                ;

                var servicesByOrder = new Dictionary<int, List<string>>();
                var totalByOrder = new Dictionary<int, decimal>();
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
                ;

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

                        if (!string.IsNullOrEmpty(addr) && addrToOrder.TryGetValue(addr, out var orderId))
                        {
                            row["ORDER_ID"] = orderId;

                            if (orderInfo.TryGetValue(orderId, out var info))
                            {
                                if (info.orderDate.HasValue)
                                    row["ORDER_DATE"] = info.orderDate.Value;
                                row["ORDER_TOTAL"] = info.total;
                            }

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

                        // SEARCH: адрес, сотрудник, результат, услуги
                        row["SEARCH"] = string.Join(" ", new[] {
                    addr ?? string.Empty,
                    row["EMPLOYEE_FIO"]?.ToString() ?? string.Empty,
                    row["RESULT"]?.ToString() ?? string.Empty,
                    row["SERVICE_NAMES"]?.ToString() ?? string.Empty
                }.Where(s => !string.IsNullOrEmpty(s))).ToLowerInvariant();

                        table.Rows.Add(row);
                    }
                }

                _callsBinding.DataSource = table;
                dataGridView4.DataSource = _callsBinding;

                foreach (DataGridViewColumn col in dataGridView4.Columns)
                {
                    if (table.Columns.Contains(col.Name) && !string.IsNullOrEmpty(table.Columns[col.Name].Caption))
                        col.HeaderText = table.Columns[col.Name].Caption;

                    if (string.Equals(col.Name, "CALL_ID", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(col.Name, "ORDER_ID", StringComparison.OrdinalIgnoreCase))
                    {
                        col.Visible = false;
                    }

                    if (string.Equals(col.Name, "SEARCH", StringComparison.OrdinalIgnoreCase))
                        col.Visible = false;
                }

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

        /// <summary>
        /// Загружает типы услуг в dataGridView7
        /// </summary>
        private async Task LoadServiceTypesAsync()
        {
            try
            {
                dataGridView7.ReadOnly = true;
                dataGridView7.AllowUserToAddRows = false;
                dataGridView7.AllowUserToDeleteRows = false;
                dataGridView7.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
                dataGridView7.MultiSelect = false;
                dataGridView7.AutoGenerateColumns = true;
                dataGridView7.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
                dataGridView7.Visible = true;

                var table = new DataTable();

                await using var conn = await FirebirdConnection.CreateOpenConnectionAsync();
                await using var cmd = conn.CreateCommand();

                try
                {
                    cmd.CommandText = "GET_SERVICETYPES";
                    cmd.CommandType = System.Data.CommandType.StoredProcedure;

                    await using var rdrSp = await cmd.ExecuteReaderAsync();
                    table.Load(rdrSp);
                }
                catch (FbException)
                {
                    cmd.Parameters.Clear();
                    cmd.CommandText = "SELECT * FROM \"SERVICETYPES\"";
                    cmd.CommandType = System.Data.CommandType.Text;

                    await using var rdr = await cmd.ExecuteReaderAsync();
                    table.Load(rdr);
                }

                // Добавим колонку SEARCH (если ее нет)
                if (!table.Columns.Contains("SEARCH"))
                    table.Columns.Add("SEARCH", typeof(string));

                // Заполним SEARCH для каждой строки
                foreach (DataRow r in table.Rows)
                {
                    var parts = new List<string>();
                    if (table.Columns.Contains("SERVICENAME") && r["SERVICENAME"] != DBNull.Value)
                        parts.Add(r["SERVICENAME"].ToString()!);
                    if (table.Columns.Contains("UNIT_COST") && r["UNIT_COST"] != DBNull.Value)
                        parts.Add(r["UNIT_COST"].ToString()!);
                    r["SEARCH"] = string.Join(" ", parts).ToLowerInvariant();
                }

                if (table.Columns.Contains("ID"))
                    table.Columns.Remove("ID");

                _servicesBinding.DataSource = table;
                dataGridView7.DataSource = _servicesBinding;

                var hdr = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["SERVICENAME"] = "Название",
                    ["UNIT_COST"] = "Стоимость",
                };

                foreach (DataGridViewColumn col in dataGridView7.Columns)
                {
                    var colName = col?.Name;
                    if (string.IsNullOrEmpty(colName)) continue;

                    if (hdr.TryGetValue(colName, out var caption))
                    {
                        col.HeaderText = caption;
                    }
                    else
                    {
                        var n = colName.Replace('_', ' ').Trim();
                        if (!string.IsNullOrEmpty(n))
                            col.HeaderText = char.ToUpperInvariant(n[0]) + (n.Length > 1 ? n.Substring(1).ToLowerInvariant() : string.Empty);
                    }

                    if (string.Equals(colName, "SEARCH", StringComparison.OrdinalIgnoreCase))
                        col.Visible = false;
                }

                _servicesBinding.RemoveFilter();
            }
            catch (FbException fbEx)
            {
                MessageBox.Show(this, $"Ошибка БД при загрузке типов услуг: {fbEx.Message}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Ошибка при загрузке типов услуг: {ex.Message}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
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
            // Сделать элементы panel6 доступными для ввода и очистить их
            if (panel2 == null) return;

            panel2.Enabled = true;

            // Очистим поля внутри panel6
            foreach (Control c in panel2.Controls)
            {
                switch (c)
                {
                    case TextBox tb:
                        tb.Clear();
                        break;
                    case RichTextBox rtb:
                        rtb.Clear();
                        break;
                    case ComboBox cb:
                        cb.SelectedIndex = -1;
                        break;
                    case NumericUpDown nud:
                        nud.Value = nud.Minimum;
                        break;
                }
            }

            // Очистим текущее фото — пользователь может загрузить новое
            _currentPhoto = null;
            if (pictureBox1 != null)
            {
                if (pictureBox1.Image != null)
                {
                    var old = pictureBox1.Image;
                    pictureBox1.Image = null;
                    old.Dispose();
                }
                pictureBox1.Enabled = true;
                pictureBox1.Cursor = Cursors.Hand;
            }

            // Переместим фокус на первый элемент
            if (panel2.Controls.Count > 0)
                panel2.Controls[0].Focus();

            // Режим редактирования — добавление
            _isEditMode = false;
        }

        private void button24_Click(object sender, EventArgs e)
        {
            // Решаем, какая сетка содержит выбранную строку: clients (dataGridView2) имеет приоритет над employees (dataGridView1)
            DataGridViewRow? row = null;
            if (dataGridView1 != null && dataGridView1.CurrentRow != null)
            {
                _editEntity = EditEntityType.Employee;
                row = dataGridView1.CurrentRow;
            }
            //else if (dataGridView1 != null && dataGridView1.CurrentRow != null)
            //{
            //    _editEntity = EditEntityType.Employee;
            //    row = dataGridView1.CurrentRow;
            //}
            else
            {
                MessageBox.Show(this, "Выберите строку в списке (Клиенты или Сотрудники).", "Информация", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (row == null)
            {
                MessageBox.Show(this, "Не удалось получить выбранную строку.", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // Сохранить ID (если есть колонка ID)
            _editingId = -1;
            if (row.DataGridView.Columns.Contains("ID"))
            {
                var idStr = GetRowCellString(row, "ID");
                if (!int.TryParse(idStr, out _editingId))
                    _editingId = -1;
            }

            // Заполнение полей panel2 в зависимости от редактируемой сущности
            if (_editEntity == EditEntityType.Employee)
            {
                textBox5.Text = GetRowCellString(row, "NAME");
                textBox6.Text = GetRowCellString(row, "SURNAME");
                textBox7.Text = GetRowCellString(row, "SECOND_NAME");

                if (row.DataGridView.Columns.Contains("HIRE_DATE"))
                {
                    var s = GetRowCellString(row, "HIRE_DATE");
                    if (DateTime.TryParse(s, out var dt))
                        dateTimePicker3.Value = dt;
                }

                if (row.DataGridView.Columns.Contains("SALARY"))
                {
                    var s = GetRowCellString(row, "SALARY");
                    if (decimal.TryParse(s, out var sal))
                    {
                        sal = Math.Max(numericUpDown1.Minimum, Math.Min(numericUpDown1.Maximum, sal));
                        numericUpDown1.Value = sal;
                    }
                }

                textBox8.Text = GetRowCellString(row, "EDUCATION");

                var posName = GetRowCellString(row, "POSITION_NAME");
                if (!string.IsNullOrEmpty(posName) && comboBox3?.DataSource != null)
                {
                    for (int i = 0; i < comboBox3.Items.Count; i++)
                    {
                        if (comboBox3.Items[i] is DataRowView drv && drv.Row.Table.Columns.Contains("NAME"))
                        {
                            if (string.Equals(drv["NAME"].ToString(), posName, StringComparison.OrdinalIgnoreCase))
                            {
                                comboBox3.SelectedIndex = i;
                                break;
                            }
                        }
                    }
                }
                else
                {
                    if (comboBox3 != null)
                        comboBox3.SelectedIndex = -1;
                }

                // Подхватим фото из выбранной строки (если есть) в _currentPhoto и покажем в pictureBox1
                var bytes = GetRowCellBytes(row, "PHOTO");
                _currentPhoto = bytes;
                if (bytes != null && bytes.Length > 0)
                {
                    try
                    {
                        if (pictureBox1.Image != null)
                        {
                            var old = pictureBox1.Image;
                            pictureBox1.Image = null;
                            old.Dispose();
                        }
                        using var ms = new MemoryStream(bytes);
                        var img = Image.FromStream(ms);
                        pictureBox1.Image = new Bitmap(img);
                        pictureBox1.SizeMode = PictureBoxSizeMode.Zoom;
                    }
                    catch
                    {
                        // игнорируем ошибку отображения
                    }
                }
            }
            else if (_editEntity == EditEntityType.Client)
            {
                // В таблице clients есть колонки NAME, SURNAME, SECOND_NAME, ADDRESS, PHONE, ACCOUNT_NUMBER
                textBox5.Text = GetRowCellString(row, "NAME"); // используем те же поля panel2 для заполнения
                textBox6.Text = GetRowCellString(row, "SURNAME");
                textBox7.Text = GetRowCellString(row, "SECOND_NAME");
                textBox8.Text = GetRowCellString(row, "ADDRESS");

                // Если в panel2 есть поле для телефона/счёта — пытаемся заполнить по возможным именам контролов
                SetControlTextSafe(panel2, new[] { "textBoxPhone", "maskedTextBoxPhone", "textBox9" }, GetRowCellString(row, "PHONE"));
                SetControlTextSafe(panel2, new[] { "textBoxAccount", "textBox10" }, GetRowCellString(row, "ACCOUNT_NUMBER"));

                // Для клиентов фото не используется — сбрасываем текущее фото
                _currentPhoto = null;
                if (pictureBox1 != null)
                {
                    if (pictureBox1.Image != null)
                    {
                        var old = pictureBox1.Image;
                        pictureBox1.Image = null;
                        old.Dispose();
                    }
                }
            }
            else if (_editEntity == EditEntityType.Position)
            {
                // Если будете поддерживать редактирование позиций — сюда поместите заполнение
                SetControlTextSafe(panel2, new[] { "textBox5", "textBoxPositionName" }, GetRowCellString(row, "NAME"));
                _currentPhoto = null;
            }

            // Включаем редактирование и возможность двойного клика по pictureBox1 для загрузки фото
            if (panel2 != null)
                panel2.Enabled = true;

            if (pictureBox1 != null)
            {
                pictureBox1.Enabled = (_editEntity == EditEntityType.Employee);
                pictureBox1.Cursor = pictureBox1.Enabled ? Cursors.Hand : Cursors.Default;
            }

            _isEditMode = true;

            if (panel2?.Controls.Count > 0)
                panel2.Controls[0].Focus();
        }

        private void dataGridView1_CellContentClick(object sender, DataGridViewCellEventArgs e)
        {

        }

        private void textBox1_TextChanged(object sender, EventArgs e)
        {
            try
            {
                if (_employeesBinding == null || _employeesBinding.DataSource == null) return;

                var q = textBox1.Text.Trim().ToLowerInvariant();
                if (string.IsNullOrEmpty(q))
                {
                    _employeesBinding.RemoveFilter();
                    return;
                }
                q = q.Replace("'", "''");
                _employeesBinding.Filter = $"SEARCH LIKE '%{q}%'";
            }
            catch
            {
                _employeesBinding.RemoveFilter();
            }
        }

        private void textBox2_TextChanged(object sender, EventArgs e)
        {
            try
            {
                if (_clientsBinding == null || _clientsBinding.DataSource == null) return;

                var q = textBox2.Text.Trim().ToLowerInvariant();
                if (string.IsNullOrEmpty(q))
                {
                    _clientsBinding.RemoveFilter();
                    return;
                }
                q = q.Replace("'", "''");
                _clientsBinding.Filter = $"SEARCH LIKE '%{q}%'";
            }
            catch
            {
                _clientsBinding.RemoveFilter();
            }
        }

        private void textBox3_TextChanged(object sender, EventArgs e)
        {
            try
            {
                if (_objectsBinding == null || _objectsBinding.DataSource == null) return;

                var q = textBox3.Text.Trim().ToLowerInvariant();
                if (string.IsNullOrEmpty(q))
                {
                    _objectsBinding.RemoveFilter();
                    return;
                }
                q = q.Replace("'", "''");
                _objectsBinding.Filter = $"SEARCH LIKE '%{q}%'";
            }
            catch
            {
                _objectsBinding.RemoveFilter();
            }
        }

        private void textBox4_TextChanged(object sender, EventArgs e)
        {
            try
            {
                if (_callsBinding == null || _callsBinding.DataSource == null) return;

                var q = textBox4.Text.Trim().ToLowerInvariant();
                if (string.IsNullOrEmpty(q))
                {
                    _callsBinding.RemoveFilter();
                    return;
                }
                q = q.Replace("'", "''");
                _callsBinding.Filter = $"SEARCH LIKE '%{q}%'";
            }
            catch
            {
                _callsBinding.RemoveFilter();
            }
        }

        private void textBox12_TextChanged(object sender, EventArgs e)
        {
            try
            {
                if (_servicesBinding == null || _servicesBinding.DataSource == null) return;

                var q = textBox12.Text.Trim().ToLowerInvariant();
                if (string.IsNullOrEmpty(q))
                {
                    _servicesBinding.RemoveFilter();
                    return;
                }
                q = q.Replace("'", "''");
                _servicesBinding.Filter = $"SEARCH LIKE '%{q}%'";
            }
            catch
            {
                _servicesBinding.RemoveFilter();
            }
        }

        private async void button1_Click(object sender, EventArgs e)
        {
            try
            {
                if (dataGridView1.DataSource == null || dataGridView1.Rows.Count == 0)
                {
                    MessageBox.Show(this, "Нет данных для экспорта.", "Информация", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                using var sfd = new SaveFileDialog
                {
                    Title = "Сохранить как CSV",
                    Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
                    FileName = "employees.csv",
                    DefaultExt = "csv"
                };

                if (sfd.ShowDialog(this) != DialogResult.OK) return;

                var delimiter = ';'; // разделитель столбцов для CSV (подходит для Excel в RU-локали)

                // Собираем видимые колонки (по DisplayIndex)
                var cols = dataGridView1.Columns
                    .Cast<DataGridViewColumn>()
                    .Where(c => c.Visible && !string.Equals(c.Name, "PHOTO", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(c => c.DisplayIndex)
                    .ToArray();

                // Функция экранирования CSV-поля
                static string EscapeCsv(string? s, char delimiter)
                {
                    if (string.IsNullOrEmpty(s)) return string.Empty;
                    var needsQuotes = s.Contains(delimiter) || s.Contains('"') || s.Contains('\r') || s.Contains('\n');
                    var escaped = s.Replace("\"", "\"\"");
                    return needsQuotes ? $"\"{escaped}\"" : escaped;
                }

                // Запишем файл в UTF-8 с BOM для корректного открытия в Excel
                await using var fs = File.Open(sfd.FileName, FileMode.Create, FileAccess.Write, FileShare.None);
                await using var sw = new StreamWriter(fs, new System.Text.UTF8Encoding(true));

                // Заголовок
                var header = string.Join(delimiter, cols.Select(c => EscapeCsv(c.HeaderText ?? c.Name, delimiter)));
                await sw.WriteLineAsync(header);

                // Строки
                foreach (DataGridViewRow row in dataGridView1.Rows)
                {
                    if (row.IsNewRow) continue;

                    var parts = new List<string>(cols.Length);
                    foreach (var col in cols)
                    {
                        var cell = row.Cells[col.Index];
                        object? val = cell?.Value;

                        if (val == null || val == DBNull.Value)
                        {
                            parts.Add(string.Empty);
                            continue;
                        }

                        // Не пытаться сериализовать BLOB/byte[]: оставить пустым или можно поставить псевдо-метку
                        if (val is byte[])
                        {
                            parts.Add(string.Empty);
                            continue;
                        }

                        // Для дат и чисел используем инвариантный формат — при необходимости можно менять на CurrentCulture
                        string text;
                        if (val is DateTime dt)
                            text = dt.ToString("o"); // ISO-формат
                        else if (val is decimal dec)
                            text = dec.ToString(System.Globalization.CultureInfo.InvariantCulture);
                        else if (val is double d)
                            text = d.ToString(System.Globalization.CultureInfo.InvariantCulture);
                        else
                            text = val.ToString() ?? string.Empty;

                        parts.Add(EscapeCsv(text, delimiter));
                    }

                    var line = string.Join(delimiter, parts);
                    await sw.WriteLineAsync(line);
                }

                await sw.FlushAsync();

                MessageBox.Show(this, $"Таблица успешно сохранена в {sfd.FileName}", "Готово", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Ошибка при экспорте в CSV: {ex.Message}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
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

        //private void textBox2_TextChanged(object sender, EventArgs e)
        //{

        //}

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

        //private void textBox3_TextChanged(object sender, EventArgs e)
        //{

        //}

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

        //private void textBox4_TextChanged(object sender, EventArgs e)
        //{

        //}

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

        private void splitContainer5_Panel1_Paint(object sender, PaintEventArgs e)
        {

        }

        /// <summary>
        /// Обработчик кнопки Сохранить (button26). Подтверждение -> вызов ADD_EMPLOYEE -> обновление грида.
        /// </summary>
        private async void button26_Click(object? sender, EventArgs e)
        {
            try
            {
                // Сбор данных из полей (panel2) — общие для добавления/обновления
                var name = textBox5.Text.Trim();
                var surname = textBox6.Text.Trim();
                var secondName = textBox7.Text.Trim();
                var hireDate = dateTimePicker3.Value.Date;
                var salary = numericUpDown1.Value;
                var education = textBox8.Text.Trim();

                int positionId = -1;
                if (comboBox3?.SelectedValue != null)
                {
                    if (!int.TryParse(comboBox3.SelectedValue.ToString(), out positionId))
                        positionId = -1;
                }

                if (_isEditMode)
                {
                    var dr = MessageBox.Show(this, "Вы уверены что хотите изменить информацию?", "Подтвердите действие", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                    if (dr != DialogResult.Yes) return;

                    if (_editingId <= 0)
                    {
                        MessageBox.Show(this, "Не удалось определить ID записи для изменения.", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }

                    try
                    {
                        await using var conn = await FirebirdConnection.CreateOpenConnectionAsync();
                        await using var cmd = conn.CreateCommand();

                        if (_editEntity == EditEntityType.Client)
                        {
                            cmd.CommandText = "UPDATE_CLIENT";
                            cmd.CommandType = CommandType.StoredProcedure;

                            // Параметры процедуры UPDATE_CLIENT: ID, NAME, SURNAME, SECOND_NAME, ADDRESS, PHONE, ACCOUNT_NUMBER
                            cmd.Parameters.AddWithValue("ID", _editingId);
                            cmd.Parameters.AddWithValue("NAME", textBox5.Text.Trim());
                            cmd.Parameters.AddWithValue("SURNAME", textBox6.Text.Trim());
                            cmd.Parameters.AddWithValue("SECOND_NAME", textBox7.Text.Trim());
                            cmd.Parameters.AddWithValue("ADDRESS", GetControlTextSafe(panel2, new[] { "textBoxAddress", "textBox8" }) ?? textBox8.Text.Trim());
                            var phone = GetControlTextSafe(panel2, new[] { "textBoxPhone", "maskedTextBoxPhone", "textBox9" });
                            cmd.Parameters.AddWithValue("PHONE", string.IsNullOrEmpty(phone) ? DBNull.Value : (object)phone);
                            var account = GetControlTextSafe(panel2, new[] { "textBoxAccount", "textBox10" });
                            cmd.Parameters.AddWithValue("ACCOUNT_NUMBER", string.IsNullOrEmpty(account) ? DBNull.Value : (object)account);

                            await cmd.ExecuteNonQueryAsync();

                            MessageBox.Show(this, "Данные клиента успешно обновлены.", "Успех", MessageBoxButtons.OK, MessageBoxIcon.Information);

                            _isEditMode = false;
                            _editingId = -1;
                            if (panel2 != null) panel2.Enabled = false;

                            await LoadClientsWithCountsAsync();
                            return;
                        }
                        else if (_editEntity == EditEntityType.Position)
                        {
                            cmd.CommandText = "UPDATE_POSITION";
                            cmd.CommandType = CommandType.StoredProcedure;

                            // Параметры UPDATE_POSITION: ID, NAME
                            cmd.Parameters.AddWithValue("ID", _editingId);

                            var posName = GetControlTextSafe(panel2, new[] { "textBoxPositionName", "textBox5" }).Trim();
                            cmd.Parameters.AddWithValue("NAME", posName);

                            await cmd.ExecuteNonQueryAsync();

                            MessageBox.Show(this, "Должность успешно обновлена.", "Успех", MessageBoxButtons.OK, MessageBoxIcon.Information);

                            _isEditMode = false;
                            _editingId = -1;
                            if (panel2 != null) panel2.Enabled = false;

                            await LoadPositionsAsync();
                            return;
                        }
                        else // Employee (по умолчанию)
                        {
                            cmd.CommandText = "UPDATE_EMPLOYEE";
                            cmd.CommandType = CommandType.StoredProcedure;

                            cmd.Parameters.AddWithValue("ID", _editingId);
                            cmd.Parameters.AddWithValue("NAME", textBox5.Text.Trim());
                            cmd.Parameters.AddWithValue("SURNAME", textBox6.Text.Trim());
                            cmd.Parameters.AddWithValue("SECOND_NAME", textBox7.Text.Trim());
                            cmd.Parameters.AddWithValue("HIRE_DATE", hireDate);
                            cmd.Parameters.AddWithValue("SALARY", salary);
                            cmd.Parameters.AddWithValue("EDUCATION", education);
                            cmd.Parameters.AddWithValue("POSITION_ID", positionId > 0 ? (object)positionId : DBNull.Value);
                            // Передаём байты или DBNull
                            cmd.Parameters.AddWithValue("PHOTO", _currentPhoto != null ? (object)_currentPhoto : DBNull.Value);

                            await cmd.ExecuteNonQueryAsync();

                            MessageBox.Show(this, "Данные сотрудника успешно обновлены.", "Успех", MessageBoxButtons.OK, MessageBoxIcon.Information);

                            _isEditMode = false;
                            _editingId = -1;
                            _currentPhoto = null;
                            if (panel2 != null) panel2.Enabled = false;
                            if (pictureBox1 != null)
                            {
                                pictureBox1.Enabled = false;
                                pictureBox1.Cursor = Cursors.Default;
                            }

                            await LoadEmployeesAsync();
                            return;
                        }
                    }
                    catch (FbException fbEx)
                    {
                        MessageBox.Show(this, $"Ошибка БД при обновлении записи: {fbEx.Message}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }
                }

                // Ниже — существующая логика добавления (ADD_EMPLOYEE)
                var addConfirm = MessageBox.Show(this, "Вы уверены что хотите добавить запись?", "Подтвердите действие", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (addConfirm != DialogResult.Yes) return;

                // Валидация при добавлении
                if (string.IsNullOrEmpty(name))
                {
                    MessageBox.Show(this, "Введите имя сотрудника.", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                if (string.IsNullOrEmpty(surname))
                {
                    MessageBox.Show(this, "Введите фамилию сотрудника.", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                if (positionId <= 0)
                {
                    MessageBox.Show(this, "Выберите должность.", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                // Вызов ADD_EMPLOYEE
                try
                {
                    await using var conn2 = await FirebirdConnection.CreateOpenConnectionAsync();
                    await using var cmd2 = conn2.CreateCommand();
                    cmd2.CommandText = "ADD_EMPLOYEE";
                    cmd2.CommandType = CommandType.StoredProcedure;

                    cmd2.Parameters.AddWithValue("NAME", name);
                    cmd2.Parameters.AddWithValue("SURNAME", surname);
                    cmd2.Parameters.AddWithValue("SECOND_NAME", secondName);
                    cmd2.Parameters.AddWithValue("HIRE_DATE", hireDate);
                    cmd2.Parameters.AddWithValue("SALARY", salary);
                    cmd2.Parameters.AddWithValue("EDUCATION", education);
                    cmd2.Parameters.AddWithValue("POSITION_ID", positionId);
                    // Передаём фото (байты) или DBNull
                    cmd2.Parameters.AddWithValue("PHOTO", _currentPhoto != null ? (object)_currentPhoto : DBNull.Value);

                    int newId = -1;
                    await using var reader = await cmd2.ExecuteReaderAsync();
                    if (await reader.ReadAsync())
                    {
                        if (ColumnExists(reader, "ID") && !reader.IsDBNull(reader.GetOrdinal("ID")))
                            newId = reader.GetInt32(reader.GetOrdinal("ID"));
                    }

                    if (newId > 0)
                    {
                        MessageBox.Show(this, $"Сотрудник успешно добавлен (ID: {newId}).", "Успех", MessageBoxButtons.OK, MessageBoxIcon.Information);

                        // Очистим поля ввода
                        textBox5.Clear();
                        textBox6.Clear();
                        textBox7.Clear();
                        numericUpDown1.Value = numericUpDown1.Minimum;
                        textBox8.Clear();
                        if (comboBox3 != null) comboBox3.SelectedIndex = -1;
                        dateTimePicker3.Value = DateTime.Today;

                        _currentPhoto = null;
                        if (pictureBox1 != null)
                        {
                            if (pictureBox1.Image != null)
                            {
                                var old = pictureBox1.Image;
                                pictureBox1.Image = null;
                                old.Dispose();
                            }
                            pictureBox1.Enabled = false;
                            pictureBox1.Cursor = Cursors.Default;
                        }

                        if (panel2 != null)
                            panel2.Enabled = false;

                        await LoadEmployeesAsync();
                    }
                    else
                    {
                        MessageBox.Show(this, "Сотрудник добавлен, но не удалось получить ID.", "Внимание", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        await LoadEmployeesAsync();
                    }
                }
                catch (FbException fbEx2)
                {
                    MessageBox.Show(this, $"Ошибка БД при добавлении сотрудника: {fbEx2.Message}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, $"Ошибка при добавлении сотрудника: {ex.Message}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Ошибка: {ex.Message}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void dataGridView1_CellClick(object? sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0) return;

            var row = dataGridView1.Rows[e.RowIndex];

            string surname = GetRowCellString(row, "SURNAME");
            string name = GetRowCellString(row, "NAME");
            string secondName = GetRowCellString(row, "SECOND_NAME");

            var fio = string.Join(" ", new[] { surname, name, secondName }.Where(s => !string.IsNullOrEmpty(s)));
            richTextBox1.Text = fio;

            // Показываем фото (если есть) и сохраняем в _currentPhoto
            var bytes = GetRowCellBytes(row, "PHOTO");
            _currentPhoto = bytes;
            if (bytes != null && bytes.Length > 0)
            {
                try
                {
                    // Освободим предыдущее изображение чтобы избежать утечек
                    if (pictureBox1.Image != null)
                    {
                        var old = pictureBox1.Image;
                        pictureBox1.Image = null;
                        old.Dispose();
                    }

                    using var ms = new MemoryStream(bytes);
                    var img = Image.FromStream(ms);
                    pictureBox1.Image = new Bitmap(img); // копия в памяти
                    pictureBox1.SizeMode = PictureBoxSizeMode.Zoom;
                }
                catch
                {
                    pictureBox1.Image = null; // при ошибке просто очистим
                }
            }
            else
            {
                // Нет фото — очистим
                if (pictureBox1.Image != null)
                {
                    var old = pictureBox1.Image;
                    pictureBox1.Image = null;
                    old.Dispose();
                }
            }
        }

        /// <summary>
        /// Получает байтовый массив из указанной колонки DataGridViewRow (если есть).
        /// </summary>
        private static byte[]? GetRowCellBytes(DataGridViewRow row, string columnName)
        {
            var dgv = row.DataGridView;
            if (dgv == null) return null;
            if (!dgv.Columns.Contains(columnName)) return null;

            var idx = dgv.Columns[columnName].Index;
            var val = row.Cells[idx].Value;
            return val as byte[];
        }

        /// <summary>
        /// Безопасно получает строковое значение из строки по имени колонки (если колонка есть).
        /// </summary>
        private static string GetRowCellString(DataGridViewRow row, string columnName)
        {
            var dgv = row.DataGridView;
            if (dgv == null) return string.Empty;
            if (!dgv.Columns.Contains(columnName)) return string.Empty;

            var idx = dgv.Columns[columnName].Index;
            var val = row.Cells[idx].Value;
            return val?.ToString() ?? string.Empty;
        }

        // Вспомогательные методы — добавить в класс Form1
        private static Control? FindControlRecursive(Control parent, string[] names)
        {
            if (parent == null) return null;
            foreach (var name in names)
            {
                var found = parent.Controls.Find(name, true);
                if (found != null && found.Length > 0)
                    return found[0];
            }
            return null;
        }

        private static string GetControlTextSafe(Control parent, string[] names)
        {
            var ctrl = FindControlRecursive(parent, names);
            if (ctrl == null) return string.Empty;
            return ctrl is TextBox tb ? tb.Text :
                   ctrl is ComboBox cb ? Convert.ToString(cb.SelectedItem) ?? string.Empty :
                   ctrl is Label lb ? lb.Text :
                   ctrl.Text ?? string.Empty;
        }

        private static void SetControlTextSafe(Control parent, string[] names, string text)
        {
            var ctrl = FindControlRecursive(parent, names);
            if (ctrl == null) return;
            switch (ctrl)
            {
                case TextBox tb:
                    tb.Text = text;
                    break;
                case ComboBox cb:
                    cb.Text = text;
                    break;
                case Label lb:
                    lb.Text = text;
                    break;
                default:
                    ctrl.Text = text;
                    break;
            }
        }

        private void pictureBox1_DoubleClick(object? sender, EventArgs? e)
        {
            // Разрешаем загрузку фото только если panel2 включена и pictureBox1 активен
            if (panel2 == null || !panel2.Enabled) return;
            if (pictureBox1 == null || !pictureBox1.Enabled) return;

            using var ofd = new OpenFileDialog
            {
                Filter = "Image Files (*.jpg;*.jpeg;*.png;*.bmp;*.gif)|*.jpg;*.jpeg;*.png;*.bmp;*.gif|All files (*.*)|*.*",
                Title = "Выберите изображение"
            };

            if (ofd.ShowDialog(this) != DialogResult.OK) return;

            try
            {
                var bytes = File.ReadAllBytes(ofd.FileName);
                // Обновим текущую картинку в UI
                if (pictureBox1.Image != null)
                {
                    var old = pictureBox1.Image;
                    pictureBox1.Image = null;
                    old.Dispose();
                }

                using var ms = new MemoryStream(bytes);
                var img = Image.FromStream(ms);
                pictureBox1.Image = new Bitmap(img);
                pictureBox1.SizeMode = PictureBoxSizeMode.Zoom;

                // Сохраним байты для отправки в БД при сохранении
                _currentPhoto = bytes;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Не удалось загрузить изображение: {ex.Message}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>
        /// Обработчик кнопки Удалить (button37). Удаляет сотрудника и перезагружает список.
        /// </summary>
        private async void button37_Click(object? sender, EventArgs e)
        {
            try
            {
                // Проверим выбранную строку
                if (dataGridView1.CurrentRow == null)
                {
                    MessageBox.Show(this, "Выберите сотрудника в списке.", "Информация", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                // Получаем ID из скрытой колонки "ID"
                var idStr = GetRowCellString(dataGridView1.CurrentRow, "ID");
                if (!int.TryParse(idStr, out var employeeId) || employeeId <= 0)
                {
                    MessageBox.Show(this, "Не удалось определить ID выбранного сотрудника.", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                // Подтверждение удаления
                var dr = MessageBox.Show(this, "Вы уверены что хотите удалить данную запись?", "Подтвердите действие", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (dr != DialogResult.Yes) return;

                // Вызов процедуры DELETE_EMPLOYEE
                await using var conn = await FirebirdConnection.CreateOpenConnectionAsync();
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = "DELETE_EMPLOYEE";
                cmd.CommandType = CommandType.StoredProcedure;
                cmd.Parameters.AddWithValue("EMPLOYEE_ID", employeeId);

                await cmd.ExecuteNonQueryAsync();

                MessageBox.Show(this, "Сотрудник успешно удалён.", "Успех", MessageBoxButtons.OK, MessageBoxIcon.Information);

                // Обновим грид сотрудников
                await LoadEmployeesAsync();
            }
            catch (FbException fbEx)
            {
                // Отобразим сообщение от СУБД (например, исключение EX_EMPLOYEE_HAS_CALLS)
                MessageBox.Show(this, $"Ошибка БД при удалении сотрудника: {fbEx.Message}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Ошибка при удалении сотрудника: {ex.Message}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void button29_Click(object sender, EventArgs e)
        {

        }

        private void button31_Click(object sender, EventArgs e)
        {

        }

        private void button30_Click(object sender, EventArgs e)
        {

        }

        private void button38_Click(object sender, EventArgs e)
        {

        }

        private void button34_Click(object sender, EventArgs e)
        {

        }

        private void button33_Click(object sender, EventArgs e)
        {

        }

        private void dataGridView7_CellContentClick(object sender, DataGridViewCellEventArgs e)
        {

        }

        //private void textBox12_TextChanged_1(object sender, EventArgs e)
        //{

        //}
    }
}

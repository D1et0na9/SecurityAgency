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

        // Флаг чтобы избежать повторных одновременных обновлений при быстром переключении вкладок
        private bool _isTabRefreshInProgress = false;

        private string _currentTable1Name = ""; // Текущая таблица для dataGridView5
        private string _currentTable2Name = "";

        public Form1()
        {
            InitializeComponent();

            // Подписываемся на Click кнопки сохранения (добавлено программно — чтобы не трогать Designer)
            button26.Click += button26_Click;
            //button23.Click += button23_Click;

            // Подписываемся на кнопку удаления сотрудника
            button37.Click += button37_Click;
            button29.Click += button29_Click_1;
            //button31.Click += button31_Click;
            //button36.Click += button36_Click_1;

            // В конструкторе Form1, после строки button27.Click += button27_Click;
            button17.Click += button17_Click;
            button35.Click += button35_Click;

            // Загружаем данные при старте формы
            Load += Form1_Load;

            // В конструкторе — после InitializeComponent() и текущих подписок
            dataGridView1.CellClick += dataGridView1_CellClick;
            // Запрет редактирования richTextBox1
            richTextBox1.ReadOnly = true;

            // Подписываемся на DoubleClick для загрузки фото (активируется, когда panel2 и pictureBox1 включены)
            pictureBox1.DoubleClick += pictureBox1_DoubleClick;

            // Подписка на переключение вкладок — при переходе будем обновлять гриды вкладки назначения
            if (tabControl1 != null)
                tabControl1.SelectedIndexChanged += tabControl1_SelectedIndexChanged;

            // В конструкторе Form1, после строки button26.Click += button26_Click;
            button27.Click += button27_Click;
        }

        private async void Form1_Load(object? sender, EventArgs e)
        {
            await LoadAvailableTablesAsync();

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

            // В конце метода Form1_Load, после загрузки других данных
            //await LoadClientsForComboBox();
            //await LoadEmployeesForComboBox();

            // По умолчанию делаем panel8 недоступной для ввода (если нужно)
            if (panel2 != null)
                panel2.Enabled = false;

            if (panel10 != null)
                panel10.Enabled = false;

            if (panel8 != null)
                panel8.Enabled = false;

            if (panel11 != null)
                panel11.Enabled = false;

            if (panel17 != null)
                panel17.Enabled = false;

            if (comboBox1 != null)
                comboBox1.SelectedIndexChanged += ComboBox1_SelectedIndexChanged;

            if (comboBox2 != null)
                comboBox2.SelectedIndexChanged += ComboBox2_SelectedIndexChanged;

            if (checkedListBox1 != null)
                checkedListBox1.ItemCheck += CheckedListBox1_ItemCheck;

            if (checkedListBox2 != null)
                checkedListBox2.ItemCheck += CheckedListBox2_ItemCheck;

        }

        /// <summary>
        /// Обработчик переключения вкладок: при переходе на вкладку запускает её функцию обновления (если она есть).
        /// Защищает от повторного одновременного выполнения.
        /// </summary>
        private async void tabControl1_SelectedIndexChanged(object? sender, EventArgs e)
        {
            if (_isTabRefreshInProgress) return;
            if (tabControl1 == null) return;

            try
            {
                _isTabRefreshInProgress = true;

                var selected = tabControl1.SelectedTab;
                if (selected == null) return;

                // Сопоставления: вкладка -> функция обновления
                if (selected == tabPage1)
                {
                    // Сотрудники
                    await LoadEmployeesAsync();
                }
                else if (selected == tabPage2)
                {
                    // Клиенты
                    await LoadClientsWithCountsAsync();
                }
                else if (selected == tabPage3)
                {
                    // Объекты
                    await LoadGuardedObjectsCombinedAsync();
                }
                else if (selected == tabPage4)
                {
                    // Вызовы
                    await LoadGuardCallsCombinedAsync();
                }
                else if (selected == tabPage6)
                {
                    // Услуги
                    await LoadServiceTypesAsync();
                }
                else
                {
                    // Для прочих вкладок (например, Отчёты tabPage5) специфического загрузчика нет — ничего не делаем.
                }
            }
            catch (Exception ex)
            {
                // Безопасно показываем ошибку, чтобы не ломать переключение вкладок
                MessageBox.Show(this, $"Ошибка при обновлении вкладки: {ex.Message}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                _isTabRefreshInProgress = false;
            }
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
        /// - aggregированную информацию по GUARDCALLS: количество вызовов и дата последнего вызова
        /// Результат отображается в одном DataGridView — dataGridView3. ID не показывается.
        /// Убраны колонки "Номер заказа", "Сотрудник" и "Результат" — запрашиваются только необходимые данные.
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
                table.Columns.Add("CLIENT_FIO", typeof(string)).Caption = "Клиент";
                table.Columns.Add("OBJECT_ADDRESS", typeof(string)).Caption = "Адрес объекта";
                table.Columns.Add("DESCRIPTION", typeof(string)).Caption = "Описание";
                table.Columns.Add("CALL_COUNT", typeof(int)).Caption = "Вызовов";
                table.Columns.Add("LAST_CALL_DATETIME", typeof(DateTime)).Caption = "Последний вызов";
                // Убраны: ORDER_ID, LAST_CALL_EMPLOYEE, LAST_CALL_RESULT

                // Колонка поиска
                table.Columns.Add("SEARCH", typeof(string));

                // Сохраняем только даты вызовов для каждого адреса
                var callsByAddress = new Dictionary<string, List<DateTime>>(StringComparer.OrdinalIgnoreCase);

                await using var conn = await FirebirdConnection.CreateOpenConnectionAsync();

                // Читаем только те поля из GET_GUARDCALLS, которые нам нужны: OBJECT_ADDRESS и CALL_DATETIME
                await using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "GET_GUARDCALLS";
                    cmd.CommandType = System.Data.CommandType.StoredProcedure;

                    await using var reader = await cmd.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                    {
                        string? addr = null;
                        DateTime? dt = null;

                        if (ColumnExists(reader, "OBJECT_ADDRESS") && !reader.IsDBNull(reader.GetOrdinal("OBJECT_ADDRESS")))
                            addr = reader.GetString(reader.GetOrdinal("OBJECT_ADDRESS"));

                        if (ColumnExists(reader, "CALL_DATETIME") && !reader.IsDBNull(reader.GetOrdinal("CALL_DATETIME")))
                            dt = reader.GetDateTime(reader.GetOrdinal("CALL_DATETIME"));

                        if (!string.IsNullOrEmpty(addr) && dt.HasValue)
                        {
                            if (!callsByAddress.TryGetValue(addr, out var list))
                            {
                                list = new List<DateTime>();
                                callsByAddress[addr] = list;
                            }

                            list.Add(dt.Value);
                        }
                    }
                }

                // Получаем охраняемые объекты и приклеиваем агрегацию вызовов
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
                        row["LAST_CALL_DATETIME"] = DBNull.Value;

                        if (!string.IsNullOrEmpty(addr) && callsByAddress.TryGetValue(addr, out var calls) && calls.Count > 0)
                        {
                            row["CALL_COUNT"] = calls.Count;
                            var last = calls.OrderByDescending(x => x).First();
                            row["LAST_CALL_DATETIME"] = last;
                        }

                        // SEARCH: клиент, адрес, описание, дата последнего вызова
                        var searchParts = new List<string> {
                            clientFio,
                            addr ?? string.Empty,
                            desc
                        };

                        if (row["LAST_CALL_DATETIME"] != DBNull.Value && row["LAST_CALL_DATETIME"] is DateTime dtLast)
                            searchParts.Add(dtLast.ToString("g"));

                        row["SEARCH"] = string.Join(" ", searchParts.Where(s => !string.IsNullOrEmpty(s))).ToLowerInvariant();

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

                // Добавляем скрытую колонку ID
                table.Columns.Add("ID", typeof(int));

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

                    // Скрываем служебные колонки ID и SEARCH
                    if (string.Equals(colName, "ID", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(colName, "SEARCH", StringComparison.OrdinalIgnoreCase))
                    {
                        col.Visible = false;
                        continue;
                    }

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
            // Сохранение в CSV — оставлено без изменений пока
            await Task.CompletedTask;
        }

        private async void button2_Click(object sender, EventArgs e)
        {
            // Кнопка "Обновить" для списка сотрудников — перезагрузим данные
            await LoadEmployeesAsync();
        }

        private void button5_Click(object sender, EventArgs e)
        {
            try
            {
                if (dataGridView1 == null || dataGridView1.Columns.Count == 0)
                {
                    MessageBox.Show(this, "В таблице нет колонок для выбора.", "Информация", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                // Собираем колонки, у которых есть Name (используем Name как уникальный ключ)
                var cols = new List<DataGridViewColumn>();
                foreach (DataGridViewColumn c in dataGridView1.Columns)
                {
                    // включаем в список все колонки, у которых задано имя (имена в вашем коде присутствуют для всех полезных колонок)
                    if (!string.IsNullOrEmpty(c.Name))
                        cols.Add(c);
                }

                using var dlg = new ColumnSelectorForm(cols);
                var res = dlg.ShowDialog(this);
                if (res != DialogResult.OK) return;

                var selected = new HashSet<string>(dlg.SelectedColumnNames, StringComparer.OrdinalIgnoreCase);

                // Применяем — видимыми остаются только выбранные колонки (по Name)
                foreach (DataGridViewColumn c in dataGridView1.Columns)
                {
                    if (string.IsNullOrEmpty(c.Name)) continue;
                    c.Visible = selected.Contains(c.Name);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Ошибка при выборе колонок: {ex.Message}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
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
            // Клиенты -> dataGridView2
            ShowColumnSelectorForGrid(dataGridView2);
        }

        private void button10_Click(object sender, EventArgs e)
        {
            // Объекты -> dataGridView3
            ShowColumnSelectorForGrid(dataGridView3);
        }

        private void button13_Click(object sender, EventArgs e)
        {
            // Вызовы -> dataGridView4
            ShowColumnSelectorForGrid(dataGridView4);
        }

        private void button34_Click(object sender, EventArgs e)
        {
            // Услуги -> dataGridView7
            ShowColumnSelectorForGrid(dataGridView7);
        }

        private void checkBox2_CheckedChanged(object sender, EventArgs e)
        {

        }

        //private void button20_Click(object sender, EventArgs e)
        //{

        //}

        private void button21_Click(object sender, EventArgs e)
        {
            // Проверим выбранную строку в dataGridView2
            if (dataGridView2.CurrentRow == null)
            {
                MessageBox.Show(this, "Выберите клиента в списке.", "Информация", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            // Разблокируем panel10
            if (panel10 == null) return;
            panel10.Enabled = true;

            // Получаем данные из выбранной строки
            var clientId = GetRowCellString(dataGridView2.CurrentRow, "ID");
            var name = GetRowCellString(dataGridView2.CurrentRow, "NAME");
            var surname = GetRowCellString(dataGridView2.CurrentRow, "SURNAME");
            var secondName = GetRowCellString(dataGridView2.CurrentRow, "SECOND_NAME");
            var address = GetRowCellString(dataGridView2.CurrentRow, "ADDRESS");
            var phone = GetRowCellString(dataGridView2.CurrentRow, "PHONE");
            var accountNumber = GetRowCellString(dataGridView2.CurrentRow, "ACCOUNT_NUMBER");

            // Заполняем элементы panel10 (в panel10: textBox11, textBox10, textBox9, textBox14, textBox13, richTextBox4)
            textBox11.Text = name;
            textBox10.Text = surname;
            textBox9.Text = secondName;
            textBox14.Text = address;
            textBox13.Text = phone;
            richTextBox4.Text = accountNumber;

            // Сохраняем ID редактируемого клиента
            _editingId = int.TryParse(clientId, out var id) ? id : -1;

            // Переместим фокус на первый элемент
            if (panel10.Controls.Count > 0)
                panel10.Controls[0].Focus();
        }

        private async void button22_Click(object sender, EventArgs e)
        {
            try
            {
                // Проверим выбранную строку в dataGridView2
                if (dataGridView2.CurrentRow == null)
                {
                    MessageBox.Show(this, "Выберите клиента в списке.", "Информация", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                // Получаем ID из скрытой колонки
                var idStr = GetRowCellString(dataGridView2.CurrentRow, "ID");
                if (!int.TryParse(idStr, out var clientId) || clientId <= 0)
                {
                    MessageBox.Show(this, "Не удалось определить ID выбранного клиента.", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                // Получим ФИО клиента для подтверждения
                var clientName = GetRowCellString(dataGridView2.CurrentRow, "SURNAME");
                var clientFirstName = GetRowCellString(dataGridView2.CurrentRow, "NAME");
                var clientFullName = string.IsNullOrEmpty(clientFirstName)
                    ? clientName
                    : $"{clientName} {clientFirstName}".Trim();

                // Подтверждение удаления
                var message = string.IsNullOrEmpty(clientFullName)
                    ? "Вы уверены что хотите удалить данную запись?"
                    : $"Вы уверены что хотите удалить клиента '{clientFullName}'?";

                var dr = MessageBox.Show(this, message, "Подтвердите действие", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (dr != DialogResult.Yes) return;

                // Вызов процедуры DELETE_CLIENT
                await using var conn = await FirebirdConnection.CreateOpenConnectionAsync();
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = "DELETE_CLIENT";
                cmd.CommandType = CommandType.StoredProcedure;
                cmd.Parameters.AddWithValue("CLIENT_ID", clientId);

                await cmd.ExecuteNonQueryAsync();

                MessageBox.Show(this, "Клиент успешно удалён.", "Успех", MessageBoxButtons.OK, MessageBoxIcon.Information);

                // Обновим грид клиентов
                await LoadClientsWithCountsAsync();
            }
            catch (FbException fbEx)
            {
                MessageBox.Show(this, $"Ошибка БД при удалении клиента: {fbEx.Message}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Ошибка при удалении клиента: {ex.Message}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void dataGridView2_CellContentClick(object sender, DataGridViewCellEventArgs e)
        {

        }

        //private void textBox2_TextChanged(object sender, EventArgs e)
        //{

        //}

        private void button4_Click(object sender, EventArgs e)
        {
            try
            {
                // Проверим, есть ли данные в dataGridView2
                if (dataGridView2 == null || dataGridView2.Rows.Count == 0)
                {
                    MessageBox.Show(this, "В таблице нет данных для сохранения.", "Информация", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                // Подтверждение сохранения
                var dr = MessageBox.Show(this, "Вы уверены что хотите сохранить таблицу?", "Подтвердите действие", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (dr != DialogResult.Yes) return;

                // Диалог выбора папки для сохранения
                using var sfd = new SaveFileDialog
                {
                    FileName = "clients.csv",
                    Filter = "CSV Files (*.csv)|*.csv|All files (*.*)|*.*",
                    Title = "Сохранить таблицу клиентов",
                    DefaultExt = "csv"
                };

                if (sfd.ShowDialog(this) != DialogResult.OK) return;

                var filePath = sfd.FileName;

                // Экспорт в CSV
                using (var writer = new StreamWriter(filePath, false, System.Text.Encoding.UTF8))
                {
                    // Заголовки колонок
                    var headers = new List<string>();
                    foreach (DataGridViewColumn col in dataGridView2.Columns)
                    {
                        // Пропускаем скрытые колонки
                        if (!col.Visible) continue;
                        headers.Add($"\"{col.HeaderText}\"");
                    }
                    writer.WriteLine(string.Join(",", headers));

                    // Данные строк
                    foreach (DataGridViewRow row in dataGridView2.Rows)
                    {
                        var values = new List<string>();
                        foreach (DataGridViewColumn col in dataGridView2.Columns)
                        {
                            // Пропускаем скрытые колонки
                            if (!col.Visible) continue;

                            var cellValue = row.Cells[col.Index].Value?.ToString() ?? string.Empty;
                            // Экранируем кавычки и оборачиваем в кавычки
                            cellValue = $"\"{cellValue.Replace("\"", "\"\"")}\"";
                            values.Add(cellValue);
                        }
                        writer.WriteLine(string.Join(",", values));
                    }
                }

                MessageBox.Show(this, $"Таблица успешно сохранена в файл:\n{filePath}", "Успех", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (UnauthorizedAccessException)
            {
                MessageBox.Show(this, "Нет прав доступа для сохранения файла в выбранную папку.", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch (IOException ioEx)
            {
                MessageBox.Show(this, $"Ошибка при сохранении файла: {ioEx.Message}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Ошибка при сохранении таблицы: {ex.Message}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void button17_Click(object sender, EventArgs e)
        {
            // Разблокировать элементы panel8 для ввода данных нового охраняемого объекта
            if (panel8 == null) return;

            panel8.Enabled = true;

            // Очистим все поля внутри panel8
            foreach (Control c in panel8.Controls)
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

            // Переместим фокус на первый элемент
            if (panel8.Controls.Count > 0)
                panel8.Controls[0].Focus();
        }

        private void button18_Click(object sender, EventArgs e)
        {

        }

        private async void button19_Click(object sender, EventArgs e)
        {
            try
            {
                // Проверим выбранную строку в dataGridView3
                if (dataGridView3.CurrentRow == null)
                {
                    MessageBox.Show(this, "Выберите объект в списке.", "Информация", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                // Получаем ID из скрытой колонки
                var idStr = GetRowCellString(dataGridView3.CurrentRow, "ID");
                if (!int.TryParse(idStr, out var objectId) || objectId <= 0)
                {
                    MessageBox.Show(this, "Не удалось определить ID выбранного объекта.", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                // Получим информацию об объекте для подтверждения
                var clientFio = GetRowCellString(dataGridView3.CurrentRow, "CLIENT_FIO");
                var objectAddress = GetRowCellString(dataGridView3.CurrentRow, "OBJECT_ADDRESS");
                var displayInfo = string.IsNullOrEmpty(objectAddress)
                    ? clientFio
                    : string.IsNullOrEmpty(clientFio)
                        ? objectAddress
                        : $"{clientFio} - {objectAddress}";

                // Подтверждение удаления
                var message = string.IsNullOrEmpty(displayInfo)
                    ? "Вы уверены что хотите удалить данный объект?"
                    : $"Вы уверены что хотите удалить объект '{displayInfo}'?\n\nБудут удалены также все связанные вызовы.";

                var dr = MessageBox.Show(this, message, "Подтвердите действие", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (dr != DialogResult.Yes) return;

                // Вызов процедуры DELETE_GUARDEDOBJECT
                await using var conn = await FirebirdConnection.CreateOpenConnectionAsync();
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = "DELETE_GUARDEDOBJECT";
                cmd.CommandType = CommandType.StoredProcedure;
                cmd.Parameters.AddWithValue("OBJECT_ID", objectId);

                await cmd.ExecuteNonQueryAsync();

                MessageBox.Show(this, "Объект успешно удалён.", "Успех", MessageBoxButtons.OK, MessageBoxIcon.Information);

                // Обновим гриды объектов и вызовов
                await LoadGuardedObjectsCombinedAsync();
                await LoadGuardCallsCombinedAsync();
            }
            catch (FbException fbEx)
            {
                MessageBox.Show(this, $"Ошибка БД при удалении объекта: {fbEx.Message}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Ошибка при удалении объекта: {ex.Message}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
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

        //private void button10_Click(object sender, EventArgs e)
        //{

        //}

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

        private async void button16_Click(object sender, EventArgs e)
        {
            try
            {
                // Проверим выбранную строку в dataGridView4
                if (dataGridView4.CurrentRow == null)
                {
                    MessageBox.Show(this, "Выберите вызов в списке.", "Информация", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                // Получаем ID вызова из скрытой колонки CALL_ID
                var idStr = GetRowCellString(dataGridView4.CurrentRow, "CALL_ID");
                if (!int.TryParse(idStr, out var callId) || callId <= 0)
                {
                    MessageBox.Show(this, "Не удалось определить ID выбранного вызова.", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                // Получим информацию о вызове для подтверждения
                var objectAddress = GetRowCellString(dataGridView4.CurrentRow, "OBJECT_ADDRESS");
                var employeeFio = GetRowCellString(dataGridView4.CurrentRow, "EMPLOYEE_FIO");
                var callDateTime = GetRowCellString(dataGridView4.CurrentRow, "CALL_DATETIME");

                var displayInfo = string.IsNullOrEmpty(objectAddress)
                    ? employeeFio
                    : string.IsNullOrEmpty(employeeFio)
                        ? objectAddress
                        : $"{objectAddress} ({employeeFio})";

                // Подтверждение удаления
                var message = string.IsNullOrEmpty(displayInfo)
                    ? "Вы уверены что хотите удалить данный вызов?"
                    : $"Вы уверены что хотите удалить вызов на адрес '{displayInfo}'?";

                var dr = MessageBox.Show(this, message, "Подтвердите действие", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (dr != DialogResult.Yes) return;

                // Вызов процедуры DELETE_GUARDCALL
                await using var conn = await FirebirdConnection.CreateOpenConnectionAsync();
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = "DELETE_GUARDCALL";
                cmd.CommandType = CommandType.StoredProcedure;
                cmd.Parameters.AddWithValue("CALL_ID", callId);

                await cmd.ExecuteNonQueryAsync();

                MessageBox.Show(this, "Вызов успешно удалён.", "Успех", MessageBoxButtons.OK, MessageBoxIcon.Information);

                // Обновим грид вызовов
                await LoadGuardCallsCombinedAsync();
            }
            catch (FbException fbEx)
            {
                MessageBox.Show(this, $"Ошибка БД при удалении вызова: {fbEx.Message}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Ошибка при удалении вызова: {ex.Message}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
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

        private async void button12_Click(object sender, EventArgs e)
        {
            await LoadGuardCallsCombinedAsync();
            // Кнопка "Обновить" на вкладке Вызовы (designer: button12) — перезагружает dataGridView4
        }

        //private void button13_Click(object sender, EventArgs e)
        //{

        //}

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

        private string GetTableDisplayName(string tableName)
        {
            return tableName switch
            {
                "GET_CLIENTS" => "Клиенты",
                "GET_EMPLOYEES" => "Сотрудники",
                "GET_GUARDEDOBJECTS" => "Охраняемые объекты",
                "GET_GUARDCALLS" => "Вызовы",
                "GET_ORDERS" => "Заказы",
                "GET_ORDER_DETAILS" => "Детали заказов",
                "GET_POSITIONS" => "Должности",
                "GET_SERVICETYPES" => "Типы услуг",
                _ => tableName
            };
        }

        private void WriteTableToCSV(StreamWriter writer, DataGridView dgv, string tableName)
        {
            if (dgv.Rows.Count == 0)
                return;

            // Заголовок таблицы
            writer.WriteLine($"--- {GetTableDisplayName(tableName)} ---");

            // Собираем видимые колонки (без ID)
            var visibleColumns = new List<DataGridViewColumn>();
            foreach (DataGridViewColumn col in dgv.Columns)
            {
                if (col.Visible && !string.IsNullOrEmpty(col.Name) &&
                    !col.Name.Contains("ID", StringComparison.OrdinalIgnoreCase))
                {
                    visibleColumns.Add(col);
                }
            }

            // Записываем заголовки колонок
            var headers = visibleColumns.Select(col => $"\"{col.HeaderText}\"").ToList();
            writer.WriteLine(string.Join(",", headers));

            // Записываем данные строк
            foreach (DataGridViewRow row in dgv.Rows)
            {
                var values = new List<string>();
                foreach (var col in visibleColumns)
                {
                    var cellValue = row.Cells[col.Index].Value?.ToString() ?? string.Empty;
                    // Экранируем кавычки и оборачиваем в кавычки
                    cellValue = $"\"{cellValue.Replace("\"", "\"\"")}\"";
                    values.Add(cellValue);
                }
                writer.WriteLine(string.Join(",", values));
            }
        }

        private void WriteMergedRelatedTables(StreamWriter writer, DataGridView dgv1, DataGridView dgv2, string table1Name, string table2Name)
        {
            // Пока напишем таблицы отдельно
            // В будущем здесь можно добавить логику объединения по JOIN
            WriteTableToCSV(writer, dgv1, table1Name);
            writer.WriteLine();
            WriteTableToCSV(writer, dgv2, table2Name);
        }

        private bool AreTablesRelated(string table1Name, string table2Name)
        {
            // Определяем связи между таблицами на основе их первичных и внешних ключей
            var relationships = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase)
    {
        { "GET_CLIENTS", new HashSet<string> { "GET_ORDERS", "GET_GUARDEDOBJECTS" } },
        { "GET_ORDERS", new HashSet<string> { "GET_CLIENTS", "GET_ORDER_DETAILS", "GET_GUARDEDOBJECTS" } },
        { "GET_EMPLOYEES", new HashSet<string> { "GET_GUARDCALLS", "GET_POSITIONS" } },
        { "GET_GUARDCALLS", new HashSet<string> { "GET_EMPLOYEES", "GET_GUARDEDOBJECTS" } },
        { "GET_GUARDEDOBJECTS", new HashSet<string> { "GET_ORDERS", "GET_GUARDCALLS" } },
        { "GET_ORDER_DETAILS", new HashSet<string> { "GET_ORDERS", "GET_SERVICETYPES" } },
        { "GET_POSITIONS", new HashSet<string> { "GET_EMPLOYEES" } },
    };

            if (relationships.TryGetValue(table1Name, out var related))
            {
                return related.Contains(table2Name);
            }

            return false;
        }

        private void ExportMergedTablesToCSV(string filePath, DataGridView dgv1, DataGridView dgv2, string table1Name, string table2Name)
        {
            using (var writer = new StreamWriter(filePath, false, System.Text.Encoding.UTF8))
            {
                // Если таблицы связаны, объединяем их
                if (AreTablesRelated(table1Name, table2Name))
                {
                    WriteMergedRelatedTables(writer, dgv1, dgv2, table1Name, table2Name);
                }
                else
                {
                    // Если таблицы не связаны, пишем их отдельно
                    WriteTableToCSV(writer, dgv1, table1Name);
                    writer.WriteLine();
                    WriteTableToCSV(writer, dgv2, table2Name);
                }
            }
        }

        private void button3_Click(object sender, EventArgs e)
        {
            try
            {
                // Проверим, загружены ли обе таблицы
                if (dataGridView5.Rows.Count == 0 && dataGridView6.Rows.Count == 0)
                {
                    MessageBox.Show(this, "Обе таблицы пусты. Выберите таблицы в выпадающих списках.", "Информация", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                // Подтверждение сохранения
                var dr = MessageBox.Show(this, "Вы уверены что хотите сохранить объединённые таблицы?", "Подтвердите действие", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (dr != DialogResult.Yes) return;

                // Диалог выбора папки для сохранения
                using var sfd = new SaveFileDialog
                {
                    FileName = "mergedoutput.csv",
                    Filter = "CSV Files (*.csv)|*.csv|All files (*.*)|*.*",
                    Title = "Сохранить объединённые таблицы",
                    DefaultExt = "csv"
                };

                if (sfd.ShowDialog(this) != DialogResult.OK) return;

                var filePath = sfd.FileName;

                // Экспорт в CSV
                ExportMergedTablesToCSV(filePath, dataGridView5, dataGridView6, _currentTable1Name, _currentTable2Name);

                MessageBox.Show(this, $"Таблицы успешно сохранены в файл:\n{filePath}", "Успех", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (UnauthorizedAccessException)
            {
                MessageBox.Show(this, "Нет прав доступа для сохранения файла в выбранную папку.", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch (IOException ioEx)
            {
                MessageBox.Show(this, $"Ошибка при сохранении файла: {ioEx.Message}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Ошибка при сохранении таблиц: {ex.Message}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
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

        private async void button33_Click(object sender, EventArgs e)
        {
            await LoadServiceTypesAsync();
        }

        private void button38_Click(object sender, EventArgs e)
        {
            HandleLogout();
        }

        private void button39_Click(object sender, EventArgs e)
        {
            HandleLogout();
        }

        private void button40_Click(object sender, EventArgs e)
        {
            HandleLogout();
        }

        private void button41_Click(object sender, EventArgs e)
        {
            HandleLogout();
        }

        private void button43_Click(object sender, EventArgs e)
        {
            try
            {
                // Проверим, есть ли данные в dataGridView7
                if (dataGridView7 == null || dataGridView7.Rows.Count == 0)
                {
                    MessageBox.Show(this, "В таблице нет данных для сохранения.", "Информация", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                // Подтверждение сохранения
                var dr = MessageBox.Show(this, "Вы уверены что хотите сохранить таблицу?", "Подтвердите действие", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (dr != DialogResult.Yes) return;

                // Диалог выбора папки для сохранения
                using var sfd = new SaveFileDialog
                {
                    FileName = "servicetypes.csv",
                    Filter = "CSV Files (*.csv)|*.csv|All files (*.*)|*.*",
                    Title = "Сохранить таблицу услуг",
                    DefaultExt = "csv"
                };

                if (sfd.ShowDialog(this) != DialogResult.OK) return;

                var filePath = sfd.FileName;

                // Экспорт в CSV
                using (var writer = new StreamWriter(filePath, false, System.Text.Encoding.UTF8))
                {
                    // Заголовки колонок
                    var headers = new List<string>();
                    foreach (DataGridViewColumn col in dataGridView7.Columns)
                    {
                        // Пропускаем скрытые колонки
                        if (!col.Visible) continue;
                        headers.Add($"\"{col.HeaderText}\"");
                    }
                    writer.WriteLine(string.Join(",", headers));

                    // Данные строк
                    foreach (DataGridViewRow row in dataGridView7.Rows)
                    {
                        var values = new List<string>();
                        foreach (DataGridViewColumn col in dataGridView7.Columns)
                        {
                            // Пропускаем скрытые колонки
                            if (!col.Visible) continue;

                            var cellValue = row.Cells[col.Index].Value?.ToString() ?? string.Empty;
                            // Экранируем кавычки и оборачиваем в кавычки
                            cellValue = $"\"{cellValue.Replace("\"", "\"\"")}\"";
                            values.Add(cellValue);
                        }
                        writer.WriteLine(string.Join(",", values));
                    }
                }

                MessageBox.Show(this, $"Таблица успешно сохранена в файл:\n{filePath}", "Успех", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (UnauthorizedAccessException)
            {
                MessageBox.Show(this, "Нет прав доступа для сохранения файла в выбранную папку.", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch (IOException ioEx)
            {
                MessageBox.Show(this, $"Ошибка при сохранении файла: {ioEx.Message}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Ошибка при сохранении таблицы: {ex.Message}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        //private void button34_Click(object sender, EventArgs e)
        //{

        //}

        private void ShowColumnSelectorForGrid(DataGridView dgv)
        {
            if (dgv == null)
            {
                MessageBox.Show(this, "Таблица не найдена.", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (dgv.Columns == null || dgv.Columns.Count == 0)
            {
                MessageBox.Show(this, "В таблице нет колонок для выбора.", "Информация", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            try
            {
                var cols = new List<DataGridViewColumn>();
                foreach (DataGridViewColumn c in dgv.Columns)
                {
                    if (!string.IsNullOrEmpty(c.Name))
                        cols.Add(c);
                }

                using var dlg = new ColumnSelectorForm(cols);
                var res = dlg.ShowDialog(this);
                if (res != DialogResult.OK) return;

                var selected = new HashSet<string>(dlg.SelectedColumnNames, StringComparer.OrdinalIgnoreCase);

                // Сделаем видимыми только выбранные колонки (по Name).
                // Сохраним порядок и остальные свойства.
                foreach (DataGridViewColumn c in dgv.Columns)
                {
                    if (string.IsNullOrEmpty(c.Name)) continue;
                    c.Visible = selected.Contains(c.Name);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Ошибка при выборе колонок: {ex.Message}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        private void button42_Click(object sender, EventArgs e)
        {
            HandleLogout();
        }

        //private void button27_Click(object sender, EventArgs e)
        //{

        //}


        private void button20_Click(object sender, EventArgs e)
        {
            // Разблокировать элементы panel10 для ввода данных нового клиента
            if (panel10 == null) return;

            panel10.Enabled = true;

            // Очистим все поля внутри panel10
            foreach (Control c in panel10.Controls)
            {
                switch (c)
                {
                    case TextBox tb:
                        tb.Clear();
                        break;
                    case RichTextBox rtb:
                        rtb.Clear();
                        break;
                }
            }

            // Переместим фокус на первый элемент
            if (panel10.Controls.Count > 0)
                panel10.Controls[0].Focus();
        }

        private async void button27_Click(object sender, EventArgs e)
        {
            try
            {
                // Проверяем, редактируется ли клиент (был ли вызван button21)
                if (_editingId > 0)
                {
                    // РЕЖИМ РЕДАКТИРОВАНИЯ
                    if (panel10 == null || !panel10.Enabled)
                    {
                        MessageBox.Show(this, "Панель редактирования не активна.", "Информация", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        return;
                    }

                    // Сбор данных из полей panel10
                    var name = textBox11.Text.Trim();
                    var surname = textBox10.Text.Trim();
                    var secondName = textBox9.Text.Trim();
                    var address = textBox14.Text.Trim();
                    var phone = textBox13.Text.Trim();
                    var accountNumber = richTextBox4.Text.Trim();

                    // Валидация обязательных полей
                    if (string.IsNullOrEmpty(name))
                    {
                        MessageBox.Show(this, "Введите имя клиента.", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }

                    if (string.IsNullOrEmpty(surname))
                    {
                        MessageBox.Show(this, "Введите фамилию клиента.", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }

                    // Подтверждение сохранения
                    var dr = MessageBox.Show(this, "Вы уверены что хотите сохранить изменения?", "Подтвердите действие", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                    if (dr != DialogResult.Yes) return;

                    // Вызов UPDATE_CLIENT
                    try
                    {
                        await using var conn = await FirebirdConnection.CreateOpenConnectionAsync();
                        await using var cmd = conn.CreateCommand();
                        cmd.CommandText = "UPDATE_CLIENT";
                        cmd.CommandType = CommandType.StoredProcedure;

                        cmd.Parameters.AddWithValue("ID", _editingId);
                        cmd.Parameters.AddWithValue("NAME", name);
                        cmd.Parameters.AddWithValue("SURNAME", surname);
                        cmd.Parameters.AddWithValue("SECOND_NAME", string.IsNullOrEmpty(secondName) ? DBNull.Value : (object)secondName);
                        cmd.Parameters.AddWithValue("ADDRESS", string.IsNullOrEmpty(address) ? DBNull.Value : (object)address);
                        cmd.Parameters.AddWithValue("PHONE", string.IsNullOrEmpty(phone) ? DBNull.Value : (object)phone);
                        cmd.Parameters.AddWithValue("ACCOUNT_NUMBER", string.IsNullOrEmpty(accountNumber) ? DBNull.Value : (object)accountNumber);

                        await cmd.ExecuteNonQueryAsync();

                        MessageBox.Show(this, "Клиент успешно обновлён.", "Успех", MessageBoxButtons.OK, MessageBoxIcon.Information);

                        // Очищаем и блокируем panel10
                        textBox11.Clear();
                        textBox10.Clear();
                        textBox9.Clear();
                        textBox14.Clear();
                        textBox13.Clear();
                        richTextBox4.Clear();

                        panel10.Enabled = false;
                        _editingId = -1;

                        // Обновляем таблицу клиентов
                        await LoadClientsWithCountsAsync();
                    }
                    catch (FbException fbEx)
                    {
                        MessageBox.Show(this, $"Ошибка БД при обновлении клиента: {fbEx.Message}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(this, $"Ошибка при обновлении клиента: {ex.Message}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }

                    return;
                }

                // РЕЖИМ ДОБАВЛЕНИЯ НОВОГО КЛИЕНТА (существующая логика)
                if (panel10 == null || !panel10.Enabled)
                {
                    MessageBox.Show(this, "Сначала нажмите 'Добавить заказчика'.", "Информация", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                // Сбор данных из полей panel10
                var newName = textBox11.Text.Trim();
                var newSurname = textBox10.Text.Trim();
                var newSecondName = textBox9.Text.Trim();
                var newAddress = textBox14.Text.Trim();
                var newPhone = textBox13.Text.Trim();
                var newAccountNumber = richTextBox4.Text.Trim();

                // Валидация обязательных полей
                if (string.IsNullOrEmpty(newName))
                {
                    MessageBox.Show(this, "Введите имя клиента.", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                if (string.IsNullOrEmpty(newSurname))
                {
                    MessageBox.Show(this, "Введите фамилию клиента.", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                // Подтверждение сохранения
                var confirmDr = MessageBox.Show(this, "Вы уверены что хотите сохранить запись?", "Подтвердите действие", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (confirmDr != DialogResult.Yes) return;

                // Вызов ADD_CLIENT
                try
                {
                    await using var conn = await FirebirdConnection.CreateOpenConnectionAsync();
                    await using var cmd = conn.CreateCommand();
                    cmd.CommandText = "ADD_CLIENT";
                    cmd.CommandType = CommandType.StoredProcedure;

                    cmd.Parameters.AddWithValue("NAME", newName);
                    cmd.Parameters.AddWithValue("SURNAME", newSurname);
                    cmd.Parameters.AddWithValue("SECOND_NAME", newSecondName);
                    cmd.Parameters.AddWithValue("ADDRESS", string.IsNullOrEmpty(newAddress) ? DBNull.Value : (object)newAddress);
                    cmd.Parameters.AddWithValue("PHONE", string.IsNullOrEmpty(newPhone) ? DBNull.Value : (object)newPhone);
                    cmd.Parameters.AddWithValue("ACCOUNT_NUMBER", string.IsNullOrEmpty(newAccountNumber) ? DBNull.Value : (object)newAccountNumber);

                    int newClientId = -1;
                    await using var reader = await cmd.ExecuteReaderAsync();
                    if (await reader.ReadAsync())
                    {
                        if (ColumnExists(reader, "CLIENT_ID") && !reader.IsDBNull(reader.GetOrdinal("CLIENT_ID")))
                            newClientId = reader.GetInt32(reader.GetOrdinal("CLIENT_ID"));
                    }

                    if (newClientId > 0)
                    {
                        MessageBox.Show(this, $"Клиент успешно добавлен (ID: {newClientId}).", "Успех", MessageBoxButtons.OK, MessageBoxIcon.Information);

                        // Очистим поля ввода
                        textBox11.Clear();
                        textBox10.Clear();
                        textBox9.Clear();
                        textBox14.Clear();
                        textBox13.Clear();
                        richTextBox4.Clear();

                        // Блокируем panel10
                        if (panel10 != null)
                            panel10.Enabled = false;

                        // Обновляем таблицу клиентов
                        await LoadClientsWithCountsAsync();
                    }
                    else
                    {
                        MessageBox.Show(this, "Клиент добавлен, но не удалось получить ID.", "Внимание", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        await LoadClientsWithCountsAsync();
                    }
                }
                catch (FbException fbEx)
                {
                    MessageBox.Show(this, $"Ошибка БД при добавлении клиента: {fbEx.Message}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, $"Ошибка при добавлении клиента: {ex.Message}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Ошибка: {ex.Message}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void button29_Click_1(object sender, EventArgs e)
        {
            if (panel17 == null) return;

            panel17.Enabled = true;

            // Очистим все поля внутри panel17
            foreach (Control c in panel17.Controls)
            {
                switch (c)
                {
                    case TextBox tb:
                        tb.Clear();
                        break;
                    case RichTextBox rtb:
                        rtb.Clear();
                        break;
                    case NumericUpDown nud:
                        nud.Value = nud.Minimum;
                        break;
                }
            }

            // Переместим фокус на первый элемент
            if (panel17.Controls.Count > 0)
                panel17.Controls[0].Focus();
        }

        private async void button36_Click_1(object sender, EventArgs e)
        {
            try
            {
                // Проверка, что panel17 активна
                if (panel17 == null || !panel17.Enabled)
                {
                    MessageBox.Show(this, "Сначала выберите услугу для редактирования.", "Информация", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                // Сбор данных из полей panel17
                var serviceName = GetControlTextSafe(panel17, new[] { "richTextBox6", "textBox15" }).Trim();
                var costStr = GetControlTextSafe(panel17, new[] { "numericUpDown2" }).Trim();

                // Валидация обязательных полей
                if (string.IsNullOrEmpty(serviceName))
                {
                    MessageBox.Show(this, "Введите название услуги.", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                if (!decimal.TryParse(costStr, out var unitCost) || unitCost < 0)
                {
                    MessageBox.Show(this, "Введите корректную стоимость услуги.", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                // Если редактируем существующую услугу
                if (_editingId > 0)
                {
                    // Подтверждение сохранения
                    var dr = MessageBox.Show(this, "Вы уверены что хотите изменить запись?", "Подтвердите действие", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                    if (dr != DialogResult.Yes) return;

                    // Вызов UPDATE_SERVICETYPE
                    try
                    {
                        await using var conn = await FirebirdConnection.CreateOpenConnectionAsync();
                        await using var cmd = conn.CreateCommand();
                        cmd.CommandText = "UPDATE_SERVICETYPE";
                        cmd.CommandType = CommandType.StoredProcedure;

                        cmd.Parameters.AddWithValue("ID", _editingId);
                        cmd.Parameters.AddWithValue("SERVICENAME", serviceName);
                        cmd.Parameters.AddWithValue("UNIT_COST", unitCost);

                        await cmd.ExecuteNonQueryAsync();

                        MessageBox.Show(this, "Тип услуги успешно обновлён.", "Успех", MessageBoxButtons.OK, MessageBoxIcon.Information);

                        // Очищаем и блокируем panel17
                        foreach (Control c in panel17.Controls)
                        {
                            switch (c)
                            {
                                case TextBox tb:
                                    tb.Clear();
                                    break;
                                case RichTextBox rtb:
                                    rtb.Clear();
                                    break;
                                case NumericUpDown nud:
                                    nud.Value = nud.Minimum;
                                    break;
                            }
                        }

                        panel17.Enabled = false;
                        _editingId = -1;

                        // Обновляем таблицу услуг
                        await LoadServiceTypesAsync();
                    }
                    catch (FbException fbEx)
                    {
                        MessageBox.Show(this, $"Ошибка БД при обновлении услуги: {fbEx.Message}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(this, $"Ошибка при обновлении услуги: {ex.Message}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }

                    return;
                }

                // Если добавляем новую услугу (существующая логика)
                var addConfirm = MessageBox.Show(this, "Вы уверены что хотите добавить запись?", "Подтвердите действие", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (addConfirm != DialogResult.Yes) return;

                try
                {
                    await using var conn = await FirebirdConnection.CreateOpenConnectionAsync();
                    await using var cmd = conn.CreateCommand();
                    cmd.CommandText = "ADD_SERVICETYPE";
                    cmd.CommandType = CommandType.StoredProcedure;

                    cmd.Parameters.AddWithValue("SERVICENAME", serviceName);
                    cmd.Parameters.AddWithValue("UNIT_COST", unitCost);

                    int newServiceTypeId = -1;
                    await using var reader = await cmd.ExecuteReaderAsync();
                    if (await reader.ReadAsync())
                    {
                        if (ColumnExists(reader, "ID") && !reader.IsDBNull(reader.GetOrdinal("ID")))
                            newServiceTypeId = reader.GetInt32(reader.GetOrdinal("ID"));
                    }

                    if (newServiceTypeId > 0)
                    {
                        MessageBox.Show(this, $"Тип услуги успешно добавлен (ID: {newServiceTypeId}).", "Успех", MessageBoxButtons.OK, MessageBoxIcon.Information);

                        // Очищаем и блокируем panel17
                        foreach (Control c in panel17.Controls)
                        {
                            switch (c)
                            {
                                case TextBox tb:
                                    tb.Clear();
                                    break;
                                case RichTextBox rtb:
                                    rtb.Clear();
                                    break;
                                case NumericUpDown nud:
                                    nud.Value = nud.Minimum;
                                    break;
                            }
                        }

                        panel17.Enabled = false;

                        await LoadServiceTypesAsync();
                    }
                    else
                    {
                        MessageBox.Show(this, "Тип услуги добавлен, но не удалось получить ID.", "Внимание", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        await LoadServiceTypesAsync();
                    }
                }
                catch (FbException fbEx)
                {
                    MessageBox.Show(this, $"Ошибка БД при добавлении услуги: {fbEx.Message}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, $"Ошибка при добавлении услуги: {ex.Message}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Ошибка: {ex.Message}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            await LoadServiceTypesAsync();
        }

        private async void button30_Click(object sender, EventArgs e)
        {
            try
            {
                // Проверим выбранную строку в dataGridView7
                if (dataGridView7.CurrentRow == null)
                {
                    MessageBox.Show(this, "Выберите тип услуги в списке.", "Информация", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                // Получаем ID из скрытой колонки
                var idStr = GetRowCellString(dataGridView7.CurrentRow, "ID");
                if (!int.TryParse(idStr, out var serviceTypeId) || serviceTypeId <= 0)
                {
                    MessageBox.Show(this, "Не удалось определить ID выбранного типа услуги.", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                // Подтверждение удаления
                var dr = MessageBox.Show(this, "Вы уверены что хотите удалить данную запись?", "Подтвердите действие", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (dr != DialogResult.Yes) return;

                // Вызов процедуры DELETE_SERVICETYPE
                await using var conn = await FirebirdConnection.CreateOpenConnectionAsync();
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = "DELETE_SERVICETYPE";
                cmd.CommandType = CommandType.StoredProcedure;
                cmd.Parameters.AddWithValue("SERVICETYPE_ID", serviceTypeId);

                await cmd.ExecuteNonQueryAsync();

                MessageBox.Show(this, "Тип услуги успешно удалён.", "Успех", MessageBoxButtons.OK, MessageBoxIcon.Information);

                // Обновим грид услуг
                await LoadServiceTypesAsync();
            }
            catch (FbException fbEx)
            {
                // Отобразим сообщение от СУБД 
                MessageBox.Show(this, $"Ошибка БД при удалении услуги: {fbEx.Message}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Ошибка при удалении услуги: {ex.Message}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private Dictionary<string, string> GetColumnHeadersForTable(string tableName)
        {
            return tableName switch
            {
                "GET_CLIENTS" => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "NAME", "Имя" },
            { "SURNAME", "Фамилия" },
            { "SECOND_NAME", "Отчество" },
            { "ADDRESS", "Адрес" },
            { "PHONE", "Телефон" },
            { "ACCOUNT_NUMBER", "Л/сч" },
            { "IS_ACTIVE", "Активен" }
        },
                "GET_EMPLOYEES" => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "NAME", "Имя" },
            { "SURNAME", "Фамилия" },
            { "SECOND_NAME", "Отчество" },
            { "HIRE_DATE", "Дата приёма" },
            { "SALARY", "Оклад" },
            { "EDUCATION", "Образование" },
            { "POSITION_NAME", "Должность" },
            { "PHOTO", "Фото" }
        },
                "GET_GUARDEDOBJECTS" => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "ORDER_ID", "Номер заказа" },
            { "CLIENT_FIO", "Клиент" },
            { "OBJECT_ADDRESS", "Адрес объекта" },
            { "DESCRIPTION", "Описание" }
        },
                "GET_GUARDCALLS" => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "OBJECT_ADDRESS", "Адрес объекта" },
            { "EMPLOYEE_FIO", "Сотрудник" },
            { "CALL_DATETIME", "Дата/время вызова" },
            { "RESULT", "Результат" }
        },
                "GET_ORDERS" => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "CLIENT_ID", "ID клиента" },
            { "CLIENT_FIO", "Клиент" },
            { "ORDER_DATE", "Дата заказа" },
            { "EXECUTION_DATE", "Дата исполнения" },
            { "TOTAL_REVENUE", "Итоговая сумма" }
        },
                "GET_ORDER_DETAILS" => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "ORDER_ID", "Номер заказа" },
            { "CLIENT_FIO", "Клиент" },
            { "SERVICENAME", "Услуга" },
            { "QUANTITY", "Кол-во" },
            { "SERVICE_AMOUNT", "Стоимость" },
            { "TOTAL_LINE", "Сумма" }
        },
                "GET_POSITIONS" => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "NAME", "Название" }
        },
                "GET_SERVICETYPES" => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "SERVICENAME", "Название" },
            { "UNIT_COST", "Стоимость" }
        },
                _ => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            };
        }
        private void HandleLogout()
        {
            try
            {
                // Подтверждение смены учётной записи
                var dr = MessageBox.Show(
                    this,
                    "Вы уверены что хотите сменить учётную запись?",
                    "Подтвердите действие",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);

                if (dr != DialogResult.Yes) return;

                // Закрываем текущую форму с результатом Retry
                this.DialogResult = DialogResult.Retry;
                this.Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Ошибка при смене учётной записи: {ex.Message}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private async Task LoadAvailableTablesAsync()
        {
            try
            {
                var tables = new Dictionary<string, string>
        {
            { "GET_CLIENTS", "Клиенты" },
            { "GET_EMPLOYEES", "Сотрудники" },
            { "GET_GUARDEDOBJECTS", "Охраняемые объекты" },
            { "GET_GUARDCALLS", "Вызовы" },
            { "GET_ORDERS", "Заказы" },
            { "GET_ORDER_DETAILS", "Детали заказов" },
            { "GET_POSITIONS", "Должности" },
            { "GET_SERVICETYPES", "Типы услуг" }
        };

                if (comboBox1 != null)
                {
                    comboBox1.DataSource = new BindingSource(tables.ToList(), null);
                    comboBox1.DisplayMember = "Value";
                    comboBox1.ValueMember = "Key";
                    comboBox1.SelectedIndex = -1; // Ничего не выбирать по умолчанию
                }

                if (comboBox2 != null)
                {
                    comboBox2.DataSource = new BindingSource(new List<KeyValuePair<string, string>>(tables), null);
                    comboBox2.DisplayMember = "Value";
                    comboBox2.ValueMember = "Key";
                    comboBox2.SelectedIndex = -1; // Ничего не выбирать по умолчанию
                }

                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Ошибка при загрузке списка таблиц: {ex.Message}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void PopulateCheckedListBox(CheckedListBox checkedListBox, DataGridView dgv)
        {
            try
            {
                checkedListBox.Items.Clear();

                foreach (DataGridViewColumn col in dgv.Columns)
                {
                    if (!string.IsNullOrEmpty(col.Name) &&
                        !col.Name.Contains("ID", StringComparison.OrdinalIgnoreCase))
                    {
                        // Добавляем элемент с русским заголовком
                        int index = checkedListBox.Items.Add(col.HeaderText, col.Visible);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Ошибка при заполнении списка столбцов: {ex.Message}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>
        /// Обновляет видимость столбцов в DataGridView на основе состояния CheckedListBox
        /// </summary>
        private void UpdateDataGridViewColumnsVisibility(CheckedListBox checkedListBox, DataGridView dgv)
        {
            try
            {
                var checkedItems = new HashSet<string>();
                foreach (var item in checkedListBox.CheckedItems)
                {
                    checkedItems.Add(item.ToString() ?? "");
                }

                foreach (DataGridViewColumn col in dgv.Columns)
                {
                    if (!string.IsNullOrEmpty(col.Name) &&
                        col.Name.Contains("ID", StringComparison.OrdinalIgnoreCase))
                    {
                        // Столбцы с ID всегда скрыты
                        col.Visible = false;
                    }
                    else
                    {
                        // Столбец видим только если его заголовок в списке отмеченных
                        col.Visible = checkedItems.Contains(col.HeaderText);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Ошибка при обновлении видимости столбцов: {ex.Message}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>
        /// Загружает данные выбранной таблицы в dataGridView, скрывая все колонки с ID
        /// </summary>
        private async Task LoadTableDataAsync(string tableName, DataGridView dgv)
        {
            try
            {
                if (string.IsNullOrEmpty(tableName) || dgv == null)
                    return;

                dgv.ReadOnly = true;
                dgv.AllowUserToAddRows = false;
                dgv.AllowUserToDeleteRows = false;
                dgv.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
                dgv.MultiSelect = false;
                dgv.AutoGenerateColumns = true;
                dgv.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.AllCells;

                var table = new DataTable();

                await using var conn = await FirebirdConnection.CreateOpenConnectionAsync();
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = tableName;
                cmd.CommandType = CommandType.StoredProcedure;

                await using var reader = await cmd.ExecuteReaderAsync();
                table.Load(reader);

                dgv.DataSource = table;

                // Словарь с русскими названиями столбцов для каждой таблицы
                var columnHeaders = GetColumnHeadersForTable(tableName);

                // Скрываем все колонки с "ID" и применяем русские подписи
                foreach (DataGridViewColumn col in dgv.Columns)
                {
                    if (!string.IsNullOrEmpty(col.Name))
                    {
                        // Скрываем колонки с ID
                        if (col.Name.Contains("ID", StringComparison.OrdinalIgnoreCase))
                        {
                            col.Visible = false;
                            continue;
                        }

                        if (col.Name.Contains("PHOTO", StringComparison.OrdinalIgnoreCase))
                        {
                            col.Visible = false;
                            continue;
                        }

                        if (col.Name.Contains("CONTRACT_SCAN", StringComparison.OrdinalIgnoreCase))
                        {
                            col.Visible = false;
                            continue;
                        }

                        // Применяем русский заголовок если есть в словаре
                        if (columnHeaders.TryGetValue(col.Name, out var russianHeader))
                        {
                            col.HeaderText = russianHeader;
                        }
                        else
                        {
                            // Если нет в словаре, форматируем как есть (пробелы вместо подчеркивания)
                            col.HeaderText = col.Name.Replace('_', ' ');
                        }
                    }

                    // Форматируем дата/время колонки
                    if (col.ValueType == typeof(DateTime))
                    {
                        col.DefaultCellStyle.Format = "g";
                    }

                    // Форматируем decimal колонки
                    if (col.ValueType == typeof(decimal))
                    {
                        col.DefaultCellStyle.Format = "N2";
                        col.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
                    }
                }
            }
            catch (FbException fbEx)
            {
                MessageBox.Show(this, $"Ошибка БД при загрузке таблицы '{tableName}': {fbEx.Message}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Ошибка при загрузке таблицы '{tableName}': {ex.Message}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private async void ComboBox1_SelectedIndexChanged(object? sender, EventArgs e)
        {
            try
            {
                if (comboBox1.SelectedValue is string tableName && !string.IsNullOrEmpty(tableName))
                {
                    _currentTable1Name = tableName;
                    await LoadTableDataAsync(tableName, dataGridView5);
                    PopulateCheckedListBox(checkedListBox1, dataGridView5);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Ошибка: {ex.Message}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private async void ComboBox2_SelectedIndexChanged(object? sender, EventArgs e)
        {
            try
            {
                if (comboBox2.SelectedValue is string tableName && !string.IsNullOrEmpty(tableName))
                {
                    _currentTable2Name = tableName;
                    await LoadTableDataAsync(tableName, dataGridView6);
                    PopulateCheckedListBox(checkedListBox2, dataGridView6);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Ошибка: {ex.Message}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        private async void button35_Click(object sender, EventArgs e)
        {

        }

        private void comboBox7_SelectedIndexChanged(object sender, EventArgs e)
        {

        }

        private void CheckedListBox1_ItemCheck(object? sender, ItemCheckEventArgs e)
        {
            // Используем BeginInvoke чтобы обновить видимость после изменения состояния
            this.BeginInvoke(new Action(() => UpdateDataGridViewColumnsVisibility(checkedListBox1, dataGridView5)));
        }

        private void CheckedListBox2_ItemCheck(object? sender, ItemCheckEventArgs e)
        {
            // Используем BeginInvoke чтобы обновить видимость после изменения состояния
            this.BeginInvoke(new Action(() => UpdateDataGridViewColumnsVisibility(checkedListBox2, dataGridView6)));
        }

        private void button31_Click(object sender, EventArgs e)
        {
            try
            {
                // Проверяем, выбрана ли строка в dataGridView7
                if (dataGridView7.CurrentRow == null)
                {
                    MessageBox.Show(this, "Выберите тип услуги в списке.", "Информация", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                // Разблокируем panel17
                if (panel17 == null) return;
                panel17.Enabled = true;

                // Получаем данные из выбранной строки
                var serviceId = GetRowCellString(dataGridView7.CurrentRow, "ID");
                var serviceName = GetRowCellString(dataGridView7.CurrentRow, "SERVICENAME");
                var unitCost = GetRowCellString(dataGridView7.CurrentRow, "UNIT_COST");

                // Заполняем элементы panel17
                var nameControl = FindControlRecursive(panel17, new[] { "richTextBox6", "textBox15" });
                if (nameControl is RichTextBox rtb)
                    rtb.Text = serviceName;
                else if (nameControl is TextBox tb)
                    tb.Text = serviceName;

                var costControl = FindControlRecursive(panel17, new[] { "numericUpDown2" });
                if (costControl is NumericUpDown nud && decimal.TryParse(unitCost, out var cost))
                {
                    cost = Math.Max(nud.Minimum, Math.Min(nud.Maximum, cost));
                    nud.Value = cost;
                }

                // Сохраняем ID редактируемой услуги
                _editingId = int.TryParse(serviceId, out var id) ? id : -1;

                // Переместим фокус на первый элемент
                if (panel17.Controls.Count > 0)
                    panel17.Controls[0].Focus();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Ошибка: {ex.Message}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}

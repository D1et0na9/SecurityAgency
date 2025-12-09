using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using System.Drawing;
using System.Windows.Forms.VisualStyles;

namespace SecurityAgencysApp
{
    /// <summary>
    /// Простой диалог для выбора видимых колонок DataGridView.
    /// Возвращает набор имен колонок (DataGridViewColumn.Name) которые должны быть видимы.
    /// </summary>
    public class ColumnSelectorForm : Form
    {
        private readonly CheckedListBox _checkedList;
        private readonly Button _btnApply;
        private readonly Button _btnCancel;

        public ColumnSelectorForm(IEnumerable<DataGridViewColumn> columns)
        {
            if (columns == null) throw new ArgumentNullException(nameof(columns));

            Text = "Выберите колонки";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(420, 360);

            _checkedList = new CheckedListBox
            {
                Dock = DockStyle.Top,
                Height = 300,
                CheckOnClick = true
            };

            // Добавляем элементы: текст — HeaderText (или Name). Храним в объекте CheckedListItem.
            foreach (var col in columns)
            {
                var display = string.IsNullOrEmpty(col.HeaderText) ? col.Name : $"{col.HeaderText} ({col.Name})";
                var item = new CheckedListItem(display, col.Name);
                var idx = _checkedList.Items.Add(item);
                // Устанавливаем состояние чекбокса через CheckedListBox API — это синхронизируется с UI
                _checkedList.SetItemChecked(idx, col.Visible);
            }

            // Кнопки
            _btnApply = new Button
            {
                Text = "Применить",
                DialogResult = DialogResult.OK,
                Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
                Width = 100,
                Height = 28,
                Left = ClientSize.Width - 220,
                Top = _checkedList.Bottom + 8
            };

            _btnCancel = new Button
            {
                Text = "Отмена",
                DialogResult = DialogResult.Cancel,
                Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
                Width = 100,
                Height = 28,
                Left = ClientSize.Width - 110,
                Top = _checkedList.Bottom + 8
            };

            Controls.Add(_checkedList);
            Controls.Add(_btnApply);
            Controls.Add(_btnCancel);

            AcceptButton = _btnApply;
            CancelButton = _btnCancel;
        }

        /// <summary>
        /// Возвращает имена (DataGridViewColumn.Name) выбранных колонок.
        /// Читаем состояние через CheckedListBox.GetItemChecked — это текущее состояние UI.
        /// </summary>
        public IReadOnlyList<string> SelectedColumnNames
        {
            get
            {
                var list = new List<string>();
                for (int i = 0; i < _checkedList.Items.Count; i++)
                {
                    if (_checkedList.GetItemChecked(i) && _checkedList.Items[i] is CheckedListItem cli)
                        list.Add(cli.Name);
                }
                return list;
            }
        }

        // Вспомогательный контейнер для хранения данных в элементе списка
        private class CheckedListItem
        {
            public string Text { get; }
            public string Name { get; }

            public CheckedListItem(string text, string name)
            {
                Text = text;
                Name = name;
            }

            public override string ToString() => Text;
        }
    }
}
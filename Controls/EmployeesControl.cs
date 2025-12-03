using System.Windows.Forms;

namespace SecurityAgencysApp.Controls
{
    // Пример реального контролла — создайте аналогично Controls/ClientsControl, Controls/ObjectsControl и т.д.
    public class EmployeesControl : UserControl
    {
        public EmployeesControl()
        {
            var lbl = new Label
            {
                Text = "Сотрудники",
                Dock = DockStyle.Top,
                AutoSize = true
            };
            Controls.Add(lbl);

            // Добавляйте здесь таблицу/панель/кнопки и т.д.
            Dock = DockStyle.Fill;
        }
    }
}
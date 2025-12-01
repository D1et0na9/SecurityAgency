namespace SecurityAgencysApp
{
    partial class Form1
    {
        /// <summary>
        ///  Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        ///  Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        ///  Required method for Designer support - do not modify
        ///  the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            сотрудникиToolStripMenuItem = new ToolStripMenuItem();
            заказчикиToolStripMenuItem = new ToolStripMenuItem();
            объектыToolStripMenuItem = new ToolStripMenuItem();
            вызовыToolStripMenuItem = new ToolStripMenuItem();
            отчётыToolStripMenuItem = new ToolStripMenuItem();
            учётнаяЗаписьToolStripMenuItem = new ToolStripMenuItem();
            menuStrip1 = new MenuStrip();
            сменитьToolStripMenuItem = new ToolStripMenuItem();
            выйтиToolStripMenuItem = new ToolStripMenuItem();
            menuStrip1.SuspendLayout();
            SuspendLayout();
            // 
            // сотрудникиToolStripMenuItem
            // 
            сотрудникиToolStripMenuItem.Name = "сотрудникиToolStripMenuItem";
            сотрудникиToolStripMenuItem.Size = new Size(105, 24);
            сотрудникиToolStripMenuItem.Text = "Сотрудники";
            // 
            // заказчикиToolStripMenuItem
            // 
            заказчикиToolStripMenuItem.Name = "заказчикиToolStripMenuItem";
            заказчикиToolStripMenuItem.Size = new Size(98, 24);
            заказчикиToolStripMenuItem.Text = "Заказчики ";
            // 
            // объектыToolStripMenuItem
            // 
            объектыToolStripMenuItem.Name = "объектыToolStripMenuItem";
            объектыToolStripMenuItem.Size = new Size(84, 24);
            объектыToolStripMenuItem.Text = "Объекты";
            // 
            // вызовыToolStripMenuItem
            // 
            вызовыToolStripMenuItem.Name = "вызовыToolStripMenuItem";
            вызовыToolStripMenuItem.Size = new Size(78, 24);
            вызовыToolStripMenuItem.Text = "Вызовы";
            // 
            // отчётыToolStripMenuItem
            // 
            отчётыToolStripMenuItem.Name = "отчётыToolStripMenuItem";
            отчётыToolStripMenuItem.Size = new Size(73, 24);
            отчётыToolStripMenuItem.Text = "Отчёты";
            // 
            // учётнаяЗаписьToolStripMenuItem
            // 
            учётнаяЗаписьToolStripMenuItem.Alignment = ToolStripItemAlignment.Right;
            учётнаяЗаписьToolStripMenuItem.DropDownItems.AddRange(new ToolStripItem[] { сменитьToolStripMenuItem, выйтиToolStripMenuItem });
            учётнаяЗаписьToolStripMenuItem.Name = "учётнаяЗаписьToolStripMenuItem";
            учётнаяЗаписьToolStripMenuItem.Size = new Size(131, 24);
            учётнаяЗаписьToolStripMenuItem.Text = "Учётная запись";
            // 
            // menuStrip1
            // 
            menuStrip1.ImageScalingSize = new Size(20, 20);
            menuStrip1.Items.AddRange(new ToolStripItem[] { сотрудникиToolStripMenuItem, заказчикиToolStripMenuItem, объектыToolStripMenuItem, вызовыToolStripMenuItem, отчётыToolStripMenuItem, учётнаяЗаписьToolStripMenuItem });
            menuStrip1.Location = new Point(0, 0);
            menuStrip1.Name = "menuStrip1";
            menuStrip1.Size = new Size(932, 28);
            menuStrip1.TabIndex = 0;
            menuStrip1.Text = "menuStrip1";
            // 
            // сменитьToolStripMenuItem
            // 
            сменитьToolStripMenuItem.Name = "сменитьToolStripMenuItem";
            сменитьToolStripMenuItem.Size = new Size(224, 26);
            сменитьToolStripMenuItem.Text = "Сменить ";
            // 
            // выйтиToolStripMenuItem
            // 
            выйтиToolStripMenuItem.Name = "выйтиToolStripMenuItem";
            выйтиToolStripMenuItem.Size = new Size(224, 26);
            выйтиToolStripMenuItem.Text = "Выйти";
            // 
            // Form1
            // 
            AutoScaleDimensions = new SizeF(8F, 20F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(932, 526);
            Controls.Add(menuStrip1);
            MainMenuStrip = menuStrip1;
            Name = "Form1";
            Text = "АИС Охранного агентства \"Щит\"";
            menuStrip1.ResumeLayout(false);
            menuStrip1.PerformLayout();
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private ToolStripMenuItem сотрудникиToolStripMenuItem;
        private ToolStripMenuItem заказчикиToolStripMenuItem;
        private ToolStripMenuItem объектыToolStripMenuItem;
        private ToolStripMenuItem вызовыToolStripMenuItem;
        private ToolStripMenuItem отчётыToolStripMenuItem;
        private ToolStripMenuItem учётнаяЗаписьToolStripMenuItem;
        private MenuStrip menuStrip1;
        private ToolStripMenuItem сменитьToolStripMenuItem;
        private ToolStripMenuItem выйтиToolStripMenuItem;
    }
}

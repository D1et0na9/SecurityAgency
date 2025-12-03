using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace SecurityAgencysApp
{
    public partial class Form1 : Form
    {
        private readonly Dictionary<string, UserControl> _views = new();
        private UserControl? _currentView;

        public Form1()
        {
            InitializeComponent();

        }

    }
}

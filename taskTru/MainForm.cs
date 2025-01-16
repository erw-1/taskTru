using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace taskTru
{
    /// <summary>
    /// The main UI form for the taskTru application.
    /// Enumerates open windows, shows them as rows,
    /// and allows toggling click-through, topmost, and opacity.
    /// </summary>
    public partial class MainForm : Form
    {
        // Dictionary from window handle to row controls
        private readonly Dictionary<IntPtr, WindowControls> _windowsData = new Dictionary<IntPtr, WindowControls>();

        // FlowLayoutPanel that stacks each row
        private readonly FlowLayoutPanel _containerPanel;

        // Row index used to alternate background colors
        private int _rowIndex;

        // A "Refresh" button to manually re-enumerate windows
        private Button? _refreshButton;

        /// <summary>
        /// MainForm constructor - sets up dark theme, auto-sizing,
        /// and initializes UI elements.
        /// </summary>
        public MainForm()
        {
            InitializeComponent();

            // Dark theme
            BackColor = Color.FromArgb(30, 30, 30);
            ForeColor = Color.White;
            Text = "taskTru";
            TopMost = true;

            // Auto-size the form around its contents
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;

            // Create the FlowLayoutPanel (no fill docking, so it can shrink)
            _containerPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.None,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                AutoScroll = false,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                BackColor = BackColor,
                Location = new Point(0, 0) // top-left
            };
            Controls.Add(_containerPanel);

            // Create a Refresh button
            _refreshButton = new Button
            {
                Text = "Refresh",
                AutoSize = true,
                ForeColor = Color.White,
                BackColor = Color.FromArgb(60, 60, 60),
                Margin = new Padding(5),
            };
            // When clicked, we re-populate the window list
            _refreshButton.Click += RefreshButton_Click;
            // We'll place this at the top of the FlowLayoutPanel
            _containerPanel.Controls.Add(_refreshButton);

            // Enumerate windows once at startup
            RefreshWindowList();
        }

        /// <summary>
        /// Handles the "Refresh" button click, re-enumerating windows.
        /// </summary>
        private void RefreshButton_Click(object? sender, EventArgs e)
        {
            RefreshWindowList();
        }

        /// <summary>
        /// Clears the container and enumerates the current open windows, building rows for each.
        /// </summary>
        private void RefreshWindowList()
        {
            _containerPanel.SuspendLayout();
            try
            {
                // Remove any old rows except the refresh button (which is at index 0)
                // If you want to remove the button, use .Clear() and re-add it afterward
                while (_containerPanel.Controls.Count > 1)
                {
                    _containerPanel.Controls.RemoveAt(1);
                }

                _windowsData.Clear();
                _rowIndex = 0;

                // Grab the current windows
                var allWindows = WindowManager.EnumerateWindows();

                // Exclude the MainForm’s own window handle
                allWindows.RemoveAll(w => w.Handle == this.Handle);

                foreach (var w in allWindows)
                {
                    var data = new WindowControls { Handle = w.Handle };
                    var rowPanel = CreateWindowRow(w.Title, data, _rowIndex);

                    _containerPanel.Controls.Add(rowPanel);
                    _windowsData[w.Handle] = data;
                    _rowIndex++;
                }
            }
            finally
            {
                _containerPanel.ResumeLayout();
            }
        }

        /// <summary>
        /// Creates a single row (TableLayoutPanel) for a given window.
        /// </summary>
        private TableLayoutPanel CreateWindowRow(string windowTitle, WindowControls data, int rowIndex)
        {
            // Alternate row colors
            Color rowColor = (rowIndex % 2 == 0)
                ? Color.FromArgb(45, 45, 45)
                : Color.FromArgb(55, 55, 55);

            var rowPanel = new TableLayoutPanel
            {
                ColumnCount = 6,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Top,
                Padding = new Padding(5),
                BackColor = rowColor,
                Margin = new Padding(0, 0, 0, 5),
                RowCount = 1
            };
            rowPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            // Title column
            rowPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 250F));
            var titleLabel = new Label
            {
                Text = windowTitle,
                AutoSize = true,
                ForeColor = Color.White,
                BackColor = Color.Transparent,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft
            };
            rowPanel.Controls.Add(titleLabel, 0, 0);

            // Click-Through
            rowPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            var clickThroughCheck = new CheckBox
            {
                Text = "Click-Through",
                AutoSize = true,
                ForeColor = Color.White,
                BackColor = Color.Transparent,
                Margin = new Padding(10, 0, 10, 0),
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter
            };
            clickThroughCheck.CheckedChanged += ClickThroughCheck_CheckedChanged;
            clickThroughCheck.Tag = data;
            data.ClickThrough = clickThroughCheck;
            rowPanel.Controls.Add(clickThroughCheck, 1, 0);

            // Lock On Top
            rowPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            var topMostCheck = new CheckBox
            {
                Text = "Lock On Top",
                AutoSize = true,
                ForeColor = Color.White,
                BackColor = Color.Transparent,
                Margin = new Padding(10, 0, 10, 0),
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter
            };
            topMostCheck.CheckedChanged += TopMostCheck_CheckedChanged;
            topMostCheck.Tag = data;
            data.TopMost = topMostCheck;
            rowPanel.Controls.Add(topMostCheck, 2, 0);

            // "Opacity:" label
            rowPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            var opacityLabel = new Label
            {
                Text = "Opacity:",
                AutoSize = true,
                ForeColor = Color.White,
                BackColor = Color.Transparent,
                Margin = new Padding(5, 0, 5, 0),
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleRight
            };
            rowPanel.Controls.Add(opacityLabel, 3, 0);

            // TrackBar
            rowPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100F));
            var opacityTrack = new TrackBar
            {
                Minimum = 0,
                Maximum = 100,
                Value = 100,
                TickFrequency = 10,
                SmallChange = 5,
                LargeChange = 10,
                BackColor = rowColor,

                AutoSize = false,
                Width = 100,
                Height = 24,
                TickStyle = TickStyle.None,
                Anchor = AnchorStyles.None
            };
            opacityTrack.Scroll += OpacityTrack_Scroll;
            opacityTrack.Tag = data;
            data.OpacityTrack = opacityTrack;
            rowPanel.Controls.Add(opacityTrack, 4, 0);

            // "100%" label
            rowPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            var opacityValueLabel = new Label
            {
                Text = "100%",
                AutoSize = true,
                ForeColor = Color.White,
                BackColor = Color.Transparent,
                Margin = new Padding(5, 0, 5, 0),
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft
            };
            data.OpacityValueLabel = opacityValueLabel;
            rowPanel.Controls.Add(opacityValueLabel, 5, 0);

            return rowPanel;
        }

        // =============== Event Handlers ===============

        private void ClickThroughCheck_CheckedChanged(object? sender, EventArgs e)
        {
            if (sender is CheckBox cb && cb.Tag is WindowControls data)
            {
                WindowManager.ToggleClickThrough(data.Handle, cb.Checked);
            }
        }

        private void TopMostCheck_CheckedChanged(object? sender, EventArgs e)
        {
            if (sender is CheckBox cb && cb.Tag is WindowControls data)
            {
                WindowManager.ToggleTopMost(data.Handle, cb.Checked);
            }
        }

        private void OpacityTrack_Scroll(object? sender, EventArgs e)
        {
            if (sender is TrackBar tb && tb.Tag is WindowControls data)
            {
                WindowManager.SetWindowOpacity(data.Handle, tb.Value);
                if (data.OpacityValueLabel != null)
                {
                    data.OpacityValueLabel.Text = $"{tb.Value}%";
                }
            }
        }
    }
}

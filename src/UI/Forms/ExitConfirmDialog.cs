using System.Drawing;

namespace taskTru;

internal sealed class ExitConfirmDialog : Form
{
    private readonly CheckBox _neverAskAgain;

    public bool NeverAskAgain => _neverAskAgain.Checked;

    public ExitConfirmDialog(string taskText)
    {
        Text = "Exit taskTru?";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        MaximizeBox = false;
        MinimizeBox = false;
        TopMost = true;
        BackColor = UiTheme.AppBackground;
        ForeColor = Color.White;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new(96f, 96f);
        Font = new("Segoe UI", 9f);
        ClientSize = new(340, 132);

        var message = new Label
        {
            Text = $"taskTru has {taskText}. Exit now?",
            AutoSize = false,
            Bounds = new(16, 14, 308, 40),
            ForeColor = Color.White,
            TextAlign = ContentAlignment.TopLeft
        };
        _neverAskAgain = new()
        {
            Text = "Never show this again",
            AutoSize = true,
            Location = new(16, 58),
            ForeColor = UiTheme.SecondaryText,
            Cursor = Cursors.Hand
        };
        var exit = new RoundedActionButton
        {
            Text = "Exit",
            Size = new(76, 28),
            Location = new(164, 90),
            BorderColor = UiTheme.Accent,
            BorderWidth = 2
        };
        exit.Click += (_, _) =>
        {
            DialogResult = DialogResult.Yes;
            Close();
        };
        var cancel = new RoundedActionButton
        {
            Text = "Cancel",
            Size = new(76, 28),
            Location = new(248, 90)
        };
        cancel.Click += (_, _) =>
        {
            DialogResult = DialogResult.No;
            Close();
        };

        Controls.Add(message);
        Controls.Add(_neverAskAgain);
        Controls.Add(exit);
        Controls.Add(cancel);
        AcceptButton = cancel;
        CancelButton = cancel;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        WindowTheme.Apply(Handle);
    }
}

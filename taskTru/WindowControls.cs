using System.Windows.Forms;

namespace taskTru
{
    /// <summary>
    /// Holds references to each row’s controls in the MainForm UI.
    /// Because we create these after the object is constructed,
    /// we mark them as nullable (?).
    /// </summary>
    internal sealed class WindowControls
    {
        public IntPtr Handle;

        public CheckBox? ClickThrough;
        public CheckBox? TopMost;
        public TrackBar? OpacityTrack;
        public Label? OpacityValueLabel;
    }
}

namespace taskTru;

internal static class Program
{
    private const string SingleInstanceMutexName =
        @"Local\taskTru.SingleInstance";
    private const string ActivationEventName =
        @"Local\taskTru.Activate";

    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        using var activationEvent = new EventWaitHandle(
            initialState: false,
            EventResetMode.AutoReset,
            ActivationEventName);
        using var instanceMutex = new Mutex(
            initiallyOwned: false,
            SingleInstanceMutexName);
        bool ownsMutex;
        try
        {
            ownsMutex = instanceMutex.WaitOne(
                TimeSpan.Zero,
                exitContext: false);
        }
        catch (AbandonedMutexException)
        {
            ownsMutex = true;
        }

        if (!ownsMutex)
        {
            activationEvent.Set();
            FocusRunningInstance();
            return;
        }

        try
        {
            using var mainForm = new MainForm(
                args.Any(argument =>
                    argument.Equals(
                        "--minimized",
                        StringComparison.OrdinalIgnoreCase)),
                activationEvent);
            Application.Run(mainForm);
        }
        finally
        {
            instanceMutex.ReleaseMutex();
        }
    }

    private static void FocusRunningInstance()
    {
        for (int attempt = 0; attempt < 20; attempt++)
        {
            nint window = NativeMethods.FindWindow(
                className: null,
                windowName: "taskTru");
            if (window != 0)
            {
                FocusWindow(window);
                return;
            }

            Thread.Sleep(50);
        }
    }

    private static void FocusWindow(nint window)
    {
        uint currentThread = NativeMethods.GetCurrentThreadId();
        uint foregroundThread = NativeMethods.GetWindowThreadProcessId(
            NativeMethods.GetForegroundWindow(),
            out _);
        bool attached = foregroundThread != 0
            && foregroundThread != currentThread
            && NativeMethods.AttachThreadInput(
                currentThread,
                foregroundThread,
                attach: true);

        try
        {
            NativeMethods.ShowWindow(
                window,
                NativeMethods.ShowWindowCommand.Restore);
            NativeMethods.BringWindowToTop(window);
            NativeMethods.SetForegroundWindow(window);
            NativeMethods.SetFocus(window);
        }
        finally
        {
            if (attached)
            {
                NativeMethods.AttachThreadInput(
                    currentThread,
                    foregroundThread,
                    attach: false);
            }
        }
    }
}

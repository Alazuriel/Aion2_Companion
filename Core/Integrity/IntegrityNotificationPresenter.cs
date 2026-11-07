using AION_2_Companion.Views.Overlays;
using Avalonia.Threading;

namespace AION_2_Companion.Core.Integrity;

public static class IntegrityNotificationPresenter
{
    private static IntegrityToastWindow? _toast;
    private static IntegrityMonitorState? _lastShown;

    public static void ShowIfNeeded(IntegrityMonitorSnapshot snapshot)
    {
        if (snapshot.State is not (IntegrityMonitorState.ProblemDetected or
            IntegrityMonitorState.RepairReady or IntegrityMonitorState.RepairFailed) ||
            _lastShown == snapshot.State)
            return;

        _lastShown = snapshot.State;
        Dispatcher.UIThread.Post(() =>
        {
            _toast?.Close();
            var created = new IntegrityToastWindow();
            created.Closed += (_, _) =>
            {
                if (ReferenceEquals(_toast, created))
                    _toast = null;
            };
            _toast = created;
            created.ShowStatus(snapshot.State);
            created.Show();
        });
    }
}

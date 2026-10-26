namespace AION_2_Companion.Core.EventTimers;

using System.Media;
using System.Collections.Generic;
using AION_2_Companion.Views.Overlays;
using Avalonia.Threading;

public static class EventTimerNotificationPresenter
{
    private static EventTimerToastWindow? _toast;
    private static readonly Queue<EventTimerNotification> Pending = new();

    public static void Show(EventTimerNotification notification)
    {
        Dispatcher.UIThread.Post(() =>
        {
            Pending.Enqueue(notification);
            ShowNext();
        });
    }

    private static void ShowNext()
    {
        if (_toast is not null || Pending.Count == 0)
            return;

        EventTimerNotification notification = Pending.Dequeue();
        if (notification.SoundEnabled)
        {
            try { SystemSounds.Asterisk.Play(); }
            catch { /* A missing Windows sound must not break timer notifications. */ }
        }

        if (!notification.VisualEnabled)
        {
            ShowNext();
            return;
        }

        var created = new EventTimerToastWindow();
        created.Closed += (_, _) =>
        {
            if (ReferenceEquals(_toast, created))
                _toast = null;
            ShowNext();
        };
        _toast = created;
        created.ShowNotification(notification);
        created.Show();
    }
}

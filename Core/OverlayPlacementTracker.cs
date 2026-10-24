using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace AION_2_Companion.Core;

public static class OverlayPlacementTracker
{
    public static void Attach(Window window, string key, string? positionFallbackKey = null)
    {
        _ = new Tracker(window, key, positionFallbackKey);
    }

    private sealed class Tracker
    {
        private readonly Window _window;
        private readonly string _key;
        private readonly string? _positionFallbackKey;
        private readonly DispatcherTimer _saveTimer;
        private bool _opened;

        public Tracker(Window window, string key, string? positionFallbackKey)
        {
            _window = window;
            _key = key;
            _positionFallbackKey = positionFallbackKey;
            _saveTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(500),
            };
            _saveTimer.Tick += SaveTimerTick;

            _window.Opened += WindowOpened;
            _window.PositionChanged += WindowPositionChanged;
            _window.Resized += WindowResized;
            _window.Closing += WindowClosing;
        }

        private void WindowOpened(object? sender, EventArgs e)
        {
            Restore();
            _opened = true;
        }

        private void WindowPositionChanged(object? sender, PixelPointEventArgs e) => ScheduleSave();

        private void WindowResized(object? sender, WindowResizedEventArgs e) => ScheduleSave();

        private void WindowClosing(object? sender, WindowClosingEventArgs e)
        {
            _saveTimer.Stop();
            Save();
        }

        private void ScheduleSave()
        {
            if (!_opened)
                return;

            _saveTimer.Stop();
            _saveTimer.Start();
        }

        private void SaveTimerTick(object? sender, EventArgs e)
        {
            _saveTimer.Stop();
            Save();
        }

        private void Restore()
        {
            OverlayPlacement? placement = CompanionSettings.GetOverlayPlacement(_key);
            bool restoreSize = placement is not null;

            // Egy uj overlay-layout atveheti a regi poziciot anelkul, hogy a
            // regi, mar nem megfelelo meretet is magaval hozna.
            if (placement is null && !string.IsNullOrWhiteSpace(_positionFallbackKey))
                placement = CompanionSettings.GetOverlayPlacement(_positionFallbackKey);

            if (placement is null)
                return;

            if (restoreSize && double.IsFinite(placement.Width) && placement.Width >= _window.MinWidth)
                _window.Width = placement.Width;

            if (restoreSize && double.IsFinite(placement.Height) && placement.Height >= _window.MinHeight)
                _window.Height = placement.Height;

            var position = new PixelPoint(placement.X, placement.Y);
            if (_window.Screens.ScreenFromPoint(position) is null)
                return;

            _window.WindowStartupLocation = WindowStartupLocation.Manual;
            _window.Position = position;
        }

        private void Save()
        {
            if (!_opened)
                return;

            CompanionSettings.SetOverlayPlacement(_key, new OverlayPlacement
            {
                X = _window.Position.X,
                Y = _window.Position.Y,
                Width = _window.Bounds.Width,
                Height = _window.Bounds.Height,
            });
        }
    }
}

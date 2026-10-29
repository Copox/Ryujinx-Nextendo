using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Ryujinx.Input;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using MouseButton = Ryujinx.Input.MouseButton;
using PointerMouseButton = Avalonia.Input.MouseButton;
using Size = System.Drawing.Size;

namespace Ryujinx.Ava.Input
{
    internal class AvaloniaMouseDriver : IGamepadDriver
    {
        private const int ScrollTimerIntervalMilliseconds = 50;

        private Control _widget;
        private bool _isDisposed;
        private Size _size;
        private readonly TopLevel _window;
        private DispatcherTimer _scrollStopTimer;
        private int _isTouchPressed;
        private int _pendingTouchPress;
        private int _pendingLeftMousePress;

        public bool[] PressedButtons { get; }
        public bool IsTouchPressed =>
            Volatile.Read(ref _isTouchPressed) != 0 || Interlocked.Exchange(ref _pendingTouchPress, 0) != 0;
        public Vector2 CurrentPosition { get; private set; }
        public Vector2 Scroll { get; private set; }

        public string DriverName => "AvaloniaMouseDriver";
        public ReadOnlySpan<string> GamepadsIds => new[] { "0" };

        public AvaloniaMouseDriver(TopLevel window, Control parent)
        {
            _widget = parent;
            _window = window;

            // SteamOS/Wayland controls may mark touch-generated pointer events as handled
            // before they bubble to the emulator surface. Observe handled events so the
            // guest touchscreen receives taps even when Avalonia UI controls consume them.
            _widget.AddHandler(InputElement.PointerMovedEvent, Parent_PointerMovedEvent, RoutingStrategies.Bubble, handledEventsToo: true);
            _widget.AddHandler(InputElement.PointerPressedEvent, Parent_PointerPressedEvent, RoutingStrategies.Bubble, handledEventsToo: true);
            _widget.AddHandler(InputElement.PointerReleasedEvent, Parent_PointerReleasedEvent, RoutingStrategies.Bubble, handledEventsToo: true);
            _widget.AddHandler(InputElement.PointerWheelChangedEvent, Parent_PointerWheelChanged, RoutingStrategies.Bubble, handledEventsToo: true);

            _window.AddHandler(InputElement.PointerMovedEvent, Parent_PointerMovedEvent, RoutingStrategies.Bubble, handledEventsToo: true);
            _window.AddHandler(InputElement.PointerPressedEvent, Parent_PointerPressedEvent, RoutingStrategies.Bubble, handledEventsToo: true);
            _window.AddHandler(InputElement.PointerReleasedEvent, Parent_PointerReleasedEvent, RoutingStrategies.Bubble, handledEventsToo: true);
            _window.AddHandler(InputElement.PointerWheelChangedEvent, Parent_PointerWheelChanged, RoutingStrategies.Bubble, handledEventsToo: true);

            _scrollStopTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(ScrollTimerIntervalMilliseconds)
            };

            PressedButtons = new bool[(int)MouseButton.Count];

            _size = new Size((int)parent.Bounds.Width, (int)parent.Bounds.Height);

            parent.GetObservable(Visual.BoundsProperty).Subscribe(Resized);
        }

        public event Action<string> OnGamepadConnected
        {
            add { }
            remove { }
        }

        public event Action<string> OnGamepadDisconnected
        {
            add { }
            remove { }
        }

        private void Resized(Rect rect)
        {
            _size = new Size((int)rect.Width, (int)rect.Height);
        }

        private void HandleScrollStopped()
        {
            Scroll = new Vector2(0, 0);
        }

        private void Parent_PointerWheelChanged(object o, PointerWheelEventArgs args)
        {
            Scroll = new Vector2((float)args.Delta.X, (float)args.Delta.Y);

            _scrollStopTimer?.Stop();

            _scrollStopTimer.Tick += (_, __) =>
            {
                _scrollStopTimer.Stop();

                HandleScrollStopped();

            };
            _scrollStopTimer.Start();
        }

        private void Parent_PointerReleasedEvent(object o, PointerReleasedEventArgs args)
        {
            if (args.Pointer.Type == PointerType.Touch)
            {
                Volatile.Write(ref _isTouchPressed, 0);
            }

            MouseButton? button = args.Pointer.Type == PointerType.Touch
                ? MouseButton.Button1
                : MapMouseButton(args.InitialPressMouseButton);
            if (button.HasValue)
            {
                PressedButtons[(int)button.Value] = false;
            }
        }

        private void Parent_PointerPressedEvent(object o, PointerPressedEventArgs args)
        {
            PointerPoint currentPoint = args.GetCurrentPoint(_widget);
            if (args.Pointer.Type == PointerType.Touch)
            {
                Volatile.Write(ref _isTouchPressed, 1);
                Interlocked.Exchange(ref _pendingTouchPress, 1);
            }
            MouseButton? button = args.Pointer.Type == PointerType.Touch
                ? MouseButton.Button1
                : MapPointerUpdateKind(currentPoint.Properties.PointerUpdateKind);

            // SteamOS Game Mode can synthesize touchscreen taps as mouse pointer
            // presses while leaving PointerUpdateKind and button properties empty.
            // Treat an otherwise-unclassified press as left-click; AppHost routes it
            // to the guest touchscreen when Direct Mouse Access is disabled.
            if (!button.HasValue && args.Pointer.Type != PointerType.Touch)
            {
                button = MouseButton.Button1;
            }

            if (button.HasValue)
            {
                PressedButtons[(int)button.Value] = true;

                if (args.Pointer.Type != PointerType.Touch && button == MouseButton.Button1)
                {
                    Interlocked.Exchange(ref _pendingLeftMousePress, 1);
                }
            }

            // Capture the press location even when SteamOS synthesizes a tap as a mouse press.
            CurrentPosition = new Vector2((float)currentPoint.Position.X, (float)currentPoint.Position.Y);
        }

        private static MouseButton? MapPointerUpdateKind(PointerUpdateKind kind) => kind switch
        {
            PointerUpdateKind.LeftButtonPressed => MouseButton.Button1,
            PointerUpdateKind.RightButtonPressed => MouseButton.Button2,
            PointerUpdateKind.MiddleButtonPressed => MouseButton.Button3,
            PointerUpdateKind.XButton1Pressed => MouseButton.Button4,
            PointerUpdateKind.XButton2Pressed => MouseButton.Button5,
            _ => null,
        };

        private static MouseButton? MapMouseButton(PointerMouseButton button) => button switch
        {
            PointerMouseButton.Left => MouseButton.Button1,
            PointerMouseButton.Right => MouseButton.Button2,
            PointerMouseButton.Middle => MouseButton.Button3,
            PointerMouseButton.XButton1 => MouseButton.Button4,
            PointerMouseButton.XButton2 => MouseButton.Button5,
            _ => null,
        };

        private void Parent_PointerMovedEvent(object o, PointerEventArgs args)
        {
            Point position = args.GetPosition(_widget);

            CurrentPosition = new Vector2((float)position.X, (float)position.Y);
        }

        public void SetMousePressed(MouseButton button)
        {
            if ((uint)PressedButtons.Length > (uint)button)
            {
                PressedButtons[(uint)button] = true;
            }
        }

        public void SetMouseReleased(MouseButton button)
        {
            if ((uint)PressedButtons.Length > (uint)button)
            {
                PressedButtons[(uint)button] = false;
            }
        }

        public void SetPosition(double x, double y)
        {
            CurrentPosition = new Vector2((float)x, (float)y);
        }

        public bool ConsumeLeftMousePress()
        {
            return IsButtonPressed(MouseButton.Button1) || Interlocked.Exchange(ref _pendingLeftMousePress, 0) != 0;
        }

        public bool IsButtonPressed(MouseButton button)
        {
            if ((uint)PressedButtons.Length > (uint)button)
            {
                return PressedButtons[(uint)button];
            }

            return false;
        }

        public Size GetClientSize()
        {
            return _size;
        }

        public IGamepad GetGamepad(string id)
        {
            return new AvaloniaMouse(this);
        }

        public IEnumerable<IGamepad> GetGamepads() => [GetGamepad("0")];

        public void Dispose()
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;

            _widget.RemoveHandler(InputElement.PointerMovedEvent, Parent_PointerMovedEvent);
            _widget.RemoveHandler(InputElement.PointerPressedEvent, Parent_PointerPressedEvent);
            _widget.RemoveHandler(InputElement.PointerReleasedEvent, Parent_PointerReleasedEvent);
            _widget.RemoveHandler(InputElement.PointerWheelChangedEvent, Parent_PointerWheelChanged);

            _window.RemoveHandler(InputElement.PointerMovedEvent, Parent_PointerMovedEvent);
            _window.RemoveHandler(InputElement.PointerPressedEvent, Parent_PointerPressedEvent);
            _window.RemoveHandler(InputElement.PointerReleasedEvent, Parent_PointerReleasedEvent);
            _window.RemoveHandler(InputElement.PointerWheelChangedEvent, Parent_PointerWheelChanged);

            _widget = null;
        }
    }
}

using Ryujinx.Common.Logging;
using Ryujinx.HLE.HOS.Services.Hid;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;

namespace Ryujinx.Input.HLE
{
    // Development aid: presses buttons at given times so a menu can be driven without a person
    // at the keyboard. NEXTENDO_INPUT_SCRIPT="18:A;20.5:DpadUp;22:A;40:Home" (seconds since the
    // first input update, then a ControllerKeys name, or Home for the console's HOME button).
    // Each press is held for a fifth of a second.
    public static class ScriptedInput
    {
        private const double HoldSeconds = 0.2;

        private static readonly List<double> _homeTimes = [];
        private static readonly List<(double Time, uint X, uint Y)> _touches = [];
        private static readonly List<(double Time, int X, int Y, double Hold)> _sticks = [];
        private static readonly List<(double Time, ControllerKeys Keys, double Hold)> _steps = Load();
        private static readonly Stopwatch _clock = new();

        private static ControllerKeys _last;

        private static List<(double, ControllerKeys, double)> Load()
        {
            List<(double, ControllerKeys, double)> steps = [];
            string script = Environment.GetEnvironmentVariable("NEXTENDO_INPUT_SCRIPT");

            if (string.IsNullOrEmpty(script))
            {
                return steps;
            }

            foreach (string step in script.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                string[] parts = step.Split(':');

                if (parts.Length < 2 || !double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double time))
                {
                    continue;
                }

                if (parts[1].Equals("LStick", StringComparison.OrdinalIgnoreCase) && parts.Length >= 4 &&
                    int.TryParse(parts[2], out int stickX) && int.TryParse(parts[3], out int stickY))
                {
                    // "t:LStick:x:y[:hold]": the left stick of the first pad held there (-32767..32767).
                    _sticks.Add((time, stickX, stickY, parts.Length > 4 && double.TryParse(parts[4], NumberStyles.Float, CultureInfo.InvariantCulture, out double sh) ? sh : 0.6));
                }
                else if (parts[1].Equals("Touch", StringComparison.OrdinalIgnoreCase) && parts.Length == 4 &&
                    uint.TryParse(parts[2], out uint touchX) && uint.TryParse(parts[3], out uint touchY))
                {
                    // "t:Touch:x:y": a tap on the 1280x720 screen.
                    _touches.Add((time, touchX, touchY));
                }
                else if (parts[1].Equals("Home", StringComparison.OrdinalIgnoreCase))
                {
                    _homeTimes.Add(time);
                }
                else if (parts[1].Equals("Dock", StringComparison.OrdinalIgnoreCase) || parts[1].Equals("Handheld", StringComparison.OrdinalIgnoreCase))
                {
                    // The console put in its dock or taken out of it.
                    (_dockSteps ??= []).Add((time, parts[1].Equals("Dock", StringComparison.OrdinalIgnoreCase)));
                }
                else if (Enum.TryParse(parts[1], true, out ControllerKeys keys))
                {
                    // An optional third field is how long the button stays down.
                    double hold = parts.Length > 2 && double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double h) ? h : HoldSeconds;

                    steps.Add((time, keys, hold));
                }
            }

            return steps;
        }

        // True while a scripted tap is down.
        public static bool Touch(out uint x, out uint y)
        {
            x = y = 0;

            if (_touches.Count == 0 || !_clock.IsRunning)
            {
                return false;
            }

            double now = _clock.Elapsed.TotalSeconds;

            foreach ((double time, uint touchX, uint touchY) in _touches)
            {
                if (now >= time && now < time + HoldSeconds)
                {
                    x = touchX;
                    y = touchY;

                    return true;
                }
            }

            return false;
        }

        private static List<(double Time, bool Docked)> _dockSteps;

        // The dock state of a "Dock"/"Handheld" step whose time has come, once.
        public static bool? DockStep()
        {
            if (_dockSteps == null || _dockSteps.Count == 0 || !_clock.IsRunning || _clock.Elapsed.TotalSeconds < _dockSteps[0].Time)
            {
                return null;
            }

            bool docked = _dockSteps[0].Docked;

            Logger.Notice.Print(LogClass.Hid, $"[script] {(docked ? "Dock" : "Handheld")} at {_dockSteps[0].Time:F1}s");

            _dockSteps.RemoveAt(0);

            return docked;
        }

        // True once for each "Home" step of the script whose time has come.
        public static bool HomePressed()
        {
            if (_homeTimes.Count == 0 || !_clock.IsRunning)
            {
                return false;
            }

            if (_clock.Elapsed.TotalSeconds >= _homeTimes[0])
            {
                Logger.Notice.Print(LogClass.Hid, $"[script] Home pressed at {_homeTimes[0]:F1}s");

                _homeTimes.RemoveAt(0);

                return true;
            }

            return false;
        }

        public static void Apply(List<GamepadInput> states)
        {
            if ((_steps.Count == 0 && _homeTimes.Count == 0) || states.Count == 0)
            {
                return;
            }

            if (!_clock.IsRunning)
            {
                _clock.Start();

                // Lets the driving script line its own clock up with this one.
                string mark = Environment.GetEnvironmentVariable("NEXTENDO_INPUT_MARK");

                if (!string.IsNullOrEmpty(mark))
                {
                    try
                    {
                        System.IO.File.WriteAllText(mark, "0");
                    }
                    catch (Exception)
                    {
                    }
                }
            }

            double now = _clock.Elapsed.TotalSeconds;
            ControllerKeys keys = 0;

            foreach ((double time, ControllerKeys stepKeys, double hold) in _steps)
            {
                if (now >= time && now < time + hold)
                {
                    keys |= stepKeys;
                }
            }

            // A scripted run is driven by the script alone: whatever a real pad or keyboard
            // sends at the same time (someone using the machine) is dropped.
            for (int i = 0; i < states.Count; i++)
            {
                GamepadInput idle = states[i];
                idle.Buttons = 0;
                idle.LStick = default;
                idle.RStick = default;
                states[i] = idle;
            }

            foreach ((double time, int x, int y, double hold) in _sticks)
            {
                if (now >= time && now < time + hold && states.Count > 0)
                {
                    GamepadInput moved = states[0];
                    moved.LStick = new Ryujinx.HLE.HOS.Services.Hid.JoystickPosition { Dx = x, Dy = y };
                    states[0] = moved;
                }
            }

            if (keys != 0)
            {
                GamepadInput state = states[0];
                state.Buttons = keys;
                states[0] = state;

                if (keys != _last)
                {
                    Logger.Notice.Print(LogClass.Hid, $"[script] {keys} pressed at {now:F1}s for player {state.PlayerId} ({states.Count} pads)");
                }
            }

            _last = keys;
        }
    }
}

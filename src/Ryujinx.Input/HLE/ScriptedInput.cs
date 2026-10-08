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
    static class ScriptedInput
    {
        private const double HoldSeconds = 0.2;

        private static readonly List<double> _homeTimes = [];
        private static readonly List<(double Time, ControllerKeys Keys)> _steps = Load();
        private static readonly Stopwatch _clock = new();

        private static ControllerKeys _last;

        private static List<(double, ControllerKeys)> Load()
        {
            List<(double, ControllerKeys)> steps = [];
            string script = Environment.GetEnvironmentVariable("NEXTENDO_INPUT_SCRIPT");

            if (string.IsNullOrEmpty(script))
            {
                return steps;
            }

            foreach (string step in script.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                string[] parts = step.Split(':');

                if (parts.Length != 2 || !double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double time))
                {
                    continue;
                }

                if (parts[1].Equals("Home", StringComparison.OrdinalIgnoreCase))
                {
                    _homeTimes.Add(time);
                }
                else if (Enum.TryParse(parts[1], true, out ControllerKeys keys))
                {
                    steps.Add((time, keys));
                }
            }

            return steps;
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
            }

            double now = _clock.Elapsed.TotalSeconds;
            ControllerKeys keys = 0;

            foreach ((double time, ControllerKeys stepKeys) in _steps)
            {
                if (now >= time && now < time + HoldSeconds)
                {
                    keys |= stepKeys;
                }
            }

            if (keys != 0)
            {
                GamepadInput state = states[0];
                state.Buttons |= keys;
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

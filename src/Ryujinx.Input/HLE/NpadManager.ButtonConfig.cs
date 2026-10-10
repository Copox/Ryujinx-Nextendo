using Ryujinx.Common.Configuration.Hid;
using Ryujinx.Common.Configuration.Hid.Controller;
using Ryujinx.Common.Configuration.Hid.Keyboard;
using Ryujinx.HLE.HOS.Services.Hid;
using System;

namespace Ryujinx.Input.HLE
{
    // [Nextendo, HOME Menu work] "Change button mapping" of the HOME Menu reads and writes the
    // input configuration of the emulator itself: what the HOME Menu shows is what the input
    // settings of Ryujinx show, and a mapping changed in one is changed in the other.
    //
    // The HOME Menu speaks of the buttons of a Pro Controller: the "hardware" button X of a
    // controller is the input the default configuration of that controller puts on X (on an Xbox
    // pad, the button in the place of A is its B). The mapping is then: that input, in the
    // current configuration, gives which button.
    public partial class NpadManager
    {
        // The entries of nn::hid HidcfgButtonConfigFull, in order.
        private enum Slot { Left, Up, Right, Down, A, B, X, Y, StickL, StickR, L, R, Zl, Zr, Minus, Plus }

        private const int Slots = 16;

        // HidcfgDigitalButtonAssignment of each button.
        private static readonly uint[] _assignment = [12, 13, 14, 15, 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11];
        private const uint Invalid = 22;

        /// <summary>Raised when the HOME Menu changed the input configuration (to be saved).</summary>
        public static event Action ButtonConfigChanged;

        private void HookButtonConfig()
        {
            NextendoButtonBridge.Read = ReadButtonConfig;
            NextendoButtonBridge.Write = WriteButtonConfig;
        }

        private InputConfig ActiveConfig(int player)
        {
            foreach (InputConfig config in _inputConfig)
            {
                if ((int)config.PlayerIndex == player)
                {
                    return config;
                }
            }

            return null;
        }

        private static InputConfig DefaultFor(InputConfig config)
        {
            return config switch
            {
                StandardKeyboardInputConfig => InputConfigDefaults.CreateDefaultKeyboardConfiguration(config.Id, config.Name, config.ControllerType, config.PlayerIndex),
                StandardControllerInputConfig => InputConfigDefaults.CreateDefaultControllerConfiguration(config.Id, config.Name, config.ControllerType, config.PlayerIndex, IsNintendoStyle(config)),
                _ => null,
            };
        }

        private static bool IsNintendoStyle(InputConfig config)
        {
            string name = config.Name ?? "";

            return name.Contains("Nintendo", StringComparison.OrdinalIgnoreCase) || name.Contains("Pro Controller", StringComparison.OrdinalIgnoreCase)
                || name.Contains("Joy-Con", StringComparison.OrdinalIgnoreCase);
        }

        private static int[] Inputs(InputConfig config)
        {
            return config switch
            {
                StandardKeyboardInputConfig k => Inputs(k, k.LeftJoyconStick.StickButton, k.RightJoyconStick.StickButton),
                StandardControllerInputConfig c => Inputs(c, c.LeftJoyconStick.StickButton, c.RightJoyconStick.StickButton),
                _ => null,
            };
        }

        private static int[] Inputs<T>(GenericInputConfigurationCommon<T> c, T stickL, T stickR) where T : unmanaged, Enum
        {
            T[] v =
            [
                c.LeftJoycon.DpadLeft, c.LeftJoycon.DpadUp, c.LeftJoycon.DpadRight, c.LeftJoycon.DpadDown,
                c.RightJoycon.ButtonA, c.RightJoycon.ButtonB, c.RightJoycon.ButtonX, c.RightJoycon.ButtonY,
                stickL, stickR, c.LeftJoycon.ButtonL, c.RightJoycon.ButtonR,
                c.LeftJoycon.ButtonZl, c.RightJoycon.ButtonZr, c.LeftJoycon.ButtonMinus, c.RightJoycon.ButtonPlus,
            ];

            return Array.ConvertAll(v, x => Convert.ToInt32(x));
        }

        private static void Assign<T>(GenericInputConfigurationCommon<T> c, Action<T> stickL, Action<T> stickR, int[] inputs) where T : unmanaged, Enum
        {
            T V(Slot s) => (T)Enum.ToObject(typeof(T), inputs[(int)s]);

            c.LeftJoycon.DpadLeft = V(Slot.Left);
            c.LeftJoycon.DpadUp = V(Slot.Up);
            c.LeftJoycon.DpadRight = V(Slot.Right);
            c.LeftJoycon.DpadDown = V(Slot.Down);
            c.RightJoycon.ButtonA = V(Slot.A);
            c.RightJoycon.ButtonB = V(Slot.B);
            c.RightJoycon.ButtonX = V(Slot.X);
            c.RightJoycon.ButtonY = V(Slot.Y);
            stickL(V(Slot.StickL));
            stickR(V(Slot.StickR));
            c.LeftJoycon.ButtonL = V(Slot.L);
            c.RightJoycon.ButtonR = V(Slot.R);
            c.LeftJoycon.ButtonZl = V(Slot.Zl);
            c.RightJoycon.ButtonZr = V(Slot.Zr);
            c.LeftJoycon.ButtonMinus = V(Slot.Minus);
            c.RightJoycon.ButtonPlus = V(Slot.Plus);
        }

        private byte[] ReadButtonConfig(int player)
        {
            lock (_lock)
            {
                InputConfig config = ActiveConfig(player);
                InputConfig defaults = config == null ? null : DefaultFor(config);

                if (defaults == null)
                {
                    return null;
                }

                int[] current = Inputs(config);
                int[] reference = Inputs(defaults);
                byte[] result = NextendoButtonBridge.DefaultConfig();

                for (int hardware = 0; hardware < Slots; hardware++)
                {
                    uint assigned = Invalid;

                    for (int button = 0; button < Slots; button++)
                    {
                        if (current[button] == reference[hardware])
                        {
                            assigned = _assignment[button];

                            break;
                        }
                    }

                    BitConverter.TryWriteBytes(result.AsSpan(hardware * 4, 4), assigned);
                }

                return result;
            }
        }

        private bool WriteButtonConfig(int player, byte[] mapping)
        {
            lock (_lock)
            {
                InputConfig config = ActiveConfig(player);
                InputConfig defaults = config == null ? null : DefaultFor(config);

                if (defaults == null)
                {
                    return false;
                }

                int[] reference = Inputs(defaults);
                int[] inputs = new int[Slots];

                Array.Fill(inputs, config is StandardKeyboardInputConfig ? (int)PhysicalKey.Unbound : (int)GamepadInputId.Unbound);

                // A button of the emulator takes one input: when two buttons of the controller give
                // the same one, the button moved there wins over the one left in its place.
                for (int pass = 0; pass < 2; pass++)
                {
                    for (int hardware = 0; hardware < Slots; hardware++)
                    {
                        uint assigned = mapping == null ? _assignment[hardware] : BitConverter.ToUInt32(mapping, hardware * 4);
                        int button = Array.IndexOf(_assignment, assigned);

                        if (button >= 0 && (button != hardware) == (pass == 1))
                        {
                            inputs[button] = reference[hardware];
                        }
                    }
                }

                switch (config)
                {
                    case StandardKeyboardInputConfig k:
                        Assign(k, v => k.LeftJoyconStick.StickButton = v, v => k.RightJoyconStick.StickButton = v, inputs);
                        break;
                    case StandardControllerInputConfig c:
                        Assign(c, v => c.LeftJoyconStick.StickButton = v, v => c.RightJoyconStick.StickButton = v, inputs);
                        break;
                }

                _controllers[player]?.UpdateUserConfiguration(config);
            }

            ButtonConfigChanged?.Invoke();

            return true;
        }
    }
}

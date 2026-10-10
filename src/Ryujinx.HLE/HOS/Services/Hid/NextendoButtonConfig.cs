using Ryujinx.Common.Configuration;
using Ryujinx.Common.Logging;
using Ryujinx.HLE.HOS.Services.Hid.Types.SharedMemory.Npad;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Ryujinx.HLE.HOS.Services.Hid
{
    /// <summary>
    /// [Nextendo, HOME Menu work] "Change button mapping" of the HOME Menu settings: the mapping
    /// chosen for a controller (nn::hid HidcfgButtonConfigFull, 0x54 bytes) is applied to what that
    /// player presses, and kept with the emulator data, as are the five saved mappings.
    /// </summary>
    /// <summary>
    /// [Nextendo, HOME Menu work] The input configuration of the emulator, seen as a HOME Menu
    /// button mapping (set by the input manager of the emulator).
    /// </summary>
    public static class NextendoButtonBridge
    {
        /// <summary>The mapping of a player (HidcfgButtonConfigFull), or null if it has no controller.</summary>
        public static Func<int, byte[]> Read { get; set; }

        /// <summary>Applies a mapping (null: the default one) to the input configuration of a player.</summary>
        public static Func<int, byte[], bool> Write { get; set; }

        public static byte[] DefaultConfig() => NextendoButtonConfig.Default();
    }

    static class NextendoButtonConfig
    {
        public const int FullSize = 0x54;
        private const int Hardware = 17; // left, up, right, down, A, B, X, Y, stick L, stick R, L, R, ZL, ZR, select, start, capture

        // The hardware button of each entry of the configuration.
        private static readonly NpadButton[] _hardware =
        [
            NpadButton.Left, NpadButton.Up, NpadButton.Right, NpadButton.Down,
            NpadButton.A, NpadButton.B, NpadButton.X, NpadButton.Y,
            NpadButton.StickL, NpadButton.StickR, NpadButton.L, NpadButton.R,
            NpadButton.ZL, NpadButton.ZR, NpadButton.Minus, NpadButton.Plus,
            NpadButton.None, // capture: not part of the pad state
        ];

        // HidcfgDigitalButtonAssignment -> the button it gives.
        private static NpadButton Assigned(uint assignment) => assignment switch
        {
            <= 9 => (NpadButton)(1UL << (int)assignment),
            10 => NpadButton.Minus,
            11 => NpadButton.Plus,
            >= 12 and <= 15 => (NpadButton)(1UL << (int)assignment),
            >= 16 and <= 19 => (NpadButton)(1UL << (int)(assignment + 8)),
            _ => NpadButton.None,
        };

        private sealed class Saved
        {
            public Dictionary<int, string> Players { get; set; } = new();
            public Dictionary<int, string> Storage { get; set; } = new();
            public Dictionary<int, string> StorageNames { get; set; } = new();
            public bool Enabled { get; set; } = true;
        }

        private static readonly object _lock = new();
        private static Saved _saved;
        private static readonly Dictionary<int, uint[]> _remaps = new();

        private static string FilePath => Path.Combine(AppDataManager.BaseDirPath, "nextendo_button_config.json");

        public static byte[] Default()
        {
            byte[] config = new byte[FullSize];
            uint[] identity = [12, 13, 14, 15, 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 21];

            for (int i = 0; i < Hardware; i++)
            {
                BitConverter.TryWriteBytes(config.AsSpan(i * 4, 4), identity[i]);
            }

            return config;
        }

        private static Saved Load()
        {
            if (_saved != null)
            {
                return _saved;
            }

            try
            {
                _saved = File.Exists(FilePath) ? JsonSerializer.Deserialize<Saved>(File.ReadAllText(FilePath)) : null;
            }
            catch (Exception)
            {
                _saved = null;
            }

            _saved ??= new Saved();

            foreach ((int player, string config) in _saved.Players)
            {
                Remember(player, Convert.FromBase64String(config));
            }

            return _saved;
        }

        private static void Store()
        {
            try
            {
                File.WriteAllText(FilePath, JsonSerializer.Serialize(_saved));
            }
            catch (Exception e)
            {
                Logger.Warning?.Print(LogClass.ServiceHid, $"[home] button mapping not kept: {e.Message}");
            }
        }

        private static void Remember(int player, byte[] config)
        {
            uint[] map = new uint[Hardware];

            for (int i = 0; i < Hardware; i++)
            {
                map[i] = BitConverter.ToUInt32(config, i * 4);
            }

            _remaps[player] = map;
        }

        public static byte[] Get(int player)
        {
            // The emulator's own input configuration is the mapping.
            if (NextendoButtonBridge.Read?.Invoke(player) is byte[] emulator)
            {
                return emulator;
            }

            lock (_lock)
            {
                return Load().Players.TryGetValue(player, out string config) ? Convert.FromBase64String(config) : Default();
            }
        }

        public static void Set(int player, byte[] config)
        {
            if (NextendoButtonBridge.Write?.Invoke(player, config) == true)
            {
                lock (_lock)
                {
                    Load();

                    if (_saved.Players.Remove(player) | _remaps.Remove(player))
                    {
                        Store();
                    }
                }

                Logger.Info?.Print(LogClass.ServiceHid, $"[home] button mapping of player {player + 1} written to the input settings of the emulator");

                return;
            }

            lock (_lock)
            {
                Load();

                if (config == null || config.AsSpan().SequenceEqual(Default()))
                {
                    _saved.Players.Remove(player);
                    _remaps.Remove(player);
                }
                else
                {
                    _saved.Players[player] = Convert.ToBase64String(config);
                    Remember(player, config);
                }

                Store();
            }

            Logger.Info?.Print(LogClass.ServiceHid, $"[home] button mapping of player {player + 1}: {(config == null ? "default" : "set")}");
        }

        public static bool IsCustom(int player)
        {
            if (NextendoButtonBridge.Read?.Invoke(player) is byte[] emulator)
            {
                return !emulator.AsSpan().SequenceEqual(Default());
            }

            lock (_lock)
            {
                return Load().Players.ContainsKey(player);
            }
        }

        public static bool AnyCustom()
        {
            for (int player = 0; player < 8; player++)
            {
                if (NextendoButtonBridge.Read?.Invoke(player) is byte[] emulator && !emulator.AsSpan().SequenceEqual(Default()))
                {
                    return true;
                }
            }

            lock (_lock)
            {
                return Load().Enabled && _saved.Players.Count > 0;
            }
        }

        public static void SetEnabled(bool enabled)
        {
            lock (_lock)
            {
                Load().Enabled = enabled;
                Store();
            }
        }

        public static void ResetAll()
        {
            for (int player = 0; player < 8; player++)
            {
                if (NextendoButtonBridge.Read?.Invoke(player) is byte[] emulator && !emulator.AsSpan().SequenceEqual(Default()))
                {
                    NextendoButtonBridge.Write(player, null);
                }
            }

            lock (_lock)
            {
                Load().Players.Clear();
                _remaps.Clear();
                Store();
            }
        }

        public static (byte[] Config, string Name) GetStorage(int index)
        {
            lock (_lock)
            {
                Saved saved = Load();

                return saved.Storage.TryGetValue(index, out string config)
                    ? (Convert.FromBase64String(config), saved.StorageNames.GetValueOrDefault(index, ""))
                    : (null, "");
            }
        }

        public static void SetStorage(int index, byte[] config, string name)
        {
            lock (_lock)
            {
                Saved saved = Load();

                if (config == null)
                {
                    saved.Storage.Remove(index);
                    saved.StorageNames.Remove(index);
                }
                else
                {
                    saved.Storage[index] = Convert.ToBase64String(config);
                    saved.StorageNames[index] = name ?? "";
                }

                Store();
            }
        }

        /// <summary>The buttons a player presses, through the mapping chosen for that controller.</summary>
        public static NpadButton Apply(int player, NpadButton buttons)
        {
            uint[] map;

            lock (_lock)
            {
                if (_saved == null)
                {
                    Load();
                }

                if (!_saved.Enabled || !_remaps.TryGetValue(player, out map))
                {
                    return buttons;
                }
            }

            NpadButton result = buttons;

            for (int i = 0; i < Hardware; i++)
            {
                result &= ~_hardware[i];
            }

            for (int i = 0; i < Hardware; i++)
            {
                if (_hardware[i] != NpadButton.None && (buttons & _hardware[i]) != 0)
                {
                    result |= Assigned(map[i]);
                }
            }

            return result;
        }
    }
}

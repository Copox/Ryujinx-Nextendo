using Avalonia.Threading;
using Ryujinx.Ava.Systems.Configuration;
using Ryujinx.Common.Configuration;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace Ryujinx.Ava.Common
{
    /// <summary>
    /// [Nextendo, HOME Menu work] The emulator's settings shown in the HOME Menu's "Ryujinx |
    /// Nextendo" section: changed there, kept in the emulator's settings (and the other way round:
    /// the section shows them as they are).
    /// </summary>
    internal static class NextendoHomeMenuSettings
    {
        // A value picked in the HOME Menu, until the interface has taken it.
        private static readonly ConcurrentDictionary<string, int> _pending = new();

        public static void Register()
        {
            ConfigurationState.GraphicsSection graphics = ConfigurationState.Instance.Graphics;
            ConfigurationState.SystemSection system = ConfigurationState.Instance.System;

            NextendoEmulatorSettings.List =
            [
                Make("resolution", ["native", "2x", "3x", "4x"], [1, 2, 3, 4], () => graphics.ResScale.Value, v =>
                {
                    graphics.ResScale.Value = v;
                    Ryujinx.Graphics.Gpu.GraphicsConfig.ResScale = v;
                }),
                Make("aa", ["off", "FXAA", "SMAA L", "SMAA M", "SMAA H", "SMAA U"],
                    [AntiAliasing.None, AntiAliasing.Fxaa, AntiAliasing.SmaaLow, AntiAliasing.SmaaMedium, AntiAliasing.SmaaHigh, AntiAliasing.SmaaUltra],
                    () => graphics.AntiAliasing.Value, v => graphics.AntiAliasing.Value = v),
                Make("filter", ["Bilinear", "Nearest", "FSR", "Area"],
                    [ScalingFilter.Bilinear, ScalingFilter.Nearest, ScalingFilter.Fsr, ScalingFilter.Area],
                    () => graphics.ScalingFilter.Value, v => graphics.ScalingFilter.Value = v),
                Make("aniso", ["auto", "2x", "4x", "8x", "16x"], [-1f, 2f, 4f, 8f, 16f], () => graphics.MaxAnisotropy.Value, v =>
                {
                    graphics.MaxAnisotropy.Value = v;
                    Ryujinx.Graphics.Gpu.GraphicsConfig.MaxAnisotropy = v;
                }),
                Make("aspect", ["4:3", "16:9", "16:10", "21:9", "32:9", "stretch"],
                    [AspectRatio.Fixed4x3, AspectRatio.Fixed16x9, AspectRatio.Fixed16x10, AspectRatio.Fixed21x9, AspectRatio.Fixed32x9, AspectRatio.Stretched],
                    () => graphics.AspectRatio.Value, v => graphics.AspectRatio.Value = v),
                Make("mode", ["tv", "handheld"], [true, false], () => system.EnableDockedMode.Value, v => system.EnableDockedMode.Value = v),
                // These are read when the emulation starts: it starts again.
                Make("api", ["Vulkan", "OpenGL"], [GraphicsBackend.Vulkan, GraphicsBackend.OpenGl], () => graphics.GraphicsBackend.Value, v => graphics.GraphicsBackend.Value = v, true),
                Make("threading", ["auto", "off", "on"], [BackendThreading.Auto, BackendThreading.Off, BackendThreading.On],
                    () => graphics.BackendThreading.Value, v => graphics.BackendThreading.Value = v, true),
                Make("shadercache", ["on", "off"], [true, false], () => graphics.EnableShaderCache.Value, v => graphics.EnableShaderCache.Value = v, true),
                Make("recompression", ["on", "off"], [true, false], () => graphics.EnableTextureRecompression.Value, v => graphics.EnableTextureRecompression.Value = v, true),
                Make("macrohle", ["on", "off"], [true, false], () => graphics.EnableMacroHLE.Value, v => graphics.EnableMacroHLE.Value = v, true),
                // the game cards (.xci) as virtual game cards (live: the HOME Menu lists its games again)
                new NextendoEmulatorSetting
                {
                    Key = "xcicards",
                    Values = ["off", "on"],
                    Get = () => Ryujinx.HLE.HOS.NextendoVirtualCards.Option ? 1 : 0,
                    Set = v => Ryujinx.HLE.HOS.NextendoVirtualCards.Enabled = v == 1,
                },
                // the HOME Menu as the console has it (this section goes with it: back from the emulator's Nextendo menu)
                new NextendoEmulatorSetting
                {
                    Key = "vanilla",
                    Values = ["off", "on"],
                    Get = () => NextendoHomeMenuVanilla.Enabled ? 1 : 0,
                    Set = v => NextendoHomeMenuVanilla.Enabled = v == 1,
                    Restart = true,
                },
            ];
        }

        private static NextendoEmulatorSetting Make<T>(string key, string[] values, T[] options, Func<T> get, Action<T> set, bool restart = false) => new()
        {
            Key = key,
            Values = values,
            Restart = restart,
            Get = () => _pending.TryGetValue(key, out int picked) ? picked : Math.Max(0, Array.IndexOf(options, get())),
            Set = index =>
            {
                _pending[key] = index;

                // Those read when the emulation starts: the HOME Menu asks for a restart when the
                // section is left (or gives their values back).
                Dispatcher.UIThread.Post(() =>
                {
                    set(options[index]);
                    ConfigurationState.Instance.ToFileFormat().SaveConfig(Program.ConfigurationPath);
                    _pending.TryRemove(key, out _);
                });
            },
        };
    }
}

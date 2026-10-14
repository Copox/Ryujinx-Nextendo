using Ryujinx.Common.Logging;
using Ryujinx.Graphics.Gpu;
using System;

namespace Ryujinx.HLE.HOS
{
    /// <summary>
    /// [Nextendo, HOME Menu work] The HOME Menu draws in a 1280x720 frame, as on a Switch. On a
    /// TV (docked) it is drawn at 1.5x, 1920x1080, unless the resolution chosen in the settings
    /// is already higher; a game started from it gets the chosen resolution back.
    /// </summary>
    static class NextendoHomeResolution
    {
        private const float HomeScale = 1.5f;

        private static float? _chosen;

        public static void HomeInFront(Horizon system)
        {
            if (!system.State.DockedMode)
            {
                return;
            }

            _chosen ??= GraphicsConfig.ResScale;

            if (_chosen.Value > 0 && _chosen.Value < HomeScale)
            {
                GraphicsConfig.ResScale = HomeScale;

                Logger.Info?.Print(LogClass.Application, "[home] HOME Menu drawn in 1080p (1.5x)");
            }
        }

        public static void GameInFront()
        {
            if (_chosen is float chosen)
            {
                GraphicsConfig.ResScale = chosen;
                _chosen = null;
            }
        }
    }
}

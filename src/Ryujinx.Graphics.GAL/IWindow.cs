using Ryujinx.Common.Configuration;
using System;

namespace Ryujinx.Graphics.GAL
{
    public interface IWindow
    {
        void Present(ITexture texture, ImageCrop crop, Action swapBuffersCallback);

        /// <summary>
        /// [Nextendo, HOME Menu work] A picture laid over the next frames (the notifications of the
        /// system), blended by its alpha; null for none. A backend that cannot does nothing.
        /// </summary>
        void SetOverlay(ITexture overlay) { }

        /// <summary>
        /// [Nextendo] A notification of the emulator laid over the next frames, at a place given in
        /// parts of the picture (0 to 1); null for none.
        /// </summary>
        void SetNotification(ITexture notification, float x, float y, float width, float height) { }

        void SetSize(int width, int height);

        void ChangeVSyncMode(VSyncMode vSyncMode);

        void SetAntiAliasing(AntiAliasing antialiasing);
        void SetScalingFilter(ScalingFilter type);
        void SetScalingFilterLevel(float level);
        void SetColorSpacePassthrough(bool colorSpacePassThroughEnabled);
    }
}

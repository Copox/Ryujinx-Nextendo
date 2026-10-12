using Ryujinx.Graphics.GAL.Multithreading.Model;
using Ryujinx.Graphics.GAL.Multithreading.Resources;
using System;

namespace Ryujinx.Graphics.GAL.Multithreading.Commands.Window
{
    struct WindowPresentCommand : IGALCommand, IGALCommand<WindowPresentCommand>
    {
        public readonly CommandType CommandType => CommandType.WindowPresent;
        private TableRef<ThreadedTexture> _texture;
        private ImageCrop _crop;
        private TableRef<Action> _swapBuffersCallback;
        private TableRef<ThreadedTexture> _overlay;

        public void Set(TableRef<ThreadedTexture> texture, ImageCrop crop, TableRef<Action> swapBuffersCallback, TableRef<ThreadedTexture> overlay)
        {
            _overlay = overlay;
            _texture = texture;
            _crop = crop;
            _swapBuffersCallback = swapBuffersCallback;
        }

        public static void Run(ref WindowPresentCommand command, ThreadedRenderer threaded, IRenderer renderer)
        {
            threaded.SignalFrame();
            renderer.Window.SetOverlay(command._overlay.Get(threaded)?.Base);
            renderer.Window.Present(command._texture.Get(threaded)?.Base, command._crop, command._swapBuffersCallback.Get(threaded));
        }
    }
}

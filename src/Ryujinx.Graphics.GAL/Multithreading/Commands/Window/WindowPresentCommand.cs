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
        private TableRef<ThreadedTexture> _notification;
        private float _notificationX, _notificationY, _notificationWidth, _notificationHeight;

        public void Set(TableRef<ThreadedTexture> texture, ImageCrop crop, TableRef<Action> swapBuffersCallback, TableRef<ThreadedTexture> overlay,
            TableRef<ThreadedTexture> notification, float notificationX, float notificationY, float notificationWidth, float notificationHeight)
        {
            _overlay = overlay;
            _notification = notification;
            _notificationX = notificationX;
            _notificationY = notificationY;
            _notificationWidth = notificationWidth;
            _notificationHeight = notificationHeight;
            _texture = texture;
            _crop = crop;
            _swapBuffersCallback = swapBuffersCallback;
        }

        public static void Run(ref WindowPresentCommand command, ThreadedRenderer threaded, IRenderer renderer)
        {
            threaded.SignalFrame();

            // The references come out of the table in the order they were put in (see
            // ThreadedWindow.Present): the frame, the callback, the overlay, the notification.
            ThreadedTexture texture = command._texture.Get(threaded);
            Action swapBuffersCallback = command._swapBuffersCallback.Get(threaded);
            ThreadedTexture overlay = command._overlay.Get(threaded);
            ThreadedTexture notification = command._notification.Get(threaded);

            renderer.Window.SetOverlay(overlay?.Base);
            renderer.Window.SetNotification(notification?.Base, command._notificationX, command._notificationY, command._notificationWidth, command._notificationHeight);
            renderer.Window.Present(texture?.Base, command._crop, swapBuffersCallback);
        }
    }
}

using Ryujinx.Common.Logging;
using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace Ryujinx.Common.Configuration
{
    /// <summary>
    /// [Nextendo, HOME Menu work] Pictures the console downloads itself (the picture of a friend
    /// request) for accounts with none: the default avatar of Nextendo, tinted by the PID, served
    /// by the emulator on the loopback ("http://127.0.0.1:port/avatar/&lt;pid&gt;.jpg").
    /// </summary>
    public static class NextendoLocalImages
    {
        private static readonly object _lock = new();
        private static int _port;

        /// <summary>The address of the default avatar of an account.</summary>
        public static string DefaultAvatarUrl(ulong pid)
        {
            int port = Start();

            return port == 0 ? "" : $"http://127.0.0.1:{port}/avatar/{pid}.jpg";
        }

        private static int Start()
        {
            lock (_lock)
            {
                if (_port != 0)
                {
                    return _port;
                }

                try
                {
                    TcpListener listener = new(IPAddress.Loopback, 0);

                    listener.Start();
                    _port = ((IPEndPoint)listener.LocalEndpoint).Port;

                    Task.Run(async () =>
                    {
                        while (true)
                        {
                            TcpClient client = await listener.AcceptTcpClientAsync();

                            _ = Task.Run(() => Serve(client));
                        }
                    });
                }
                catch (Exception)
                {
                    _port = 0;
                }

                return _port;
            }
        }

        private static async Task Serve(TcpClient client)
        {
            using (client)
            {
                try
                {
                    NetworkStream stream = client.GetStream();
                    byte[] buffer = new byte[2048];
                    int read = await stream.ReadAsync(buffer);
                    string request = Encoding.ASCII.GetString(buffer, 0, read);
                    string path = request.Split(' ').Length > 1 ? request.Split(' ')[1] : "";

                    byte[] body = null;

                    if (path.StartsWith("/avatar/", StringComparison.Ordinal) &&
                        ulong.TryParse(path["/avatar/".Length..].Replace(".jpg", ""), out ulong pid))
                    {
                        body = NextendoImages.Default(pid, 256);
                    }

                    Logger.Info?.Print(LogClass.Application, $"[home] local picture asked: {request.Split(new[] { (char)13, (char)10 })[0]} -> {(body == null ? "none" : body.Length + " bytes")}");

                    string header = body == null
                        ? "HTTP/1.1 404 Not Found\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"
                        : $"HTTP/1.1 200 OK\r\nContent-Type: image/jpeg\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n";

                    await stream.WriteAsync(Encoding.ASCII.GetBytes(header));

                    if (body != null)
                    {
                        await stream.WriteAsync(body);
                    }
                }
                catch (Exception)
                {
                    // the console asks again
                }
            }
        }
    }
}

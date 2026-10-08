using LibHac.Ncm;
using LibHac.Tools.FsSystem.NcaUtils;
using Ryujinx.Common.Logging;
using Ryujinx.HLE.FileSystem;
using Ryujinx.HLE.HOS.Kernel;
using Ryujinx.HLE.HOS.Kernel.Process;
using Ryujinx.HLE.HOS.Kernel.Threading;
using Ryujinx.HLE.HOS.SystemState;
using System;
using System.Collections.Concurrent;
using System.Threading;

namespace Ryujinx.HLE.HOS.Services.Am.AppletAE
{
    // A library applet run from its real system title (MyPage, the settings, the player
    // selector...) as a second guest process, the way the HOME Menu starts them on a console.
    // Games keep the emulator's own applets: this path is only taken when the caller is itself
    // a system applet.
    class RealApplet
    {
        public ulong Pid;
        public ulong CallerPid;
        public AppletId AppletId;
        public LibraryAppletMode Mode;

        // The applet's ends of the two data channels.
        public AppletSession Normal;
        public AppletSession Interactive;

        public AppletStateMgr State;
        public KEvent PopInDataEvent;
        public KEvent PopInteractiveInDataEvent;

        public volatile bool Exited;
        public Action OnExited;
    }

    static class RealAppletManager
    {
        private static readonly ConcurrentDictionary<ulong, RealApplet> _applets = new();

        public static bool IsSystemAppletProgram(ulong programId)
        {
            return programId is >= 0x0100000000001000 and <= 0x0100000000001FFF;
        }

        public static bool CallerIsSystemApplet(Switch device, ulong pid)
        {
            return pid != 0 && device.Processes.TryGetProcess(pid, out var process) && IsSystemAppletProgram(process.ProgramId);
        }

        public static ulong ProgramIdOf(AppletId appletId)
        {
            return appletId switch
            {
                AppletId.Auth => 0x0100000000001001,
                AppletId.Cabinet => 0x0100000000001002,
                AppletId.Controller => 0x0100000000001003,
                AppletId.DataErase => 0x0100000000001004,
                AppletId.Error => 0x0100000000001005,
                AppletId.NetConnect => 0x0100000000001006,
                AppletId.PlayerSelect => 0x0100000000001007,
                AppletId.SoftwareKeyboard => 0x0100000000001008,
                AppletId.MiiEdit => 0x0100000000001009,
                AppletId.LibAppletWeb => 0x010000000000100A,
                AppletId.LibAppletShop => 0x010000000000100B,
                AppletId.PhotoViewer => 0x010000000000100D,
                AppletId.Settings => 0x010000000000100E,
                AppletId.LibAppletOff => 0x010000000000100F,
                AppletId.LibAppletWhitelisted => 0x0100000000001010,
                AppletId.LibAppletAuth => 0x0100000000001011,
                AppletId.MyPage => 0x0100000000001013,
                _ => 0,
            };
        }

        public static string PathOf(Switch device, AppletId appletId)
        {
            ulong programId = ProgramIdOf(appletId);

            if (programId == 0)
            {
                return null;
            }

            string path = device.System.ContentManager.GetInstalledContentPath(programId, StorageId.BuiltInSystem, NcaContentType.Program);

            return string.IsNullOrEmpty(path) ? null : VirtualFileSystem.SwitchPathToSystemPath(path);
        }

        public static bool TryGet(ulong pid, out RealApplet applet) => _applets.TryGetValue(pid, out applet);

        // The message queue and focus state of a process: its own when it is a real library
        // applet, the system-wide one otherwise.
        public static AppletStateMgr StateFor(Horizon system, ulong pid)
        {
            return pid != 0 && _applets.TryGetValue(pid, out RealApplet applet) ? applet.State : system.AppletState;
        }

        public static void Start(Switch device, RealApplet applet, string path)
        {
            Horizon system = device.System;

            applet.State = new AppletStateMgr(system);
            applet.PopInDataEvent = new KEvent(system.KernelContext);
            applet.PopInteractiveInDataEvent = new KEvent(system.KernelContext);
            applet.Normal.DataAvailable += (_, _) => applet.PopInDataEvent.ReadableEvent.Signal();
            applet.Interactive.DataAvailable += (_, _) => applet.PopInteractiveInDataEvent.ReadableEvent.Signal();

            // Loading a program is not something an IPC server thread should do while its
            // client waits on it.
            Thread thread = new(() =>
            {
                ulong pid = device.Processes.LoadApplet(path, id => { applet.Pid = id; _applets[id] = applet; });

                if (pid == 0)
                {
                    Logger.Error?.Print(LogClass.ServiceAm, $"Applet {applet.AppletId}: the program could not be started.");

                    applet.Exited = true;
                    applet.OnExited?.Invoke();

                    return;
                }

                Logger.Notice.Print(LogClass.ServiceAm, $"Applet {applet.AppletId} started as process {pid} (caller {applet.CallerPid}, mode {applet.Mode}).");

                HomeMenuStatus.Set(applet.AppletId.ToString());

                // The caller leaves the foreground, the applet takes it.
                StateFor(system, applet.CallerPid).SetFocus(false);
                applet.State.SetFocus(true);

                while (system.KernelContext.Processes.TryGetValue(pid, out KProcess process) &&
                       process.State != ProcessState.Exited && !applet.Exited)
                {
                    Thread.Sleep(30);
                }

                Finish(device, applet);
            })
            {
                Name = $"HLE.RealApplet.{applet.AppletId}",
                IsBackground = true,
            };

            thread.Start();
        }

        // The applet said it is done (ExitProcessAndReturn): it then sleeps until it is killed.
        public static void RequestFinish(RealApplet applet)
        {
            applet.Exited = true;
        }

        private static void Finish(Switch device, RealApplet applet)
        {
            Horizon system = device.System;
            ulong pid = applet.Pid;

            Logger.Notice.Print(LogClass.ServiceAm, $"Applet {applet.AppletId} (process {pid}) is over.");

            Vi.SystemSharedBuffer.Release(device, pid);

            if (system.KernelContext.Processes.TryGetValue(pid, out KProcess process) && process.State != ProcessState.Exited)
            {
                Terminate(system, process);
            }

            _applets.TryRemove(pid, out _);
            device.Processes.ClearProcess(pid);

            applet.Exited = true;
            applet.OnExited?.Invoke();

            HomeMenuStatus.Set(TryGet(applet.CallerPid, out RealApplet caller) ? caller.AppletId.ToString() : "");

            StateFor(system, applet.CallerPid).SetFocus(true);
        }

        private static void Terminate(Horizon system, KProcess process)
        {
            KProcess killerProcess = new(system.KernelContext);
            KThread killer = new(system.KernelContext);

            killer.Initialize(0, 0, 0, 3, 0, killerProcess, ThreadType.Kernel, () =>
            {
                process.Terminate();

                KernelStatic.GetCurrentThread().Exit();
            });

            killer.Start();
            killer.HostThread.Join();
        }
    }
}

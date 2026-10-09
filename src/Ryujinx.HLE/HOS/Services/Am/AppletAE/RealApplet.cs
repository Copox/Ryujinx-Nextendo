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
        public bool Homebrew; // a homebrew program (.nro) run in the place of this applet

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
        public bool FocusGiven;
        public Action OnExited;
    }

    // A game started from the HOME Menu.
    class RunningApplication
    {
        public ulong Pid;
        public ulong CallerPid;
        public HomeMenuTitle Title;
        public long LayerId;

        public volatile bool Exited;
        public volatile bool TerminateRequested;
        public Action OnExited;
    }

    static class RealAppletManager
    {
        private static readonly ConcurrentDictionary<ulong, RealApplet> _applets = new();

        // Message queues of the system applets (the HOME Menu). An application keeps the
        // system-wide one, as when it runs alone.
        private static readonly ConcurrentDictionary<ulong, AppletStateMgr> _systemApplets = new();

        private static volatile RunningApplication _application;

        public static void RegisterSystemApplet(Horizon system, ulong pid)
        {
            if (!_systemApplets.ContainsKey(pid) && Environment.GetEnvironmentVariable("NEXTENDO_QL_NOOWN") != "1")
            {
                AppletStateMgr state = new(system);

                state.SetFocus(true);

                Logger.Notice.Print(LogClass.ServiceAm, $"[home] system applet {pid} gets its own message queue");

                _systemApplets[pid] = state;
            }
        }

        public static RunningApplication StartApplication(Switch device, HomeMenuTitle title, ulong callerPid, Action onExited)
        {
            Horizon system = device.System;

            RunningApplication application = new()
            {
                Title = title,
                CallerPid = callerPid,
                OnExited = onExited,
            };

            _application = application;
            Ryujinx.Common.Configuration.NextendoGames.Playing = title.Id;

            Thread thread = new(() =>
            {
                bool isXci = title.Path.EndsWith(".xci", StringComparison.OrdinalIgnoreCase);
                bool started;

                device.Processes.SetFallback(callerPid);

                try
                {
                    started = isXci ? device.Processes.LoadXci(title.Path, title.Id) : device.Processes.LoadNsp(title.Path, title.Id);
                }
                catch (Exception e)
                {
                    Logger.Error?.Print(LogClass.ServiceAm, $"HOME Menu: {title.Name} could not be loaded: {e.Message}");

                    started = false;
                }

                ulong pid = started ? device.Processes.ActiveApplication?.ProcessId ?? 0 : 0;

                if (pid == 0 || pid == callerPid)
                {
                    Logger.Error?.Print(LogClass.ServiceAm, $"HOME Menu: {title.Name} did not start.");

                    FinishApplication(device, application);

                    return;
                }

                application.Pid = pid;

                Logger.Notice.Print(LogClass.ServiceAm, $"HOME Menu: {title.Name} [{title.Id:X16}] started as process {pid}.");

                // The game takes the foreground with a fresh message queue.
                while (system.AppletState.Messages.TryDequeue(out _))
                {
                }

                StateFor(system, callerPid).SetFocus(false);
                system.AppletState.SetFocus(true);

                HomeMenuStatus.Set("Application:" + title.Name);

                System.Diagnostics.Stopwatch appRunning = System.Diagnostics.Stopwatch.StartNew();
                bool appDumped = false;

                while (system.KernelContext.Processes.TryGetValue(pid, out KProcess process) && process.State != ProcessState.Exited)
                {
                    if (!appDumped && int.TryParse(Environment.GetEnvironmentVariable("NEXTENDO_QL_APPDUMP_MS"), out int appDumpAt) && appRunning.ElapsedMilliseconds > appDumpAt)
                    {
                        appDumped = true;

                        try
                        {
                            Logger.Notice.Print(LogClass.ServiceAm, "[dump] " + process.Debugger.GetMinidump());
                        }
                        catch (Exception e)
                        {
                            Logger.Notice.Print(LogClass.ServiceAm, "[dump] failed: " + e.Message);
                        }
                    }

                    if (application.TerminateRequested)
                    {
                        Terminate(system, process);

                        break;
                    }

                    Thread.Sleep(30);
                }

                FinishApplication(device, application);
            })
            {
                Name = "HLE.HomeMenuApplication",
                IsBackground = true,
            };

            thread.Start();

            return application;
        }

        private static void FinishApplication(Switch device, RunningApplication application)
        {
            Horizon system = device.System;

            Logger.Notice.Print(LogClass.ServiceAm, $"HOME Menu: {application.Title.Name} is over.");

            application.Exited = true;

            if (_application == application)
            {
                _application = null;
                Ryujinx.Common.Configuration.NextendoGames.Playing = Ryujinx.Common.Configuration.NextendoGames.HomeMenuId;
            }

            if (application.Pid != 0)
            {
                // The memory of the game goes back to the pool: the next game needs it.
                if (system.KernelContext.Processes.TryGetValue(application.Pid, out KProcess ended) && ended.State == ProcessState.Exited)
                {
                    ReleaseMemory(system, ended);
                }

                device.Processes.ClearProcess(application.Pid);
            }

            device.Processes.SetActive(application.CallerPid);

            Vi.SystemSharedBuffer.Show(device, application.CallerPid);

            application.OnExited?.Invoke();

            AppletStateMgr caller = StateFor(system, application.CallerPid);

            caller.Messages.Enqueue(AllSystemAppletProxiesService.SystemAppletProxy.AppletMessage.ApplicationExited);
            caller.SetFocus(true);

            HomeMenuStatus.Set("");
        }

        public static void RequestApplicationExit(Switch device, RunningApplication application)
        {
            if (!application.Exited)
            {
                device.System.AppletState.Messages.Enqueue(AllSystemAppletProxiesService.SystemAppletProxy.AppletMessage.Exit);
                device.System.AppletState.MessageEvent.ReadableEvent.Signal();

                // A game that does not listen is ended anyway.
                application.TerminateRequested = true;
            }
        }

        public static void BringApplicationToForeground(Switch device, RunningApplication application)
        {
            if (application.Exited || application.LayerId == 0)
            {
                return;
            }

            device.System.SurfaceFlinger.SetRenderLayer(application.LayerId);

            StateFor(device.System, application.CallerPid).SetFocus(false);
            device.System.AppletState.SetFocus(true);

            HomeMenuStatus.Set("Application:" + application.Title.Name);
        }

        // The HOME button: the game goes to the background, the HOME Menu is told.
        public static bool PressHome(Switch device)
        {
            foreach ((ulong pid, AppletStateMgr state) in _systemApplets)
            {
                RunningApplication application = _application;

                if (application != null && !application.Exited && application.Pid != 0)
                {
                    long current = device.System.SurfaceFlinger.RenderLayerId;
                    Vi.SystemSharedBuffer session = Vi.SystemSharedBuffer.Of(pid);

                    if (session != null && current != session.LayerId)
                    {
                        application.LayerId = current;
                    }
                }

                state.Messages.Enqueue(AllSystemAppletProxiesService.SystemAppletProxy.AppletMessage.DetectShortPressingHomeButton);
                state.MessageEvent.ReadableEvent.Signal();

                return true;
            }

            return false;
        }

        // The HOME Menu asks for the screen (after the HOME button).
        public static void SystemAppletToForeground(Switch device, ulong pid)
        {
            RunningApplication application = _application;

            if (application == null || application.Exited)
            {
                return;
            }

            Vi.SystemSharedBuffer.Show(device, pid);

            device.System.AppletState.SetFocus(false);
            StateFor(device.System, pid).SetFocus(true);

            HomeMenuStatus.Set("");
        }

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

            // [Nextendo] As on a console running Atmosphere: the album opens the homebrew menu
            // when there is one at the root of the SD card.
            if (appletId == AppletId.PhotoViewer)
            {
                string homebrewMenu = System.IO.Path.Combine(VirtualFileSystem.GetSdCardPath(), "hbmenu.nro");

                if (System.IO.File.Exists(homebrewMenu))
                {
                    return homebrewMenu;
                }
            }

            string path = device.System.ContentManager.GetInstalledContentPath(programId, StorageId.BuiltInSystem, NcaContentType.Program);

            return string.IsNullOrEmpty(path) ? null : VirtualFileSystem.SwitchPathToSystemPath(path);
        }

        public static bool TryGet(ulong pid, out RealApplet applet) => _applets.TryGetValue(pid, out applet);

        private static void GiveFocus(RealApplet applet)
        {
            lock (applet)
            {
                if (!applet.FocusGiven && !applet.Exited)
                {
                    applet.FocusGiven = true;
                    applet.State.TakeForeground();
                }
            }
        }

        // An applet presented its first frame: its main loop runs, it can take the foreground.
        public static void OnFirstFrame(ulong pid)
        {
            if (_applets.TryGetValue(pid, out RealApplet applet) && !applet.FocusGiven)
            {
                new Thread(() =>
                {
                    Thread.Sleep(250);
                    GiveFocus(applet);
                })
                { IsBackground = true, Name = "HLE.RealApplet.Focus" }.Start();
            }
        }

        // The message queue and focus state of a process: its own when it is a real library
        // applet, the system-wide one otherwise.
        public static AppletStateMgr StateFor(Horizon system, ulong pid)
        {
            if (pid != 0 && _applets.TryGetValue(pid, out RealApplet applet))
            {
                return applet.State;
            }

            return pid != 0 && _systemApplets.TryGetValue(pid, out AppletStateMgr state) ? state : system.AppletState;
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
                // A homebrew menu in the place of the album (see PathOf) is run the way the
                // homebrew loader runs it: when it exits it names the program to start next,
                // and when that one exits the menu comes back.
                bool homebrew = path.EndsWith(".nro", StringComparison.OrdinalIgnoreCase);

                applet.Homebrew = homebrew;
                string menuPath = path;
                string arguments = homebrew ? HomebrewCommandLine(path) : null;

                while (true)
                {
                ulong pid = 0;

                // The memory of the program that just ended is given back a moment after it
                // exits: a homebrew that does not fit yet is tried again.
                for (int attempt = 0; pid == 0 && attempt < (homebrew ? 24 : 1); attempt++)
                {
                    if (attempt > 0)
                    {
                        Thread.Sleep(250);
                    }

                    pid = device.Processes.LoadApplet(path, id => { applet.Pid = id; _applets[id] = applet; }, arguments);
                }

                if (pid == 0)
                {
                    Logger.Error?.Print(LogClass.ServiceAm, $"Applet {applet.AppletId}: the program could not be started.");

                    applet.Exited = true;
                    applet.OnExited?.Invoke();

                    return;
                }

                Logger.Notice.Print(LogClass.ServiceAm, $"Applet {applet.AppletId} started as process {pid} (caller {applet.CallerPid}, mode {applet.Mode}).");

                HomeMenuStatus.Set(homebrew ? "Homebrew:" + System.IO.Path.GetFileNameWithoutExtension(path) : applet.AppletId.ToString());

                // The caller leaves the foreground. The applet is told it has it once it shows
                // its first frame (see OnFirstFrame): told at once, it reads the news while it
                // is still setting itself up, drops it, and then ignores every button.
                StateFor(system, applet.CallerPid).SetFocus(false);

                System.Diagnostics.Stopwatch running = System.Diagnostics.Stopwatch.StartNew();
                bool dumped = false;

                while (system.KernelContext.Processes.TryGetValue(pid, out KProcess process) &&
                       process.State != ProcessState.Exited && !applet.Exited)
                {
                    if (!dumped && int.TryParse(Environment.GetEnvironmentVariable("NEXTENDO_QL_DUMP_MS"), out int dumpAt) && running.ElapsedMilliseconds > dumpAt)
                    {
                        // Debug aid: what every thread of the applet is doing at that moment.
                        dumped = true;

                        try
                        {
                            Logger.Notice.Print(LogClass.ServiceAm, "[dump] " + process.Debugger.GetMinidump());
                        }
                        catch (Exception e)
                        {
                            Logger.Notice.Print(LogClass.ServiceAm, "[dump] failed: " + e.Message);
                        }
                    }

                    if (!applet.FocusGiven && running.ElapsedMilliseconds > 4000)
                    {
                        GiveFocus(applet); // an applet without a screen of its own
                    }

                    Thread.Sleep(30);
                }

                // The emulation itself is stopping: nothing to start next, nothing to give back.
                if (!homebrew || applet.Exited || system.Stopping)
                {
                    break;
                }

                string next = null, nextArguments = null;

                if (system.KernelContext.Processes.TryGetValue(pid, out KProcess ended) && !string.IsNullOrEmpty(ended.HomebrewNextPath))
                {
                    next = HomebrewHostPath(ended.HomebrewNextPath);
                    nextArguments = string.IsNullOrEmpty(ended.HomebrewNextArguments) ? HomebrewCommandLine(next) : ended.HomebrewNextArguments;

                    if (next == null || !System.IO.File.Exists(next))
                    {
                        Logger.Warning?.Print(LogClass.ServiceAm, $"Homebrew: \"{ended.HomebrewNextPath}\" is not on the SD card.");

                        next = null;
                    }
                }

                if (next == null && path != menuPath)
                {
                    // A homebrew is over: back to the homebrew menu.
                    next = menuPath;
                    nextArguments = HomebrewCommandLine(menuPath);
                }

                if (next == null)
                {
                    ReleaseMemory(system, ended);

                    break; // the menu itself was left: back to the HOME Menu
                }

                Logger.Notice.Print(LogClass.ServiceAm, $"Homebrew: process {pid} is over, starting \"{next}\".");

                ReleaseMemory(system, ended);

                Vi.SystemSharedBuffer.Release(device, pid);
                _applets.TryRemove(pid, out _);
                device.Processes.ClearProcess(pid);

                applet.State = new AppletStateMgr(system);
                applet.FocusGiven = false;

                path = next;
                arguments = nextArguments;
                }

                Finish(device, applet);
            })
            {
                Name = $"HLE.RealApplet.{applet.AppletId}",
                IsBackground = true,
            };

            thread.Start();
        }

        // "sdmc:/switch/x.nro" -> the file in the folder that is the SD card.
        private static string HomebrewHostPath(string guestPath)
        {
            string relative = guestPath;

            int colon = relative.IndexOf(':');

            if (colon >= 0)
            {
                relative = relative[(colon + 1)..];
            }

            relative = relative.TrimStart('/', '\\');

            if (relative.Contains(".."))
            {
                return null;
            }

            return System.IO.Path.Combine(VirtualFileSystem.GetSdCardPath(), relative.Replace('/', System.IO.Path.DirectorySeparatorChar));
        }

        // The command line a homebrew gets when the menu gives none: its own path, quoted.
        private static string HomebrewCommandLine(string hostPath)
        {
            string card = VirtualFileSystem.GetSdCardPath();
            string relative = hostPath != null && hostPath.StartsWith(card, StringComparison.OrdinalIgnoreCase)
                ? hostPath[card.Length..].Replace('\\', '/').TrimStart('/')
                : System.IO.Path.GetFileName(hostPath ?? "");

            return "\"sdmc:/" + relative + "\"";
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

            // What the applet held goes back to its pool: the pool is small, and after a few
            // applets the HOME Menu itself could not grow its heap any more (it froze).
            if (system.KernelContext.Processes.TryGetValue(pid, out KProcess over))
            {
                ReleaseMemory(system, over);
            }

            _applets.TryRemove(pid, out _);
            device.Processes.ClearProcess(pid);

            if (applet.Homebrew)
            {
                // A homebrew draws on a screen of its own: the picture goes back to the caller.
                device.Processes.SetActive(applet.CallerPid);
                Vi.SystemSharedBuffer.Show(device, applet.CallerPid);
            }

            applet.Exited = true;
            applet.OnExited?.Invoke();

            HomeMenuStatus.Set(TryGet(applet.CallerPid, out RealApplet caller) ? caller.AppletId.ToString() : "");

            StateFor(system, applet.CallerPid).SetFocus(true);
        }

        // What a program that ended still holds goes back to the pool, for the next one.
        private static void ReleaseMemory(Horizon system, KProcess process)
        {
            // Not while the emulation is being stopped: the address spaces are being freed by
            // the stop itself, and touching them at the same time crashed the emulator.
            if (process == null || system.Stopping || !system.KernelContext.Running)
            {
                return;
            }

            KProcess helperProcess = new(system.KernelContext);
            KThread helper = new(system.KernelContext);

            helper.Initialize(0, 0, 0, 3, 0, helperProcess, ThreadType.Kernel, () =>
            {
                try
                {
                    ulong released = process.MemoryManager.ReleaseOwnedMemory();

                    Logger.Notice.Print(LogClass.ServiceAm, $"Process {process.Pid}: {released / (1024 * 1024)} MiB given back.");
                }
                catch (Exception e)
                {
                    Logger.Warning?.Print(LogClass.ServiceAm, $"Process {process.Pid}: memory not given back ({e.Message}).");
                }

                KernelStatic.GetCurrentThread().Exit();
            });

            helper.Start();
            helper.HostThread.Join();
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

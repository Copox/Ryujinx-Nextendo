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

        // Winding (see IProcessWindingController): the in-data the applet gave back, the context
        // it kept, the applet it wants started in its place, and that applet once it is over.
        public System.Collections.Concurrent.ConcurrentStack<byte[]> Unpopped = new();
        public System.Collections.Concurrent.ConcurrentStack<byte[]> WindContext = new();
        public object Reserved;
        public object Calling;
        public volatile bool WindRequested;
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
        public bool WasDocked; // the initial setup: the docked mode to give back
        public Action OnExited;
    }

    static class RealAppletManager
    {
        private static readonly ConcurrentDictionary<ulong, RealApplet> _applets = new();

        // Message queues of the system applets (the HOME Menu). An application keeps the
        // system-wide one, as when it runs alone.
        private static readonly ConcurrentDictionary<ulong, AppletStateMgr> _systemApplets = new();

        private static volatile RunningApplication _application;

        /// <summary>A new emulation starts: the applets and the game of the previous one are gone.</summary>
        public static void Reset()
        {
            _applets.Clear();
            _systemApplets.Clear();
            _application = null;

            Ryujinx.Common.Configuration.NextendoGames.Playing = Ryujinx.Common.Configuration.NextendoGames.HomeMenuId;
            HomeMenuStatus.Set("");
            HomeMenuStatus.Running = false;
        }

        public static void RegisterSystemApplet(Horizon system, ulong pid)
        {
            if (!_systemApplets.ContainsKey(pid))
            {
                AppletStateMgr state = new(system);

                state.SetFocus(true);

                Logger.Notice.Print(LogClass.ServiceAm, $"[home] system applet {pid} gets its own message queue");

                bool first = _systemApplets.IsEmpty;

                HomeMenuStatus.Running = true;

                if (system.KernelContext.Processes.TryGetValue(pid, out Kernel.Process.KProcess applet))
                {
                    Themes.QlaunchThemePage.Activate(applet);
                }

                if (first)
                {
                    NextendoHomeResolution.HomeInFront(system);
                }

                _systemApplets[pid] = state;

                // [Nextendo, HOME Menu work] With the HOME Menu, the overlay applet that draws the
                // notifications of the system.
                if (first)
                {
                    StartOverlay(system.Device);
                }
            }
        }

        /// <summary>The process of the overlay applet (overlayDisp), when it runs.</summary>
        public static ulong OverlayPid { get; private set; }

        public static bool IsOverlay(ulong pid) => pid != 0 && pid == OverlayPid;

        // The overlay applet has a message queue of its own but not the focus: the buttons stay
        // with the HOME Menu and the game.
        public static void RegisterOverlayApplet(Horizon system, ulong pid)
        {
            if (!_systemApplets.ContainsKey(pid))
            {
                AppletStateMgr state = new(system);

                state.SetFocus(false);
                _systemApplets[pid] = state;

                Logger.Notice.Print(LogClass.ServiceAm, $"[overlay] overlay applet {pid} gets its own message queue, without the focus");
            }
        }

        private static void StartOverlay(Switch device)
        {
            string path = PathOf(device, AppletId.OverlayDisplay);

            if (path == null)
            {
                Logger.Warning?.Print(LogClass.ServiceAm, "[overlay] overlayDisp is not in the firmware");

                return;
            }

            new Thread(() =>
            {
                Thread.Sleep(12000);

                // The HOME Menu may have been closed in the meantime: starting the overlay in a
                // stopped emulator took the whole emulator down (disposed GPU events).
                if (device.System.Stopping)
                {
                    return;
                }

                ulong pid;

                try
                {
                    pid = device.Processes.LoadApplet(path, id => OverlayPid = id);
                }
                catch (Exception) when (device.System.Stopping)
                {
                    return;
                }

                Logger.Notice.Print(LogClass.ServiceAm, $"[overlay] overlayDisp started as process {pid}");
            })
            { IsBackground = true, Name = "HLE.RealApplet.Overlay" }.Start();
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

            // A program of the firmware (the initial setup) is not a game: friends and Discord
            // keep seeing the HOME Menu.
            if (!IsSystemProgram(title))
            {
                Ryujinx.Common.Configuration.NextendoGames.Playing = title.Id;
            }

            // The initial setup asks to take the console out of its dock and shows the Joy-Con
            // being attached: done for the player, the docked mode back once it is over.
            if (title.Id == StarterId)
            {
                application.WasDocked = system.State.DockedMode;
                system.AccountManager.HideDefaultUserForSetup();
                Hid.NpadDevices.VirtualJoyCons = true;
                Hid.NpadDevices.VirtualJoyConsDetached = false;
                system.ChangeDockedModeState(false);
            }

            NextendoHomeResolution.GameInFront();

            Thread thread = new(() =>
            {
                bool isXci = title.Path.EndsWith(".xci", StringComparison.OrdinalIgnoreCase);
                bool isSystem = IsSystemProgram(title);
                bool started;
                ulong systemPid = 0;

                device.Processes.SetFallback(callerPid);

                try
                {
                    if (isSystem)
                    {
                        // A program of the firmware: started like an applet, then followed like a game.
                        systemPid = device.Processes.LoadApplet(title.Path, null);
                        started = systemPid != 0;

                        if (started)
                        {
                            device.Processes.SetActive(systemPid);
                        }
                    }
                    else
                    {
                        // its online data up to date first, as from the game list
                        Ryujinx.Common.Configuration.NextendoHomeLaunch.Run(title.Id);

                        started = isXci ? device.Processes.LoadXci(title.Path, title.Id) : device.Processes.LoadNsp(title.Path, title.Id);
                    }
                }
                catch (Exception e)
                {
                    Logger.Error?.Print(LogClass.ServiceAm, $"HOME Menu: {title.Name} could not be loaded: {e}");

                    started = false;
                }

                ulong pid = started ? (isSystem ? systemPid : device.Processes.ActiveApplication?.ProcessId ?? 0) : 0;

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

                HomeMenuStatus.Set(StatusOf(title));

                System.Diagnostics.Stopwatch appRunning = System.Diagnostics.Stopwatch.StartNew();

                while (system.KernelContext.Processes.TryGetValue(pid, out KProcess process) && process.State != ProcessState.Exited)
                {

                    if (application.TerminateRequested)
                    {
                        // ended as it is, suspended or not (run again, it would start requests
                        // to services that its end then leaves without memory to read)
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

            if (application.Title.Id == StarterId)
            {
                system.AccountManager.EndSetup();
                Hid.NpadDevices.VirtualJoyCons = false;
                system.ChangeDockedModeState(application.WasDocked);
            }

            if (_application == application)
            {
                _application = null;
                Ryujinx.Common.Configuration.NextendoGames.Playing = Ryujinx.Common.Configuration.NextendoGames.HomeMenuId;
                NextendoHomeResolution.HomeInFront(system);
            }

            if (application.Pid != 0)
            {
                // The memory of the game goes back to the pool: the next game needs it. Once the
                // graphics processor is done with the commands the game left (a game closed right
                // after it ran again leaves some, which read its memory).
                if (system.KernelContext.Processes.TryGetValue(application.Pid, out KProcess ended) && ended.State == ProcessState.Exited)
                {
                    int renderers = Ryujinx.Audio.Renderer.Server.AudioRendererManager.DisposeAllFor(ended.CpuMemory);

                    if (renderers > 0)
                    {
                        Logger.Info?.Print(LogClass.ServiceAm, $"HOME Menu: {renderers} audio renderer(s) of {application.Title.Name} ended");
                    }

                    for (int waited = 0; waited < 2000 && !device.Gpu.GPFifo.IsIdle; waited += 10)
                    {
                        Thread.Sleep(10);
                    }

                    ReleaseMemory(system, ended);
                }

                // Its place in arp (the properties of the running applications: two places, the
                // HOME Menu takes one) is free again. Kept, the next game found no place, was
                // not known there, and what asks arp for it failed (Splatoon 3's BCAT data).
                try
                {
                    if (system.ServiceTable.ArpReader.GetApplicationInstanceId(out ulong instance, application.Pid).IsSuccess)
                    {
                        system.ServiceTable.ArpWriter.UnregisterApplicationInstance(instance);
                    }
                }
                catch (Exception e)
                {
                    Logger.Warning?.Print(LogClass.ServiceAm, $"HOME Menu: arp place of {application.Title.Name} not freed ({e.Message})");
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

            Pause(device, application, false);

            StateFor(device.System, application.CallerPid).SetFocus(false);
            device.System.AppletState.SetFocus(true);

            HomeMenuStatus.Set(StatusOf(application.Title));
        }

        // The HOME button: the game goes to the background, the HOME Menu is told.
        public static bool PressHome(Switch device)
        {
            // The last screen of the initial setup says "Press the HOME button": the setup is
            // done. The HOME Menu started in setup mode never builds its own screens (HOME only
            // makes it read the setup as done): the console restarts, as after the setup of a
            // real one, and the HOME Menu then starts as usual.
            if (_application is { Exited: false } setup && setup.Title.Id == StarterId)
            {
                Settings.ISystemSettingsServer.MarkInitialSetupDone();
                device.System.AccountManager.EndSetup();
                HomeMenuStatus.RequestRelaunch();

                return true;
            }

            foreach ((ulong pid, AppletStateMgr state) in _systemApplets)
            {
                // the overlay applet is one of them: the button is the HOME Menu's
                if (IsOverlay(pid))
                {
                    continue;
                }

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

            // As on a console, the game is suspended while the HOME Menu is in front: it no
            // longer runs, and so no longer reads the controllers (all programs read the same
            // ones here).
            Pause(device, application, true);

            HomeMenuStatus.Set("");
        }

        // The game suspended (pause) or running again.
        private static void Pause(Switch device, RunningApplication application, bool pause)
        {
            if (application == null || application.Pid == 0 || !device.System.KernelContext.Processes.TryGetValue(application.Pid, out KProcess process) ||
                process.State is ProcessState.Exiting or ProcessState.Exited || process.IsPaused == pause)
            {
                return;
            }

            if (process.SetActivity(pause) == Ryujinx.Horizon.Common.Result.Success)
            {
                Logger.Info?.Print(LogClass.ServiceAm, $"HOME Menu: {application.Title.Name} {(pause ? "suspended" : "running again")}");
            }
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
                AppletId.OverlayDisplay => 0x010000000000100C,
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

        /// <summary>
        /// A program of the firmware the HOME Menu starts in the place of a game (the initial
        /// setup), as a title of its own; null when the firmware does not have it.
        /// </summary>
        public static HomeMenuTitle SystemProgram(Switch device, ulong programId)
        {
            string path = device.System.ContentManager.GetInstalledContentPath(programId, StorageId.BuiltInSystem, NcaContentType.Program);

            if (string.IsNullOrEmpty(path))
            {
                return null;
            }

            return new HomeMenuTitle { Id = programId, Name = programId == 0x0100000000001012 ? "Starter" : $"{programId:X16}", Path = VirtualFileSystem.SwitchPathToSystemPath(path) };
        }

        // Programs of the firmware are below 0100000000010000, games above.
        private static bool IsSystemProgram(HomeMenuTitle title) => title.Id < 0x0100000000010000;

        // What the HOME Menu status says of a title in front: "Application:<name>" for a game,
        // the program's own name for one of the firmware ("Starter").
        private static string StatusOf(HomeMenuTitle title) => IsSystemProgram(title) ? title.Name : "Application:" + title.Name;

        // The general channel: messages of the programs for the HOME Menu (am PushToGeneralChannel,
        // read by the HOME Menu with PopFromGeneralChannel when its event says one waits).
        private static readonly ConcurrentQueue<byte[]> _generalChannel = new();

        public static event Action GeneralChannelPushed;

        public static int GeneralChannelCount => _generalChannel.Count;

        public static void PushToGeneralChannel(byte[] data)
        {
            Logger.Notice.Print(LogClass.ServiceAm, $"[channel] message of {data.Length} bytes for the HOME Menu: {Convert.ToHexString(data.AsSpan(0, Math.Min(data.Length, 0x40)))}");

            _generalChannel.Enqueue(data);
            GeneralChannelPushed?.Invoke();
        }

        public static bool TryPopFromGeneralChannel(out byte[] data) => _generalChannel.TryDequeue(out data);

        /// <summary>The initial setup of the console (starter).</summary>
        public const ulong StarterId = 0x0100000000001012;

        public static string PathOf(Switch device, AppletId appletId)
        {
            ulong programId = ProgramIdOf(appletId);

            // [Nextendo, HOME Menu work] The eShop button opens the Nintendo website in the
            // browser of the computer (the shop applet of the firmware cannot reach a store).
            if (appletId == AppletId.LibAppletShop)
            {
                return null;
            }

            // [Nextendo] The web applets of the firmware need the browser engine of the console
            // (system data the emulator does not run): started by the HOME Menu (Parental
            // Controls, the help pages), the real one ended at once and the HOME Menu stopped
            // (2000-0000). The emulator's own answers as a page the user closed.
            if (appletId is AppletId.LibAppletWeb or AppletId.LibAppletOff or AppletId.LibAppletWhitelisted)
            {
                return null;
            }

            if (programId == 0)
            {
                return null;
            }

            // [Nextendo] As on a console running Atmosphere: the album opens the homebrew menu
            // when there is one on the SD card. Sphaira first, unless hbmenu was chosen (setting
            // "nextendo_album_menu.txt": "sphaira" or "hbmenu"); either one, when the other is
            // missing.
            if (appletId == AppletId.PhotoViewer && HomebrewMenuPath() is { } homebrewMenu)
            {
                return homebrewMenu;
            }

            string path = device.System.ContentManager.GetInstalledContentPath(programId, StorageId.BuiltInSystem, NcaContentType.Program);

            return string.IsNullOrEmpty(path) ? null : VirtualFileSystem.SwitchPathToSystemPath(path);
        }

        // [Nextendo] The homebrew the emulator puts on the SD card when it is not there, as on a
        // console set up with Atmosphere: the homebrew menus (Sphaira, chosen by default, and
        // switchbrew's hbmenu). Nothing else is bundled.
        private static readonly (string Resource, string[] Path)[] _bundledHomebrew =
        [
            ("sphaira.nro", ["switch", "sphaira", "sphaira.nro"]),
            ("hbmenu.nro", ["hbmenu.nro"]),
        ];

        private static bool _homebrewProvided;

        public static void ProvideHomebrew()
        {
            if (_homebrewProvided)
            {
                return;
            }

            _homebrewProvided = true;

            string sd = VirtualFileSystem.GetSdCardPath();

            foreach ((string resource, string[] path) in _bundledHomebrew)
            {
                string file = System.IO.Path.Combine([sd, .. path]);

                if (System.IO.File.Exists(file))
                {
                    continue;
                }

                try
                {
                    System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(file));
                    System.IO.File.WriteAllBytes(file, Ryujinx.Common.EmbeddedResources.Read("Ryujinx.HLE/HOS/Services/Am/AppletAE/SdCard/" + resource));

                    Logger.Notice.Print(LogClass.ServiceAm, $"[album] {resource} put on the SD card");
                }
                catch (Exception e)
                {
                    Logger.Warning?.Print(LogClass.ServiceAm, $"[album] {resource} not put on the SD card: {e.Message}");
                }
            }
        }

        public static string HomebrewMenuPath()
        {
            ProvideHomebrew();

            string sd = VirtualFileSystem.GetSdCardPath();
            string sphaira = System.IO.Path.Combine(sd, "switch", "sphaira", "sphaira.nro");
            string hbmenu = System.IO.Path.Combine(sd, "hbmenu.nro");
            string choice = "sphaira";

            try
            {
                string setting = System.IO.Path.Combine(Ryujinx.Common.Configuration.AppDataManager.BaseDirPath, "nextendo_album_menu.txt");

                if (System.IO.File.Exists(setting))
                {
                    choice = System.IO.File.ReadAllText(setting).Trim().ToLowerInvariant();
                }
            }
            catch (System.Exception)
            {
                // the default stays
            }

            string[] order = choice == "hbmenu" ? [hbmenu, sphaira] : [sphaira, hbmenu];

            foreach (string path in order)
            {
                if (System.IO.File.Exists(path))
                {
                    return path;
                }
            }

            return null;
        }

        public static bool TryGet(ulong pid, out RealApplet applet) => _applets.TryGetValue(pid, out applet);

        private static void GiveFocus(RealApplet applet, string reason = "first frame")
        {
            lock (applet)
            {
                if (!applet.FocusGiven && !applet.Exited)
                {
                    applet.FocusGiven = true;
                    Logger.Notice.Print(LogClass.ServiceAm, $"[focus] applet {applet.AppletId} given the foreground ({reason})");
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
                long span = 0, requestsBefore = 0, askedBefore = 0, busiest = 0;
                int quiet = 0;

                while (system.KernelContext.Processes.TryGetValue(pid, out KProcess process) &&
                       process.State != ProcessState.Exited && !applet.Exited)
                {

                    // An applet that shows nothing before it has the foreground (the user selection)
                    // never tells its first frame. It is given the foreground once it has set itself
                    // up: its requests to the services, hundreds while it starts, fall to the steady
                    // few of a main loop that waits (measured per 300 ms: user selection 548, 196,
                    // 30, 33 ; controllers 617, 407, 138, 142). Four seconds at most (a blank screen
                    // until then: it was always four seconds).
                    if (!applet.FocusGiven && running.ElapsedMilliseconds / 300 != span)
                    {
                        span = running.ElapsedMilliseconds / 300;

                        long requests = process.Requests;

                        // steady: this span asked about as much as the one before (within 15%, or
                        // a handful), and far less than the busiest span of the start
                        long asked = requests - requestsBefore;
                        bool steady = requests >= 200 && asked * 5 <= busiest * 2 &&
                                      Math.Abs(asked - askedBefore) <= Math.Max(8, asked * 15 / 100);

                        quiet = steady ? quiet + 1 : 0;
                        busiest = Math.Max(busiest, asked);
                        askedBefore = asked;
                        requestsBefore = requests;

                        if (quiet >= 1)
                        {
                            GiveFocus(applet, "set up, quiet");
                        }
                    }

                    if (!applet.FocusGiven && running.ElapsedMilliseconds > 4000)
                    {
                        GiveFocus(applet, "four seconds"); // an applet without a screen of its own
                    }

                    Thread.Sleep(30);
                }

                // [Nextendo, HOME Menu work] Winding: the reserved applet runs in the place of
                // this one, and when it is over this one starts again, told why.
                if (applet.WindRequested && !applet.Exited && !system.Stopping &&
                    applet.Reserved is AllSystemAppletProxiesService.LibraryAppletCreator.ILibraryAppletAccessor reserved)
                {
                    applet.WindRequested = false;
                    applet.Reserved = null;

                    Logger.Notice.Print(LogClass.ServiceAm, $"Applet {applet.AppletId}: process {pid} winds to the reserved applet.");

                    system.KernelContext.Processes.TryGetValue(pid, out KProcess wound);
                    ReleaseMemory(system, wound);
                    Vi.SystemSharedBuffer.Release(device, pid);
                    _applets.TryRemove(pid, out _);
                    device.Processes.ClearProcess(pid);

                    Func<bool> otherIsOver = reserved.StartInPlaceOf(device, applet.CallerPid);

                    while (!otherIsOver() && !system.Stopping)
                    {
                        Thread.Sleep(30);
                    }

                    if (system.Stopping)
                    {
                        break;
                    }

                    Logger.Notice.Print(LogClass.ServiceAm, $"Applet {applet.AppletId}: the reserved applet is over, starting again.");

                    applet.Calling = reserved;
                    applet.State = new AppletStateMgr(system);
                    applet.FocusGiven = false;

                    if (!applet.Unpopped.IsEmpty)
                    {
                        applet.PopInDataEvent.ReadableEvent.Signal();
                    }

                    continue;
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

        // [Nextendo, HOME Menu work] The applet winds (WindAndDoReserved): its process ends at
        // once, and its loop (see Start) starts the reserved applet, then this one again.
        public static void Wind(Switch device, RealApplet applet)
        {
            applet.WindRequested = true;

            if (device.System.KernelContext.Processes.TryGetValue(applet.Pid, out KProcess process))
            {
                new Thread(() =>
                {
                    Thread.Sleep(50); // the reply goes out first
                    Terminate(device.System, process);
                })
                { IsBackground = true, Name = "HLE.RealApplet.Wind" }.Start();
            }
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

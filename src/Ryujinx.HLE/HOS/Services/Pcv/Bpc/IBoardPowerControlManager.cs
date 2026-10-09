namespace Ryujinx.HLE.HOS.Services.Pcv.Bpc
{
    [Service("bpc")]
    class IBoardPowerControlManager : IpcService
    {
        public IBoardPowerControlManager(ServiceCtx context) { }

        [CommandCmif(0)]
        // ShutdownSystem()
        public ResultCode ShutdownSystem(ServiceCtx context)
        {
            // [Nextendo, HOME Menu work] A homebrew turning the console off: the emulation stops.
            HomeMenuStatus.RequestPower(reboot: false);

            return ResultCode.Success;
        }

        [CommandCmif(1)]
        // RebootSystem()
        public ResultCode RebootSystem(ServiceCtx context)
        {
            // A homebrew restarting the console (NXThemes Installer after a theme): the program
            // that runs (the HOME Menu) starts again.
            HomeMenuStatus.RequestPower(reboot: true);

            return ResultCode.Success;
        }
    }
}

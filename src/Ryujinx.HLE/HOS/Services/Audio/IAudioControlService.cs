namespace Ryujinx.HLE.HOS.Services.Audio
{
    // nn::audioctrl::detail::IAudioController: system volume and output routing, used by the
    // HOME Menu. Its commands are answered as they turn up.
    [Service("audctl")]
    class IAudioControlService : IpcService
    {
        public IAudioControlService(ServiceCtx context) { }

        [CommandCmif(5000)] // 19.0.0+
        // (u64) -> object
        // Undocumented. qlaunch 22.5.0 passes 8 bytes and keeps the returned interface as its
        // audio controller for the rest of the session (seen in its code: the out parameter is
        // a reference-counted object, released through its vtable).
        public ResultCode OpenAudioControllerSession(ServiceCtx context)
        {
            MakeObject(context, new IAudioControllerSession());

            return ResultCode.Success;
        }
    }

    class IAudioControllerSession : IpcService
    {
        public IAudioControllerSession() { }
        [CommandCmif(13)]
        // GetOutputModeSetting(u32 target) -> u32
        public ResultCode GetOutputModeSetting(ServiceCtx context)
        {
            context.ResponseData.Write(0u);

            return ResultCode.Success;
        }

        [CommandCmif(18)]
        // GetHeadphoneOutputLevelMode() -> u32
        public ResultCode GetHeadphoneOutputLevelMode(ServiceCtx context)
        {
            context.ResponseData.Write(0u);

            return ResultCode.Success;
        }

        [CommandCmif(31)]
        // IsSpeakerAutoMuteEnabled() -> bool
        public ResultCode IsSpeakerAutoMuteEnabled(ServiceCtx context)
        {
            context.ResponseData.Write(false);

            return ResultCode.Success;
        }
    }
}

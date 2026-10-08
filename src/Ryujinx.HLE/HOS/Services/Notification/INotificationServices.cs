namespace Ryujinx.HLE.HOS.Services.Notification
{
    [Service("notif:s")] // 9.0.0+
    class INotificationServices : IpcService
    {
        public INotificationServices(ServiceCtx context) { }
        
        [CommandCmif(1000)] // 9.0.0+
        // GetNotificationCount() -> nn::notification::server::INotificationSystemEventAccessor
        public ResultCode GetNotificationCount(ServiceCtx context)
        {
            MakeObject(context, new INotificationSystemEventAccessor(context));
            return ResultCode.Success;
        }
        
        [CommandCmif(1040)] // 9.0.0+
        // GetNotificationSendingNotifier() -> nn::notification::server::INotificationSystemEventAccessor
        public ResultCode GetNotificationSendingNotifier(ServiceCtx context)
        {
            MakeObject(context, new INotificationSystemEventAccessor(context));
            return ResultCode.Success;
        }
        [CommandCmif(520)]
        // ListAlarmSettings() -> (s32 count, buffer<nn::notification::AlarmSetting, 6>)
        public ResultCode ListAlarmSettings(ServiceCtx context)
        {
            context.ResponseData.Write(0);

            return ResultCode.Success;
        }
    }
}

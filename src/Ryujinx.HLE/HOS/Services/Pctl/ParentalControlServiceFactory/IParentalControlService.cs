using Ryujinx.Common.Logging;
using Ryujinx.HLE.HOS.Services.Arp;
using System;
using static LibHac.Ns.ApplicationControlProperty;

namespace Ryujinx.HLE.HOS.Services.Pctl.ParentalControlServiceFactory
{
    class IParentalControlService : IpcService
    {
        // [Nextendo, HOME Menu work] No restriction is set.
        [CommandCmif(1003)]
        // ConfirmResumeApplicationPermission(nn::ncm::ApplicationId, ...)
        public ResultCode ConfirmResumeApplicationPermission(ServiceCtx context)
        {
            return ResultCode.Success;
        }

        [CommandCmif(1010)]
        // IsRestrictedSystemSettingsEntered() -> bool
        public ResultCode IsRestrictedSystemSettingsEntered(ServiceCtx context)
        {
            context.ResponseData.Write(false);

            return ResultCode.Success;
        }

        [CommandCmif(1019)] // 20.0.0+
        // ConfirmLaunchApplicationPermission(...)
        public ResultCode ConfirmLaunchApplicationPermissionEx(ServiceCtx context)
        {
            return ResultCode.Success;
        }

        [CommandCmif(1451)]
        // StartPlayTimer()
        public ResultCode StartPlayTimer(ServiceCtx context)
        {
            return ResultCode.Success;
        }

        [CommandCmif(1452)]
        // StopPlayTimer()
        public ResultCode StopPlayTimer(ServiceCtx context)
        {
            return ResultCode.Success;
        }

        private readonly ulong _pid;
        private readonly StubEvent _synchronizationEvent;
        private readonly StubEvent _playTimerEvent;
        private readonly StubEvent _unlinkedEvent;
        private readonly int _permissionFlag;
        private ulong _titleId;
        private ParentalControlFlagValue _parentalControlFlag;
#pragma warning disable IDE0052, CS0414 // Remove unread private member
        private int[] _ratingAge;

        // TODO: Find where they are set.
        private readonly bool _restrictionEnabled = false;
        private readonly bool _featuresRestriction = false;
        private bool _freeCommunicationEnabled = false;
        private readonly bool _stereoVisionRestrictionConfigurable = true;
        private bool _stereoVisionRestriction = false;
#pragma warning restore IDE0052, CS0414

        public IParentalControlService(ServiceCtx context, ulong pid, bool withInitialize, int permissionFlag)
        {
            _pid = pid;
            _synchronizationEvent = new StubEvent(context);
            _playTimerEvent = new StubEvent(context);
            _unlinkedEvent = new StubEvent(context);
            _permissionFlag = permissionFlag;

            if (withInitialize)
            {
                Initialize(context);
            }
        }

        [CommandCmif(1)] // 4.0.0+
        // Initialize()
        public ResultCode Initialize(ServiceCtx context)
        {
            if ((_permissionFlag & 0x8001) == 0)
            {
                return ResultCode.PermissionDenied;
            }

            ResultCode resultCode = ResultCode.InvalidPid;

            if (_pid != 0)
            {
                if ((_permissionFlag & 0x40) == 0)
                {
                    ulong titleId = ApplicationLaunchProperty.GetByPid(context, _pid).TitleId;

                    if (titleId != 0)
                    {
                        _titleId = titleId;
                        var process = context.Device.Processes.GetProcess(_pid);

                        // TODO: Call nn::arp::GetApplicationControlProperty here when implemented, if it return ResultCode.Success we assign fields.
                        _ratingAge = new int[process.ApplicationControlProperties.RatingAge.Length];

                        for (int i = 0; i < _ratingAge.Length; i++)
                        {
                            _ratingAge[i] = Convert.ToInt32(process.ApplicationControlProperties.RatingAge[i]);
                        }

                        _parentalControlFlag = process.ApplicationControlProperties.ParentalControlFlag;
                    }
                }

                if (_titleId != 0)
                {
                    // TODO: Service store some private fields in another object.

                    if ((_permissionFlag & 0x8040) == 0)
                    {
                        // TODO: Service store TitleId and FreeCommunicationEnabled in another object.
                        //       When it's done it signal an event in this object.
                        Logger.Stub?.PrintStub(LogClass.ServicePctl);
                    }
                }

                resultCode = ResultCode.Success;
            }

            return resultCode;
        }

        [CommandCmif(1001)]
        // CheckFreeCommunicationPermission()
        public ResultCode CheckFreeCommunicationPermission(ServiceCtx context)
        {
            if (_parentalControlFlag == ParentalControlFlagValue.FreeCommunication && _restrictionEnabled)
            {
                // TODO: It seems to checks if an entry exists in the FreeCommunicationApplicationList using the TitleId.
                //       Then it returns FreeCommunicationDisabled if the entry doesn't exist.

                return ResultCode.FreeCommunicationDisabled;
            }

            _freeCommunicationEnabled = true;

            Logger.Stub?.PrintStub(LogClass.ServicePctl);

            return ResultCode.Success;
        }

        [CommandCmif(1017)] // 10.0.0+
        // EndFreeCommunication()
        public ResultCode EndFreeCommunication(ServiceCtx context)
        {
            _freeCommunicationEnabled = false;

            return ResultCode.Success;
        }

        [CommandCmif(1013)] // 4.0.0+
        // ConfirmStereoVisionPermission()
        public ResultCode ConfirmStereoVisionPermission(ServiceCtx context)
        {
            return IsStereoVisionPermittedImpl();
        }

        [CommandCmif(1018)]
        // IsFreeCommunicationAvailable()
        public ResultCode IsFreeCommunicationAvailable(ServiceCtx context)
        {
            if (_parentalControlFlag == ParentalControlFlagValue.FreeCommunication && _restrictionEnabled)
            {
                // TODO: It seems to checks if an entry exists in the FreeCommunicationApplicationList using the TitleId.
                //       Then it returns FreeCommunicationDisabled if the entry doesn't exist.

                return ResultCode.FreeCommunicationDisabled;
            }

            Logger.Stub?.PrintStub(LogClass.ServicePctl);

            return ResultCode.Success;
        }

        [CommandCmif(1031)]
        // IsRestrictionEnabled() -> b8
        public ResultCode IsRestrictionEnabled(ServiceCtx context)
        {
            if ((_permissionFlag & 0x140) == 0)
            {
                return ResultCode.PermissionDenied;
            }

            context.ResponseData.Write(_restrictionEnabled);

            return ResultCode.Success;
        }

        [CommandCmif(1061)] // 4.0.0+
        // ConfirmStereoVisionRestrictionConfigurable()
        public ResultCode ConfirmStereoVisionRestrictionConfigurable(ServiceCtx context)
        {
            if ((_permissionFlag & 2) == 0)
            {
                return ResultCode.PermissionDenied;
            }

            if (_stereoVisionRestrictionConfigurable)
            {
                return ResultCode.Success;
            }
            else
            {
                return ResultCode.StereoVisionRestrictionConfigurableDisabled;
            }
        }

        [CommandCmif(1062)] // 4.0.0+
        // GetStereoVisionRestriction() -> bool
        public ResultCode GetStereoVisionRestriction(ServiceCtx context)
        {
            if ((_permissionFlag & 0x200) == 0)
            {
                return ResultCode.PermissionDenied;
            }

#pragma warning disable // Remove unnecessary value assignment
            bool stereoVisionRestriction = false;

            if (_stereoVisionRestrictionConfigurable)
            {
                stereoVisionRestriction = _stereoVisionRestriction;
            }

            context.ResponseData.Write(stereoVisionRestriction);

            return ResultCode.Success;
        }

        [CommandCmif(1063)] // 4.0.0+
        // SetStereoVisionRestriction(bool)
        public ResultCode SetStereoVisionRestriction(ServiceCtx context)
        {
            if ((_permissionFlag & 0x200) == 0)
            {
                return ResultCode.PermissionDenied;
            }

            bool stereoVisionRestriction = context.RequestData.ReadBoolean();

            if (!_featuresRestriction)
            {
                if (_stereoVisionRestrictionConfigurable)
                {
                    _stereoVisionRestriction = stereoVisionRestriction;

                    // TODO: It signals an internal event of service. We have to determine where this event is used.
                }
            }

            return ResultCode.Success;
        }

        [CommandCmif(1064)] // 5.0.0+
        // ResetConfirmedStereoVisionPermission()
        public ResultCode ResetConfirmedStereoVisionPermission(ServiceCtx context)
        {
            return ResultCode.Success;
        }

        [CommandCmif(1065)] // 5.0.0+
        // IsStereoVisionPermitted() -> bool
        public ResultCode IsStereoVisionPermitted(ServiceCtx context)
        {
            bool isStereoVisionPermitted = false;

            ResultCode resultCode = IsStereoVisionPermittedImpl();

            if (resultCode == ResultCode.Success)
            {
                isStereoVisionPermitted = true;
            }

            context.ResponseData.Write(isStereoVisionPermitted);

            return resultCode;
        }

        private ResultCode IsStereoVisionPermittedImpl()
        {
            /*
                // TODO: Application Exemptions are read from file "appExemptions.dat" in the service savedata.
                //       Since we don't support the pctl savedata for now, this can be implemented later.

                if (appExemption)
                {
                    return ResultCode.Success;
                }
            */

            if (_stereoVisionRestrictionConfigurable && _stereoVisionRestriction)
            {
                return ResultCode.StereoVisionDenied;
            }
            else
            {
                return ResultCode.Success;
            }
        }
        // [Nextendo, HOME Menu work] Parental controls are off: nothing is restricted, no play
        // timer runs, the console is not linked to the mobile app.

        [CommandCmif(1006)]
        // IsRestrictionTemporaryUnlocked() -> bool
        public ResultCode IsRestrictionTemporaryUnlocked(ServiceCtx context)
        {
            context.ResponseData.Write(false);

            return ResultCode.Success;
        }

        [CommandCmif(1432)]
        // GetSynchronizationEvent() -> handle<copy>
        public ResultCode GetSynchronizationEvent(ServiceCtx context) { _synchronizationEvent.ReplyWithHandle(context); return ResultCode.Success; }

        [CommandCmif(1453)]
        // IsPlayTimerEnabled() -> bool
        public ResultCode IsPlayTimerEnabled(ServiceCtx context)
        {
            context.ResponseData.Write(false);

            return ResultCode.Success;
        }

        [CommandCmif(1454)]
        // GetPlayTimerRemainingTime() -> nn::TimeSpanType
        public ResultCode GetPlayTimerRemainingTime(ServiceCtx context)
        {
            context.ResponseData.Write(0L);

            return ResultCode.Success;
        }

        [CommandCmif(1455)]
        // IsRestrictedByPlayTimer() -> bool
        public ResultCode IsRestrictedByPlayTimer(ServiceCtx context)
        {
            context.ResponseData.Write(false);

            return ResultCode.Success;
        }

        [CommandCmif(1457)]
        // GetPlayTimerEventToRequestSuspension() -> handle<copy>
        public ResultCode GetPlayTimerEventToRequestSuspension(ServiceCtx context) { _playTimerEvent.ReplyWithHandle(context); return ResultCode.Success; }

        [CommandCmif(1458)]
        // IsPlayTimerAlarmDisabled() -> bool
        public ResultCode IsPlayTimerAlarmDisabled(ServiceCtx context)
        {
            context.ResponseData.Write(false);

            return ResultCode.Success;
        }

        [CommandCmif(1473)]
        // GetUnlinkedEvent() -> handle<copy>
        public ResultCode GetUnlinkedEvent(ServiceCtx context) { _unlinkedEvent.ReplyWithHandle(context); return ResultCode.Success; }
        [CommandCmif(1032)]
        // GetSafetyLevel() -> u32
        public ResultCode GetSafetyLevel(ServiceCtx context)
        {
            context.ResponseData.Write(0u);

            return ResultCode.Success;
        }

        [CommandCmif(1035)]
        // GetCurrentSettings() -> nn::pctl::RestrictionSettings
        public ResultCode GetCurrentSettings(ServiceCtx context)
        {
            context.ResponseData.Write((byte)0);
            context.ResponseData.Write(false);
            context.ResponseData.Write(false);

            return ResultCode.Success;
        }

        [CommandCmif(1039)]
        // GetFreeCommunicationApplicationListCount() -> s32
        public ResultCode GetFreeCommunicationApplicationListCount(ServiceCtx context)
        {
            context.ResponseData.Write(0);

            return ResultCode.Success;
        }

        [CommandCmif(1403)]
        // IsPairingActive() -> bool
        public ResultCode IsPairingActive(ServiceCtx context)
        {
            context.ResponseData.Write(false);

            return ResultCode.Success;
        }
    }
}

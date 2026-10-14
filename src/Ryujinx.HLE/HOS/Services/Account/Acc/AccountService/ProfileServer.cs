using Ryujinx.Common.Logging;
using Ryujinx.Cpu;
using Ryujinx.HLE.Utilities;
using System;
using System.Text;

namespace Ryujinx.HLE.HOS.Services.Account.Acc.AccountService
{
    class ProfileServer
    {
        private readonly UserProfile _profile;

        public ProfileServer(UserProfile profile)
        {
            _profile = profile;
        }

        public ResultCode Get(ServiceCtx context)
        {
            context.Response.PtrBuff[0] = context.Response.PtrBuff[0].WithSize(0x80UL);

            ulong bufferPosition = context.Request.RecvListBuff[0].Position;

            MemoryHelper.FillWithZeros(context.Memory, bufferPosition, 0x80);

            // [Nextendo] The user data the user page stored (see StoreImpl), when there is some.
            byte[] stored = null;

            try
            {
                stored = System.IO.File.Exists(UserDataPath) ? System.IO.File.ReadAllBytes(UserDataPath) : null;
            }
            catch (System.Exception)
            {
                stored = null;
            }

            if (stored is { Length: > 0 })
            {
                context.Memory.Write(bufferPosition, stored.AsSpan(0, System.Math.Min(stored.Length, 0x80)));

                return GetBase(context);
            }

            // TODO: Determine the struct.
            context.Memory.Write(bufferPosition, 0); // Unknown
            context.Memory.Write(bufferPosition + 4, 1); // Icon ID. 0 = Mii, the rest are character icon IDs.
            context.Memory.Write(bufferPosition + 8, (byte)1); // Profile icon background color ID
            // 0x07 bytes - Unknown
            // 0x10 bytes - Some ID related to the Mii? All zeros when a character icon is used.
            // 0x60 bytes - Usually zeros?

            Logger.Stub?.PrintStub(LogClass.ServiceAcc);

            return GetBase(context);
        }

        public ResultCode GetBase(ServiceCtx context)
        {
            _profile.UserId.Write(context.ResponseData);

            context.ResponseData.Write(_profile.LastModifiedTimestamp);

            byte[] username = StringUtils.GetFixedLengthBytes(_profile.Name, 0x20, Encoding.UTF8);

            context.ResponseData.Write(username);

            return ResultCode.Success;
        }

        // [Nextendo, HOME Menu work] The picture this session hands out, fixed at its first use: the
        // size and the picture asked next must match, even when the decorated one becomes ready
        // in between (NextendoOwnDecoration).
        private byte[] _shown;

        private byte[] Shown()
        {
            if (_shown != null)
            {
                return _shown;
            }

            byte[] image = _profile.Image;

            if (!Ryujinx.Common.Configuration.NextendoHomeMenuVanilla.Enabled && Ryujinx.Common.Configuration.NextendoAccount.IsLinked &&
                string.Equals(Ryujinx.Common.Configuration.NextendoAccount.ProfileUserId, _profile.UserId.ToString(), System.StringComparison.OrdinalIgnoreCase))
            {
                image = NextendoOwnDecoration.Apply(image);
            }

            return _shown = image;
        }

        public ResultCode GetImageSize(ServiceCtx context)
        {
            context.ResponseData.Write(Shown().Length);

            return ResultCode.Success;
        }

        public ResultCode LoadImage(ServiceCtx context)
        {
            ulong bufferPosition = context.Request.ReceiveBuff[0].Position;
            ulong bufferLen = context.Request.ReceiveBuff[0].Size;

            byte[] image = Shown();

            if ((ulong)image.Length > bufferLen)
            {
                return ResultCode.InvalidBufferSize;
            }

            context.Memory.Write(bufferPosition, image);

            context.ResponseData.Write(image.Length);

            return ResultCode.Success;
        }

        // [Nextendo, HOME Menu work] The profile as the console stores it: the nickname and the
        // icon (character, background) the user page set, kept with the emulator's profile. The
        // user data (which icon, which colour) is kept beside it, for the page to read back.
        private string UserDataPath => System.IO.Path.Combine(
            Ryujinx.Common.Configuration.AppDataManager.BaseDirPath, "system", "nextendo_profile_userdata", _profile.UserId + ".bin");

        public ResultCode Store(ServiceCtx context)
        {
            return StoreImpl(context, withImage: false);
        }

        public ResultCode StoreWithImage(ServiceCtx context)
        {
            return StoreImpl(context, withImage: true);
        }

        private ResultCode StoreImpl(ServiceCtx context, bool withImage)
        {
            // nn::account::profile::ProfileBase: uid (0x10), timestamp (8), nickname (0x20).
            context.RequestData.ReadBytes(0x18);

            string name = Encoding.UTF8.GetString(context.RequestData.ReadBytes(0x20)).TrimEnd('\0');

            byte[] userData = new byte[context.Request.PtrBuff[0].Size];

            context.Memory.Read(context.Request.PtrBuff[0].Position, userData);

            byte[] image = null;

            if (withImage && context.Request.SendBuff.Count > 0 && context.Request.SendBuff[0].Size > 0)
            {
                image = new byte[context.Request.SendBuff[0].Size];

                context.Memory.Read(context.Request.SendBuff[0].Position, image);
            }

            string newName = name.Length > 0 && name != _profile.Name ? name : null;

            // The account bound to this profile takes the change first; refused (offline, name
            // taken), nothing is changed here either.
            if ((newName != null || image != null) &&
                !Ryujinx.Common.Configuration.NextendoProfileEdits.TryPush(_profile.UserId.ToString(), newName, image))
            {
                Logger.Warning?.Print(LogClass.ServiceAcc, $"[acc] profile {_profile.UserId}: the account did not take the change, nothing stored.");

                return ResultCode.InternetRequestDenied;
            }

            AccountManager accounts = context.Device.System.AccountManager;

            if (newName != null)
            {
                accounts.SetUserName(_profile.UserId, newName);
            }

            if (image != null)
            {
                accounts.SetUserImage(_profile.UserId, image);
            }

            try
            {
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(UserDataPath));
                System.IO.File.WriteAllBytes(UserDataPath, userData);
            }
            catch (System.Exception)
            {
                // kept for this session only
            }

            Logger.Info?.Print(LogClass.ServiceAcc, $"[acc] profile {_profile.UserId} stored: name {(newName ?? "unchanged")}, icon {(image != null ? image.Length + " bytes" : "unchanged")}.");

            return ResultCode.Success;
        }
    }
}

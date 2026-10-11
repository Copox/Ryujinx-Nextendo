using LibHac;
using LibHac.Common;
using LibHac.Fs;
using LibHac.Fs.Shim;
using Ryujinx.Common;
using Ryujinx.Common.Logging;
using Ryujinx.Horizon.Sdk.Account;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace Ryujinx.HLE.HOS.Services.Account.Acc
{
    public class AccountManager : IEmulatorAccountManager
    {
        public static readonly UserId DefaultUserId = new("00000000000000010000000000000000");

        private readonly AccountSaveDataManager _accountSaveDataManager;

        // Todo: The account service doesn't have the permissions to delete save data. Qlaunch takes care of deleting
        // save data, so we're currently passing a client with full permissions. Consider moving save data deletion
        // outside of the AccountManager.
        private readonly HorizonClient _horizonClient;

        private readonly ConcurrentDictionary<string, UserProfile> _profiles;
        private UserProfile[] _storedOpenedUsers;

        // [Nextendo, HOME Menu work] Users being made by "Add User": edited, then kept or dropped.
        private readonly ConcurrentDictionary<string, UserProfile> _pending = new();

        public UserProfile LastOpenedUser { get; private set; }

        public AccountManager(HorizonClient horizonClient, string initialProfileName = null)
        {
            _horizonClient = horizonClient;

            _profiles = new ConcurrentDictionary<string, UserProfile>();
            _storedOpenedUsers = [];

            _accountSaveDataManager = new AccountSaveDataManager(_profiles);

            // [Nextendo, HOME Menu work] The default user is made only when there is no user at all:
            // the initial setup of the console takes it away once the player has made their own.
            if (_profiles.IsEmpty)
            {
                byte[] defaultUserImage = EmbeddedResources.Read("Ryujinx.HLE/HOS/Services/Account/Acc/DefaultUserImage.jpg");

                AddUser("RyuPlayer", defaultUserImage, DefaultUserId);

                OpenUser(DefaultUserId);
            }
            else
            {
                UserId commandLineUserProfileOverride = default;
                if (!string.IsNullOrEmpty(initialProfileName))
                {
                    commandLineUserProfileOverride = _profiles.Values.FirstOrDefault(x => x.Name == initialProfileName)?.UserId ?? default;
                    if (commandLineUserProfileOverride.IsNull)
                    {
                        Logger.Warning?.Print(LogClass.Application, $"The command line specified profile named '{initialProfileName}' was not found");
                    }
                }

                UserId opened = commandLineUserProfileOverride.IsNull ? _accountSaveDataManager.LastOpened : commandLineUserProfileOverride;

                OpenUser(_profiles.ContainsKey(opened.ToString()) ? opened : GetFirst().UserId);
            }
        }

        public void AddUser(string name, byte[] image, UserId userId = new())
        {
            if (userId.IsNull)
            {
                userId = new UserId(Guid.NewGuid().ToString().Replace("-", string.Empty));
            }

            UserProfile profile = new(userId, name, image);

            _profiles.AddOrUpdate(userId.ToString(), profile, (key, old) => profile);

            _accountSaveDataManager.Save(_profiles);
        }

        public void OpenUser(UserId userId)
        {
            if (_profiles.TryGetValue(userId.ToString(), out UserProfile profile))
            {
                // TODO: Support multiple open users ?
                foreach (UserProfile userProfile in GetAllUsers())
                {
                    if (userProfile == LastOpenedUser)
                    {
                        userProfile.AccountState = AccountState.Closed;

                        break;
                    }
                }

                (LastOpenedUser = profile).AccountState = AccountState.Open;

                _accountSaveDataManager.LastOpened = userId;
            }

            _accountSaveDataManager.Save(_profiles);
        }

        public void CloseUser(UserId userId)
        {
            if (_profiles.TryGetValue(userId.ToString(), out UserProfile profile))
            {
                profile.AccountState = AccountState.Closed;
            }

            _accountSaveDataManager.Save(_profiles);
        }

        public void OpenUserOnlinePlay(Uid userId)
        {
            OpenUserOnlinePlay(new UserId((long)userId.Low, (long)userId.High));
        }

        public void OpenUserOnlinePlay(UserId userId)
        {
            if (_profiles.TryGetValue(userId.ToString(), out UserProfile profile))
            {
                // TODO: Support multiple open online users ?
                foreach (UserProfile userProfile in GetAllUsers())
                {
                    if (userProfile == LastOpenedUser)
                    {
                        userProfile.OnlinePlayState = AccountState.Closed;

                        break;
                    }
                }

                profile.OnlinePlayState = AccountState.Open;
            }

            _accountSaveDataManager.Save(_profiles);
        }

        public void CloseUserOnlinePlay(Uid userId)
        {
            CloseUserOnlinePlay(new UserId((long)userId.Low, (long)userId.High));
        }

        public void CloseUserOnlinePlay(UserId userId)
        {
            if (_profiles.TryGetValue(userId.ToString(), out UserProfile profile))
            {
                profile.OnlinePlayState = AccountState.Closed;
            }

            _accountSaveDataManager.Save(_profiles);
        }

        public UserId BeginUserRegistration()
        {
            UserId userId = new(Guid.NewGuid().ToString("N"));

            _pending[userId.ToString()] = new UserProfile(userId, "", []);

            return userId;
        }

        public bool CompleteUserRegistration(UserId userId)
        {
            if (!_pending.TryRemove(userId.ToString(), out UserProfile profile))
            {
                return _profiles.ContainsKey(userId.ToString());
            }

            AddUser(profile.Name, profile.Image, userId);

            Logger.Info?.Print(LogClass.ServiceAcc, $"[acc] user {userId} ({profile.Name}) added from the HOME Menu");

            return true;
        }

        public void CancelUserRegistration(UserId userId)
        {
            _pending.TryRemove(userId.ToString(), out _);
        }

        internal bool TryGetPendingUser(UserId userId, out UserProfile profile)
        {
            return _pending.TryGetValue(userId.ToString(), out profile);
        }

        public void SetUserImage(UserId userId, byte[] image)
        {
            if (_pending.TryGetValue(userId.ToString(), out UserProfile pending))
            {
                pending.Image = image;

                return;
            }

            foreach (UserProfile userProfile in GetAllUsers())
            {
                if (userProfile.UserId == userId)
                {
                    userProfile.Image = image;

                    break;
                }
            }

            _accountSaveDataManager.Save(_profiles);
        }

        public void SetUserName(UserId userId, string name)
        {
            if (_pending.TryGetValue(userId.ToString(), out UserProfile pending))
            {
                pending.Name = name;

                return;
            }

            foreach (UserProfile userProfile in GetAllUsers())
            {
                if (userProfile.UserId == userId)
                {
                    userProfile.Name = name;

                    break;
                }
            }

            _accountSaveDataManager.Save(_profiles);
        }

        public void DeleteUser(UserId userId)
        {
            try
            {
                DeleteSaveData(userId);
            }
            catch (Exception e)
            {
                Logger.Warning?.Print(LogClass.ServiceAcc, $"[acc] saves of {userId} not all deleted: {e.Message}");
            }

            _profiles.Remove(userId.ToString(), out _);

            if (LastOpenedUser == null || LastOpenedUser.UserId == userId)
            {
                OpenUser(_profiles.ContainsKey(DefaultUserId.ToString()) || _profiles.IsEmpty ? DefaultUserId : GetFirst().UserId);
            }

            _accountSaveDataManager.Save(_profiles);
        }

        public void DeleteUserSaveData(UserId userId)
        {
            try
            {
                DeleteSaveData(userId);

                Logger.Info?.Print(LogClass.ServiceAcc, $"[acc] saves of {userId} deleted");
            }
            catch (Exception e)
            {
                Logger.Warning?.Print(LogClass.ServiceAcc, $"[acc] saves of {userId} not all deleted: {e.Message}");
            }
        }

        private void DeleteSaveData(UserId userId)
        {
            SaveDataFilter saveDataFilter = SaveDataFilter.Make(programId: default, saveType: default,
                new LibHac.Fs.UserId((ulong)userId.High, (ulong)userId.Low), saveDataId: default, index: default);

            using UniqueRef<SaveDataIterator> saveDataIterator = new();

            _horizonClient.Fs.OpenSaveDataIterator(ref saveDataIterator.Ref, SaveDataSpaceId.User, in saveDataFilter).ThrowIfFailure();

            Span<SaveDataInfo> saveDataInfo = stackalloc SaveDataInfo[10];

            while (true)
            {
                saveDataIterator.Get.ReadSaveDataInfo(out long readCount, saveDataInfo).ThrowIfFailure();

                if (readCount == 0)
                {
                    break;
                }

                for (int i = 0; i < readCount; i++)
                {
                    _horizonClient.Fs.DeleteSaveData(SaveDataSpaceId.User, saveDataInfo[i].SaveDataId).ThrowIfFailure();
                }
            }
        }

        internal int GetUserCount()
        {
            return _profiles.Count - (IsHidden(_hiddenDuringSetup) ? 1 : 0);
        }

        internal bool TryGetUser(UserId userId, out UserProfile profile)
        {
            if (userId == _hiddenDuringSetup && IsHidden(userId))
            {
                profile = null;

                return false;
            }

            return _profiles.TryGetValue(userId.ToString(), out profile);
        }

        // [Nextendo, HOME Menu work] The initial setup on a new console: the user the emulator made
        // by itself (RyuPlayer) is not shown, so the setup asks for the player's own user; once
        // the setup is over with a user of the player's, the default one goes away.
        private UserId _hiddenDuringSetup;

        private bool IsHidden(UserId userId) => !userId.IsNull && _profiles.ContainsKey(userId.ToString());

        /// <summary>The initial setup starts: the default user the emulator made by itself is not shown.</summary>
        public void HideDefaultUserForSetup()
        {
            if (_profiles.TryGetValue(DefaultUserId.ToString(), out UserProfile only) && only.Name == "RyuPlayer")
            {
                _hiddenDuringSetup = DefaultUserId;

                Logger.Info?.Print(LogClass.ServiceAcc, "[setup] default user hidden during the initial setup");
            }
        }

        /// <summary>The initial setup is over: the default user goes when the player made one.</summary>
        public void EndSetup()
        {
            UserId hidden = _hiddenDuringSetup;

            _hiddenDuringSetup = default;

            if (!hidden.IsNull && _profiles.Count > 1 && _profiles.ContainsKey(hidden.ToString()))
            {
                _profiles.Remove(hidden.ToString(), out _);

                if (LastOpenedUser == null || LastOpenedUser.UserId == hidden)
                {
                    OpenUser(GetFirst().UserId);
                }

                _accountSaveDataManager.Save(_profiles);

                Logger.Info?.Print(LogClass.ServiceAcc, "[setup] default user removed: the player made their own");
            }
        }

        public IEnumerable<UserProfile> GetAllUsers()
        {
            List<string> order = UserOrder();

            return _profiles.Values.Where(profile => !(profile.UserId == _hiddenDuringSetup && IsHidden(profile.UserId))).OrderBy(profile =>
            {
                int index = order.IndexOf(profile.UserId.ToString());

                return index < 0 ? int.MaxValue : index;
            });
        }

        // [Nextendo, HOME Menu work] The order of the users ("Change Order" of the HOME Menu).
        private static string UserOrderPath => System.IO.Path.Combine(Ryujinx.Common.Configuration.AppDataManager.BaseDirPath, "nextendo_user_order.txt");

        private List<string> _userOrder;

        private List<string> UserOrder()
        {
            if (_userOrder == null)
            {
                try
                {
                    _userOrder = System.IO.File.Exists(UserOrderPath) ? System.IO.File.ReadAllLines(UserOrderPath).Where(x => x.Length > 0).ToList() : [];
                }
                catch (Exception)
                {
                    _userOrder = [];
                }
            }

            return _userOrder;
        }

        public void SetUserPosition(UserId userId, int position)
        {
            List<string> order = GetAllUsers().Select(profile => profile.UserId.ToString()).ToList();

            order.Remove(userId.ToString());
            order.Insert(Math.Clamp(position, 0, order.Count), userId.ToString());
            _userOrder = order;

            try
            {
                System.IO.File.WriteAllLines(UserOrderPath, order);
            }
            catch (Exception e)
            {
                Logger.Warning?.Print(LogClass.ServiceAcc, $"[acc] order of the users not kept: {e.Message}");
            }
        }

        internal IEnumerable<UserProfile> GetOpenedUsers()
        {
            return _profiles.Values.Where(x => x.AccountState == AccountState.Open);
        }

        internal IEnumerable<UserProfile> GetStoredOpenedUsers()
        {
            return _storedOpenedUsers;
        }

        internal void StoreOpenedUsers()
        {
            _storedOpenedUsers = _profiles.Values.Where(x => x.AccountState == AccountState.Open).ToArray();
        }

        internal UserProfile GetFirst()
        {
            return _profiles.First().Value;
        }
    }
}

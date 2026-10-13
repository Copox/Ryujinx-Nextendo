using Ryujinx.Common.Logging;
using Ryujinx.SDL3.Common;
using System;
using System.Collections.Generic;
using System.Threading;
using SDL;
using System.Linq;
using static SDL.SDL3;

namespace Ryujinx.Input.SDL3
{
    
    
    public unsafe class SDL3GamepadDriver : IGamepadDriver
    {
        private readonly Dictionary<SDL_JoystickID, string> _gamepadsInstanceIdsMapping;
        private readonly Dictionary<SDL_JoystickID, string> _gamepadsIds;
        /// <summary>
        /// Unlinked joy-cons
        /// </summary>
        private readonly Dictionary<SDL_JoystickID, string> _joyConsIds;
        /// <summary>
        /// Linked joy-cons, remove dual joy-con from <c>_gamepadsIds</c> when a linked joy-con is removed
        /// </summary>
        private readonly Dictionary<SDL_JoystickID,string> _linkedJoyConsIds;
        private readonly Lock _lock = new();

        public ReadOnlySpan<string> GamepadsIds
        {
            get
            {
                lock (_lock)
                {
                    List<string> temp = [];
                    temp.AddRange(_gamepadsIds.Values);
                    temp.AddRange(_joyConsIds.Values);
                    temp.AddRange(_linkedJoyConsIds.Values);
                    return temp.ToArray();
                }
            }
        }

        public string DriverName => "SDL3";

        public event Action<string> OnGamepadConnected;
        public event Action<string> OnGamepadDisconnected;

        public SDL3GamepadDriver()
        {
            _gamepadsInstanceIdsMapping = new Dictionary<SDL_JoystickID, string>();
            _gamepadsIds = [];
            _joyConsIds = [];
            _linkedJoyConsIds = [];

            SDL3Driver.Instance.Initialize();
            SDL3Driver.Instance.OnJoyStickConnected += HandleJoyStickConnected;
            SDL3Driver.Instance.OnJoystickDisconnected += HandleJoyStickDisconnected;
            SDL3Driver.Instance.OnJoyBatteryUpdated += HandleJoyBatteryUpdated;

            // Add already connected gamepads
            int joystickCount = 0;

            SDL_JoystickID* pJoystickInstanceIds = SDL_GetJoysticks(&joystickCount);

            for (int i = 0; i < joystickCount; i++)
            {
                HandleJoyStickConnected(pJoystickInstanceIds[i]);
            }
        }

        private unsafe static string SDLGuidToString(SDL_GUID guid)
        {
            string map = "0123456789abcdef";
            char[] guidBytes = new char[33];

            for (int i = 0; i < 16; i++) {
                byte c = guid.data[i];
                guidBytes[i * 2] = map[c >> 4];
                guidBytes[(i * 2) + 1] = map[c & 0x0f];
            }

            string strGuid = new(guidBytes);

            return $"{strGuid[4..6]}{strGuid[6..8]}{strGuid[2..4]}{strGuid[0..2]}-{strGuid[10..12]}{strGuid[8..10]}-{strGuid[12..16]}-{strGuid[16..20]}-{strGuid[20..32]}";

        }

        private unsafe string GenerateGamepadId(SDL_JoystickID joystickInstanceId)
        {
            SDL_GUID sdlGuid = SDL_GetJoystickGUIDForID(joystickInstanceId);
            string guidBytes = SDLGuidToString(sdlGuid);
            Guid guid = Guid.Parse(guidBytes);

            // Add a unique identifier to the start of the GUID in case of duplicates.

            if (guid == Guid.Empty)
            {
                return null;
            }

            // Remove the first 4 char of the guid (CRC part) to make it stable
            string guidString = $"0000{guid.ToString()[4..]}";

            string id;

            lock (_lock)
            {
                int guidIndex = 0;
                id = guidIndex + "-" + guidString;

                while (_gamepadsIds.ContainsValue(id) || _joyConsIds.ContainsValue(id) || _linkedJoyConsIds.ContainsValue(id))
                {
                    id = (++guidIndex) + "-" + guidString;
                }
            }

            return id;
        }

        // [Nextendo] Controllers of the computer, for the emulator's notifications: one gone, one
        // low on battery (by name). The keyboard is no controller here: it has neither.
        public static event Action<string> NextendoControllerDisconnected;
        // A controller plugged in or paired while the emulator runs (those found when it starts
        // are not told).
        public static event Action<string> NextendoControllerConnected;

        private static readonly DateTime _nextendoSince = DateTime.UtcNow;
        public static event Action<string, int> NextendoControllerBatteryLow;

        private static readonly System.Collections.Concurrent.ConcurrentDictionary<SDL_JoystickID, string> _names = new();

        /// <summary>The names of the controllers of the computer, as the system gives them.</summary>
        public static System.Collections.Generic.ICollection<string> NextendoControllerNames => _names.Values;
        private readonly System.Collections.Concurrent.ConcurrentDictionary<SDL_JoystickID, bool> _toldLow = new();

        private void HandleJoyStickDisconnected(SDL_JoystickID joystickInstanceId)
        {
            if (_names.TryRemove(joystickInstanceId, out string name) && !string.IsNullOrEmpty(name))
            {
                NextendoControllerDisconnected?.Invoke(name);
            }

            _toldLow.TryRemove(joystickInstanceId, out _);

            bool joyConPairDisconnected = false;
            string fakeId = null;

            if (!_gamepadsInstanceIdsMapping.Remove(joystickInstanceId, out string id))
                return;

            lock (_lock)
            {
                if (!_linkedJoyConsIds.ContainsKey(joystickInstanceId))
                {
                    if (!_joyConsIds.Remove(joystickInstanceId))
                    {
                        _gamepadsIds.Remove(joystickInstanceId);
                    }
                }
                else
                {
                    foreach (string matchId in _gamepadsIds.Values)
                    {
                        if (matchId.Contains(id))
                        {
                            fakeId = matchId;
                            break;
                        }
                    }
                        
                    string leftId = fakeId!.Split('_')[0];
                    string rightId = fakeId!.Split('_')[1];

                    if (leftId == id)
                    {
                        _linkedJoyConsIds.Remove(GetInstanceIdFromId(rightId));
                        _joyConsIds.Add(GetInstanceIdFromId(rightId), rightId);
                    }
                    else
                    {
                        _linkedJoyConsIds.Remove(GetInstanceIdFromId(leftId));
                        _joyConsIds.Add(GetInstanceIdFromId(leftId), leftId);
                    }
                        
                    _linkedJoyConsIds.Remove(joystickInstanceId);
                    _gamepadsIds.Remove(GetInstanceIdFromId(fakeId));
                    joyConPairDisconnected = true;
                }
            }

            OnGamepadDisconnected?.Invoke(id);
            if (joyConPairDisconnected)
            {
                OnGamepadDisconnected?.Invoke(fakeId);
            }
        }

        private void HandleJoyStickConnected(SDL_JoystickID joystickInstanceId)
        {
            bool joyConPairConnected = false;
            string fakeId = null;

            if (SDL_IsGamepad(joystickInstanceId))
            {
                if (_gamepadsInstanceIdsMapping.ContainsKey(joystickInstanceId))
                {
                    // Sometimes a JoyStick connected event fires after the app starts even though it was connected before
                    // so it is rejected to avoid doubling the entries.
                    return;
                }

                string id = GenerateGamepadId(joystickInstanceId);

                if (id == null)
                {
                    return;
                }

                if (_gamepadsInstanceIdsMapping.TryAdd(joystickInstanceId, id))
                {
                    _names[joystickInstanceId] = SDL_GetGamepadNameForID(joystickInstanceId);

                    if (DateTime.UtcNow - _nextendoSince > TimeSpan.FromSeconds(10) && !string.IsNullOrEmpty(_names[joystickInstanceId]))
                    {
                        string plugged = _names[joystickInstanceId];

                        if (SDL3JoyCon.IsJoyCon(joystickInstanceId))
                        {
                            // its colours first (read from it), away from the events of the controllers
                            string path = SDL_GetGamepadPathForID(joystickInstanceId);

                            System.Threading.Tasks.Task.Run(() =>
                            {
                                JoyConColors.Read(plugged, path);
                                NextendoControllerConnected?.Invoke(plugged);
                            });
                        }
                        else
                        {
                            NextendoControllerConnected?.Invoke(plugged);
                        }
                    }

                    lock (_lock)
                    {
                        if (!SDL3JoyCon.IsJoyCon(joystickInstanceId))
                        {
                            _gamepadsIds.Add(joystickInstanceId, id);
                        }
                        else
                        {
                            if (SDL3JoyConPair.IsCombinable(joystickInstanceId, _joyConsIds, out SDL_JoystickID match))
                            {
                                _joyConsIds.Remove(match, out string matchId);
                                _linkedJoyConsIds.Add(joystickInstanceId, id);
                                _linkedJoyConsIds.Add(match, matchId);
                                
                                uint fakeInstanceId = uint.MaxValue;
                                fakeId = SDL3JoyCon.IsLeftJoyCon(joystickInstanceId)
                                    ? $"{id}_{matchId}"
                                    : $"{matchId}_{id}";
                                while (!_gamepadsIds.TryAdd((SDL_JoystickID)fakeInstanceId, fakeId))
                                {
                                    fakeInstanceId--;
                                }
                                _gamepadsInstanceIdsMapping.Add((SDL_JoystickID)fakeInstanceId, fakeId);
                                joyConPairConnected = true;
                            }
                            else
                            {
                                _joyConsIds.Add(joystickInstanceId, id);
                            }
                        }
                    }

                    OnGamepadConnected?.Invoke(id);
                    if (joyConPairConnected)
                    {
                        OnGamepadConnected?.Invoke(fakeId);
                    }
                }
            }
        }

        private void HandleJoyBatteryUpdated(SDL_JoystickID joystickInstanceId, SDL_PowerState powerLevel)
        {
            Logger.Info?.Print(LogClass.Hid,
                $"{SDL_GetGamepadNameForID(joystickInstanceId)} power level: {powerLevel}");

            if (powerLevel != SDL_PowerState.SDL_POWERSTATE_ON_BATTERY)
            {
                _toldLow.TryRemove(joystickInstanceId, out _);

                return;
            }

            int percent = -1;

            unsafe
            {
                SDL_Joystick* joystick = SDL_GetJoystickFromID(joystickInstanceId);

                if (joystick != null)
                {
                    int level;

                    SDL_GetJoystickPowerInfo(joystick, &level);
                    percent = level;
                }
            }

            // Told once under 15 %, again only after it went over 20 % (charged).
            if (percent > 20)
            {
                _toldLow.TryRemove(joystickInstanceId, out _);
            }
            else if (percent >= 0 && percent <= 15 && _toldLow.TryAdd(joystickInstanceId, true))
            {
                NextendoControllerBatteryLow?.Invoke(SDL_GetGamepadNameForID(joystickInstanceId), percent);
            }
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                SDL3Driver.Instance.OnJoyStickConnected -= HandleJoyStickConnected;
                SDL3Driver.Instance.OnJoystickDisconnected -= HandleJoyStickDisconnected;

                // Simulate a full disconnect when disposing
                foreach (var gamepad in _gamepadsIds)
                {
                    OnGamepadDisconnected?.Invoke(gamepad.Value);
                }
                
                foreach (var gamepad in _joyConsIds)
                {
                    OnGamepadDisconnected?.Invoke(gamepad.Value);
                }
                
                foreach (var gamepad in _linkedJoyConsIds)
                {
                    OnGamepadDisconnected?.Invoke(gamepad.Value);
                }

                lock (_lock)
                {
                    _gamepadsIds.Clear();
                    _joyConsIds.Clear();
                    _linkedJoyConsIds.Clear();
                }

                SDL3Driver.Instance.Dispose();
            }
        }

        public void Dispose()
        {
            GC.SuppressFinalize(this);
            Dispose(true);
        }

        public SDL_JoystickID GetInstanceIdFromId(string id) {
            return _gamepadsInstanceIdsMapping.Where(e => e.Value == id).FirstOrDefault().Key;
        }

        public IGamepad GetGamepad(string id)
        {
            // joy-con pair ids is the combined ids of its parts which are split using a '_'
            if (id.Contains('_'))
            {
                lock (_lock)
                {
                    string leftId = id.Split('_')[0];
                    string rightId = id.Split('_')[1];
                    
                    SDL_JoystickID leftInstanceId = GetInstanceIdFromId(leftId);
                    SDL_JoystickID rightInstanceId = GetInstanceIdFromId(rightId);

                    SDL_Gamepad* leftGamepadHandle = SDL_OpenGamepad(leftInstanceId);
                    SDL_Gamepad* rightGamepadHandle = SDL_OpenGamepad(rightInstanceId);

                    if (leftGamepadHandle == null || rightGamepadHandle == null)
                    {
                        return null;
                    }

                    return new SDL3JoyConPair(new SDL3JoyCon(leftGamepadHandle, leftId),
                        new SDL3JoyCon(rightGamepadHandle, rightId));
                }
            }

            SDL_JoystickID instanceId = GetInstanceIdFromId(id);

            SDL_Gamepad* gamepadHandle = SDL_OpenGamepad(instanceId);

            if (gamepadHandle == null)
            {
                return null;
            }

            if (SDL3JoyCon.IsJoyCon(instanceId))
            {
                return new SDL3JoyCon(gamepadHandle, id);
            }

            return new SDL3Gamepad(gamepadHandle, id);
        }

        public IEnumerable<IGamepad> GetGamepads()
        {
            lock (_gamepadsIds)
            {
                foreach (var gamepad in _gamepadsIds)
                {
                    yield return GetGamepad(gamepad.Value);
                }
            }

            lock (_joyConsIds)
            {
                foreach (var gamepad in _joyConsIds)
                {
                    yield return GetGamepad(gamepad.Value);
                }
            }
            
            lock (_linkedJoyConsIds)
            {
                foreach (var gamepad in _linkedJoyConsIds)
                {
                    yield return GetGamepad(gamepad.Value);
                }
            }
        }
    }
}

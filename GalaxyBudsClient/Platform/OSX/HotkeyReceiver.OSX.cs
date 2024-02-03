using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GalaxyBudsClient.Model;
using GalaxyBudsClient.Model.Hotkeys;
using GalaxyBudsClient.Platform.Interfaces;
using Serilog;
using ThePBone.OSX.Native.Unmanaged;

namespace GalaxyBudsClient.Platform.OSX
{
    public class HotkeyReceiver : IHotkeyReceiver
    {
#if OSX
        private unsafe void* _hotkeyMgrObjc = null;
        private readonly AppUtils.HotkeyOnDispatch Callback;
        private readonly IList<Hotkey> _hotkeys = new List<Hotkey>();
        private static readonly SemaphoreSlim Semaphore = new(1, 1);

        public HotkeyReceiver()
        {
            Callback = OnDispatchHotkey;
            unsafe
            {
                fixed (void** ptr = &_hotkeyMgrObjc)
                {
                    AppUtils.allocHotkeyMgr(ptr, Callback);
                }

                if (_hotkeyMgrObjc == null)
                {
                    Log.Error("OSX.HotkeyReceiver: failed to allocate hotkey manager");
                }
            }
        }

        private void OnDispatchHotkey(uint identifier)
        {
            var key = _hotkeys[(int)identifier - 1];
            EventDispatcher.Instance.Dispatch(key.Action);
        }
#endif
        
        public async Task RegisterHotkeyAsync(Hotkey hotkey)
        {
#if OSX
            ModifierKeys modFlags = 0;
            if (hotkey.Modifier.ToList().Count > 0)
            {
                modFlags = hotkey.Modifier.Aggregate((prev, next) => prev | next);
            }
            
            if (hotkey.Keys.ToList().Count != 1)
            {
                throw new HotkeyRegisterException($"More than one key isn't supported, sorry", hotkey);
            }
            Keys keyFlags = hotkey.Keys.ToList()[0];
            await Task.Run(async () =>
            {
                if (!await Semaphore.WaitAsync(5000))
                {
                    throw new HotkeyRegisterException("Hotkey processing blocked, this shouldn't happen", hotkey);
                }
                Log.Debug("OSX.HotkeyReceiver: Registering hotkey...");
                _hotkeys.Add(hotkey);
                bool result;
                unsafe
                {
                    result = AppUtils.registerHotKey(_hotkeyMgrObjc, (uint)keyFlags, (uint)modFlags);
                }

                Semaphore.Release();
                Log.Debug("OSX.HotkeyReceiver: Registered hotkey.");

                if (!result)
                {
                    throw new HotkeyRegisterException($"Failed to register hotkey", hotkey);
                }
            });
#endif
        }
        
        public async Task ValidateHotkeyAsync(Hotkey hotkey)
        {
#if OSX
            Log.Debug("OSX.HotkeyReceiver: Validating hotkey...");
            HotkeyRegisterException? error = null;
            var backup = _hotkeys.ToList();
            await UnregisterAllAsync();
            try
            {
                await RegisterHotkeyAsync(hotkey);
            }
            catch (HotkeyRegisterException ex)
            {
                error = ex;
            }
                
            await UnregisterAllAsync();
            foreach (var b in backup)
            {
                try
                {
                    await RegisterHotkeyAsync(b);
                }
                catch(HotkeyRegisterException){}
            }

            if (error != null)
            {
                throw error;
            }

            Log.Debug("OSX.HotkeyReceiver: Done validating hotkey.");
#endif
        }

        public async Task UnregisterAllAsync()
        {
#if OSX
            await Task.Run(async () => {
                Log.Debug("OSX.HotkeyReceiver: Unregistering hotkeys...");
                if (!await Semaphore.WaitAsync(5000))
                {
                    Log.Error("OSX.HotkeyReceiver: Blocked while trying to unregister hotkeys");
                    return;
                }
                unsafe
                {
                    AppUtils.unregisterAllHotkeys(_hotkeyMgrObjc);
                }

                // Unregister all the registered hotkeys.
                _hotkeys.Clear();

                Semaphore.Release();
            
                Log.Debug("OSX.HotkeyReceiver: All hotkeys unregistered");
            });
#endif
        }

        public async void Dispose()
        {
#if OSX
            await UnregisterAllAsync();
            unsafe
            {
                if (_hotkeyMgrObjc != null)
                {
                    AppUtils.deallocHotkeyMgr(_hotkeyMgrObjc);
                    _hotkeyMgrObjc = null;
                }
            }
#endif
            GC.SuppressFinalize(this);
        }
    }
}
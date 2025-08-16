#include "framework.h"
#include "TextHooker.h"

BOOL APIENTRY DllMain(HMODULE hModule, DWORD ul_reason_for_call, LPVOID lpReserved)
{
    switch (ul_reason_for_call)
    {
    case DLL_PROCESS_ATTACH:
        {
            // Disable DLL_THREAD_ATTACH and DLL_THREAD_DETACH notifications for performance
            DisableThreadLibraryCalls(hModule);
            
            // Auto-install hooks when DLL is loaded into target process
            // This happens automatically when we inject the DLL
            InstallHooks(GetCurrentProcessId(), nullptr);
        }
        break;
        
    case DLL_PROCESS_DETACH:
        {
            // Clean up when DLL is unloaded
            RemoveHooks(GetCurrentProcessId());
        }
        break;
        
    case DLL_THREAD_ATTACH:
    case DLL_THREAD_DETACH:
        // These are disabled by DisableThreadLibraryCalls
        break;
    }
    return TRUE;
}
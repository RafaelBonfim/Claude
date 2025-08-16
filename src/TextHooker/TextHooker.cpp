#include "framework.h"
#include "TextHooker.h"
#include <fstream>
#include <shlwapi.h>
#pragma comment(lib, "shlwapi.lib")

// --- Variáveis Globais ---
static std::unordered_set<std::wstring> g_seenTexts;
static std::mutex g_textMutex;
static int g_currentProcessId = 0;
static bool g_isHookActive = false;
static std::wstring g_communicationFilePath;

// --- Ponteiros para as Funções Originais ---
static BOOL (WINAPI* TextOutW_Original)(HDC, int, int, LPCWSTR, int) = nullptr;
static int (WINAPI* DrawTextW_Original)(HDC, LPCWSTR, int, LPRECT, UINT) = nullptr;
static BOOL (WINAPI* ExtTextOutW_Original)(HDC, int, int, UINT, CONST RECT*, LPCWSTR, UINT, CONST INT*) = nullptr;

// --- Função para Salvar Texto Capturado ---
void SaveCapturedText(const std::wstring& text)
{
    try 
    {
        std::lock_guard<std::mutex> lock(g_textMutex);
        
        // Open file in append mode
        std::wofstream file(g_communicationFilePath, std::ios::app);
        if (file.is_open())
        {
            file << text << L"\n";
            file.close();
        }
    }
    catch (...)
    {
        // Ignore file errors
    }
}

// --- Função Auxiliar para Processar o Texto Capturado ---
void ProcessAndSendText(const wchar_t* text, int source, UINT32 length = 0)
{
    if (!text || !g_isHookActive) return;

    size_t text_len = (length > 0) ? length : wcslen(text);
    if (text_len < 3 || text_len > 1000) return; // Filter too short or too long texts
    
    std::wstring text_str(text, text_len);
    
    // Basic filtering
    if (text_str.find(L"fps") != std::wstring::npos ||
        text_str.find(L"FPS") != std::wstring::npos ||
        text_str.find(L"debug") != std::wstring::npos ||
        text_str.find(L"DEBUG") != std::wstring::npos ||
        text_str.find(L".dll") != std::wstring::npos ||
        text_str.find(L"null") != std::wstring::npos ||
        text_str.find(L"NULL") != std::wstring::npos)
    {
        return; // Skip debug/technical text
    }

    // Check for duplicate
    std::lock_guard<std::mutex> lock(g_textMutex);
    if (g_seenTexts.find(text_str) != g_seenTexts.end()) return;
    
    g_seenTexts.insert(text_str);
    
    // Limit cache size
    if (g_seenTexts.size() > 500)
    {
        g_seenTexts.clear();
    }
    
    // Save to communication file
    SaveCapturedText(text_str);
}

// --- Funções Hooked ---
BOOL WINAPI TextOutW_Hook(HDC hdc, int nXStart, int nYStart, LPCWSTR lpString, int cchString)
{
    if (lpString && cchString > 0)
    {
        ProcessAndSendText(lpString, SOURCE_TEXTOUT, cchString);
    }
    return TextOutW_Original(hdc, nXStart, nYStart, lpString, cchString);
}

int WINAPI DrawTextW_Hook(HDC hdc, LPCWSTR lpchText, int cchText, LPRECT lprc, UINT format)
{
    if (lpchText)
    {
        ProcessAndSendText(lpchText, SOURCE_DRAWTEXT, cchText == -1 ? 0 : cchText);
    }
    return DrawTextW_Original(hdc, lpchText, cchText, lprc, format);
}

BOOL WINAPI ExtTextOutW_Hook(HDC hdc, int X, int Y, UINT fuOptions, CONST RECT* lprc, LPCWSTR lpString, UINT cbCount, CONST INT* lpDx)
{
    if (lpString && cbCount > 0)
    {
        ProcessAndSendText(lpString, SOURCE_EXTTEXTOUT, cbCount);
    }
    return ExtTextOutW_Original(hdc, X, Y, fuOptions, lprc, lpString, cbCount, lpDx);
}

// --- Funções Exportadas ---
extern "C" __declspec(dllexport) bool InstallHooks(int processId, TextCallback callback)
{
    if (g_isHookActive) return true;

    g_currentProcessId = GetCurrentProcessId(); // Use actual current process ID
    
    // Setup communication file path
    wchar_t tempPath[MAX_PATH];
    GetTempPathW(MAX_PATH, tempPath);
    g_communicationFilePath = std::wstring(tempPath) + L"UGT_Process_" + std::to_wstring(g_currentProcessId) + L".txt";

    if (MH_Initialize() != MH_OK) 
    {
        return false;
    }

    // Hook GDI text functions
    MH_STATUS status1 = MH_CreateHook(&TextOutW, &TextOutW_Hook, reinterpret_cast<LPVOID*>(&TextOutW_Original));
    MH_STATUS status2 = MH_CreateHook(&DrawTextW, &DrawTextW_Hook, reinterpret_cast<LPVOID*>(&DrawTextW_Original));
    MH_STATUS status3 = MH_CreateHook(&ExtTextOutW, &ExtTextOutW_Hook, reinterpret_cast<LPVOID*>(&ExtTextOutW_Original));

    if (status1 != MH_OK && status2 != MH_OK && status3 != MH_OK)
    {
        MH_Uninitialize();
        return false;
    }

    if (MH_EnableHook(MH_ALL_HOOKS) != MH_OK) 
    {
        MH_Uninitialize();
        return false;
    }
    
    g_isHookActive = true;
    
    // Write initial marker to communication file
    SaveCapturedText(L"[HOOK_ACTIVE]");
    
    return true;
}

extern "C" __declspec(dllexport) bool RemoveHooks(int processId)
{
    if (!g_isHookActive) return true;

    MH_DisableHook(MH_ALL_HOOKS);
    MH_Uninitialize();

    g_isHookActive = false;
    g_currentProcessId = 0;
    
    // Write final marker to communication file
    SaveCapturedText(L"[HOOK_REMOVED]");
    
    return true;
}

extern "C" __declspec(dllexport) bool IsHookActive(int processId)
{
    return g_isHookActive;
}

extern "C" __declspec(dllexport) void ClearTextCache()
{
    std::lock_guard<std::mutex> lock(g_textMutex);
    g_seenTexts.clear();
}

extern "C" __declspec(dllexport) int GetCachedTextCount()
{
    std::lock_guard<std::mutex> lock(g_textMutex);
    return static_cast<int>(g_seenTexts.size());
}
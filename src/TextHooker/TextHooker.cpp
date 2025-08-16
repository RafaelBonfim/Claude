#include "framework.h"
#include "TextHooker.h"

// --- Variáveis Globais ---
static TextCallback g_callback = nullptr;
static std::unordered_set<std::wstring> g_seenTexts;
static std::mutex g_textMutex;
static int g_currentProcessId = 0;
static bool g_isHookActive = false;

// --- Ponteiros para as Funções Originais ---
static BOOL (WINAPI* TextOutW_Original)(HDC, int, int, LPCWSTR, int) = nullptr;
static int (WINAPI* DrawTextW_Original)(HDC, LPCWSTR, int, LPRECT, UINT) = nullptr;
static BOOL (WINAPI* ExtTextOutW_Original)(HDC, int, int, UINT, CONST RECT*, LPCWSTR, UINT, CONST INT*) = nullptr;
static HRESULT (WINAPI* CreateTextLayout_Original)(IDWriteFactory*, const WCHAR*, UINT32, IDWriteTextFormat*, FLOAT, FLOAT, IDWriteTextLayout**) = nullptr;


// --- Função Auxiliar para Processar o Texto Capturado ---
void ProcessAndSendText(const wchar_t* text, int source, UINT32 length = 0)
{
    if (!g_callback || !text) return;

    size_t text_len = (length > 0) ? length : wcslen(text);
    if (text_len < 3) return;
    
    std::wstring text_str(text, text_len);

    std::lock_guard<std::mutex> lock(g_textMutex);
    if (g_seenTexts.find(text_str) == g_seenTexts.end())
    {
        g_seenTexts.insert(text_str);
        g_callback(text_str.c_str(), source, g_currentProcessId);
    }
}

// --- Funções Hooked ---
BOOL WINAPI TextOutW_Hook(HDC hdc, int nXStart, int nYStart, LPCWSTR lpString, int cchString)
{
    ProcessAndSendText(lpString, SOURCE_TEXTOUT, cchString);
    return TextOutW_Original(hdc, nXStart, nYStart, lpString, cchString);
}

int WINAPI DrawTextW_Hook(HDC hdc, LPCWSTR lpchText, int cchText, LPRECT lprc, UINT format)
{
    ProcessAndSendText(lpchText, SOURCE_DRAWTEXT, cchText == -1 ? 0 : cchText);
    return DrawTextW_Original(hdc, lpchText, cchText, lprc, format);
}

BOOL WINAPI ExtTextOutW_Hook(HDC hdc, int X, int Y, UINT fuOptions, CONST RECT* lprc, LPCWSTR lpString, UINT cbCount, CONST INT* lpDx)
{
    ProcessAndSendText(lpString, SOURCE_EXTTEXTOUT, cbCount);
    return ExtTextOutW_Original(hdc, X, Y, fuOptions, lprc, lpString, cbCount, lpDx);
}

HRESULT WINAPI CreateTextLayout_Hook(IDWriteFactory* pFactory, const WCHAR* string, UINT32 stringLength, IDWriteTextFormat* textFormat, FLOAT maxWidth, FLOAT maxHeight, IDWriteTextLayout** textLayout)
{
    ProcessAndSendText(string, SOURCE_DIRECTWRITE, stringLength);
    return CreateTextLayout_Original(pFactory, string, stringLength, textFormat, maxWidth, maxHeight, textLayout);
}

// --- Função para Obter o Endereço do CreateTextLayout ---
LPVOID GetCreateTextLayoutAddress()
{
    IDWriteFactory* pDWriteFactory = NULL;
    HRESULT hr = DWriteCreateFactory(DWRITE_FACTORY_TYPE_SHARED, __uuidof(IDWriteFactory), reinterpret_cast<IUnknown**>(&pDWriteFactory));
    if (SUCCEEDED(hr) && pDWriteFactory != nullptr)
    {
        void** vtable = *(void***)pDWriteFactory;
        pDWriteFactory->Release();
        return vtable[15]; // CreateTextLayout é geralmente o 16º item na vtable (índice 15)
    }
    return NULL;
}

// --- Funções Exportadas ---
bool InstallHooks(int processId, TextCallback callback)
{
    if (g_isHookActive) return true;

    g_currentProcessId = processId;
    g_callback = callback;

    if (MH_Initialize() != MH_OK) return false;

    MH_CreateHook(&TextOutW, &TextOutW_Hook, reinterpret_cast<LPVOID*>(&TextOutW_Original));
    MH_CreateHook(&DrawTextW, &DrawTextW_Hook, reinterpret_cast<LPVOID*>(&DrawTextW_Original));
    MH_CreateHook(&ExtTextOutW, &ExtTextOutW_Hook, reinterpret_cast<LPVOID*>(&ExtTextOutW_Original));

    LPVOID pCreateTextLayout = GetCreateTextLayoutAddress();
    if (pCreateTextLayout)
    {
        MH_CreateHook(pCreateTextLayout, &CreateTextLayout_Hook, reinterpret_cast<LPVOID*>(&CreateTextLayout_Original));
    }

    if (MH_EnableHook(MH_ALL_HOOKS) != MH_OK) {
        MH_Uninitialize();
        return false;
    }
    
    g_isHookActive = true;
    return true;
}

bool RemoveHooks(int processId)
{
    if (!g_isHookActive) return true;

    MH_DisableHook(MH_ALL_HOOKS);
    MH_Uninitialize();

    g_isHookActive = false;
    g_callback = nullptr;
    g_currentProcessId = 0;
    return true;
}

bool IsHookActive(int processId)
{
    return g_isHookActive && g_currentProcessId == processId;
}

void ClearTextCache()
{
    std::lock_guard<std::mutex> lock(g_textMutex);
    g_seenTexts.clear();
}

int GetCachedTextCount()
{
    std::lock_guard<std::mutex> lock(g_textMutex);
    return static_cast<int>(g_seenTexts.size());
}
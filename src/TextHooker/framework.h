#pragma once

#define WIN32_LEAN_AND_MEAN             // Exclui itens raramente usados dos cabeçalhos do Windows

// Cabeçalhos do Windows
#include <windows.h>

// Cabeçalhos da Biblioteca Padrão do C++
#include <string>
#include <unordered_set>
#include <mutex>
#include <memory>
#include <algorithm>
#include <cctype>
#include <locale>

// Cabeçalhos do DirectX para o hook do DirectWrite
#include <dwrite.h>
#pragma comment(lib, "dwrite.lib")

// Biblioteca MinHook para o hooking seguro de APIs
#include "MinHook.h"

// Link contra a biblioteca MinHook
#if _WIN64
#pragma comment(lib, "libMinHook.x64.lib")
#else
#pragma comment(lib, "libMinHook.x86.lib")
#endif
#pragma once
#include "framework.h"

// Tipo da função de callback para a captura de texto
typedef void (*TextCallback)(const wchar_t* text, int source, int processId);

// Enumeração da origem do texto
enum TextSource
{
    SOURCE_DRAWTEXT = 0,
    SOURCE_TEXTOUT = 1,
    SOURCE_DIRECTWRITE = 2,
    SOURCE_EXTTEXTOUT = 3,
    SOURCE_OCR = 4
};

// Funções exportadas
extern "C" {
    // Instala os hooks de texto para o processo especificado
    __declspec(dllexport) bool InstallHooks(int processId, TextCallback callback);

    // Remove todos os hooks para o processo especificado
    __declspec(dllexport) bool RemoveHooks(int processId);

    // Verifica se os hooks estão atualmente ativos para o processo
    __declspec(dllexport) bool IsHookActive(int processId);

    // Limpa a cache interna de texto
    __declspec(dllexport) void ClearTextCache();

    // Obtém o número de entradas de texto em cache
    __declspec(dllexport) int GetCachedTextCount();
}
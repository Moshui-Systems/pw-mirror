// MirrorHook.dll — DLL injetada em cada cliente do PW para o "mirror por ação".
//
// ESTADO ATUAL: esqueleto seguro. Ao ser carregada, apenas registra que entrou no
// processo (arquivo de marcação + console opcional). Nenhum hook é instalado ainda —
// os hooks das funções de ação do jogo entram DEPOIS da engenharia reversa
// (ver RE-PLAYBOOK.md). Manter este passo inofensivo é de propósito: primeiro
// validamos que a injeção funciona sem risco de crashar o cliente.

#include <windows.h>
#include <cstdio>
#include <string>

static void LogLoaded()
{
    DWORD pid = GetCurrentProcessId();

    // Marca em arquivo (para validar a injeção mesmo sem console visível)
    wchar_t tmp[MAX_PATH];
    if (GetTempPathW(MAX_PATH, tmp))
    {
        std::wstring path = std::wstring(tmp) + L"mirrorhook_loaded.txt";
        HANDLE h = CreateFileW(path.c_str(), FILE_APPEND_DATA, FILE_SHARE_READ | FILE_SHARE_WRITE,
                               nullptr, OPEN_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
        if (h != INVALID_HANDLE_VALUE)
        {
            char line[128];
            int n = _snprintf_s(line, sizeof(line), _TRUNCATE, "MirrorHook carregado no PID %lu\r\n", pid);
            DWORD written = 0;
            SetFilePointer(h, 0, nullptr, FILE_END);
            WriteFile(h, line, (DWORD)n, &written, nullptr);
            CloseHandle(h);
        }
    }
}

static DWORD WINAPI OnLoad(LPVOID)
{
    LogLoaded();
    // TODO (pós-RE): instalar hooks (MinHook) nas funções de ação do cliente:
    //   - selecionar alvo / clicar NPC
    //   - lançar skill
    //   - usar item / mover
    // e reenviá-las às outras instâncias via IPC (named pipe / shared memory),
    // reresolvendo o alvo pelo ID do jogo em cada cliente.
    return 0;
}

BOOL APIENTRY DllMain(HMODULE hModule, DWORD reason, LPVOID)
{
    if (reason == DLL_PROCESS_ATTACH)
    {
        DisableThreadLibraryCalls(hModule);
        CreateThread(nullptr, 0, OnLoad, nullptr, 0, nullptr);
    }
    return TRUE;
}

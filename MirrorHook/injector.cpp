// injector.exe — injeta MirrorHook.dll em processos alvo (por nome).
//
// Uso: injector.exe <nome_do_processo.exe> <caminho\completo\MirrorHook.dll>
// Ex.: injector.exe elementclient_64.exe "C:\...\MirrorHook.dll"
//
// Injeta em TODAS as instâncias que casarem com o nome (multibox).
// Precisa rodar com o MESMO nível/bitness do alvo (x64 + geralmente admin,
// porque os clientes do PW abertos pelo launcher são elevados).

#include <windows.h>
#include <tlhelp32.h>
#include <cstdio>
#include <string>
#include <vector>

static std::vector<DWORD> FindPids(const wchar_t* name)
{
    std::vector<DWORD> pids;
    HANDLE snap = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
    if (snap == INVALID_HANDLE_VALUE) return pids;

    PROCESSENTRY32W pe{};
    pe.dwSize = sizeof(pe);
    if (Process32FirstW(snap, &pe))
    {
        do
        {
            if (_wcsicmp(pe.szExeFile, name) == 0)
                pids.push_back(pe.th32ProcessID);
        } while (Process32NextW(snap, &pe));
    }
    CloseHandle(snap);
    return pids;
}

static bool Inject(DWORD pid, const wchar_t* dllPath)
{
    HANDLE proc = OpenProcess(PROCESS_CREATE_THREAD | PROCESS_QUERY_INFORMATION |
                              PROCESS_VM_OPERATION | PROCESS_VM_WRITE | PROCESS_VM_READ,
                              FALSE, pid);
    if (!proc)
    {
        wprintf(L"  [PID %lu] OpenProcess falhou (erro %lu). Rode como administrador.\n", pid, GetLastError());
        return false;
    }

    SIZE_T size = (wcslen(dllPath) + 1) * sizeof(wchar_t);
    LPVOID remote = VirtualAllocEx(proc, nullptr, size, MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE);
    if (!remote) { wprintf(L"  [PID %lu] VirtualAllocEx falhou.\n", pid); CloseHandle(proc); return false; }

    if (!WriteProcessMemory(proc, remote, dllPath, size, nullptr))
    { wprintf(L"  [PID %lu] WriteProcessMemory falhou.\n", pid); VirtualFreeEx(proc, remote, 0, MEM_RELEASE); CloseHandle(proc); return false; }

    HMODULE k32 = GetModuleHandleW(L"kernel32.dll");
    auto loadLib = (LPTHREAD_START_ROUTINE)GetProcAddress(k32, "LoadLibraryW");

    HANDLE th = CreateRemoteThread(proc, nullptr, 0, loadLib, remote, 0, nullptr);
    if (!th) { wprintf(L"  [PID %lu] CreateRemoteThread falhou.\n", pid); VirtualFreeEx(proc, remote, 0, MEM_RELEASE); CloseHandle(proc); return false; }

    WaitForSingleObject(th, 5000);
    DWORD exitCode = 0;
    GetExitCodeThread(th, &exitCode); // != 0 => HMODULE da DLL carregada

    CloseHandle(th);
    VirtualFreeEx(proc, remote, 0, MEM_RELEASE);
    CloseHandle(proc);

    wprintf(L"  [PID %lu] %ls\n", pid, exitCode ? L"injetado OK" : L"LoadLibrary retornou 0 (DLL nao carregou?)");
    return exitCode != 0;
}

int wmain(int argc, wchar_t** argv)
{
    if (argc < 3)
    {
        wprintf(L"Uso: injector.exe <processo.exe> <caminho\\MirrorHook.dll>\n");
        return 1;
    }

    const wchar_t* name = argv[1];
    const wchar_t* dll = argv[2];

    if (GetFileAttributesW(dll) == INVALID_FILE_ATTRIBUTES)
    { wprintf(L"DLL nao encontrada: %ls\n", dll); return 1; }

    auto pids = FindPids(name);
    if (pids.empty()) { wprintf(L"Nenhum processo '%ls' aberto.\n", name); return 1; }

    wprintf(L"Injetando em %zu processo(s) '%ls':\n", pids.size(), name);
    int ok = 0;
    for (DWORD pid : pids)
        if (Inject(pid, dll)) ok++;

    wprintf(L"Concluido: %d/%zu injetado(s).\n", ok, pids.size());
    return 0;
}

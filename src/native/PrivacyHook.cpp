// StreamPrivacy: a short-lived, thread-specific hook for an explicitly selected
// existing window. No global hooks, background worker, or DllMain initialization.
#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>

namespace {
constexpr DWORD kExcludeFromCapture = 0x00000011;
constexpr WPARAM kMessageSignature = 0x53505256; // "SPRV"
constexpr wchar_t kControlMessage[] =
    L"StreamPrivacy.SetCapturePrivacy.{A7244D43-721B-479C-A2DA-F1BDC29F64B7}";

bool IsSupportedAffinity(DWORD affinity) {
    return affinity == WDA_NONE || affinity == WDA_MONITOR ||
           affinity == kExcludeFromCapture;
}

bool IsTopLevel(HWND window) {
    return IsWindow(window) && GetAncestor(window, GA_ROOT) == window;
}

// Called in the selected window's process, after Windows loads this DLL there.
LRESULT CALLBACK CaptureHookProc(int code, WPARAM wParam, LPARAM lParam) {
    if (code == HC_ACTION && lParam != 0) {
        const auto* message = reinterpret_cast<const CWPSTRUCT*>(lParam);
        const UINT controlMessage = RegisterWindowMessageW(kControlMessage);
        if (controlMessage != 0 && message->message == controlMessage &&
            message->wParam == kMessageSignature &&
            message->lParam >= 0 &&
            static_cast<ULONG_PTR>(message->lParam) <= MAXDWORD) {
            const DWORD affinity = static_cast<DWORD>(message->lParam);
            DWORD owner = 0;
            GetWindowThreadProcessId(message->hwnd, &owner);
            if (owner == GetCurrentProcessId() && IsTopLevel(message->hwnd) &&
                IsSupportedAffinity(affinity)) {
                SetWindowDisplayAffinity(message->hwnd, affinity);
            }
        }
    }
    return CallNextHookEx(nullptr, code, wParam, lParam);
}

bool HasMatchingArchitecture(DWORD processId) {
    const HANDLE process = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION,
                                       FALSE, processId);
    if (process == nullptr) {
        return false;
    }

    using IsWow64Process2Function = BOOL(WINAPI*)(HANDLE, USHORT*, USHORT*);
    const auto query = reinterpret_cast<IsWow64Process2Function>(GetProcAddress(
        GetModuleHandleW(L"kernel32.dll"), "IsWow64Process2"));
    DWORD error = ERROR_SUCCESS;
    if (query == nullptr) {
        error = ERROR_NOT_SUPPORTED;
    } else {
        USHORT currentMachine = 0, currentNative = 0;
        USHORT targetMachine = 0, targetNative = 0;
        if (!query(GetCurrentProcess(), &currentMachine, &currentNative) ||
            !query(process, &targetMachine, &targetNative)) {
            error = GetLastError();
            if (error == ERROR_SUCCESS) error = ERROR_GEN_FAILURE;
        } else {
            const USHORT currentEffective = currentMachine == IMAGE_FILE_MACHINE_UNKNOWN
                ? currentNative : currentMachine;
            const USHORT targetEffective = targetMachine == IMAGE_FILE_MACHINE_UNKNOWN
                ? targetNative : targetMachine;
            if (currentEffective != targetEffective) {
                error = ERROR_BAD_EXE_FORMAT;
            }
        }
    }
    CloseHandle(process);
    SetLastError(error);
    return error == ERROR_SUCCESS;
}
} // namespace

// Success means the requested affinity was read back from Windows. It does NOT
// mean an OBS/Discord capture has been visually verified. A composition can occur
// after this call returns, and capture methods can behave differently.
extern "C" __declspec(dllexport)
BOOL WINAPI SetCapturePrivacy(HWND window, DWORD affinity) {
    if (!IsSupportedAffinity(affinity)) {
        SetLastError(ERROR_INVALID_PARAMETER);
        return FALSE;
    }
    if (!IsTopLevel(window) || window == GetDesktopWindow()) {
        SetLastError(ERROR_INVALID_WINDOW_HANDLE);
        return FALSE;
    }

    DWORD processId = 0;
    const DWORD threadId = GetWindowThreadProcessId(window, &processId);
    if (threadId == 0 || processId == 0) {
        SetLastError(ERROR_INVALID_WINDOW_HANDLE);
        return FALSE;
    }
    if (processId == GetCurrentProcessId()) {
        // This helper is for foreign windows; own-process code can call the
        // public Windows API directly without installing any hook.
        SetLastError(ERROR_INVALID_PARAMETER);
        return FALSE;
    }
    if (!HasMatchingArchitecture(processId)) {
        return FALSE;
    }

    const UINT controlMessage = RegisterWindowMessageW(kControlMessage);
    if (controlMessage == 0) {
        return FALSE;
    }
    HMODULE module = nullptr;
    if (!GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS |
                           GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
            reinterpret_cast<LPCWSTR>(&CaptureHookProc), &module)) {
        return FALSE;
    }

    const HHOOK hook = SetWindowsHookExW(WH_CALLWNDPROC, CaptureHookProc,
                                        module, threadId);
    if (hook == nullptr) {
        return FALSE;
    }

    DWORD error = ERROR_SUCCESS;
    DWORD_PTR messageResult = 0;
    SetLastError(ERROR_SUCCESS);
    if (!SendMessageTimeoutW(window, controlMessage, kMessageSignature,
            static_cast<LPARAM>(affinity),
            SMTO_BLOCK | SMTO_ABORTIFHUNG | SMTO_ERRORONEXIT,
            2000, &messageResult)) {
        error = GetLastError();
        if (error == ERROR_SUCCESS) error = ERROR_TIMEOUT;
    } else {
        DWORD observedAffinity = 0;
        SetLastError(ERROR_SUCCESS);
        if (!GetWindowDisplayAffinity(window, &observedAffinity)) {
            error = GetLastError();
            if (error == ERROR_SUCCESS) error = ERROR_GEN_FAILURE;
        } else if (observedAffinity != affinity) {
            // WH_CALLWNDPROC cannot return the in-target setter's LastError.
            // Report an unconfirmed postcondition instead of false success.
            error = ERROR_GEN_FAILURE;
        }
    }

    // Preserve the operational error across cleanup. A cleanup failure itself
    // also prevents success, even if Windows already applied the affinity.
    if (!UnhookWindowsHookEx(hook) && error == ERROR_SUCCESS) {
        error = GetLastError();
        if (error == ERROR_SUCCESS) error = ERROR_GEN_FAILURE;
    }
    SetLastError(error);
    return error == ERROR_SUCCESS ? TRUE : FALSE;
}

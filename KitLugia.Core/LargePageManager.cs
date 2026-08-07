using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace KitLugia.Core
{
    /// <summary>
    /// Gerenciador de Large Pages (baseado no conceito do 7-max).
    /// Fase 1 (conservadora): habilita SeLockMemoryPrivilege e permite
    /// alocações com MEM_LARGE_PAGES apenas para o próprio KitLugia.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public static class LargePageManager
    {
        // --- Constantes ---
        private const uint MEM_COMMIT = 0x1000;
        private const uint MEM_RESERVE = 0x2000;
        private const uint MEM_LARGE_PAGES = 0x20000000;
        private const uint MEM_RELEASE = 0x8000;
        private const uint PAGE_READWRITE = 0x04;

        private const int SE_PRIVILEGE_ENABLED = 0x00000002;
        private const uint TOKEN_ADJUST_PRIVILEGES = 0x0020;
        private const uint TOKEN_QUERY = 0x0008;

        // --- Estado ---
        private static bool _privilegeEnabled = false;
        private static bool _privilegeAttempted = false;
        private static ulong? _cachedLargePageSize = null;
        private static string? _lastError = null;

        public static bool PrivilegeEnabled => _privilegeEnabled;
        public static string? LastError => _lastError;

        // --- P/Invoke ---
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern UIntPtr GetLargePageMinimum();

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr VirtualAlloc(
            IntPtr lpAddress,
            UIntPtr dwSize,
            uint flAllocationType,
            uint flProtect);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool VirtualFree(IntPtr lpAddress, UIntPtr dwSize, uint dwFreeType);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool OpenProcessToken(IntPtr ProcessHandle, uint DesiredAccess, out IntPtr TokenHandle);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool LookupPrivilegeValue(string lpSystemName, string lpName, out long lpLuid);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool AdjustTokenPrivileges(
            IntPtr TokenHandle,
            bool DisableAllPrivileges,
            ref TOKEN_PRIVILEGES NewState,
            uint BufferLength,
            IntPtr PreviousState,
            IntPtr ReturnLength);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetCurrentProcess();

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        [StructLayout(LayoutKind.Sequential)]
        private struct TOKEN_PRIVILEGES
        {
            public int PrivilegeCount;
            public long Luid;
            public int Attributes;
        }

        /// <summary>
        /// Habilita SeLockMemoryPrivilege no processo atual.
        /// </summary>
        public static bool EnableLargePagesPrivilege()
        {
            if (_privilegeEnabled) return true;
            _privilegeAttempted = true;

            IntPtr hToken = IntPtr.Zero;
            try
            {
                if (!OpenProcessToken(GetCurrentProcess(), TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY, out hToken))
                {
                    _lastError = $"OpenProcessToken falhou: {Marshal.GetLastWin32Error()}";
                    return false;
                }

                if (!LookupPrivilegeValue(null, "SeLockMemoryPrivilege", out long luid))
                {
                    _lastError = $"LookupPrivilegeValue falhou: {Marshal.GetLastWin32Error()}";
                    return false;
                }

                var tp = new TOKEN_PRIVILEGES
                {
                    PrivilegeCount = 1,
                    Luid = luid,
                    Attributes = SE_PRIVILEGE_ENABLED
                };

                if (!AdjustTokenPrivileges(hToken, false, ref tp, (uint)Marshal.SizeOf<TOKEN_PRIVILEGES>(), IntPtr.Zero, IntPtr.Zero))
                {
                    _lastError = $"AdjustTokenPrivileges falhou: {Marshal.GetLastWin32Error()}";
                    return false;
                }

                // ERROR_NOT_ALL_ASSIGNED (1300) = privilégio existe mas não foi atribuído (sem admin)
                int err = Marshal.GetLastWin32Error();
                if (err == 1300)
                {
                    _lastError = "SeLockMemoryPrivilege não atribuído (execute como Administrador)";
                    return false;
                }

                _privilegeEnabled = true;
                _lastError = null;
                return true;
            }
            catch (Exception ex)
            {
                _lastError = ex.Message;
                return false;
            }
            finally
            {
                if (hToken != IntPtr.Zero) CloseHandle(hToken);
            }
        }

        /// <summary>
        /// Tamanho mínimo da Large Page (normalmente 2 MB). 0 se não suportado.
        /// </summary>
        public static ulong GetLargePageSize()
        {
            if (_cachedLargePageSize.HasValue) return _cachedLargePageSize.Value;

            UIntPtr size = GetLargePageMinimum();
            ulong value = size == UIntPtr.Zero ? 0UL : (ulong)size;
            _cachedLargePageSize = value;
            return value;
        }

        /// <summary>
        /// Alinha o tamanho ao múltiplo da Large Page.
        /// </summary>
        private static ulong AlignToLargePage(ulong size)
        {
            ulong pageSize = GetLargePageSize();
            if (pageSize == 0) return size;
            return (size + pageSize - 1) & ~(pageSize - 1);
        }

        /// <summary>
        /// Aloca memória com Large Pages. Retorna IntPtr.Zero em caso de falha.
        /// </summary>
        public static IntPtr AllocateWithLargePages(ulong size)
        {
            if (!_privilegeEnabled)
            {
                if (!EnableLargePagesPrivilege())
                {
                    _lastError = _lastError ?? "Privilégio SeLockMemoryPrivilege não habilitado";
                    return IntPtr.Zero;
                }
            }

            ulong pageSize = GetLargePageSize();
            if (pageSize == 0)
            {
                _lastError = "Sistema não suporta Large Pages";
                return IntPtr.Zero;
            }

            ulong alignedSize = AlignToLargePage(size);
            if (alignedSize == 0) alignedSize = pageSize;

            IntPtr ptr = VirtualAlloc(
                IntPtr.Zero,
                (UIntPtr)alignedSize,
                MEM_COMMIT | MEM_RESERVE | MEM_LARGE_PAGES,
                PAGE_READWRITE);

            if (ptr == IntPtr.Zero)
            {
                int err = Marshal.GetLastWin32Error();
                _lastError = err switch
                {
                    1300 => "Privilégio não atribuído (execute como Administrador)",
                    8 => "Sem memória contígua disponível (fragmentação)",
                    _ => $"VirtualAlloc falhou (erro {err})"
                };
                return IntPtr.Zero;
            }

            _lastError = null;
            return ptr;
        }

        /// <summary>
        /// Libera memória alocada com Large Pages.
        /// </summary>
        public static bool FreeLargePages(IntPtr ptr, ulong size)
        {
            if (ptr == IntPtr.Zero) return false;
            ulong alignedSize = AlignToLargePage(size);
            if (alignedSize == 0) alignedSize = GetLargePageSize();
            return VirtualFree(ptr, (UIntPtr)alignedSize, MEM_RELEASE);
        }

        /// <summary>
        /// Teste rápido: habilita privilégio e tenta alocar/liberar um buffer de teste.
        /// Usado para validar disponibilidade antes de ativar a feature.
        /// </summary>
        public static bool TryTestAllocation(out ulong largePageSize, out string error)
        {
            largePageSize = GetLargePageSize();
            error = "";

            if (largePageSize == 0)
            {
                error = "Sistema não suporta Large Pages";
                return false;
            }

            if (!EnableLargePagesPrivilege())
            {
                error = _lastError ?? "Falha ao habilitar SeLockMemoryPrivilege";
                return false;
            }

            IntPtr test = AllocateWithLargePages(largePageSize * 2);
            if (test == IntPtr.Zero)
            {
                error = _lastError ?? "Alocação de teste falhou (memória fragmentada?)";
                return false;
            }

            FreeLargePages(test, largePageSize * 2);
            error = "";
            return true;
        }

        public static void ResetPrivilegeState()
        {
            _privilegeEnabled = false;
            _privilegeAttempted = false;
            _cachedLargePageSize = null;
            _lastError = null;
        }
    }
}

using System.IO;
using System.Runtime.InteropServices;

namespace KitLugia.WinPE
{
    public static class WinPEDetector
    {
        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, EntryPoint = "RegGetValueW")]
        private static extern int RegGetValue(
            IntPtr hkey, string lpSubKey, string lpValue,
            int dwFlags, out int pdwType, IntPtr pvData, ref int pcbData);

        private const int HKEY_LOCAL_MACHINE = unchecked((int)0x80000002);
        private const int RRF_RT_REG_SZ = 0x00000002;
        private const int RRF_RT_REG_EXPAND_SZ = 0x00000004;

        /// <summary>
        /// Método OFICIAL Microsoft: SystemStartOptions com "MININT" indica WinPE/WinRE.
        /// Documentação: https://learn.microsoft.com/en-us/windows-hardware/manufacture/desktop/winpe-boot-failures
        /// Mantidos também os heurísticos antigos (winpe.jpg/startnet.cmd/X:\) para compatibilidade.
        /// </summary>
        public static bool IsWinPE()
        {
            try
            {
                if (HasMinIntStartOption())
                    return true;
                if (File.Exists(@"X:\Windows\System32\winpe.jpg"))
                    return true;
                if (File.Exists(@"X:\Windows\System32\startnet.cmd"))
                    return true;
                string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                if (pf.StartsWith("X:\\", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            catch { }
            return false;
        }

        /// <summary>
        /// Lê HKLM\SYSTEM\CurrentControlSet\Control\SystemStartOptions e verifica "MININT".
        /// Não usa Microsoft.Win32.RegistryKey para manter dependência zero (WinPE).
        /// </summary>
        private static bool HasMinIntStartOption()
        {
            try
            {
                int size = 1024;
                IntPtr buffer = Marshal.AllocHGlobal(size);
                try
                {
                    int rc = RegGetValue(
                        new IntPtr(HKEY_LOCAL_MACHINE),
                        @"SYSTEM\CurrentControlSet\Control",
                        "SystemStartOptions",
                        RRF_RT_REG_SZ | RRF_RT_REG_EXPAND_SZ,
                        out _,
                        buffer,
                        ref size);
                    if (rc != 0 || size <= 0)
                        return false;
                    string value = Marshal.PtrToStringUni(buffer, (size - 1) / 2) ?? "";
                    return value.IndexOf("MININT", StringComparison.OrdinalIgnoreCase) >= 0;
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }
            }
            catch { return false; }
        }

        public static bool IsValOS()
        {
            try
            {
                if (File.Exists(@"C:\Windows\System32\startnet.valos.cmd"))
                    return true;
                if (Directory.Exists(@"C:\KL_WINPE"))
                    return true;
            }
            catch { }
            return false;
        }

        public static string GetEnvironment()
        {
            if (IsValOS()) return "Validation OS";
            if (IsWinPE()) return "WinPE";
            return "Windows";
        }
    }
}

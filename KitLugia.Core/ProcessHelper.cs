using System;
using System.Diagnostics;

namespace KitLugia.Core
{
    /// <summary>
    /// Helper para obter processos sem lançar exceções quando o processo
    /// já foi encerrado (o Process.TryGetProcessById do .NET não existe
    /// neste SDK preview).
    /// </summary>
    public static class ProcessHelper
    {
        public static bool TryGetProcessById(int pid, out Process process)
        {
            process = null!;
            if (pid <= 0) return false;

            try
            {
                process = Process.GetProcessById(pid);
                return true;
            }
            catch (ArgumentException) { return false; }      // Processo não existe
            catch (System.ComponentModel.Win32Exception) { return false; } // Acesso negado / morreu
            catch { return false; }
        }

        public static bool TryGetProcessById(uint pid, out Process process) =>
            TryGetProcessById((int)pid, out process);
    }
}

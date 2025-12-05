using System;
using System.Runtime.InteropServices;
using System.Text;

namespace AetherLinkMonitor.Services
{
    public static class ProcessCommandLineHelper
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(int dwDesiredAccess, bool bInheritHandle, int dwProcessId);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr hObject);

        [DllImport("ntdll.dll")]
        private static extern int NtQueryInformationProcess(
            IntPtr processHandle,
            int processInformationClass,
            IntPtr processInformation,
            int processInformationLength,
            out int returnLength);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool ReadProcessMemory(
            IntPtr hProcess,
            IntPtr lpBaseAddress,
            [Out] byte[] lpBuffer,
            int dwSize,
            out int lpNumberOfBytesRead);

        private const int PROCESS_QUERY_INFORMATION = 0x0400;
        private const int PROCESS_VM_READ = 0x0010;
        private const int ProcessBasicInformation = 0;

        [StructLayout(LayoutKind.Sequential)]
        private struct PROCESS_BASIC_INFORMATION
        {
            public IntPtr Reserved1;
            public IntPtr PebBaseAddress;
            public IntPtr Reserved2_0;
            public IntPtr Reserved2_1;
            public IntPtr UniqueProcessId;
            public IntPtr InheritedFromUniqueProcessId;
        }

        public static string? GetCommandLine(int processId)
        {
            IntPtr processHandle = IntPtr.Zero;
            try
            {
                processHandle = OpenProcess(PROCESS_QUERY_INFORMATION | PROCESS_VM_READ, false, processId);
                if (processHandle == IntPtr.Zero)
                {
                    return null;
                }

                // Get PEB address
                PROCESS_BASIC_INFORMATION pbi = new PROCESS_BASIC_INFORMATION();
                int pbiSize = Marshal.SizeOf(pbi);
                IntPtr pbiPtr = Marshal.AllocHGlobal(pbiSize);

                try
                {
                    int returnLength;
                    int status = NtQueryInformationProcess(processHandle, ProcessBasicInformation, pbiPtr, pbiSize, out returnLength);
                    if (status != 0)
                    {
                        return null;
                    }

                    pbi = Marshal.PtrToStructure<PROCESS_BASIC_INFORMATION>(pbiPtr);
                }
                finally
                {
                    Marshal.FreeHGlobal(pbiPtr);
                }

                if (pbi.PebBaseAddress == IntPtr.Zero)
                {
                    return null;
                }

                // Read ProcessParameters offset from PEB
                // PEB.ProcessParameters is at offset 0x20 on 64-bit
                bool is64Bit = IntPtr.Size == 8;
                int processParametersOffset = is64Bit ? 0x20 : 0x10;

                byte[] processParametersBuffer = new byte[IntPtr.Size];
                int bytesRead;
                if (!ReadProcessMemory(processHandle, IntPtr.Add(pbi.PebBaseAddress, processParametersOffset),
                    processParametersBuffer, processParametersBuffer.Length, out bytesRead))
                {
                    return null;
                }

                IntPtr processParameters = is64Bit
                    ? new IntPtr(BitConverter.ToInt64(processParametersBuffer, 0))
                    : new IntPtr(BitConverter.ToInt32(processParametersBuffer, 0));

                if (processParameters == IntPtr.Zero)
                {
                    return null;
                }

                // Read CommandLine from PROCESS_PARAMETERS
                // CommandLine is at offset 0x70 on 64-bit, 0x40 on 32-bit
                int commandLineOffset = is64Bit ? 0x70 : 0x40;

                // UNICODE_STRING structure: Length (2 bytes), MaximumLength (2 bytes), Buffer (pointer)
                byte[] unicodeStringBuffer = new byte[is64Bit ? 16 : 8];
                if (!ReadProcessMemory(processHandle, IntPtr.Add(processParameters, commandLineOffset),
                    unicodeStringBuffer, unicodeStringBuffer.Length, out bytesRead))
                {
                    return null;
                }

                ushort length = BitConverter.ToUInt16(unicodeStringBuffer, 0);
                IntPtr commandLineAddress = is64Bit
                    ? new IntPtr(BitConverter.ToInt64(unicodeStringBuffer, 8))
                    : new IntPtr(BitConverter.ToInt32(unicodeStringBuffer, 4));

                if (commandLineAddress == IntPtr.Zero || length == 0)
                {
                    return null;
                }

                // Read the actual command line string
                byte[] commandLineBytes = new byte[length];
                if (!ReadProcessMemory(processHandle, commandLineAddress, commandLineBytes, length, out bytesRead))
                {
                    return null;
                }

                return Encoding.Unicode.GetString(commandLineBytes);
            }
            catch
            {
                return null;
            }
            finally
            {
                if (processHandle != IntPtr.Zero)
                {
                    CloseHandle(processHandle);
                }
            }
        }
    }
}

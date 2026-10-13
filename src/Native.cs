using System;
using System.Runtime.InteropServices;

namespace A3Print
{
    internal enum TaskbarState
    {
        None = 0,
        Normal = 2,
        Error = 4,
        Paused = 8
    }

    internal static class Native
    {
        private const uint EsContinuous = 0x80000000;
        private const uint EsSystemRequired = 0x00000001;

        [DllImport("kernel32.dll")]
        private static extern uint SetThreadExecutionState(uint flags);

        [DllImport("user32.dll")]
        private static extern bool FlashWindow(IntPtr window, bool invert);

        [ComImport, Guid("ea1afb91-9e28-4b86-90e9-9e9f8a5eefaf"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface ITaskbarList3
        {
            void HrInit();
            void AddTab(IntPtr window);
            void DeleteTab(IntPtr window);
            void ActivateTab(IntPtr window);
            void SetActiveAlt(IntPtr window);
            void MarkFullscreenWindow(IntPtr window, [MarshalAs(UnmanagedType.Bool)] bool fullscreen);
            void SetProgressValue(IntPtr window, ulong completed, ulong total);
            void SetProgressState(IntPtr window, int state);
        }

        [ComImport, Guid("56FDF344-FD6D-11d0-958A-006097C9A090"), ClassInterface(ClassInterfaceType.None)]
        private class TaskbarInstance
        {
        }

        private static ITaskbarList3 taskbar;

        // Empêche la mise en veille pendant un lot. À appeler et à relâcher depuis le même fil.
        public static void KeepAwake(bool on)
        {
            try
            {
                SetThreadExecutionState(on ? EsContinuous | EsSystemRequired : EsContinuous);
            }
            catch (Exception)
            {
            }
        }

        public static void Flash(IntPtr window)
        {
            try
            {
                FlashWindow(window, true);
            }
            catch (Exception)
            {
            }
        }

        // Progression sur l'icône de la barre des tâches.
        public static void SetTaskbar(IntPtr window, TaskbarState state, int value, int maximum)
        {
            try
            {
                if (taskbar == null)
                {
                    taskbar = (ITaskbarList3)new TaskbarInstance();
                    taskbar.HrInit();
                }
                taskbar.SetProgressState(window, (int)state);
                if (state != TaskbarState.None)
                {
                    taskbar.SetProgressValue(window, (ulong)Math.Max(0, value), (ulong)Math.Max(1, maximum));
                }
            }
            catch (Exception)
            {
                // Purement décoratif.
            }
        }
    }
}

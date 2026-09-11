using Cosmos.Kernel.System.Keyboard;

namespace Windose.System.Drivers
{
    public static class Keyboard
    {
        private static bool hasCurrentEvent;
        private static SystemKeyEvent currentEvent;

        public static void BeginFrame()
        {
            hasCurrentEvent = false;
            currentEvent = null;
        }

        public static bool CurrentEvent(out SystemKeyEvent keyEvent)
        {
            if (hasCurrentEvent)
            {
                keyEvent = currentEvent;
                return keyEvent != null;
            }

            hasCurrentEvent = true;

            if (KeyboardManager.KeyAvailable)
            {
                currentEvent = new SystemKeyEvent(KeyboardManager.ReadKey(), false);
                keyEvent = currentEvent;
                return true;
            }
            else
            {
                currentEvent = null;
                keyEvent = null;
                return false;
            }
        }
    }
}

using Cosmos.Kernel.System.Keyboard;

namespace Windose.System.Drivers
{
    public static class Keyboard
    {
        public static KeyEvent CurrentEvent()
        {
            if (KeyboardManager.KeyAvailable)
            {
                return KeyboardManager.ReadKey();
            }
            else
            {
                return new KeyEvent();
            }
        }
    }
}

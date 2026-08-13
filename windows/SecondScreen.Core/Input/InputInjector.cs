using System.Runtime.InteropServices;

namespace SecondScreen.Core;

// Injects mouse/scroll/click into Windows from normalized Android touch events, mapped onto
// the virtual Display 2 rectangle (PROTOCOL.md §4, ARCHITECTURE.md §Input mapping).
public sealed class InputInjector
{
    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT { public uint type; public MOUSEINPUT mi; }
    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr dwExtraInfo;
    }

    private const uint INPUT_MOUSE = 0;
    private const uint MOUSEEVENTF_MOVE = 0x0001, MOUSEEVENTF_LEFTDOWN = 0x0002,
        MOUSEEVENTF_LEFTUP = 0x0004, MOUSEEVENTF_RIGHTDOWN = 0x0008, MOUSEEVENTF_RIGHTUP = 0x0010,
        MOUSEEVENTF_WHEEL = 0x0800, MOUSEEVENTF_ABSOLUTE = 0x8000, MOUSEEVENTF_VIRTUALDESK = 0x4000;
    private const int WHEEL_DELTA = 120;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint n, INPUT[] inputs, int size);

    private bool _leftDown;

    public void Handle(TouchMessage t)
    {
        switch (t.Event)
        {
            case TouchEvent.Down:
                MoveTo(t.X, t.Y); LeftDown(); _leftDown = true; break;
            case TouchEvent.Move:
                MoveTo(t.X, t.Y); break;
            case TouchEvent.Up:
                MoveTo(t.X, t.Y); if (_leftDown) { LeftUp(); _leftDown = false; } break;
            case TouchEvent.LongPress:
                MoveTo(t.X, t.Y); RightClick(); break;
            case TouchEvent.Scroll:
                MoveTo(t.X, t.Y); Wheel(-(int)(t.Dy * WHEEL_DELTA * 3)); break;
        }
    }

    private static (int absX, int absY) MapToVirtualDesk(double nx, double ny)
    {
        var target = DisplayLayout.GetTargetDisplay();
        var (vx, vy, vw, vh) = DisplayLayout.VirtualScreen();
        if (vw <= 0) vw = 1; if (vh <= 0) vh = 1;

        double screenX = target.Left + Math.Clamp(nx, 0, 1) * target.Width;
        double screenY = target.Top + Math.Clamp(ny, 0, 1) * target.Height;
        int absX = (int)Math.Round((screenX - vx) * 65535.0 / vw);
        int absY = (int)Math.Round((screenY - vy) * 65535.0 / vh);
        return (Math.Clamp(absX, 0, 65535), Math.Clamp(absY, 0, 65535));
    }

    private static void MoveTo(double nx, double ny)
    {
        var (ax, ay) = MapToVirtualDesk(nx, ny);
        Send(MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK, ax, ay, 0);
    }
    private static void LeftDown() => Send(MOUSEEVENTF_LEFTDOWN, 0, 0, 0);
    private static void LeftUp() => Send(MOUSEEVENTF_LEFTUP, 0, 0, 0);
    private static void RightClick()
    {
        Send(MOUSEEVENTF_RIGHTDOWN, 0, 0, 0);
        Send(MOUSEEVENTF_RIGHTUP, 0, 0, 0);
    }
    private static void Wheel(int delta) => Send(MOUSEEVENTF_WHEEL, 0, 0, (uint)delta);

    private static void Send(uint flags, int ax, int ay, uint data)
    {
        var input = new INPUT
        {
            type = INPUT_MOUSE,
            mi = new MOUSEINPUT { dx = ax, dy = ay, mouseData = data, dwFlags = flags }
        };
        SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
    }
}

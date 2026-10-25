using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using static PenScroll.Interop.Native;

namespace PenScroll.Interop
{
    /// <summary>
    /// A virtual mouse created through <c>/dev/uinput</c> that only ever emits wheel events.
    /// <para/>
    /// The driver's own <c>IMouseScrollHandler</c> is unreachable from a filter: filters are
    /// constructed without a service provider, and the handler is registered only in the binding
    /// handler's.
    /// </summary>
    internal sealed unsafe class UinputScrollDevice
    {
        private const string DevicePath = "/dev/uinput";
        private const string DeviceName = "OpenTabletDriver Pen Scroll";

        private readonly int _fd;

        public UinputScrollDevice()
        {
            _fd = open(DevicePath, O_WRONLY | O_NONBLOCK | O_CLOEXEC);
            if (_fd < 0)
                throw Failure($"could not open {DevicePath}");

            try
            {
                // REL_X/REL_Y and BTN_LEFT are declared but never emitted: udev's input_id only tags
                // ID_INPUT_MOUSE when a device has both relative axes and a button, and libinput
                // ignores wheel events from anything it did not classify as a pointer.
                Set(UI_SET_EVBIT, EV_KEY);
                Set(UI_SET_KEYBIT, BTN_LEFT);
                Set(UI_SET_EVBIT, EV_REL);
                Set(UI_SET_RELBIT, REL_X);
                Set(UI_SET_RELBIT, REL_Y);
                Set(UI_SET_RELBIT, REL_WHEEL);
                Set(UI_SET_RELBIT, REL_HWHEEL);
                Set(UI_SET_RELBIT, REL_WHEEL_HI_RES);
                Set(UI_SET_RELBIT, REL_HWHEEL_HI_RES);
                Set(UI_SET_EVBIT, EV_SYN);

                var setup = new UinputSetup
                {
                    BusType = BUS_VIRTUAL,
                    Vendor = 0x1209,  // pid.codes, the free VID block for open hardware/software
                    Product = 0x0001,
                    Version = 1,
                    FfEffectsMax = 0
                };

                var name = new Span<byte>(setup.Name, UinputMaxNameSize);
                var written = Encoding.UTF8.GetBytes(DeviceName, name[..^1]);
                name[written] = 0;

                if (ioctl(_fd, UI_DEV_SETUP, &setup) < 0)
                    throw Failure("UI_DEV_SETUP failed");
                if (ioctl(_fd, UI_DEV_CREATE, 0) < 0)
                    throw Failure("UI_DEV_CREATE failed");
            }
            catch
            {
                close(_fd);
                throw;
            }
        }

        /// <summary>
        /// Emits one wheel report. Axes take hi-res units; the legacy detent counts are emitted
        /// alongside for consumers that ignore the hi-res axes. Positive is up and right.
        /// </summary>
        public void Scroll(int verticalHiRes, int verticalDetents, int horizontalHiRes, int horizontalDetents)
        {
            if (verticalHiRes == 0 && verticalDetents == 0 && horizontalHiRes == 0 && horizontalDetents == 0)
                return;

            var events = stackalloc InputEvent[5];
            var count = 0;

            if (verticalHiRes != 0)
                events[count++] = new InputEvent(EV_REL, REL_WHEEL_HI_RES, verticalHiRes);
            if (verticalDetents != 0)
                events[count++] = new InputEvent(EV_REL, REL_WHEEL, verticalDetents);
            if (horizontalHiRes != 0)
                events[count++] = new InputEvent(EV_REL, REL_HWHEEL_HI_RES, horizontalHiRes);
            if (horizontalDetents != 0)
                events[count++] = new InputEvent(EV_REL, REL_HWHEEL, horizontalDetents);
            events[count++] = new InputEvent(EV_SYN, SYN_REPORT, 0);

            var size = (nuint)(count * sizeof(InputEvent));
            if (write(_fd, events, size) < 0)
                throw Failure("writing wheel events failed");
        }

        private void Set(ulong request, ushort bit)
        {
            if (ioctl(_fd, request, bit) < 0)
                throw Failure($"ioctl 0x{request:x} with 0x{bit:x} failed");
        }

        private static InvalidOperationException Failure(string what)
        {
            var errno = Marshal.GetLastWin32Error();
            return new InvalidOperationException($"{what} (errno {errno}: {new Win32Exception(errno).Message})");
        }
    }
}

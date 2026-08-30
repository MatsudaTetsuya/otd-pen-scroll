using System;
using System.Reflection;
using System.Runtime.InteropServices;

namespace PenScroll.Interop
{
    /// <summary>Raw bindings and constants for the Linux uinput interface.</summary>
    internal static unsafe class Native
    {
        // fcntl.h
        public const int O_WRONLY = 0x0001;
        public const int O_NONBLOCK = 0x0800;
        public const int O_CLOEXEC = 0x80000;

        // linux/input-event-codes.h
        public const ushort EV_SYN = 0x00;
        public const ushort EV_KEY = 0x01;
        public const ushort EV_REL = 0x02;

        public const ushort SYN_REPORT = 0x00;

        public const ushort REL_X = 0x00;
        public const ushort REL_Y = 0x01;
        public const ushort REL_HWHEEL = 0x06;
        public const ushort REL_WHEEL = 0x08;
        public const ushort REL_WHEEL_HI_RES = 0x0b;
        public const ushort REL_HWHEEL_HI_RES = 0x0c;

        public const ushort BTN_LEFT = 0x110;

        public const ushort BUS_VIRTUAL = 0x06;

        /// <summary>Hi-res wheel units per detent, fixed by the kernel's input protocol.</summary>
        public const int HiResUnitsPerDetent = 120;

        // linux/uinput.h
        public const int UinputMaxNameSize = 80;

        private const uint IocWrite = 1;
        private const uint UinputIoctlBase = 'U';

        private static uint Io(uint nr) => (UinputIoctlBase << 8) | nr;

        private static uint Iow(uint nr, uint size) =>
            (IocWrite << 30) | (size << 16) | (UinputIoctlBase << 8) | nr;

        public static readonly ulong UI_DEV_CREATE = Io(1);
        public static readonly ulong UI_DEV_DESTROY = Io(2);
        public static readonly ulong UI_DEV_SETUP = Iow(3, (uint)sizeof(UinputSetup));
        public static readonly ulong UI_SET_EVBIT = Iow(100, sizeof(int));
        public static readonly ulong UI_SET_KEYBIT = Iow(101, sizeof(int));
        public static readonly ulong UI_SET_RELBIT = Iow(102, sizeof(int));

        private const string LibC = "libc";

        /// <summary>
        /// The runtime only probes libc, libc.so and liblibc.so. On Fedora libc.so is a
        /// glibc-devel linker script and is not dlopen-able, so try the real sonames.
        /// </summary>
        private static readonly string[] LibCCandidates =
        {
            "libc.so.6",             // glibc
            "libc.musl-x86_64.so.1", // musl
            "libc.so"
        };

        static Native()
        {
            NativeLibrary.SetDllImportResolver(typeof(Native).Assembly, ResolveLibC);
        }

        private static IntPtr ResolveLibC(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
        {
            if (libraryName != LibC)
                return IntPtr.Zero;

            foreach (var candidate in LibCCandidates)
            {
                if (NativeLibrary.TryLoad(candidate, out var handle))
                    return handle;
            }

            return IntPtr.Zero;
        }

        [DllImport(LibC, SetLastError = true)]
        public static extern int open([MarshalAs(UnmanagedType.LPUTF8Str)] string pathname, int flags);

        [DllImport(LibC, SetLastError = true)]
        public static extern int close(int fd);

        [DllImport(LibC, SetLastError = true, EntryPoint = "ioctl")]
        public static extern int ioctl(int fd, ulong request, int arg);

        [DllImport(LibC, SetLastError = true, EntryPoint = "ioctl")]
        public static extern int ioctl(int fd, ulong request, UinputSetup* arg);

        [DllImport(LibC, SetLastError = true)]
        public static extern nint write(int fd, void* buf, nuint count);
    }

    /// <summary><c>struct input_event</c>. The kernel fills in the timestamp, so it is left zeroed.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct InputEvent
    {
        public nint TimeSeconds;
        public nint TimeMicroseconds;
        public ushort Type;
        public ushort Code;
        public int Value;

        public InputEvent(ushort type, ushort code, int value)
        {
            TimeSeconds = 0;
            TimeMicroseconds = 0;
            Type = type;
            Code = code;
            Value = value;
        }
    }

    /// <summary><c>struct uinput_setup</c>, with <c>struct input_id</c> inlined.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct UinputSetup
    {
        public ushort BusType;
        public ushort Vendor;
        public ushort Product;
        public ushort Version;
        public fixed byte Name[Native.UinputMaxNameSize];
        public uint FfEffectsMax;
    }
}

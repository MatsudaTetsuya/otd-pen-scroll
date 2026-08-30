using System;
using System.Numerics;
using OpenTabletDriver.Plugin;
using OpenTabletDriver.Plugin.Attributes;
using OpenTabletDriver.Plugin.Output;
using OpenTabletDriver.Plugin.Tablet;
using PenScroll.Interop;

namespace PenScroll
{
    /// <summary>
    /// Scrolls while a pen button is held, reproducing the Wacom driver's scroll modifier.
    /// <para/>
    /// A filter rather than a binding: a binding only sees press and release, never the movement in
    /// between.
    /// <para/>
    /// Absolute Mode only. The wheel is a separate pointer device, so it scrolls wherever the system
    /// pointer is — which is the pen only in Absolute Mode. Artist Mode presents a tablet tool, and
    /// on Wayland a tablet tool carries no pointer focus.
    /// <para/>
    /// The full type name is persisted in settings.json; renaming detaches existing profiles.
    /// </summary>
    [PluginName("Pen Scroll")]
    [SupportedPlatform(PluginPlatform.Linux)]
    public class PenScrollFilter : IPositionedPipelineElement<IDeviceReport>, IDisposable
    {
        /// <summary>Post-transform, so reports arrive in screen pixels rather than tablet units.</summary>
        public PipelinePosition Position => PipelinePosition.PostTransform;

        public event Action<IDeviceReport>? Emit;

        [Property("Modifier Button")]
        [DefaultPropertyValue(1)]
        [ToolTip("Which pen button starts scrolling, counting from 1.\n\n" +
                 "Leave this button unbound in the Pen Settings tab, or whatever it is bound to " +
                 "will fire alongside the scrolling.")]
        public int ModifierButton { get; set; } = 1;

        [Property("Pixels per Notch")]
        [DefaultPropertyValue(20f)]
        [ToolTip("How far the pen travels for one scroll notch. Smaller values scroll faster.")]
        public float PixelsPerNotch { get; set; } = 20f;

        private UinputScrollDevice? _device;

        private bool _scrolling;
        private Vector2 _anchor;
        private Vector2 _previous;

        // Sub-unit remainder, so slow movement still scrolls instead of being rounded away.
        private float _pendingVertical;

        // Hi-res units already emitted but not yet worth a legacy detent.
        private int _residualVertical;

        public void Consume(IDeviceReport report)
        {
            if (report is ITabletReport tabletReport)
                Process(tabletReport);

            // Never swallowed: dropping a report would hide the button release from the binding
            // handler and leave whatever that button is bound to held down.
            Emit?.Invoke(report);
        }

        private void Process(ITabletReport report)
        {
            if (!IsModifierHeld(report.PenButtons))
            {
                StopScrolling();
                return;
            }

            var position = report.Position;

            if (!_scrolling)
            {
                _scrolling = true;
                _anchor = position;
                _pendingVertical = 0f;
                _residualVertical = 0;
            }
            else
            {
                Scroll(position - _previous);
            }

            _previous = position;

            // Pin the cursor by rewriting the position rather than by dropping the report.
            report.Position = _anchor;
        }

        private bool IsModifierHeld(bool[]? penButtons)
        {
            var index = ModifierButton - 1;
            return penButtons is not null
                && index >= 0
                && index < penButtons.Length
                && penButtons[index];
        }

        private void Scroll(Vector2 delta)
        {
            // A zero or negative setting would turn one report into an unbounded jump.
            var pixelsPerNotch = Math.Max(PixelsPerNotch, 1f);
            var scale = Native.HiResUnitsPerDetent / pixelsPerNotch;

            // evdev counts positive as up, so the downward pen axis is negated.
            var vertical = TakeWhole(ref _pendingVertical, -delta.Y * scale);
            if (vertical == 0)
                return;

            var verticalDetents = TakeDetents(ref _residualVertical, vertical);

            EnsureDevice().Scroll(vertical, verticalDetents, 0, 0);
        }

        /// <summary>
        /// Returns the whole part of <paramref name="pending"/> plus <paramref name="amount"/>,
        /// leaving the fraction for the next report. Truncating toward zero keeps the remainder on
        /// the same side as the movement, so reversing direction owes nothing from the other side.
        /// </summary>
        private static int TakeWhole(ref float pending, float amount)
        {
            if (float.IsNaN(amount) || float.IsInfinity(amount))
                return 0;

            pending += amount;
            var whole = (int)pending;
            pending -= whole;
            return whole;
        }

        /// <summary>Converts emitted hi-res units into legacy detents, carrying the remainder.</summary>
        private static int TakeDetents(ref int residual, int hiResUnits)
        {
            residual += hiResUnits;
            var detents = residual / Native.HiResUnitsPerDetent;
            residual -= detents * Native.HiResUnitsPerDetent;
            return detents;
        }

        /// <summary>
        /// Created on first use, not in the constructor: the GUI also instantiates plugins just to
        /// read their default values.
        /// </summary>
        private UinputScrollDevice EnsureDevice() => _device ??= new UinputScrollDevice();

        private void StopScrolling()
        {
            _scrolling = false;
            _pendingVertical = 0f;
            _residualVertical = 0;
        }

        public void Dispose()
        {
            _device?.Dispose();
            _device = null;
            GC.SuppressFinalize(this);
        }
    }
}

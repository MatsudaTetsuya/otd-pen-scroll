using System;
using System.Diagnostics;
using System.Numerics;
using OpenTabletDriver.Plugin;
using OpenTabletDriver.Plugin.Attributes;
using OpenTabletDriver.Plugin.Output;
using OpenTabletDriver.Plugin.Tablet;
using PenScroll.Interop;

namespace PenScroll
{
    /// <summary>
    /// Scrolls while a pen button is held: the press marks an anchor, and how far the pen is held
    /// from it sets the scroll speed. The cursor stays on the anchor for the duration, so the scroll
    /// keeps landing on whatever was under the pen when the button went down.
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
        private const string LogGroup = "Pen Scroll";

        /// <summary>Displacement at which <see cref="Speed"/> is quoted.</summary>
        private const float SpeedReferencePixels = 100f;

        /// <summary>A stalled pipeline must not discharge as one long scroll.</summary>
        private const float MaxStepSeconds = 0.05f;

        /// <summary>Post-transform, so reports arrive in screen pixels rather than tablet units.</summary>
        public PipelinePosition Position => PipelinePosition.PostTransform;

        public event Action<IDeviceReport>? Emit;

        [Property("Modifier Button")]
        [DefaultPropertyValue(1)]
        [ToolTip("Which pen button starts scrolling, counting from 1.\n\n" +
                 "Leave this button unbound in the Pen Settings tab, or whatever it is bound to " +
                 "will fire alongside the scrolling.")]
        public int ModifierButton { get; set; } = 1;

        [Property("Dead Zone")]
        [Unit("px")]
        [DefaultPropertyValue(12f)]
        [ToolTip("How far the pen must be held from the anchor before scrolling starts.")]
        public float DeadZone { get; set; } = 12f;

        [Property("Speed")]
        [Unit("notches/s")]
        [DefaultPropertyValue(15f)]
        [ToolTip("Scroll speed when the pen is held 100 px past the dead zone.\n\n" +
                 "Twice that distance scrolls twice as fast.")]
        public float Speed { get; set; } = 15f;

        [BooleanProperty("Invert Direction", "Scroll the content with the pen instead of against it.")]
        [DefaultPropertyValue(false)]
        public bool Invert { get; set; }

        [BooleanProperty("Horizontal Scrolling", "Also scroll sideways from horizontal displacement.")]
        [DefaultPropertyValue(false)]
        public bool HorizontalScrolling { get; set; }

        private UinputScrollDevice? _device;
        private bool _deviceUnavailable;

        private bool _scrolling;
        private Vector2 _anchor;
        private long _lastTimestamp;

        // Sub-unit remainders, so a slow rate still scrolls instead of being rounded away.
        private float _pendingVertical;
        private float _pendingHorizontal;

        // Hi-res units already emitted but not yet worth a legacy detent.
        private int _residualVertical;
        private int _residualHorizontal;

        public void Consume(IDeviceReport report)
        {
            try
            {
                if (report is ITabletReport tabletReport)
                    Process(tabletReport);
            }
            catch (Exception ex)
            {
                Log.Exception(ex);
                StopScrolling();
            }

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

            var device = EnsureDevice();
            if (device is null)
                return;

            var now = Stopwatch.GetTimestamp();

            if (!_scrolling)
            {
                _scrolling = true;
                _anchor = report.Position;
                _pendingVertical = 0f;
                _pendingHorizontal = 0f;
                _residualVertical = 0;
                _residualHorizontal = 0;
            }
            else
            {
                var seconds = (float)(now - _lastTimestamp) / Stopwatch.Frequency;
                Scroll(device, report.Position - _anchor, Math.Min(seconds, MaxStepSeconds));
            }

            _lastTimestamp = now;

            // Pin the cursor by rewriting the position.
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

        private void Scroll(UinputScrollDevice device, Vector2 displacement, float seconds)
        {
            // evdev counts positive as up and right, so the downward pen axis is negated.
            var vertical = TakeWhole(ref _pendingVertical, Rate(-displacement.Y) * seconds);
            var horizontal = HorizontalScrolling
                ? TakeWhole(ref _pendingHorizontal, Rate(displacement.X) * seconds)
                : 0;

            if (vertical == 0 && horizontal == 0)
                return;

            var verticalDetents = TakeDetents(ref _residualVertical, vertical);
            var horizontalDetents = TakeDetents(ref _residualHorizontal, horizontal);

            device.Scroll(vertical, verticalDetents, horizontal, horizontalDetents);
        }

        /// <summary>
        /// Hi-res units per second for one axis. The dead zone is applied per axis rather than to
        /// the distance, so drifting a little sideways during a vertical scroll does not start
        /// scrolling sideways.
        /// </summary>
        private float Rate(float offset)
        {
            var past = Math.Abs(offset) - Math.Max(DeadZone, 0f);
            if (past <= 0f)
                return 0f;

            var notchesPerSecond = past / SpeedReferencePixels * Math.Max(Speed, 0f);
            var direction = offset < 0f ? -1f : 1f;

            return direction * notchesPerSecond * Native.HiResUnitsPerDetent * (Invert ? -1f : 1f);
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
        /// Created on first press, not in the constructor: the GUI also instantiates plugins just to
        /// read their default values.
        /// </summary>
        private UinputScrollDevice? EnsureDevice()
        {
            if (_device is not null || _deviceUnavailable)
                return _device;

            try
            {
                _device = new UinputScrollDevice();
                Log.Debug(LogGroup, "Created the virtual scroll wheel.");
            }
            catch (Exception ex)
            {
                _deviceUnavailable = true;
                Log.Write(LogGroup, $"Scrolling is disabled: {ex.Message}", LogLevel.Error, notify: true);
            }

            return _device;
        }

        private void StopScrolling()
        {
            _scrolling = false;
            _pendingVertical = 0f;
            _pendingHorizontal = 0f;
            _residualVertical = 0;
            _residualHorizontal = 0;
        }

        public void Dispose()
        {
            _device?.Dispose();
            _device = null;
            GC.SuppressFinalize(this);
        }
    }
}

using System;
using System.Numerics;
using OpenTabletDriver.Plugin;
using OpenTabletDriver.Plugin.Attributes;
using OpenTabletDriver.Plugin.Output;
using OpenTabletDriver.Plugin.Tablet;

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
    public class PenScrollFilter : IPositionedPipelineElement<IDeviceReport>
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

        private bool _scrolling;
        private Vector2 _anchor;

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
                _scrolling = false;
                return;
            }

            if (!_scrolling)
            {
                _scrolling = true;
                _anchor = report.Position;
            }

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
    }
}

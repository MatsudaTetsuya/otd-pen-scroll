using System;
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

        public void Consume(IDeviceReport report)
        {
            Emit?.Invoke(report);
        }
    }
}

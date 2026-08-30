# Pen Scroll

An [OpenTabletDriver](https://opentabletdriver.net/) plugin that scrolls by holding a pen button and moving the pen away from where you pressed.
The further out you hold it, the faster it scrolls, and holding still keeps it scrolling.

## Requirements

- Linux
- OpenTabletDriver **0.6.7**, with the output mode set to **Absolute Mode**

### Absolute Mode is required

Scrolling is a pointer event, and the plugin delivers it through a virtual wheel device, so it acts wherever the system pointer is.
Absolute Mode is the mode in which the pen *is* the system pointer, so the wheel lands under the pen.

**Artist Mode does not work.** It presents the tablet as a stylus so applications receive pressure and tilt, and on Wayland a stylus is a tablet tool rather than a pointer, carrying no pointer focus of its own.
Scrolling then does nothing unless a mouse is also connected, and with a mouse connected you get two cursors and the scrolling happens at the mouse cursor.

## Installing

Copy `PenScroll.dll` into its own directory under the plugin folder and restart the daemon:

```console
$ mkdir -p ~/.config/OpenTabletDriver/Plugins/PenScroll
$ cp PenScroll.dll ~/.config/OpenTabletDriver/Plugins/PenScroll/
$ systemctl --user restart opentabletdriver
```

Then open the GUI, go to **Filters**, and enable **Pen Scroll**.

## Settings

| Setting | Default | What it does |
| --- | --- | --- |
| Modifier Button | `1` | Which pen button starts scrolling, counting from 1. |
| Dead Zone | `12` px | How far the pen must be held from the anchor before scrolling starts. |
| Speed | `15` notches/s | Scroll speed when the pen is held 100 px past the dead zone. |
| Invert Direction | off | Moves the content with the pen instead of against it. |
| Horizontal Scrolling | off | Also scrolls sideways from horizontal displacement. |

Leave the modifier button **unbound** in the Pen Settings tab.
The plugin does not consume the button press, so anything bound to it still fires while you scroll.

## Building

Requires the .NET 8 SDK:

```console
$ dotnet publish src/PenScroll/PenScroll.csproj -c Release -o publish
```

`publish/PenScroll.dll` is the plugin.

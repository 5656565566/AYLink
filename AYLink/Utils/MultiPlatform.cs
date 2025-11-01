using Avalonia.Controls;

#if WINDOWS
using System;
using System.Runtime.InteropServices;
#elif X11
using Avalonia.X11;
using static Avalonia.X11.XLib;
#elif OSX
using System.Runtime.InteropServices;
#endif

namespace AYLink.Utils;

internal static class MultiPlatform
{

}
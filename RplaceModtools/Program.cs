﻿using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using LibGit2Sharp;

namespace RplaceModtools
{
    class Program
    {
        // Initialization code. Don't use any Avalonia, third-party APIs or any
        // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
        // yet and stuff might break.
        [STAThread]
        public static void Main(string[] args)
        {
            BuildAvaloniaApp()
                .StartWithClassicDesktopLifetime(args);
        } 

        // Avalonia configuration, don't remove; also used by visual designer.
        public static AppBuilder BuildAvaloniaApp()
            => AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .LogToTrace();

        //.With(new Win32PlatformOptions { AllowEglInitialization = true }) //Perf optimisations
        //.With(new X11PlatformOptions { UseGpu = true, UseEGL = true })
        //.With(new AvaloniaNativePlatformOptions { UseGpu = true })
    }
}

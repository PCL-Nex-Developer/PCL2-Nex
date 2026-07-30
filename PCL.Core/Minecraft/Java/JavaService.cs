using PCL.Core.Minecraft.Java.Parser;
using PCL.Core.Minecraft.Java.Scanner;
using PCL.Core.App.IoC;

namespace PCL.Core.Minecraft;

[LifecycleService(LifecycleState.Loaded)]
[LifecycleScope("java", "Java 管理")]
public sealed partial class JavaService
{

    private static JavaManager? _javaManager;
    public static JavaManager JavaManager => _javaManager!;

    [LifecycleStart]
    private static void _Start()
    {
        if (_javaManager is not null) return;

        Context.Info("Initializing Java Manager...");

        _javaManager = new JavaManager(
            new PeHeaderParser(), 
            [
            new RegistryJavaScanner(),
            new DefaultPathsScanner(),
            new PathEnvironmentScanner(),
            new MicrosoftStoreJavaScanner(),
            new WhereCommandScanner()
        ]);
        _javaManager.ReadConfig();
        Context.Info($"Loaded {_javaManager.GetSortedJavaList().Count} cached Java installation(s)");
    }

    [LifecycleStop]
    private static void _Stop()
    {
        if (_javaManager is null) return;

        _javaManager.SaveConfig();
        _javaManager = null;
    }
}

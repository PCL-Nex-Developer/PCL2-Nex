using PCL.Core.App;

namespace PCL;

public class VersionDataModel
{
    public string Changelog { get; set; }
    public string Sha256 { get; set; }
    public string Source { get; set; }
    public LauncherBaseVersion BaseVersion { get; set; }
}

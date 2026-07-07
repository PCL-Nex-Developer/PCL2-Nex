using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using PCL.Core.App.Localization;
using PCL.Core.Logging;
using PCL.Network;

namespace PCL;

internal static class LogUploadService
{
    private const string UploadUrl = "https://api.mclo.gs/1/log";
    private static bool _isUploading;

    public static void UploadAndCopyAsync(string logText)
    {
        if (_isUploading)
        {
            HintService.Hint(Lang.Text("Log.Upload.AlreadyUploading"));
            return;
        }

        _isUploading = true;
        HintService.Hint(Lang.Text("Log.Upload.Started"));

        ModBase.RunInThread(() =>
        {
            try
            {
                var url = Upload(logText);
                ModBase.ClipboardSet(url, false);
                ModBase.RunInUi(() => HintService.Hint(Lang.Text("Log.Upload.Success", url), HintType.Success));
            }
            catch (Exception ex)
            {
                LogWrapper.Warn(ex, "LogUpload", "上传日志失败");
                ModBase.RunInUi(() => HintService.Hint(Lang.Text("Log.Upload.Failed", ex.Message), HintType.Error));
            }
            finally
            {
                _isUploading = false;
            }
        });
    }

    public static string Upload(string logText)
    {
        if (string.IsNullOrWhiteSpace(logText))
            throw new InvalidOperationException(Lang.Text("Log.Upload.Empty"));

        var content = new FormUrlEncodedContent([
            new KeyValuePair<string, string>("content", Sanitize(logText))
        ]);

        var response = Requester.Fetch(UploadUrl, new FetchParam
        {
            Method = "POST",
            Content = content,
            Timeout = 30000,
            Accept = "application/json",
            MakeLog = true
        });

        var json = (JsonObject)ModBase.GetJson(response);
        var url = json["url"]?.GetValue<string>();

        if (string.IsNullOrWhiteSpace(url))
            throw new InvalidOperationException(Lang.Text("Log.Upload.InvalidResponse"));

        return url;
    }

    private static string Sanitize(string logText)
    {
        logText = McLogFilter.FilterAccessToken(logText, '*');
        return McLogFilter.FilterUserName(logText, '*');
    }
}

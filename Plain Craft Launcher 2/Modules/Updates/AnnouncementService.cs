using PCL.Core.App;
using PCL.Core.App.Localization;

namespace PCL;

public static class AnnouncementService
{
    public static void Load()
    {
        if (States.System.AnnounceSolution > 1)
            return;

        var showedAnnounced = States.Hint.ShowedAnnouncements
            .Split("|".ToCharArray(), StringSplitOptions.RemoveEmptyEntries)
            .ToList();

        var showAnnounce = UpdateManager.remoteServer.GetAnnouncementList().Content
            .Where(x => !showedAnnounced.Contains(x.Id))
            .ToList();

        ModBase.Log("[System] 需要展示的公告数量：" + showAnnounce.Count);

        ModBase.RunInNewThread(() =>
        {
            foreach (var item in showAnnounce)
            {
                var buttons = GetDialogButtons(item);
                ModMain.MyMsgBox(item.Detail, item.Title,
                    button1: buttons[0].Text,
                    button2: buttons.Count > 1 ? buttons[1].Text : "",
                    button3: buttons.Count > 2 ? buttons[2].Text : "",
                    button1Action: buttons[0].Action,
                    button2Action: buttons.Count > 1 ? buttons[1].Action : null,
                    button3Action: buttons.Count > 2 ? buttons[2].Action : null);
            }
        });

        showedAnnounced.AddRange(showAnnounce.Select(x => x.Id));
        showedAnnounced = showedAnnounced.Distinct().ToList();
        States.Hint.ShowedAnnouncements = showedAnnounced.Join("|");
    }

    private static List<(string Text, Action? Action)> GetDialogButtons(VersionAnnouncementContentModel item)
    {
        var buttons = new List<(string Text, Action? Action)>();
        AddButton(item.Btn1);
        AddButton(item.Btn2);
        buttons.Add((Lang.Text("Common.Action.Close"), null));
        return buttons;

        void AddButton(AnnouncementBtnInfoModel? button)
        {
            if (button is null || string.IsNullOrWhiteSpace(button.Text)) return;
            buttons.Add((button.Text.Trim(), () => RaiseButtonEvent(button)));
        }
    }

    private static void RaiseButtonEvent(AnnouncementBtnInfoModel button)
    {
        if (string.IsNullOrWhiteSpace(button.Command)) return;
        if (EventTypeMapper.TryParse(button.Command, out var eventType))
            CustomEvent.Raise(eventType, button.CommandParameter);
    }
}

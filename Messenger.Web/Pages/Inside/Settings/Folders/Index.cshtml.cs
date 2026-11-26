using Messenger.Web.Helpers;

namespace Messenger.Web.Pages.Inside.Settings.Folders
{
    public class IndexModel : SettingsPageModel
    {
        public IndexModel(ApiHelper api) : base(api) { }

        public async Task OnGetAsync()
        {
            ViewData["Title"] = "Папки чатов";
            await InitializeSettingsAsync();
        }
    }
}

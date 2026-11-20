using Messenger.Web.Helpers;

namespace Messenger.Web.Pages.Inside.Settings.Security
{
    public class IndexModel : SettingsPageModel
    {
        public IndexModel(ApiHelper api) : base(api) { }

        public async Task OnGetAsync()
        {
            ViewData["Title"] = "Безопасность";
            await InitializeSettingsAsync();
        }
    }
}

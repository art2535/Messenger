using Messenger.Web.Helpers;

namespace Messenger.Web.Pages.Inside.Settings.Appearance
{
    public class IndexModel : SettingsPageModel
    {
        public IndexModel(ApiHelper api) : base(api) { }

        public async Task OnGetAsync()
        {
            ViewData["Title"] = "Внешний вид";
            await InitializeSettingsAsync();
        }
    }
}

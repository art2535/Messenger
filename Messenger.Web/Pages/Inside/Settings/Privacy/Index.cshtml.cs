using Messenger.Web.Helpers;

namespace Messenger.Web.Pages.Inside.Settings.Privacy
{
    public class IndexModel : SettingsPageModel
    {
        public IndexModel(ApiHelper api) : base(api) { }

        public async Task OnGetAsync()
        {
            ViewData["Title"] = "Конфиденциальность";
            await InitializeSettingsAsync();
        }
    }
}

using System;
using Playnite.SDK;
using Playnite.SDK.Plugins;

namespace QuickSearchNotesHelper
{
    public class QuickSearchNotesPlugin : GenericPlugin
    {
        private static readonly ILogger Log = LogManager.GetLogger();
        private readonly IPlayniteAPI api;
        private static bool registered;     // 防止重复注册

        public override Guid Id => Guid.Parse("48CFBCFC-545D-4737-AAAE-B495A5E2BEA6");

        public QuickSearchNotesPlugin(IPlayniteAPI api) : base(api)
        {
            this.api = api;

            if (!registered)
            {
                QuickSearch.QuickSearchSDK.AddItemSource("GameNotes",
                    new GameNotesSearchSource(api));
                registered = true;
                Log.Info("NotesSearch: source registered.");
            }
        }
    }
}
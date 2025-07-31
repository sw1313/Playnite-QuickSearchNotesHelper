using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using Playnite.SDK;
using Playnite.SDK.Models;
using QuickSearch.SearchItems;

namespace QuickSearchNotesHelper
{
    public sealed class GameNotesSearchSource : ISearchItemSource<string>
    {
        private readonly IPlayniteAPI api;
        private static readonly ILogger Log = LogManager.GetLogger();

        public GameNotesSearchSource(IPlayniteAPI api) => this.api = api;

        public string Name => "📝 Name & Notes";
        public string Keyword => null;          // 想无前缀就改为 null
        public int Priority => 100;

        // ① 永远返回空：避免同步/异步双通道各出一份结果
        public IEnumerable<ISearchItem<string>> GetItems() => Array.Empty<ISearchItem<string>>();
        public IEnumerable<ISearchItem<string>> GetItems(string _) => Array.Empty<ISearchItem<string>>();

        // ② 仅在异步接口返回真正的搜索结果
        public Task<IEnumerable<ISearchItem<string>>> GetItemsTask(string s, IReadOnlyList<Candidate> _)
            => Task.FromResult(SearchSafe(s));

        private IEnumerable<ISearchItem<string>> SearchSafe(string term)
        {
            Log.Debug($"NotesSearch: term='{term}'");
            try { return Search(term); }
            catch (Exception ex)
            {
                Log.Error(ex, "NotesSearch: Search failed");
                return Array.Empty<ISearchItem<string>>();
            }
        }

        private IEnumerable<ISearchItem<string>> Search(string term)
        {
            if (string.IsNullOrWhiteSpace(term))
                yield break;

            var n = term.Trim();

            // 支持 "note 关键字" / "note关键字"
            if (!string.IsNullOrEmpty(Keyword))
            {
                if (n.StartsWith(Keyword + " ", StringComparison.OrdinalIgnoreCase))
                    n = n.Substring(Keyword.Length + 1);
                else if (n.StartsWith(Keyword, StringComparison.OrdinalIgnoreCase))
                    n = n.Substring(Keyword.Length);
                n = n.Trim();
            }
            if (n.Length == 0)
                yield break;

            // ③ 保险去重：同一个 Game.Id 只返回一次
            var seen = new HashSet<Guid>();

            foreach (var g in api.Database.Games)
            {
                if (!seen.Add(g.Id))
                    continue;

                if (Match(g, n))
                    yield return BuildItem(g);
            }
        }

        private static bool Match(Game g, string n) =>
               (!string.IsNullOrEmpty(g.Name) && g.Name.IndexOf(n, StringComparison.OrdinalIgnoreCase) >= 0) ||
               (!string.IsNullOrEmpty(g.Notes) && g.Notes.IndexOf(n, StringComparison.OrdinalIgnoreCase) >= 0);

        private ISearchItem<string> BuildItem(Game g)
        {
            var act = new CommandAction
            {
                Name = "在库中显示",
                Action = () => api.MainView.SelectGame(g.Id),
                CloseAfterExecute = true
            };

            // Keys 供 QuickSearch 二次过滤
            var keys = new List<ISearchKey<string>> { new SimpleKey(g.Name) };
            if (!string.IsNullOrWhiteSpace(g.Notes))
                keys.Add(new SimpleKey(g.Notes));

            return new NoteItem(g, keys, act, api);
        }
    }

    internal sealed class SimpleKey : ISearchKey<string>
    {
        public SimpleKey(string k) { Key = k; Weight = 100f; }
        public string Key { get; }
        public float Weight { get; }
    }

    internal sealed class NoteItem : ISearchItem<string>
    {
        public NoteItem(Game game,
                        IList<ISearchKey<string>> keys,
                        ISearchAction<string> primary,
                        IPlayniteAPI api)
        {
            Game = game;
            Keys = keys;
            Actions = new List<ISearchAction<string>> { primary };
            ScoreMode = ScoreMode.WeightedMaxScore;

            // 图标
            var iconPath = api.Database.GetFullFilePath(game.Icon);
            if (!string.IsNullOrEmpty(iconPath))
                Icon = new Uri(iconPath);

            // 展示信息
            Platform = string.Join(", ", game.Platforms?.ConvertAll(p => p.Name) ?? new List<string>());
            PlaytimeText = $"{game.Playtime / 60}h{game.Playtime % 60}min";
            InstallStatus = game.IsInstalled ? "已安装" : "未安装";
        }

        // —— QuickSearch 用来渲染 —— //
        public string TopLeft => Game.Name;
        public string TopRight => InstallStatus;
        public string BottomLeft => Platform;
        public string BottomCenter => $"游玩时间：{PlaytimeText}";
        public string BottomRight => null;
        public Uri Icon { get; }
        public char? IconChar => null;

        // —— 其余接口字段 —— //
        public string Key => Game.Id.ToString();
        public IList<ISearchKey<string>> Keys { get; }
        public IList<ISearchAction<string>> Actions { get; }
        public ISearchAction<string> PrimaryAction => Actions[0];
        public FrameworkElement DetailsView => null;
        public ScoreMode ScoreMode { get; }

        // 这些在 UI 不需要，返回 null
        public string Name => null;
        public string Description => null;

        // 自用缓存
        public Game Game { get; }
        private string Platform { get; }
        private string PlaytimeText { get; }
        private string InstallStatus { get; }
    }
}
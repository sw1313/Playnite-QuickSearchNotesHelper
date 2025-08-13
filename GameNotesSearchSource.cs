using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
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
        public string Keyword => null;          // 想要无前缀就保持 null
        public int Priority => 100;

        // ① 永远返回空：避免同步/异步双通道各出一份结果
        public IEnumerable<ISearchItem<string>> GetItems() => Array.Empty<ISearchItem<string>>();
        public IEnumerable<ISearchItem<string>> GetItems(string _) => Array.Empty<ISearchItem<string>>();

        // ② 仅在异步接口返回真正的搜索结果；并且在这里物化，避免惰性枚举阶段抛异常
        public Task<IEnumerable<ISearchItem<string>>> GetItemsTask(string s, IReadOnlyList<Candidate> _)
            => Task.FromResult(SearchSafe(s).ToList().AsEnumerable());

        private IEnumerable<ISearchItem<string>> SearchSafe(string term)
        {
            Log.Debug($"NotesSearch: term='{term}'");
            var results = new List<ISearchItem<string>>();

            try
            {
                if (string.IsNullOrWhiteSpace(term))
                    return results;

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
                    return results;

                // ③ 保险去重：同一个 Game.Id 只返回一次
                var seen = new HashSet<Guid>();

                foreach (var g in api.Database.Games)
                {
                    if (g == null || !seen.Add(g.Id))
                        continue;

                    try
                    {
                        if (Match(g, n))
                        {
                            var item = BuildItem(g);
                            if (item != null)
                                results.Add(item);
                        }
                    }
                    catch (Exception exPerGame)
                    {
                        Log.Warn(exPerGame, $"NotesSearch: skip game '{g?.Name}' ({g?.Id})");
                        // 单条失败不影响其它
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "NotesSearch: Search failed");
            }

            return results;
        }

        private static bool Match(Game g, string n) =>
               (!string.IsNullOrEmpty(g.Name) && g.Name.IndexOf(n, StringComparison.OrdinalIgnoreCase) >= 0) ||
               (!string.IsNullOrEmpty(g.Notes) && g.Notes.IndexOf(n, StringComparison.OrdinalIgnoreCase) >= 0) ||
               (!string.IsNullOrEmpty(g.Description) && g.Description.IndexOf(n, StringComparison.OrdinalIgnoreCase) >= 0); // 如只想搜 Notes，可删掉本行

        private ISearchItem<string> BuildItem(Game g)
        {
            var act = new CommandAction
            {
                Name = "在库中显示",
                Action = () => api.MainView.SelectGame(g.Id),
                CloseAfterExecute = true
            };

            // Keys 供 QuickSearch 二次过滤
            var keys = new List<ISearchKey<string>>
            {
                new SimpleKey(g.Name ?? string.Empty)
            };
            if (!string.IsNullOrWhiteSpace(g.Notes))
                keys.Add(new SimpleKey(g.Notes));
            if (!string.IsNullOrWhiteSpace(g.Description))
                keys.Add(new SimpleKey(g.Description)); // 如只想搜 Notes，可删掉本行

            return new NoteItem(g, keys, act, api);
        }
    }

    internal sealed class SimpleKey : ISearchKey<string>
    {
        public SimpleKey(string k) { Key = k ?? string.Empty; Weight = 100f; }
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
            Keys = keys ?? new List<ISearchKey<string>>();
            Actions = new List<ISearchAction<string>> { primary };
            ScoreMode = ScoreMode.WeightedMaxScore;

            // —— 安全处理图标，避免 UriFormatException —— //
            try
            {
                var iconPath = api?.Database?.GetFullFilePath(game?.Icon);
                if (!string.IsNullOrWhiteSpace(iconPath) &&
                    Uri.TryCreate(iconPath, UriKind.Absolute, out var uri))
                {
                    Icon = uri;
                }
            }
            catch (Exception) { /* 忽略图标错误 */ }

            // 展示信息
            try
            {
                var platforms = (game?.Platforms ?? new List<Platform>()).Select(p => p?.Name).Where(n => !string.IsNullOrWhiteSpace(n));
                Platform = string.Join(", ", platforms);

                var minutes = Math.Max(0, Convert.ToInt32(game?.Playtime ?? 0)); // Playtime 单位：分钟
                var h = minutes / 60;
                var m = minutes % 60;
                PlaytimeText = $"{h}h{m}min";

                InstallStatus = (game?.IsInstalled ?? false) ? "已安装" : "未安装";
            }
            catch
            {
                Platform = string.Empty;
                PlaytimeText = "0h0min";
                InstallStatus = string.Empty;
            }
        }

        // —— QuickSearch 渲染字段 —— //
        public string TopLeft => Game?.Name;
        public string TopRight => InstallStatus;
        public string BottomLeft => Platform;
        public string BottomCenter => $"游玩时间：{PlaytimeText}";
        public string BottomRight => null;
        public Uri Icon { get; }
        public char? IconChar => null;

        // —— 其余接口字段 —— //
        public string Key => Game?.Id.ToString();
        public IList<ISearchKey<string>> Keys { get; }
        public IList<ISearchAction<string>> Actions { get; }
        public ISearchAction<string> PrimaryAction => Actions[0];
        public System.Windows.FrameworkElement DetailsView => null;
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
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using AION_2_Companion.Core;
using AION_2_Companion.Core.Accounts;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;

namespace AION_2_Companion.ViewModels;

/// <summary>Node colours and coordinates describe the game board, NOT the character's unlocked path.</summary>
public sealed record DeviationCell(Bitmap? Art, bool HasNode, bool IsStart, string Tooltip);

public sealed record DeviationBoard(
    string Name, int UnlockLevel, int MaxNodes, int FullCost, bool IsPreview,
    int Columns, IReadOnlyList<DeviationCell> Cells, int? OpenNodes)
{
    public double BoardWidth => Columns * 62.0 + 1;
    public string UnlockText => LocalizationService.Format("UiDevBoardsUnlockLevel", UnlockLevel);
    public string ProgressText => OpenNodes is int count
        ? LocalizationService.Format("UiDevBoardsProgressKnown", Math.Clamp(count, 0, MaxNodes), MaxNodes, Math.Clamp(count * 100.0 / MaxNodes, 0, 100))
        : LocalizationService.Format("UiDevBoardsProgressUnknown", MaxNodes);
    public string FullCostText => LocalizationService.Format("UiDevBoardsFullCost", FullCost);
    public string LayoutStatusText => IsPreview
        ? LocalizationService.Get("UiDevBoardsArielIllustrativeLayoutOnlyExactNodeCoordinatesHaveNot")
        : LocalizationService.Get("UiDevBoardsRealBoardLayoutApiDoesNotProvideStatusIndividualUnlock");
}

public sealed class CharactersViewModel : INotifyPropertyChanged, IDisposable
{
    private sealed record BoardDefinition(string Name, int UnlockLevel, int MaxNodes,
        int FullCost, bool IsPreview, string Rows);

    // First five board coordinate and grade maps: factual grid metadata from
    // jonboy648/aion2-companion (research/daevanion_sorcerer.json, 2026-10-03).
    // c=Common, r=Rare, e=Epic/Legend, u=Unique, s=Start, .=no node.
    // Ariel currently reuses the Azphel board silhouette until its dedicated layout is available.
    private static readonly BoardDefinition[] Definitions =
    [
            new("Nezekan", 12, 88, 134, false, "ucc.rce.rcu/c.ecc.ccc.c/rcc.cec.ecr/.c..c.c.c.c/cceccccrccc/e..c.s.c..e/cccrccccecc/c.c.c.c..c./rce.cec.ccr/c.ccc.cce.c/ucr.ecr.ccu"),
            new("Zikel", 20, 88, 134, false, "ucccecrcecu/c.r.c..c..c/ecc.cceccrc/c.ccr.c.c.c/cce.c.crcce/.c..csc..c./eccrc.c.ecc/c.c.c.rcc.c/crccecc.cce/c..c..c.r.c/ucecrcecccu"),
            new("Vaizel", 30, 88, 134, false, "ucr.cecrccu/c.ccc.c.c.c/cec.ecccecr/.c..c.r..c./cccrccccecc/e..c.s.c..e/cceccccrccc/.c..r.c..c./rceccce.cec/c.c.c.ccc.c/uccrcec.rcu"),
            new("Triniel", 40, 116, 168, false, "ucc..cuc..ccu/c.eccr.rcce.c/c.c..ccc..c.c/eccrcc.cecccr/..c.c...c..c./ccccecccccrcc/e.c.c.s.c.c.e/ccrcccccecccc/.c..c...c.c../rcccec.ccrcce/c.c..ccc..c.c/c.eccr.rcce.c/ucc..cuc..ccu"),
            new("Ariel", 45, 152, 232, true, "ucce.rcu.rceccu/c..ccc.c.c..c.c/rccr.crcceccrcr/..c...c.c.c.c../ecc.eccecrcce../c.rcc.c..c..ccr/c...ccrcceccr.c/ucc..c.s.c..ccu/c.rcceccrcc...c/rcc..c..c.ccr.c/..eccrcecce.cce/..c.c.c.c...c../rcrcceccrc.rccr/c.c..c.c.ccc..c/uccecr.ucr.eccu"),
            new("Azphel", 45, 152, 232, false, "ucce.rcu.rceccu/c..ccc.c.c..c.c/rccr.crcceccrcr/..c...c.c.c.c../ecc.eccecrcce../c.rcc.c..c..ccr/c...ccrcceccr.c/ucc..c.s.c..ccu/c.rcceccrcc...c/rcc..c..c.ccr.c/..eccrcecce.cce/..c.c.c.c...c../rcrcceccrc.rccr/c.c..c.c.ccc..c/uccecr.ucr.eccu")
    ];

    private static readonly Dictionary<char, Bitmap?> Art = new();
    private DeviationBoard? _selectedBoard;
    private IReadOnlyList<DeviationBoard> _boards = [];

    public CharactersViewModel()
    {
        CharacterProfileService.Shared.Changed += OnProfileChanged;
        LocalizationService.LanguageChanged += OnLanguageChanged;
        RebuildBoards();
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public IReadOnlyList<DeviationBoard> Boards
    {
        get => _boards;
        private set { _boards = value; Notify(); }
    }
    public DeviationBoard? SelectedBoard
    {
        get => _selectedBoard;
        set { if (ReferenceEquals(_selectedBoard, value)) return; _selectedBoard = value; Notify(); }
    }

    private void RebuildBoards()
    {
        string? selectionName = SelectedBoard?.Name;
        CharacterProfile? profile = CharacterProfileService.Shared.Current;
        Boards = Definitions.Select(definition =>
        {
            CharacterDevotion? progress = profile?.Devotions.FirstOrDefault(node =>
                node.Name.Equals(definition.Name, StringComparison.OrdinalIgnoreCase) ||
                node.DevotionId.Equals(definition.Name, StringComparison.OrdinalIgnoreCase));
            int? openNodes = progress is null ? null : progress.Rank;
            string[] rows = definition.Rows.Split('/');
            var cells = new List<DeviationCell>(rows.Length * rows.Length);
            for (int y = 0; y < rows.Length; y++)
            {
                for (int x = 0; x < rows[y].Length; x++)
                {
                    char grade = rows[y][x];
                    bool active = grade != '.';
                    string category = grade switch
                    {
                        'u' => LocalizationService.Get("UiDevBoardsUnique4Points"), 'e' => LocalizationService.Get("UiDevBoardsLegend3Points"),
                        'r' => LocalizationService.Get("UiDevBoardsRare2Points"), 'c' => LocalizationService.Get("UiDevBoardsCommon1Point"),
                        's' => LocalizationService.Get("UiDevBoardsStartNode"), _ => string.Empty
                    };
                    cells.Add(new DeviationCell(
                        active ? LoadArt(grade) : null,
                        active, grade == 's',
                        active ? $"{definition.Name} · ({x + 1}, {y + 1}) · {category}"
                               : string.Empty));
                }
            }
            return new DeviationBoard(definition.Name, definition.UnlockLevel, definition.MaxNodes,
                definition.FullCost, definition.IsPreview, rows.Length, cells, openNodes);
        }).ToArray();
        SelectedBoard = Boards.FirstOrDefault(board => board.Name == selectionName) ?? Boards[0];
    }

    private static Bitmap? LoadArt(char kind)
    {
        lock (Art)
        {
            if (Art.TryGetValue(kind, out Bitmap? cached)) return cached;
            string name = kind switch
            {
                'u' => "unique.png", 'e' => "epic.png", 'r' => "rare.png",
                'c' => "common.png", 's' => "start.png", _ => "locked.png"
            };
            string path = Path.Combine(AppContext.BaseDirectory, "Data", "Assets", "Deviations", name);
            try
            {
                if (File.Exists(path))
                {
                    using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    return Art[kind] = new Bitmap(stream);
                }
                string assembly = Uri.EscapeDataString(typeof(CharactersViewModel).Assembly.GetName().Name!);
                var uri = new Uri($"avares://{assembly}/Data/Assets/Deviations/{name}");
                using var embedded = AssetLoader.Open(uri);
                return Art[kind] = new Bitmap(embedded);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or UriFormatException)
            {
                return Art[kind] = null;
            }
        }
    }

    private void OnLanguageChanged(object? sender, EventArgs e) => Dispatcher.UIThread.Post(RebuildBoards);

    private void OnProfileChanged(object? sender, EventArgs e)
        => Dispatcher.UIThread.Post(RebuildBoards);

    private void Notify([CallerMemberName] string? property = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));

    public void Dispose()
    {
        CharacterProfileService.Shared.Changed -= OnProfileChanged;
        LocalizationService.LanguageChanged -= OnLanguageChanged;
    }
}

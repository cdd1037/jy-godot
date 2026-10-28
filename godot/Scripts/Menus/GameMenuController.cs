using Godot;
using JinYong.Core.Session;

namespace JinYong.Host;

/// <summary>Navigation and modal lifetime; all party mutations remain in GameSession.</summary>
public sealed class GameMenuController : IDisposable
{
    private enum Page { None, Title, Game, System, StatusPicker, Status, MedicineActor, MedicineTarget,
        DetoxActor, DetoxTarget, LeavePicker, LeaveConfirm, Settings, NewGameConfirm, TitleConfirm, QuitConfirm }
    private readonly GameViewHost _host;
    private readonly GameMenuPanel _panel;
    private Page _page = Page.Title, _settingsReturn = Page.Title, _confirmReturn = Page.Title;
    private GameSession? _shownSession;
    private int _actor = -1, _target = -1;
    private bool _external, _hasGame, _busy, _disposed;
    private string? _message;
    private ulong? _lastActionFrame;
    private GameSession? _departureReturn;

    public bool IsOpen => _panel.IsOpen;
    public bool BlocksSimulation => _page != Page.None || _external;
    public bool HasGame => _hasGame;
    public bool IsTitle => _page is Page.Title or Page.NewGameConfirm || _page == Page.Settings && _settingsReturn == Page.Title;
    public GamePreferences Preferences { get; private set; } = new();
    public event Action? NewGameRequested;
    public event Action? LoadRequested;
    public event Action? SaveRequested;
    public event Action? InventoryRequested;
    public event Action<bool>? ResourcesRequested;
    public event Action? QuitRequested;
    public event Action? InputAcknowledged;
    public event Action<GamePreferences>? PreferencesChanged;

    public GameMenuController(GameViewHost host, GameMenuPanel panel)
    {
        _host = host; _panel = panel;
        panel.EntryActivated += Activate; panel.BackRequested += Back;
        host.SessionChanged += SessionChanged;
        Render();
    }

    public void SetBusy(bool busy) { _busy = busy; if (!_external && _page != Page.None) Render(); }
    public void SetPreferences(GamePreferences preferences, bool notify = true)
    {
        _message = null;
        Preferences = preferences;
        if (notify) PreferencesChanged?.Invoke(preferences);
        if (_page == Page.Settings && !_external) Render();
    }

    public void Advance()
    {
        if (_disposed || _busy || _external) return;
        if (_departureReturn is { } departing && ReferenceEquals(_host.Session, departing) && departing.CanSave && !_host.View.IsMoving)
        {
            _departureReturn = null;
            OpenGameMenu();
        }
        if (_page is not (Page.None or Page.Title or Page.Settings or Page.NewGameConfirm or Page.QuitConfirm) &&
            (_host.Session is not { CanSave: true } || _host.View.IsMoving)) Close();
    }

    public bool OpenGameMenu()
    {
        if (_disposed || _busy || BlocksSimulation || !_hasGame || _host.Session?.CanSave != true || _host.View.IsMoving) return false;
        Navigate(Page.Game); InputAcknowledged?.Invoke(); return true;
    }

    public void NotifyGameStarted()
    {
        _hasGame = true; _external = false; Close();
    }

    public void ShowTitle()
    {
        _external = false; Navigate(Page.Title); InputAcknowledged?.Invoke();
    }

    public void ResumeFromChild(string? error = null)
    {
        if (!_external) return;
        _external = false; _message = error;
        if (_hasGame && _host.Session?.CanSave != true) { Close(); return; }
        Render(); InputAcknowledged?.Invoke();
    }

    public void ReportError(string error)
    {
        _external = false; _message = error;
        if (_page == Page.None) _page = _hasGame ? Page.Game : Page.Title;
        Render();
    }

    public bool HandleKeyInput(InputEvent input)
    {
        if (_disposed || _external) return false;
        if (IsOpen) { _panel.HandleKeyInput(input); return true; }
        if (input is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape } && OpenGameMenu()) return true;
        return false;
    }

    private void Activate(string id)
    {
        if (_disposed || _busy || _external || _lastActionFrame == Engine.GetProcessFrames() || !IsOpen ||
            !ReferenceEquals(_host.Session, _shownSession)) return;
        _lastActionFrame = Engine.GetProcessFrames();
        if (id == "back") { BackCore(); return; }
        if (id == "cancel") { Navigate(_confirmReturn); return; }
        switch (id)
        {
            case "resume": Close(); break;
            case "new":
                if (_hasGame) Confirm(Page.NewGameConfirm, _page); else RequestExternal(NewGameRequested);
                break;
            case "start-confirmed": RequestExternal(NewGameRequested); break;
            case "load": RequestExternal(LoadRequested); break;
            case "save": RequestExternal(SaveRequested); break;
            case "inventory": RequestExternal(InventoryRequested); break;
            case "import": RequestResources(false); break;
            case "open-library": RequestResources(true); break;
            case "status": Navigate(Page.StatusPicker); break;
            case "medicine": Navigate(Page.MedicineActor); break;
            case "detox": Navigate(Page.DetoxActor); break;
            case "leave": Navigate(Page.LeavePicker); break;
            case "system": Navigate(Page.System); break;
            case "settings": _settingsReturn = _page; Navigate(Page.Settings); break;
            case "smooth": SetPreferences(Preferences with { SmoothMovement = !Preferences.SmoothMovement }); break;
            case "music": SetPreferences(Preferences with { MusicEnabled = !Preferences.MusicEnabled }); break;
            case "title": Confirm(Page.TitleConfirm, _page); break;
            case "title-confirmed": ShowTitle(); break;
            case "quit": Confirm(Page.QuitConfirm, _page); break;
            case "quit-confirmed": QuitRequested?.Invoke(); break;
            case "leave-confirmed": Depart(); break;
            default:
                if (id.StartsWith("role:", StringComparison.Ordinal) && int.TryParse(id.AsSpan(5), out var role)) SelectRole(role);
                break;
        }
    }

    private void SelectRole(int role)
    {
        if (_host.Session is not { CanSave: true, Party: { } party } session || !party.Contains(role)) return;
        switch (_page)
        {
            case Page.StatusPicker: _target = role; Navigate(Page.Status); break;
            case Page.MedicineActor: _actor = role; Navigate(Page.MedicineTarget); break;
            case Page.DetoxActor: _actor = role; Navigate(Page.DetoxTarget); break;
            case Page.MedicineTarget: case Page.DetoxTarget:
                var kind = _page == Page.MedicineTarget ? PartyActionKind.Medicine : PartyActionKind.Detox;
                var result = session.UsePartyAbility(kind, _actor, role);
                _message = result.Message; Render(); _host.View.QueueRedraw(); break;
            case Page.LeavePicker:
                if (role == 0) return;
                _target = role; Confirm(Page.LeaveConfirm, _page); break;
        }
    }

    private void Depart()
    {
        if (_host.Session is not { CanSave: true } session) return;
        var result = session.RequestCompanionDeparture(_target);
        if (result.Applied) { _departureReturn = session; Close(); }
        else { Navigate(Page.LeavePicker); _message = result.Message; Render(); }
    }

    private void Confirm(Page page, Page returnPage) { _confirmReturn = returnPage; Navigate(page); }
    private void RequestExternal(Action? handler)
    {
        if (handler is null) return;
        _external = true; _panel.Close(); InputAcknowledged?.Invoke(); handler();
    }
    private void RequestResources(bool library)
    {
        _external = true; _panel.Close(); InputAcknowledged?.Invoke(); ResourcesRequested?.Invoke(library);
    }
    private void Back()
    {
        if (_disposed || _busy || _external || _lastActionFrame == Engine.GetProcessFrames()) return;
        _lastActionFrame = Engine.GetProcessFrames(); BackCore();
    }
    private void BackCore()
    {
        switch (_page)
        {
            case Page.Title: if (_hasGame) Close(); break;
            case Page.Game: Close(); break;
            case Page.Settings: Navigate(_settingsReturn); break;
            case Page.Status: Navigate(Page.StatusPicker); break;
            case Page.MedicineTarget: Navigate(Page.MedicineActor); break;
            case Page.DetoxTarget: Navigate(Page.DetoxActor); break;
            case Page.LeaveConfirm: case Page.NewGameConfirm: case Page.TitleConfirm: case Page.QuitConfirm:
                Navigate(_confirmReturn); break;
            default: Navigate(Page.Game); break;
        }
    }
    private void Navigate(Page page) { _page = page; _message = null; Render(); }
    private void Close()
    {
        _page = Page.None; _external = false; _panel.Close(); InputAcknowledged?.Invoke();
    }
    private void SessionChanged() { _departureReturn = null; _hasGame = false; _external = false; Navigate(Page.Title); }

    private void Render()
    {
        if (_disposed || _external || _page == Page.None) return;
        _shownSession = _host.Session;
        var session = _shownSession;
        var members = session?.Party?.Members.Where(id => id >= 0).ToArray() ?? [];
        var available = !_busy && _host.Package is not null;
        GameMenuEntry[] Roles(IEnumerable<int> ids) => ids.Select(id => new GameMenuEntry($"role:{id}",
            session!.Party!.Roles[id].Name)).Append(new("back", "返回")).ToArray();
        GameMenuEntry[] ConfirmEntries(string yes) => [new(yes, "确定"), new("cancel", "取消")];
        GameMenuModel model = _page switch
        {
            Page.Title => new GameMenuModel("金庸群侠传", _busy ? "正在准备江湖……" : "一卷江湖，待君启程",
                [new("resume", "继续当前游戏", available && _hasGame), new("new", "新的开始", available),
                 new("load", "载入进度", !_busy && _host.Library is not null), new("import", "准备原版资源", !_busy),
                 new("open-library", "打开已有资源库", !_busy), new("settings", "游戏设置", !_busy), new("quit", "退出游戏", !_busy)]),
            Page.Game => new("江湖行旅", session?.IsWorld == true ? "江湖世界" : session?.Scene.Name ?? "",
                [new("medicine", "医疗", members.Length > 0), new("detox", "解毒", members.Length > 0),
                 new("inventory", "物品"), new("status", "状态", members.Length > 0),
                 new("leave", "离队", members.Any(id => id != 0)), new("system", "系统")]),
            Page.System => new("系统", "当前进度不会自动保存。",
                [new("save", "存档", _host.Library is not null), new("load", "读档", _host.Library is not null),
                 new("settings", "设置"), new("title", "标题"), new("quit", "离开")]),
            Page.StatusPicker => new("人物状态", "选择队伍中的人物。", Roles(members)),
            Page.Status => new("人物状态", session?.Party?.Roles.GetValueOrDefault(_target)?.Name ?? "",
                [new("back", "返回人物列表")], session is null ? "" : PartyStatusFormatter.Build(session, _target)),
            Page.MedicineActor or Page.DetoxActor => new(_page == Page.MedicineActor ? "医疗 · 选择施术者" : "解毒 · 选择施术者",
                "选择技能达到要求的队友。", Roles(session?.GetPartyActionActors(_page == Page.MedicineActor
                    ? PartyActionKind.Medicine : PartyActionKind.Detox) ?? [])),
            Page.MedicineTarget or Page.DetoxTarget => new(_page == Page.MedicineTarget ? "医疗 · 选择对象" : "解毒 · 选择对象",
                $"施术者：{session?.Party?.Roles.GetValueOrDefault(_actor)?.Name}", Roles(members)),
            Page.LeavePicker => new("离队", "选择一位同伴，按其剧情流程辞别。", Roles(members.Where(id => id != 0))),
            Page.LeaveConfirm => new("与同伴辞别", "是否让这位同伴离开队伍？", ConfirmEntries("leave-confirmed"),
                session?.Party?.Roles.GetValueOrDefault(_target)?.Name ?? ""),
            Page.Settings => new("游戏设置", "设置会保存到本机。",
                [new("music", $"背景音乐：{(Preferences.MusicEnabled ? "开启" : "关闭")}"),
                 new("smooth", $"平滑移动：{(Preferences.SmoothMovement ? "开启" : "关闭")}"), new("back", "返回")]),
            Page.NewGameConfirm => new("新的开始", "重新开始将丢弃当前未保存的进度。", ConfirmEntries("start-confirmed")),
            Page.TitleConfirm => new("返回标题", "当前游戏会保留，可从标题继续；进度尚未保存到磁盘。", ConfirmEntries("title-confirmed")),
            Page.QuitConfirm => new("退出游戏", "确定退出？未保存的进度将丢失。", ConfirmEntries("quit-confirmed")),
            _ => new GameMenuModel("江湖", "", [new("back", "返回")])
        };
        var layout = _page switch
        {
            Page.Title => GameMenuLayout.Title,
            Page.Game => GameMenuLayout.Main,
            Page.System => GameMenuLayout.System,
            Page.StatusPicker or Page.MedicineActor or Page.MedicineTarget or Page.DetoxActor or Page.DetoxTarget or Page.LeavePicker
                => GameMenuLayout.Party,
            _ => GameMenuLayout.Page
        };
        _panel.Render(model with { Error = _message, Layout = layout });
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _host.SessionChanged -= SessionChanged;
        _panel.EntryActivated -= Activate; _panel.BackRequested -= Back;
    }
}

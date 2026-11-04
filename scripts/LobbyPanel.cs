using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Roguelike;

/// <summary>
/// Окно сетевой игры. Два экрана:
/// 1) подключение — имя, адрес хоста, кнопки «Создать игру» и «Подключиться»;
/// 2) лобби — кто уже в отряде, выбор своего класса, у хоста — сложность и кнопка «Начать».
/// LineEdit — однострочное поле ввода текста.
/// </summary>
public partial class LobbyPanel : CanvasLayer
{
    public event Action<string> HostRequested;          // имя
    public event Action<string, string> JoinRequested;  // имя, адрес
    public event Action<int> ClassChosen;
    public event Action<DifficultyDef> StartRequested;
    public event Action ResumeRequested;                // продолжить сохранённый сетевой забег
    public event Action LeaveRequested;                 // выйти из лобби
    public event Action BackRequested;                  // назад в главное меню

    private ColorRect _connectScreen;
    private ColorRect _roomScreen;
    private LineEdit _nameEdit;
    private LineEdit _addressEdit;
    private Button _hostButton;
    private Button _joinButton;
    private Label _connectStatus;

    private VBoxContainer _memberList;
    private Label _addressesLabel;
    private HBoxContainer _difficultyRow;
    private Button _startButton;
    private Button _resumeButton;
    private Label _roomStatus;
    private readonly List<Button> _classButtons = new();
    private DifficultyDef _difficulty = Difficulties.Normal;

    /// <summary>Какой класс выбран сейчас (по нему же создаётся герой при подключении).</summary>
    public int SelectedClass { get; private set; }

    public bool IsInRoom => Visible && _roomScreen.Visible;

    public override void _Ready()
    {
        Layer = 10;
        Visible = false;
        CreateConnectScreen();
        CreateRoomScreen();
    }

    private void CreateConnectScreen()
    {
        var column = Ui.CreateCenteredScreen(this, 0.6f, out _connectScreen);
        column.AddChild(Ui.CreateTitle("Сетевая игра", 40));
        column.AddChild(Ui.CreateMutedLabel(16, "До четырёх игроков. Один создаёт игру, остальные подключаются по его адресу."));

        column.AddChild(Ui.CreateMutedLabel(15, "Твоё имя"));
        _nameEdit = CreateEdit("Игрок", 16);
        column.AddChild(_nameEdit);

        column.AddChild(Ui.CreateMutedLabel(15, $"Адрес хоста (для подключения), порт по умолчанию {NetSession.DefaultPort}"));
        _addressEdit = CreateEdit("127.0.0.1", 40);
        column.AddChild(_addressEdit);

        var buttons = new HBoxContainer();
        buttons.AddThemeConstantOverride("separation", 12);
        column.AddChild(buttons);
        _hostButton = Ui.CreateButton("Создать игру", () => HostRequested?.Invoke(_nameEdit.Text));
        buttons.AddChild(_hostButton);
        _joinButton = Ui.CreateButton("Подключиться", () => JoinRequested?.Invoke(_nameEdit.Text, _addressEdit.Text));
        buttons.AddChild(_joinButton);

        _connectStatus = Ui.CreateWrappedLabel(16, 612f);
        _connectStatus.AddThemeColorOverride("font_color", UiTheme.TextMuted);
        column.AddChild(_connectStatus);

        var back = Ui.CreateButton("Назад", () => BackRequested?.Invoke());
        back.SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin;
        column.AddChild(back);
    }

    private static LineEdit CreateEdit(string text, int maxLength)
    {
        var edit = new LineEdit { Text = text, MaxLength = maxLength, CustomMinimumSize = new Vector2(612f, 46f) };
        edit.AddThemeFontSizeOverride("font_size", 20);
        return edit;
    }

    private void CreateRoomScreen()
    {
        var column = Ui.CreateCenteredScreen(this, 0.6f, out _roomScreen);
        _roomScreen.Visible = false;
        column.AddChild(Ui.CreateTitle("Лобби", 40));

        _memberList = new VBoxContainer();
        _memberList.AddThemeConstantOverride("separation", 6);
        _memberList.CustomMinimumSize = new Vector2(560f, 0f);
        column.AddChild(_memberList);

        column.AddChild(Ui.CreateMutedLabel(15, "Твой герой"));
        var classRow = new HBoxContainer();
        classRow.AddThemeConstantOverride("separation", 12);
        column.AddChild(classRow);
        var classGroup = new ButtonGroup();
        for (int i = 0; i < ClassCatalog.All.Length; i++)
        {
            int index = i;
            var playerClass = ClassCatalog.All[i];
            var toggle = CreateToggle(playerClass.Name, classGroup, i == 0, () =>
            {
                SelectedClass = index;
                ClassChosen?.Invoke(index);
            });
            toggle.AddThemeColorOverride("font_color", playerClass.Color.Lightened(0.35f));
            classRow.AddChild(toggle);
            _classButtons.Add(toggle);
        }

        _difficultyRow = new HBoxContainer();
        _difficultyRow.AddThemeConstantOverride("separation", 12);
        column.AddChild(_difficultyRow);
        _difficultyRow.AddChild(Ui.CreateMutedLabel(15, "Сложность:"));
        var difficultyGroup = new ButtonGroup();
        foreach (var difficulty in Difficulties.All)
            _difficultyRow.AddChild(CreateToggle(difficulty.Name, difficultyGroup, difficulty == _difficulty, () => _difficulty = difficulty));

        _addressesLabel = Ui.CreateWrappedLabel(15, 560f);
        _addressesLabel.AddThemeColorOverride("font_color", UiTheme.TextMuted);
        column.AddChild(_addressesLabel);

        _roomStatus = Ui.CreateWrappedLabel(16, 560f);
        column.AddChild(_roomStatus);

        var buttons = new HBoxContainer();
        buttons.AddThemeConstantOverride("separation", 12);
        column.AddChild(buttons);
        _startButton = Ui.CreateButton("Начать забег", () => StartRequested?.Invoke(_difficulty));
        buttons.AddChild(_startButton);
        _resumeButton = Ui.CreateButton("Продолжить забег", () => ResumeRequested?.Invoke());
        buttons.AddChild(_resumeButton);
        buttons.AddChild(Ui.CreateButton("Выйти", () => LeaveRequested?.Invoke()));
    }

    /// <summary>Кнопка-переключатель: в группе нажата всегда ровно одна.</summary>
    private static Button CreateToggle(string text, ButtonGroup group, bool pressed, Action onPressed)
    {
        var toggle = new Button
        {
            Text = text,
            ToggleMode = true,
            ButtonGroup = group,
            ButtonPressed = pressed,
            CustomMinimumSize = new Vector2(150f, 40f),
        };
        toggle.AddThemeFontSizeOverride("font_size", 18);
        toggle.Pressed += onPressed;
        Ui.AddFeedback(toggle);
        return toggle;
    }

    public void OpenConnect(string status = "")
    {
        Visible = true;
        _connectScreen.Visible = true;
        _roomScreen.Visible = false;
        _connectStatus.Text = status;
        Ui.FocusLater(_hostButton);
    }

    public void SetConnectStatus(string status) => _connectStatus.Text = status;

    public void OpenRoom(bool isHost)
    {
        Visible = true;
        _connectScreen.Visible = false;
        _roomScreen.Visible = true;
        _difficultyRow.Visible = isHost;
        _startButton.Visible = isHost;
        _resumeButton.Visible = isHost && SaveSystem.HasNetSave;
        _roomStatus.Text = isHost ? "" : "Ждём, пока хост начнёт забег.";

        var addresses = NetSession.LocalAddresses();
        _addressesLabel.Visible = isHost;
        _addressesLabel.Text = addresses.Count == 0
            ? "Адрес этого компьютера не найден. Друзья в той же сети подключаются по 127.0.0.1 только с этого же компьютера."
            : $"Друзья подключаются по адресу: {string.Join("  или  ", addresses)} (порт {NetSession.DefaultPort}). " +
              "Через интернет — по адресу из Radmin VPN, ZeroTier или с пробросом порта.";
        Ui.FocusLater(_classButtons[SelectedClass]);
    }

    public void SetRoomStatus(string status) => _roomStatus.Text = status;

    /// <summary>Перерисовать список игроков. Свой — с пометкой «ты», хост — «хост».</summary>
    public void UpdateMembers(IReadOnlyList<LobbyMember> members, long myId)
    {
        foreach (Node child in _memberList.GetChildren())
        {
            _memberList.RemoveChild(child);
            child.QueueFree();
        }
        _memberList.AddChild(Ui.CreateMutedLabel(15, $"В отряде {members.Count} из {GameManager.MaxPartySize}"));
        foreach (var member in members)
        {
            var playerClass = ClassCatalog.All[member.ClassIndex];
            var tags = new List<string>();
            if (member.PeerId == 1)
                tags.Add("хост");
            if (member.PeerId == myId)
                tags.Add("ты");
            string suffix = tags.Count > 0 ? $"   ({string.Join(", ", tags)})" : "";
            var label = Ui.CreateLabel(20, $"{member.Name}  •  {playerClass.Name}{suffix}");
            label.AddThemeColorOverride("font_color", playerClass.Color.Lightened(0.35f));
            _memberList.AddChild(label);
        }
    }

    /// <summary>Подставить сохранённые имя и адрес (последние, с которыми играли).</summary>
    public void Prefill(string name, string address)
    {
        if (!string.IsNullOrWhiteSpace(name))
            _nameEdit.Text = name;
        if (!string.IsNullOrWhiteSpace(address))
            _addressEdit.Text = address;
    }

    // Для самопроверки: «нажать» те же кнопки, что и игрок. EmitSignal запускает настоящие обработчики
    public void ClickHost() => _hostButton.EmitSignal(BaseButton.SignalName.Pressed);
    public void ClickJoin() => _joinButton.EmitSignal(BaseButton.SignalName.Pressed);
    public void ClickStart() => _startButton.EmitSignal(BaseButton.SignalName.Pressed);
    public void ClickResume() => _resumeButton.EmitSignal(BaseButton.SignalName.Pressed);
    public void ClickClass(int index) => _classButtons[index].EmitSignal(BaseButton.SignalName.Pressed);

    public string EnteredName => _nameEdit.Text;
    public string EnteredAddress => _addressEdit.Text;

    /// <summary>Поле ввода сейчас в фокусе — тогда Esc и буквы не должны управлять игрой.</summary>
    public bool IsTyping => _nameEdit.HasFocus() || _addressEdit.HasFocus();
}

using System;
using Godot;

namespace Roguelike;

/// <summary>
/// Экран настроек. Любое изменение сразу применяется и сохраняется в файл,
/// поэтому кнопки «Применить» не нужно.
/// </summary>
public partial class SettingsMenu : CanvasLayer
{
    public event Action Closed;

    private HSlider _volume;
    private Label _volumeValue;
    private HSlider _sounds;
    private Label _soundsValue;
    private HSlider _music;
    private Label _musicValue;
    private CheckButton _fullscreen;
    private OptionButton _animation;
    private OptionButton _graphics;
    private CheckButton _damageNumbers;
    private CheckButton _shake;

    public override void _Ready()
    {
        Layer = 20; // поверх всех остальных меню
        Visible = false;
        var column = Ui.CreateCenteredScreen(this, 0.8f);

        var title = Ui.CreateLabel(44, "Настройки");
        title.HorizontalAlignment = HorizontalAlignment.Center;
        title.AddThemeColorOverride("font_color", Ui.TitleColor);
        column.AddChild(title);

        (_volume, _volumeValue) = CreateSlider(column, "Общая громкость", value => Settings.Current.Volume = value);
        (_sounds, _soundsValue) = CreateSlider(column, "Звуки", value => Settings.Current.Sounds = value);
        (_music, _musicValue) = CreateSlider(column, "Музыка", value => Settings.Current.Music = value);

        var animationRow = CreateRow(column, "Анимации");
        _animation = new OptionButton { CustomMinimumSize = new Vector2(220f, 36f) };
        _animation.AddThemeFontSizeOverride("font_size", 18);
        _animation.AddItem("Обычные");
        _animation.AddItem("Быстрые");
        _animation.AddItem("Без анимаций");
        _animation.ItemSelected += index =>
        {
            Settings.Current.Animation = (AnimationSpeed)index;
            Changed();
        };
        animationRow.AddChild(_animation);

        var graphicsRow = CreateRow(column, "Графика");
        _graphics = new OptionButton { CustomMinimumSize = new Vector2(220f, 36f) };
        _graphics.AddThemeFontSizeOverride("font_size", 18);
        _graphics.AddItem("Высокая");
        _graphics.AddItem("Низкая (быстрее)");
        _graphics.AddItem("Ультра");
        _graphics.ItemSelected += index =>
        {
            Settings.Current.Graphics = (GraphicsQuality)index;
            Changed();
        };
        graphicsRow.AddChild(_graphics);

        _fullscreen = CreateToggle(column, "Полный экран", on => Settings.Current.Fullscreen = on);
        _damageNumbers = CreateToggle(column, "Цифры урона", on => Settings.Current.DamageNumbers = on);
        _shake = CreateToggle(column, "Тряска камеры", on => Settings.Current.CameraShake = on);

        column.AddChild(new Control { CustomMinimumSize = new Vector2(0f, 10f) });
        var keys = new KeyBindPanel { Name = "KeyBindPanel" };
        AddChild(keys);
        column.AddChild(Ui.CreateButton("Управление", keys.Open));
        column.AddChild(Ui.CreateButton("Назад", Close));
    }

    public void Open()
    {
        // SetValueNoSignal меняет значение, не вызывая обработчики, — иначе открытие окна «сохраняло» бы настройки
        var current = Settings.Current;
        _volume.SetValueNoSignal(current.Volume);
        _volumeValue.Text = $"{current.Volume}%";
        _sounds.SetValueNoSignal(current.Sounds);
        _soundsValue.Text = $"{current.Sounds}%";
        _music.SetValueNoSignal(current.Music);
        _musicValue.Text = $"{current.Music}%";
        _animation.Selected = (int)current.Animation;
        _graphics.Selected = (int)current.Graphics;
        _fullscreen.SetPressedNoSignal(current.Fullscreen);
        _damageNumbers.SetPressedNoSignal(current.DamageNumbers);
        _shake.SetPressedNoSignal(current.CameraShake);

        Visible = true;
        _volume.CallDeferred(Control.MethodName.GrabFocus);
    }

    public void Close()
    {
        Visible = false;
        Closed?.Invoke();
    }

    private static void Changed()
    {
        Settings.Apply();
        Settings.Save();
    }

    private static (HSlider Slider, Label Value) CreateSlider(Node parent, string caption, Action<int> apply)
    {
        var row = CreateRow(parent, caption);
        var slider = new HSlider { MinValue = 0, MaxValue = 100, Step = 5, CustomMinimumSize = new Vector2(220f, 24f) };
        var value = Ui.CreateLabel(20);
        value.CustomMinimumSize = new Vector2(60f, 0f);
        slider.ValueChanged += newValue =>
        {
            apply((int)newValue);
            value.Text = $"{(int)newValue}%";
            Changed();
        };
        row.AddChild(slider);
        row.AddChild(value);
        return (slider, value);
    }

    private static HBoxContainer CreateRow(Node parent, string caption)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 16);
        parent.AddChild(row);
        var label = Ui.CreateLabel(20, caption);
        label.CustomMinimumSize = new Vector2(190f, 0f);
        row.AddChild(label);
        return row;
    }

    private static CheckButton CreateToggle(Node parent, string caption, Action<bool> apply)
    {
        var toggle = new CheckButton { Text = caption };
        toggle.AddThemeFontSizeOverride("font_size", 20);
        toggle.Toggled += on =>
        {
            apply(on);
            Changed();
        };
        parent.AddChild(toggle);
        return toggle;
    }
}

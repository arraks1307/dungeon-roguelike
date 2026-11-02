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
    private CheckButton _fullscreen;
    private OptionButton _animation;
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

        var volumeRow = CreateRow(column, "Громкость");
        _volume = new HSlider { MinValue = 0, MaxValue = 100, Step = 5, CustomMinimumSize = new Vector2(220f, 24f) };
        _volume.ValueChanged += value =>
        {
            Settings.Current.Volume = (int)value;
            _volumeValue.Text = $"{(int)value}%";
            Changed();
        };
        volumeRow.AddChild(_volume);
        _volumeValue = Ui.CreateLabel(20);
        _volumeValue.CustomMinimumSize = new Vector2(60f, 0f);
        volumeRow.AddChild(_volumeValue);

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

        _fullscreen = CreateToggle(column, "Полный экран", on => Settings.Current.Fullscreen = on);
        _damageNumbers = CreateToggle(column, "Цифры урона", on => Settings.Current.DamageNumbers = on);
        _shake = CreateToggle(column, "Тряска камеры", on => Settings.Current.CameraShake = on);

        column.AddChild(new Control { CustomMinimumSize = new Vector2(0f, 10f) });
        column.AddChild(Ui.CreateButton("Назад", Close));
    }

    public void Open()
    {
        // SetValueNoSignal меняет значение, не вызывая обработчики, — иначе открытие окна «сохраняло» бы настройки
        var current = Settings.Current;
        _volume.SetValueNoSignal(current.Volume);
        _volumeValue.Text = $"{current.Volume}%";
        _animation.Selected = (int)current.Animation;
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

    private static HBoxContainer CreateRow(Node parent, string caption)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 16);
        parent.AddChild(row);
        var label = Ui.CreateLabel(20, caption);
        label.CustomMinimumSize = new Vector2(150f, 0f);
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

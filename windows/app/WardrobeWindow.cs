using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;

internal sealed class WardrobeWindow : Window
{
    private readonly PetWindow ownerPet;
    private readonly CatScene previewScene = new CatScene();
    private readonly DispatcherTimer previewTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(40) };
    private readonly Dictionary<CatStyle, Border> charCards = new Dictionary<CatStyle, Border>();
    private readonly Dictionary<KeyboardStyle, Button> kbButtons = new Dictionary<KeyboardStyle, Button>();
    private readonly Dictionary<MouseStyle, Button> mouseButtons = new Dictionary<MouseStyle, Button>();
    private readonly Dictionary<int, Button> propButtons = new Dictionary<int, Button>();
    private readonly Dictionary<int, Button> fxButtons = new Dictionary<int, Button>();
    private readonly TextBlock statsText = new TextBlock();
    private readonly TextBlock sizeLabel = new TextBlock();
    private readonly Slider sizeSlider = new Slider();
    private readonly Button copyDiagBtn = new Button();
    private readonly CheckBox labelsCheck = new CheckBox();
    private readonly CheckBox privacyCheck = new CheckBox();
    private readonly CheckBox fullscreenCheck = new CheckBox();
    private readonly CheckBox breakCheck = new CheckBox();
    private readonly CheckBox topCheck = new CheckBox();
    private readonly CheckBox throughCheck = new CheckBox();
    private readonly CheckBox autoStartCheck = new CheckBox();
    private readonly double startTime = Environment.TickCount / 1000.0;
    private bool syncing;

    private static Brush BgPage = FreezeBrush(0xF6, 0xF3, 0xEE);
    private static Brush BgCard = FreezeBrush(0xFF, 0xFF, 0xFF);
    private static Brush BorderIdle = FreezeBrush(0xE4, 0xDD, 0xD3);
    private static Brush BorderSelected = FreezeBrush(0xE8, 0x7A, 0x3D);
    private static Brush FillSelected = FreezeBrush(0xFF, 0xF3, 0xE8);
    private static Brush TextPrimary = FreezeBrush(0x2F, 0x29, 0x25);
    private static Brush TextMuted = FreezeBrush(0x7A, 0x6F, 0x67);

    private static Brush FreezeBrush(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    public WardrobeWindow(PetWindow owner)
    {
        ownerPet = owner;
        Title = "键盘小黑猫 · 外观与桌搭面板";
        Width = 570;
        Height = 650;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = BgPage;
        FontFamily = new FontFamily("Microsoft YaHei UI");
        Topmost = true;

        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        var root = new StackPanel { Margin = new Thickness(16, 14, 16, 16) };
        scroll.Content = root;
        Content = scroll;

        root.Children.Add(BuildHeroBanner());
        root.Children.Add(SectionLabel("角色选择（18 位伙伴）"));
        root.Children.Add(BuildCharacterGrid());
        root.Children.Add(SectionLabel("键盘、鼠标与桌面小物件"));
        root.Children.Add(BuildGearCard());
        root.Children.Add(SectionLabel("动效手感和日常习惯"));
        root.Children.Add(BuildPreferencesCard());

        previewTimer.Tick += delegate
        {
            if (IsVisible)
                previewScene.Advance(Environment.TickCount / 1000.0 - startTime + 1.0);
        };
        Activated += delegate { RefreshState(); };
        IsVisibleChanged += delegate
        {
            if (IsVisible) { previewTimer.Start(); RefreshState(); }
            else previewTimer.Stop();
        };
        Closed += delegate { previewTimer.Stop(); };
        RefreshState();
    }

    private UIElement BuildHeroBanner()
    {
        var card = new Border
        {
            Background = BgCard,
            BorderBrush = BorderIdle,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(12),
            Margin = new Thickness(0, 0, 0, 12)
        };
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(168) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var previewHost = new Border
        {
            Width = 156,
            Height = 148,
            CornerRadius = new CornerRadius(10),
            Background = FreezeBrush(0xF9, 0xF7, 0xF4),
            BorderBrush = BorderIdle,
            BorderThickness = new Thickness(1),
            ClipToBounds = true
        };
        var stage = new Canvas { Width = 153, Height = 149, ClipToBounds = true };
        previewScene.Width = KeycatCore.KC_SCENE_WIDTH * 0.5;
        previewScene.Height = KeycatCore.KC_SCENE_HEIGHT * 0.5;
        Canvas.SetLeft(previewScene, -KeycatCore.KC_SCENE_OFFSET_X * 0.5);
        Canvas.SetTop(previewScene, -KeycatCore.KC_SCENE_OFFSET_Y * 0.5);
        stage.Children.Add(previewScene);
        previewHost.Child = stage;
        grid.Children.Add(previewHost);

        var info = new StackPanel { Margin = new Thickness(14, 2, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(info, 1);

        var title = new TextBlock
        {
            Text = "键盘小黑猫 v" + KeycatCore.KC_APP_VERSION,
            FontSize = 16,
            FontWeight = FontWeights.Bold,
            Foreground = TextPrimary
        };
        info.Children.Add(title);

        statsText.FontSize = 12.5;
        statsText.Foreground = TextMuted;
        statsText.Margin = new Thickness(0, 6, 0, 10);
        info.Children.Add(statsText);

        var tips = new TextBlock
        {
            Text = "提示：Ctrl+Alt+H 一键隐藏/显示；开启鼠标穿透时按住 Alt 可临时拖动与右键。",
            FontSize = 11.5,
            Foreground = TextMuted,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10)
        };
        info.Children.Add(tips);

        var btnRow = new WrapPanel();
        var appleKitBtn = MakePillButton("一键苹果极简套装");
        appleKitBtn.Click += delegate
        {
            ownerPet.ApplyWardrobeDevices(KeyboardStyle.Apple, MouseStyle.Apple);
            RefreshState();
        };
        btnRow.Children.Add(appleKitBtn);

        copyDiagBtn.Content = "复制诊断信息";
        StylePillButton(copyDiagBtn, false);
        copyDiagBtn.Margin = new Thickness(8, 0, 0, 0);
        copyDiagBtn.Click += delegate
        {
            try
            {
                Clipboard.SetText(ownerPet.BuildDiagnosticsText());
                copyDiagBtn.Content = "已复制 ✓";
            }
            catch { }
        };
        btnRow.Children.Add(copyDiagBtn);

        var guideBtn = MakePillButton("使用引导");
        guideBtn.Margin = new Thickness(8, 0, 0, 0);
        guideBtn.Click += delegate { ownerPet.ShowGuideWindow(); };
        btnRow.Children.Add(guideBtn);

        info.Children.Add(btnRow);

        grid.Children.Add(info);
        card.Child = grid;
        return card;
    }

    private UIElement BuildCharacterGrid()
    {
        var card = new Border
        {
            Background = BgCard,
            BorderBrush = BorderIdle,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(12, 10, 12, 10),
            Margin = new Thickness(0, 0, 0, 12)
        };
        var wrap = new WrapPanel();
        foreach (var entry in StyleMenu.CharacterList)
        {
            CatStyle style = entry.Key;
            var tile = new Border
            {
                Width = 82,
                Margin = new Thickness(3),
                Padding = new Thickness(4, 6, 4, 6),
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(1.5),
                Cursor = System.Windows.Input.Cursors.Hand,
                Background = BgCard,
                BorderBrush = BorderIdle
            };
            var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            var img = new Image
            {
                Width = 36,
                Height = 36,
                Source = StyleMenu.PortraitBitmap(style, 64),
                HorizontalAlignment = HorizontalAlignment.Center
            };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
            stack.Children.Add(img);
            stack.Children.Add(new TextBlock
            {
                Text = entry.Value,
                FontSize = 11,
                Foreground = TextPrimary,
                TextAlignment = TextAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(0, 4, 0, 0)
            });
            tile.Child = stack;
            tile.MouseLeftButtonUp += delegate
            {
                ownerPet.ApplyWardrobeCharacter(style);
                RefreshState();
            };
            charCards[style] = tile;
            wrap.Children.Add(tile);
        }
        card.Child = wrap;
        return card;
    }

    private UIElement BuildGearCard()
    {
        var card = new Border
        {
            Background = BgCard,
            BorderBrush = BorderIdle,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(12),
            Margin = new Thickness(0, 0, 0, 12)
        };
        var stack = new StackPanel();

        stack.Children.Add(SubRowLabel("键盘"));
        var kbRow = new WrapPanel { Margin = new Thickness(0, 2, 0, 8) };
        foreach (var entry in StyleMenu.KeyboardList)
        {
            KeyboardStyle kb = entry.Key;
            var btn = MakePillButton(entry.Value);
            btn.Margin = new Thickness(0, 0, 6, 4);
            btn.Click += delegate { ownerPet.ApplyWardrobeKeyboard(kb); RefreshState(); };
            kbButtons[kb] = btn;
            kbRow.Children.Add(btn);
        }
        stack.Children.Add(kbRow);

        stack.Children.Add(SubRowLabel("鼠标"));
        var mouseRow = new WrapPanel { Margin = new Thickness(0, 2, 0, 8) };
        foreach (var entry in StyleMenu.MouseList)
        {
            MouseStyle ms = entry.Key;
            var btn = MakePillButton(entry.Value);
            btn.Margin = new Thickness(0, 0, 6, 4);
            btn.Click += delegate { ownerPet.ApplyWardrobeMouse(ms); RefreshState(); };
            mouseButtons[ms] = btn;
            mouseRow.Children.Add(btn);
        }
        stack.Children.Add(mouseRow);

        stack.Children.Add(SubRowLabel("桌面小物件"));
        var propRow = new WrapPanel { Margin = new Thickness(0, 2, 0, 2) };
        foreach (var entry in StyleMenu.DeskPropList)
        {
            int prop = entry.Key;
            var btn = MakePillButton(entry.Value);
            btn.Margin = new Thickness(0, 0, 6, 4);
            btn.Click += delegate { ownerPet.ApplyWardrobeDeskProp(prop); RefreshState(); };
            propButtons[prop] = btn;
            propRow.Children.Add(btn);
        }
        stack.Children.Add(propRow);

        card.Child = stack;
        return card;
    }

    private UIElement BuildPreferencesCard()
    {
        var card = new Border
        {
            Background = BgCard,
            BorderBrush = BorderIdle,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(12)
        };
        var stack = new StackPanel();

        var sizeHeader = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
        sizeLabel.FontSize = 12;
        sizeLabel.Foreground = TextMuted;
        DockPanel.SetDock(sizeLabel, Dock.Right);
        sizeHeader.Children.Add(sizeLabel);
        sizeHeader.Children.Add(new TextBlock { Text = "桌宠缩放大小", FontSize = 12.5, FontWeight = FontWeights.SemiBold, Foreground = TextPrimary });
        stack.Children.Add(sizeHeader);

        sizeSlider.Minimum = Preferences.MinPercent * 10;
        sizeSlider.Maximum = Preferences.MaxPercent * 10;
        sizeSlider.TickFrequency = 100;
        sizeSlider.IsMoveToPointEnabled = true;
        sizeSlider.Margin = new Thickness(0, 0, 0, 10);
        sizeSlider.ValueChanged += delegate
        {
            if (syncing) return;
            ownerPet.ApplyWardrobeScale(sizeSlider.Value / 1000.0);
            sizeLabel.Text = (sizeSlider.Value / 10.0).ToString("0.#", CultureInfo.InvariantCulture) + "%";
        };
        stack.Children.Add(sizeSlider);

        stack.Children.Add(SubRowLabel("打字动效强度"));
        var fxRow = new WrapPanel { Margin = new Thickness(0, 2, 0, 10) };
        foreach (var entry in StyleMenu.FxLevelList)
        {
            int fx = entry.Key;
            var btn = MakePillButton(entry.Value);
            btn.Margin = new Thickness(0, 0, 6, 4);
            btn.Click += delegate { ownerPet.ApplyWardrobeFxLevel(fx); RefreshState(); };
            fxButtons[fx] = btn;
            fxRow.Children.Add(btn);
        }
        stack.Children.Add(fxRow);

        var checks = new WrapPanel();
        SetupCheck(labelsCheck, "显示浮动键帽", c => ownerPet.ApplyWardrobeToggle(p => p.Labels = c));
        SetupCheck(privacyCheck, "键帽隐私打码 (••)", c => ownerPet.ApplyWardrobeToggle(p => p.PrivacyMode = c));
        SetupCheck(fullscreenCheck, "全屏游戏/视频自动隐身", c => ownerPet.ApplyWardrobeToggle(p => p.AutoHideFullscreen = c));
        SetupCheck(breakCheck, "久坐喝水休息提醒", c => ownerPet.ApplyWardrobeToggle(p => p.BreakReminder = c));
        SetupCheck(topCheck, "保持桌面置顶", c => ownerPet.ApplyWardrobeToggle(p => p.OnTop = c));
        SetupCheck(throughCheck, "鼠标穿透 (Alt 临时可拖)", c => ownerPet.ApplyWardrobeToggle(p => p.ClickThrough = c));
        SetupCheck(autoStartCheck, "开机自动启动", delegate { ownerPet.ToggleAutoStartFromWardrobe(); RefreshState(); });

        foreach (CheckBox cb in new[] { labelsCheck, privacyCheck, fullscreenCheck, breakCheck, topCheck, throughCheck, autoStartCheck })
        {
            cb.Width = 238;
            cb.Margin = new Thickness(0, 3, 6, 5);
            checks.Children.Add(cb);
        }
        stack.Children.Add(checks);
        card.Child = stack;
        return card;
    }

    private void SetupCheck(CheckBox box, string label, Action<bool> onChanged)
    {
        box.Content = label;
        box.FontSize = 12;
        box.Foreground = TextPrimary;
        box.Click += delegate
        {
            if (syncing) return;
            onChanged(box.IsChecked == true);
            RefreshState();
        };
    }

    private TextBlock SectionLabel(string text)
    {
        return new TextBlock
        {
            Text = text,
            FontSize = 13,
            FontWeight = FontWeights.Bold,
            Foreground = TextPrimary,
            Margin = new Thickness(2, 0, 0, 6)
        };
    }

    private TextBlock SubRowLabel(string text)
    {
        return new TextBlock
        {
            Text = text,
            FontSize = 11.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = TextMuted,
            Margin = new Thickness(0, 0, 0, 2)
        };
    }

    private Button MakePillButton(string text)
    {
        var btn = new Button
        {
            Content = text,
            Padding = new Thickness(10, 5, 10, 5),
            FontSize = 11.5,
            Cursor = System.Windows.Input.Cursors.Hand
        };
        StylePillButton(btn, false);
        return btn;
    }

    private void StylePillButton(Button btn, bool active)
    {
        btn.Background = active ? FillSelected : BgCard;
        btn.BorderBrush = active ? BorderSelected : BorderIdle;
        btn.Foreground = TextPrimary;
        btn.BorderThickness = new Thickness(active ? 1.5 : 1.0);
        btn.FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal;
    }

    public void RefreshState()
    {
        syncing = true;
        try
        {
            Preferences s = ownerPet.SettingsSnapshot;
            previewScene.Cat = s.Cat;
            previewScene.Keyboard = s.Keyboard;
            previewScene.Mouse = s.Mouse;
            previewScene.DeskProp = s.DeskProp;
            previewScene.FxLevel = s.FxLevel;
            previewScene.PrivacyMode = s.PrivacyMode;
            previewScene.LabelsEnabled = s.Labels;
            if (s.Cat.IsPerson()) previewScene.ResetOfficeInteraction();

            statsText.Text = string.Format(CultureInfo.InvariantCulture,
                "今日陪伴：敲击 {0:N0} 下 · 暴走连击 {1} 次",
                ownerPet.Scene.TodayKeyCount, ownerPet.Scene.TodayBurstCount);

            foreach (var kv in charCards)
            {
                bool active = kv.Key == s.Cat;
                kv.Value.Background = active ? FillSelected : BgCard;
                kv.Value.BorderBrush = active ? BorderSelected : BorderIdle;
            }
            foreach (var kv in kbButtons) StylePillButton(kv.Value, kv.Key == s.Keyboard);
            foreach (var kv in mouseButtons) StylePillButton(kv.Value, kv.Key == s.Mouse);
            foreach (var kv in propButtons) StylePillButton(kv.Value, kv.Key == s.DeskProp);
            foreach (var kv in fxButtons) StylePillButton(kv.Value, kv.Key == s.FxLevel);

            sizeSlider.Value = Math.Round(s.Scale * 1000.0);
            sizeLabel.Text = (sizeSlider.Value / 10.0).ToString("0.#", CultureInfo.InvariantCulture) + "%";

            labelsCheck.IsChecked = s.Labels;
            privacyCheck.IsChecked = s.PrivacyMode;
            fullscreenCheck.IsChecked = s.AutoHideFullscreen;
            breakCheck.IsChecked = s.BreakReminder;
            topCheck.IsChecked = s.OnTop;
            throughCheck.IsChecked = s.ClickThrough;
            autoStartCheck.IsChecked = AutoStart.IsEnabled();
            copyDiagBtn.Content = "复制诊断信息";
        }
        finally
        {
            syncing = false;
        }
    }
}

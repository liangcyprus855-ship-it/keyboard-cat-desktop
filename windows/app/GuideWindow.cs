using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

internal sealed class GuideWindow : Window
{
    private readonly PetWindow ownerPet;
    private readonly CatScene heroScene = new CatScene();
    private readonly CatScene step2Scene = new CatScene();
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private bool renderSubscribed;
    private TimeSpan lastRenderTime = TimeSpan.MinValue;

    private int currentStep;
    private int tryKeyCount;
    private int demoPulseIdx;
    private double simBurstUntil = -10.0, nextSimKey = -10.0;
    private readonly List<Button> stepButtons = new List<Button>();
    private readonly Grid stepHost = new Grid();
    private readonly UIElement[] stepViews = new UIElement[3];

    private readonly TextBlock liveMeter = new TextBlock();
    private readonly TextBlock liveSubNote = new TextBlock();
    private readonly Button prevBtn = new Button();
    private readonly Button wardrobeBtn = new Button();
    private readonly Button nextBtn = new Button();

    private readonly Dictionary<CatStyle, Border> starterCards = new Dictionary<CatStyle, Border>();
    private readonly Dictionary<int, Button> starterPropBtns = new Dictionary<int, Button>();
    private readonly Dictionary<string, Button> starterGearBtns = new Dictionary<string, Button>();

    private readonly CheckBox step3PrivacyCheck = new CheckBox();
    private readonly Border step3CapPreview = new Border();
    private readonly TextBlock step3CapText = new TextBlock();
    private readonly Button step3VibBtn = new Button();
    private readonly Button step3QuietBtn = new Button();
    private readonly CheckBox step3BreakCheck = new CheckBox();
    private readonly CheckBox step3FullscreenCheck = new CheckBox();

    private static readonly Brush BgPage = Freeze(0xF6, 0xF3, 0xEE);
    private static readonly Brush BgCard = Freeze(0xFF, 0xFF, 0xFF);
    private static readonly Brush BgWarm = Freeze(0xFF, 0xF7, 0xEF);
    private static readonly Brush BgPill = Freeze(0xF1, 0xEA, 0xE1);
    private static readonly Brush BorderSoft = Freeze(0xE3, 0xDA, 0xCE);
    private static readonly Brush Accent = Freeze(0xE8, 0x7A, 0x3D);
    private static readonly Brush AccentDark = Freeze(0xCE, 0x61, 0x28);
    private static readonly Brush InkDark = Freeze(0x2E, 0x28, 0x24);
    private static readonly Brush InkMuted = Freeze(0x76, 0x6B, 0x63);
    private static readonly Brush Charcoal = Freeze(0x2B, 0x25, 0x21);

    private static Brush Freeze(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    public GuideWindow(PetWindow owner)
    {
        ownerPet = owner;
        Title = "键盘小黑猫 · 首次使用交互指南";
        Width = 648;
        Height = 608;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = BgPage;
        FontFamily = new FontFamily("Microsoft YaHei UI");
        Topmost = true;

        var rootGrid = new Grid { Margin = new Thickness(18, 14, 18, 16) };
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Content = rootGrid;

        var header = BuildStepperHeader();
        rootGrid.Children.Add(header);

        stepViews[0] = BuildStep1Playground();
        stepViews[1] = BuildStep2PickStarter();
        stepViews[2] = BuildStep3ShortcutsAndPrivacy();
        foreach (UIElement v in stepViews) stepHost.Children.Add(v);
        Grid.SetRow(stepHost, 1);
        rootGrid.Children.Add(stepHost);

        var footer = BuildFooter();
        Grid.SetRow(footer, 2);
        rootGrid.Children.Add(footer);

        SyncPreviewFromOwner();
        SetStep(0);

        PreviewKeyDown += OnGuideKeyDown;
        IsVisibleChanged += delegate
        {
            if (IsVisible) SyncPreviewFromOwner();
            UpdateRenderSubscription();
        };
        KeyDown += delegate(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape) Close();
        };
        Closed += delegate
        {
            SetRenderSubscription(false);
            if (ownerPet != null) ownerPet.MarkGuideCompleted();
        };
    }

    private void UpdateRenderSubscription()
    {
        SetRenderSubscription(IsVisible);
    }

    private void SetRenderSubscription(bool enable)
    {
        if (enable && !renderSubscribed)
        {
            lastRenderTime = TimeSpan.MinValue;
            CompositionTarget.Rendering += OnRendering;
            renderSubscribed = true;
        }
        else if (!enable && renderSubscribed)
        {
            CompositionTarget.Rendering -= OnRendering;
            renderSubscribed = false;
        }
    }

    private void OnRendering(object sender, EventArgs e)
    {
        if (!IsVisible) return;
        var args = e as RenderingEventArgs;
        if (args != null)
        {
            if (args.RenderingTime == lastRenderTime) return;
            lastRenderTime = args.RenderingTime;
        }
        double now = Now();
        if (currentStep == 0 && simBurstUntil > now && now >= nextSimKey)
        {
            int[] keys = { 65, 83, 68, 70, 74, 75, 76, 32 };
            TriggerPlaygroundStrike(keys[demoPulseIdx % keys.Length], now);
            demoPulseIdx++;
            nextSimKey = now + 0.055;
        }
        heroScene.Advance(now);
        step2Scene.Advance(now);
    }

    private double Now() { return clock.Elapsed.TotalSeconds + 10.0; }

    internal int CurrentStepForTest { get { return currentStep; } }
    internal int PlaygroundStrikeCountForTest { get { return tryKeyCount; } }

    internal void SetStepForTest(int step) { SetStep(step); }

    private void OnGuideKeyDown(object sender, KeyEventArgs e)
    {
        if (currentStep != 0) return;
        int vk = KeyInterop.VirtualKeyFromKey(e.Key == Key.System ? e.SystemKey : e.Key);
        if (vk <= 0 || vk == 27) return;
        TriggerPlaygroundStrike(vk, Now());
    }

    private void TriggerPlaygroundStrike(int vk, double now)
    {
        heroScene.Strike(vk, now);
        tryKeyCount++;
        bool hot = heroScene.CatMotionForTest.fury >= 0.45;
        liveMeter.Text = string.Format("实时互动中 · 已体验 {0} 次击键{1}", tryKeyCount, hot ? "（触发爆气连击!）" : "");
        liveSubNote.Text = hot
            ? "手速起飞！快速打字触发残影鞭腿 + 桌面震动 + 星芒连击。"
            : "小贴士：慢速敲键保持克制安静，连续快速连打才会激活暴走特效！";
    }

    internal void SyncPreviewFromOwner()
    {
        CatStyle cat = CatStyle.Black;
        KeyboardStyle kb = KeyboardStyle.Apple;
        MouseStyle ms = MouseStyle.Apple;
        int prop = KeycatCore.KC_PROP_COFFEE;
        int fx = KeycatCore.KC_FX_VIBRANT;
        bool labels = true, privacy = false, brk = true, fs = true;
        if (ownerPet != null)
        {
            Preferences s = ownerPet.SettingsSnapshot;
            cat = s.Cat; kb = s.Keyboard; ms = s.Mouse;
            prop = s.DeskProp; fx = s.FxLevel;
            labels = s.Labels; privacy = s.PrivacyMode;
            brk = s.BreakReminder; fs = s.AutoHideFullscreen;
        }
        foreach (CatScene sc in new[] { heroScene, step2Scene })
        {
            sc.Cat = cat;
            sc.Keyboard = kb;
            sc.Mouse = ms;
            sc.DeskProp = prop;
            sc.FxLevel = fx;
            sc.LabelsEnabled = labels;
            sc.PrivacyMode = privacy;
        }
        RefreshStep2Selection(cat, kb, ms, prop);
        RefreshStep3Controls(privacy, fx, brk, fs);
    }

    private void SetStep(int step)
    {
        currentStep = Math.Max(0, Math.Min(2, step));
        for (int i = 0; i < stepViews.Length; i++)
            stepViews[i].Visibility = i == currentStep ? Visibility.Visible : Visibility.Collapsed;

        for (int i = 0; i < stepButtons.Count; i++)
        {
            bool active = i == currentStep;
            stepButtons[i].Background = active ? Accent : BgCard;
            stepButtons[i].BorderBrush = active ? AccentDark : BorderSoft;
            stepButtons[i].Foreground = active ? Brushes.White : InkMuted;
            stepButtons[i].FontWeight = active ? FontWeights.Bold : FontWeights.SemiBold;
        }

        prevBtn.Visibility = currentStep > 0 ? Visibility.Visible : Visibility.Collapsed;
        wardrobeBtn.Visibility = currentStep >= 1 ? Visibility.Visible : Visibility.Collapsed;
        nextBtn.Content = currentStep == 0 ? "下一步：挑个搭子 ›" : (currentStep == 1 ? "下一步：职场秘籍 ›" : "开始桌面陪伴");
        nextBtn.IsDefault = currentStep == 2;
        if (currentStep == 1) SyncPreviewFromOwner();
    }

    private UIElement BuildStepperHeader()
    {
        var bar = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 0, 0, 12) };

        var pills = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        string[] labels = { "01 实时试用", "02 挑个搭子", "03 职场秘籍" };
        for (int i = 0; i < labels.Length; i++)
        {
            int idx = i;
            var b = new Button
            {
                Content = labels[i],
                Padding = new Thickness(12, 6, 12, 6),
                Margin = new Thickness(idx == 0 ? 0 : 6, 0, 0, 0),
                FontSize = 11.5,
                Cursor = Cursors.Hand
            };
            b.Click += delegate { SetStep(idx); };
            stepButtons.Add(b);
            pills.Children.Add(b);
        }
        DockPanel.SetDock(pills, Dock.Right);
        bar.Children.Add(pills);

        var left = new StackPanel();
        left.Children.Add(new TextBlock
        {
            Text = "KEYCAT STUDIO · v" + KeycatCore.KC_APP_VERSION,
            FontSize = 10,
            FontWeight = FontWeights.Bold,
            Foreground = AccentDark
        });
        left.Children.Add(new TextBlock
        {
            Text = "欢迎领养你的桌面键盘搭子",
            FontSize = 17,
            FontWeight = FontWeights.Bold,
            Foreground = InkDark
        });
        bar.Children.Add(left);
        return bar;
    }

    private UIElement BuildStep1Playground()
    {
        var card = new Border
        {
            Background = BgCard,
            BorderBrush = BorderSoft,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(16),
            Margin = new Thickness(0, 0, 0, 12)
        };
        var stack = new StackPanel();

        var banner = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        banner.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        banner.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var copy = new StackPanel();
        copy.Children.Add(new TextBlock
        {
            Text = "现在直接在键盘上敲几个字，或者点下面按钮试试手感！",
            FontSize = 14,
            FontWeight = FontWeights.Bold,
            Foreground = InkDark
        });
        liveSubNote.Text = "小宠物坐在屏幕角落陪你工作，只有手速加快连打时才会触发爆气解压特效。";
        liveSubNote.FontSize = 11.5;
        liveSubNote.Foreground = InkMuted;
        liveSubNote.Margin = new Thickness(0, 3, 0, 0);
        copy.Children.Add(liveSubNote);
        banner.Children.Add(copy);

        var meterBadge = new Border
        {
            Background = BgWarm,
            BorderBrush = BorderSoft,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(10, 5, 10, 5),
            VerticalAlignment = VerticalAlignment.Center
        };
        liveMeter.Text = "等待你的第一次敲击…";
        liveMeter.FontSize = 11.5;
        liveMeter.FontWeight = FontWeights.SemiBold;
        liveMeter.Foreground = AccentDark;
        meterBadge.Child = liveMeter;
        Grid.SetColumn(meterBadge, 1);
        banner.Children.Add(meterBadge);
        stack.Children.Add(banner);

        var stageFrame = new Border
        {
            Height = 264,
            CornerRadius = new CornerRadius(12),
            Background = BgWarm,
            BorderBrush = BorderSoft,
            BorderThickness = new Thickness(1),
            ClipToBounds = true,
            Margin = new Thickness(0, 0, 0, 12)
        };
        const double stageScale = 0.85;
        var stage = new Canvas
        {
            Width = KeycatCore.KC_WINDOW_WIDTH * stageScale,
            Height = KeycatCore.KC_WINDOW_HEIGHT * stageScale,
            ClipToBounds = true,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        heroScene.Width = KeycatCore.KC_SCENE_WIDTH * stageScale;
        heroScene.Height = KeycatCore.KC_SCENE_HEIGHT * stageScale;
        Canvas.SetLeft(heroScene, -KeycatCore.KC_SCENE_OFFSET_X * stageScale);
        Canvas.SetTop(heroScene, -KeycatCore.KC_SCENE_OFFSET_Y * stageScale);
        stage.Children.Add(heroScene);
        stage.MouseLeftButtonDown += delegate
        {
            double now = Now();
            if (heroScene.BreakOpacityForTest > 0.05) { heroScene.DismissBreak(); return; }
            if (heroScene.Cat.IsPerson()) heroScene.ClickOfficeHead(now);
            else heroScene.Pointer(new Point(214, 105), true, now);
        };
        stageFrame.Child = stage;
        stack.Children.Add(stageFrame);

        var actions = new UniformGrid { Columns = 4 };
        actions.Children.Add(ActionPill("狂敲连击体验", delegate
        {
            if (heroScene.Cat.IsPerson()) heroScene.Cat = CatStyle.Black;
            heroScene.ResetOfficeInteraction();
            simBurstUntil = Now() + 0.95;
            nextSimKey = Now();
        }, new Thickness(0, 0, 6, 0)));

        actions.Children.Add(ActionPill("慢速安静对比", delegate
        {
            simBurstUntil = -10;
            double now = Now();
            heroScene.ClearInput(now);
            heroScene.Strike(74, now);
            liveMeter.Text = "慢速轻敲 · 仅柔和抬爪（不触发震动光环）";
            liveSubNote.Text = "普通写文档或慢速浏览时保持克制不晃眼。";
        }, new Thickness(3, 0, 3, 0)));

        actions.Children.Add(ActionPill("摸同事头顶互动", delegate
        {
            simBurstUntil = -10;
            double now = Now();
            if (!heroScene.Cat.IsPerson()) heroScene.Cat = CatStyle.Boss;
            heroScene.ResetOfficeInteraction();
            heroScene.Pointer(new Point(214, 105), false, false, now, new Vector(0, 0), true);
            heroScene.ClickOfficeHead(now);
            liveMeter.Text = "已触发人类同事头顶互动表情！";
            liveSubNote.Text = "桌面使用时左键拖动动物移位置；人类同事摸头会有表情惊喜。";
        }, new Thickness(3, 0, 3, 0)));

        actions.Children.Add(ActionPill("预览喝水提醒", delegate
        {
            double now = Now();
            heroScene.DeskProp = KeycatCore.KC_PROP_COFFEE;
            heroScene.BreakReminder = true;
            heroScene.ActiveWorkSecondsForTest = 2699.9;
            heroScene.Strike(65, now);
            for (int k = 1; k <= 5; k++) heroScene.Advance(now + k * 0.08);
            liveMeter.Text = "右上角已弹出 45 分钟休息提醒气泡";
            liveSubNote.Text = "连续专注满 45 分钟出现“休息喝口水”，点击气泡或桌宠即可收起。";
        }, new Thickness(6, 0, 0, 0)));

        stack.Children.Add(actions);
        card.Child = stack;
        return card;
    }

    private UIElement BuildStep2PickStarter()
    {
        var card = new Border
        {
            Background = BgCard,
            BorderBrush = BorderSoft,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(16),
            Margin = new Thickness(0, 0, 0, 12)
        };
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(222) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var leftHost = new StackPanel();
        var previewFrame = new Border
        {
            Width = 210,
            Height = 210,
            CornerRadius = new CornerRadius(12),
            Background = BgWarm,
            BorderBrush = BorderSoft,
            BorderThickness = new Thickness(1),
            ClipToBounds = true
        };
        const double scale = 0.68;
        var stage = new Canvas
        {
            Width = KeycatCore.KC_WINDOW_WIDTH * scale,
            Height = KeycatCore.KC_WINDOW_HEIGHT * scale,
            ClipToBounds = true,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        step2Scene.Width = KeycatCore.KC_SCENE_WIDTH * scale;
        step2Scene.Height = KeycatCore.KC_SCENE_HEIGHT * scale;
        Canvas.SetLeft(step2Scene, -KeycatCore.KC_SCENE_OFFSET_X * scale);
        Canvas.SetTop(step2Scene, -KeycatCore.KC_SCENE_OFFSET_Y * scale);
        stage.Children.Add(step2Scene);
        previewFrame.Child = stage;
        leftHost.Children.Add(previewFrame);

        leftHost.Children.Add(new TextBlock
        {
            Text = "右侧点击任意搭配\n桌面小宠物会同步立刻切换",
            FontSize = 11.5,
            Foreground = InkMuted,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 12, 0, 0),
            LineHeight = 18
        });
        grid.Children.Add(leftHost);

        var right = new StackPanel { Margin = new Thickness(16, 0, 0, 0) };
        Grid.SetColumn(right, 1);

        right.Children.Add(MiniSection("1. 挑一位首发桌面伙伴（衣橱里共有 18 位）"));
        var charsPanel = new UniformGrid { Columns = 3, Margin = new Thickness(0, 4, 0, 12) };
        CatStyle[] starters = { CatStyle.Black, CatStyle.Ginger, CatStyle.Corgi, CatStyle.Dachshund, CatStyle.OfficeColleague, CatStyle.Shiba };
        foreach (CatStyle cs in starters)
        {
            CatStyle chosen = cs;
            var box = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            box.Children.Add(new Image
            {
                Source = StyleMenu.PortraitBitmap(chosen, 36),
                Width = 36,
                Height = 36,
                Margin = new Thickness(0, 0, 0, 3)
            });
            box.Children.Add(new TextBlock
            {
                Text = StyleMenu.TitleOf(chosen),
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = InkDark,
                HorizontalAlignment = HorizontalAlignment.Center
            });
            var tile = new Border
            {
                Background = BgCard,
                BorderBrush = BorderSoft,
                BorderThickness = new Thickness(1.5),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(6),
                Margin = new Thickness(3),
                Cursor = Cursors.Hand,
                Child = box
            };
            tile.MouseLeftButtonUp += delegate
            {
                step2Scene.Cat = chosen;
                heroScene.Cat = chosen;
                if (ownerPet != null) ownerPet.ApplyWardrobeCharacter(chosen);
                SyncPreviewFromOwner();
            };
            starterCards[chosen] = tile;
            charsPanel.Children.Add(tile);
        }
        right.Children.Add(charsPanel);

        right.Children.Add(MiniSection("2. 搭配一套桌面键鼠"));
        var gearRow = new WrapPanel { Margin = new Thickness(0, 4, 0, 12) };
        AddGearPreset(gearRow, "apple", "苹果妙控极简风", KeyboardStyle.Apple, MouseStyle.Apple);
        AddGearPreset(gearRow, "classic", "奶油复古打字机", KeyboardStyle.Classic, MouseStyle.Classic);
        AddGearPreset(gearRow, "neon", "冰蓝电竞桌搭", KeyboardStyle.Neon, MouseStyle.Gaming);
        right.Children.Add(gearRow);

        right.Children.Add(MiniSection("3. 桌面右角小摆件"));
        var propRow = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
        foreach (var entry in StyleMenu.DeskPropList)
        {
            int propVal = entry.Key;
            var btn = new Button
            {
                Content = entry.Value,
                Padding = new Thickness(10, 5, 10, 5),
                Margin = new Thickness(0, 0, 6, 4),
                FontSize = 11.5,
                Cursor = Cursors.Hand
            };
            btn.Click += delegate
            {
                step2Scene.DeskProp = propVal;
                heroScene.DeskProp = propVal;
                if (ownerPet != null) ownerPet.ApplyWardrobeDeskProp(propVal);
                SyncPreviewFromOwner();
            };
            starterPropBtns[propVal] = btn;
            propRow.Children.Add(btn);
        }
        right.Children.Add(propRow);

        grid.Children.Add(right);
        card.Child = grid;
        return card;
    }

    private void AddGearPreset(WrapPanel row, string key, string label, KeyboardStyle kb, MouseStyle ms)
    {
        var btn = new Button
        {
            Content = label,
            Padding = new Thickness(10, 5, 10, 5),
            Margin = new Thickness(0, 0, 6, 4),
            FontSize = 11.5,
            Cursor = Cursors.Hand
        };
        btn.Click += delegate
        {
            step2Scene.Keyboard = kb; step2Scene.Mouse = ms;
            heroScene.Keyboard = kb; heroScene.Mouse = ms;
            if (ownerPet != null) ownerPet.ApplyWardrobeDevices(kb, ms);
            SyncPreviewFromOwner();
        };
        starterGearBtns[key] = btn;
        row.Children.Add(btn);
    }

    private UIElement BuildStep3ShortcutsAndPrivacy()
    {
        var stack = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };

        var prefCard = new Border
        {
            Background = BgCard,
            BorderBrush = BorderSoft,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(16),
            Margin = new Thickness(0, 0, 0, 12)
        };
        var prefGrid = new Grid();
        prefGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        prefGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) });

        var left = new StackPanel();
        left.Children.Add(new TextBlock
        {
            Text = "立刻按你的工作习惯调整保护开关",
            FontSize = 14,
            FontWeight = FontWeights.Bold,
            Foreground = InkDark,
            Margin = new Thickness(0, 0, 0, 10)
        });

        step3PrivacyCheck.Content = "键帽隐私打码 (••) — 录屏/直播不露出输入字母";
        step3PrivacyCheck.FontSize = 12;
        step3PrivacyCheck.Foreground = InkDark;
        step3PrivacyCheck.Margin = new Thickness(0, 0, 0, 8);
        step3PrivacyCheck.Click += delegate
        {
            bool on = step3PrivacyCheck.IsChecked == true;
            if (ownerPet != null) ownerPet.ApplyWardrobeToggle(p => p.PrivacyMode = on);
            SyncPreviewFromOwner();
        };
        left.Children.Add(step3PrivacyCheck);

        var fxRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
        fxRow.Children.Add(new TextBlock
        {
            Text = "打字动效：",
            FontSize = 12,
            Foreground = InkMuted,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0)
        });
        step3VibBtn.Content = "高能解压";
        step3VibBtn.Padding = new Thickness(10, 4, 10, 4);
        step3VibBtn.Margin = new Thickness(0, 0, 6, 0);
        step3VibBtn.Cursor = Cursors.Hand;
        step3VibBtn.Click += delegate
        {
            if (ownerPet != null) ownerPet.ApplyWardrobeFxLevel(KeycatCore.KC_FX_VIBRANT);
            SyncPreviewFromOwner();
        };
        step3QuietBtn.Content = "柔和不晃眼";
        step3QuietBtn.Padding = new Thickness(10, 4, 10, 4);
        step3QuietBtn.Cursor = Cursors.Hand;
        step3QuietBtn.Click += delegate
        {
            if (ownerPet != null) ownerPet.ApplyWardrobeFxLevel(KeycatCore.KC_FX_QUIET);
            SyncPreviewFromOwner();
        };
        fxRow.Children.Add(step3VibBtn);
        fxRow.Children.Add(step3QuietBtn);
        left.Children.Add(fxRow);

        step3BreakCheck.Content = "专注满 45 分钟温柔弹出喝水提醒气泡";
        step3BreakCheck.FontSize = 12;
        step3BreakCheck.Foreground = InkDark;
        step3BreakCheck.Margin = new Thickness(0, 0, 0, 8);
        step3BreakCheck.Click += delegate
        {
            bool on = step3BreakCheck.IsChecked == true;
            if (ownerPet != null) ownerPet.ApplyWardrobeToggle(p => p.BreakReminder = on);
            SyncPreviewFromOwner();
        };
        left.Children.Add(step3BreakCheck);

        step3FullscreenCheck.Content = "全屏玩游戏 / 看视频 / 共享屏幕时自动隐藏";
        step3FullscreenCheck.FontSize = 12;
        step3FullscreenCheck.Foreground = InkDark;
        step3FullscreenCheck.Click += delegate
        {
            bool on = step3FullscreenCheck.IsChecked == true;
            if (ownerPet != null) ownerPet.ApplyWardrobeToggle(p => p.AutoHideFullscreen = on);
            SyncPreviewFromOwner();
        };
        left.Children.Add(step3FullscreenCheck);

        prefGrid.Children.Add(left);

        var capDemoBox = new Border
        {
            Background = BgWarm,
            BorderBrush = BorderSoft,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(12),
            VerticalAlignment = VerticalAlignment.Center
        };
        var capStack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        capStack.Children.Add(new TextBlock
        {
            Text = "浮动键帽预览",
            FontSize = 11,
            Foreground = InkMuted,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 10)
        });
        step3CapPreview.Background = BgCard;
        step3CapPreview.BorderBrush = BorderSoft;
        step3CapPreview.BorderThickness = new Thickness(1.5);
        step3CapPreview.CornerRadius = new CornerRadius(7);
        step3CapPreview.Padding = new Thickness(14, 7, 14, 7);
        step3CapPreview.HorizontalAlignment = HorizontalAlignment.Center;
        step3CapText.FontSize = 16;
        step3CapText.FontWeight = FontWeights.Bold;
        step3CapText.Foreground = InkDark;
        step3CapPreview.Child = step3CapText;
        capStack.Children.Add(step3CapPreview);
        capStack.Children.Add(new TextBlock
        {
            Text = "隐私打码仅本地显示",
            FontSize = 10.5,
            Foreground = InkMuted,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 8, 0, 0)
        });
        capDemoBox.Child = capStack;
        Grid.SetColumn(capDemoBox, 1);
        prefGrid.Children.Add(capDemoBox);
        prefCard.Child = prefGrid;
        stack.Children.Add(prefCard);

        var cheatCard = new Border
        {
            Background = Charcoal,
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(16, 14, 16, 14)
        };
        var cheatStack = new StackPanel();
        cheatStack.Children.Add(new TextBlock
        {
            Text = "高频快捷键速查（随时都能用）",
            FontSize = 13,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.White,
            Margin = new Thickness(0, 0, 0, 10)
        });
        cheatStack.Children.Add(KeycapRow(new[] { "Ctrl", "+", "Alt", "+", "H" }, "一键老板键：秒速隐藏 / 呼出桌面宠物"));
        cheatStack.Children.Add(KeycapRow(new[] { "按住", "Alt" }, "穿透模式下临时打破穿透，直接拖动位置或右键开菜单"));
        cheatStack.Children.Add(KeycapRow(new[] { "右键点击" }, "点击小宠物或右下角托盘图标，打开高级精致菜单栏与衣橱"));
        cheatCard.Child = cheatStack;
        stack.Children.Add(cheatCard);
        return stack;
    }

    private UIElement KeycapRow(string[] keys, string desc)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 7) };
        foreach (string k in keys)
        {
            if (k == "+")
            {
                row.Children.Add(new TextBlock
                {
                    Text = "+",
                    FontSize = 11,
                    Foreground = Freeze(0xB8, 0xAC, 0xA2),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(3, 0, 3, 0)
                });
                continue;
            }
            if (k == "按住")
            {
                row.Children.Add(new TextBlock
                {
                    Text = "按住",
                    FontSize = 11,
                    Foreground = Freeze(0xD8, 0xCE, 0xC6),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 4, 0)
                });
                continue;
            }
            row.Children.Add(new Border
            {
                Background = Freeze(0x43, 0x3B, 0x36),
                BorderBrush = Freeze(0x66, 0x5B, 0x53),
                BorderThickness = new Thickness(1, 1, 1, 2),
                CornerRadius = new CornerRadius(5),
                Padding = new Thickness(7, 2, 7, 2),
                Margin = new Thickness(0, 0, 2, 0),
                Child = new TextBlock
                {
                    Text = k,
                    FontSize = 11,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = Freeze(0xFA, 0xF5, 0xEE)
                }
            });
        }
        row.Children.Add(new TextBlock
        {
            Text = desc,
            FontSize = 11.5,
            Foreground = Freeze(0xE8, 0xDF, 0xD6),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 0, 0)
        });
        return row;
    }

    private void RefreshStep2Selection(CatStyle cat, KeyboardStyle kb, MouseStyle ms, int prop)
    {
        foreach (var pair in starterCards)
        {
            bool sel = pair.Key == cat;
            pair.Value.BorderBrush = sel ? Accent : BorderSoft;
            pair.Value.Background = sel ? BgWarm : BgCard;
        }
        string gKey = (kb == KeyboardStyle.Apple && ms == MouseStyle.Apple) ? "apple" :
                      (kb == KeyboardStyle.Neon && ms == MouseStyle.Gaming) ? "neon" :
                      (kb == KeyboardStyle.Classic && ms == MouseStyle.Classic) ? "classic" : "";
        foreach (var pair in starterGearBtns)
            StylePill(pair.Value, pair.Key == gKey);
        foreach (var pair in starterPropBtns)
            StylePill(pair.Value, pair.Key == prop);
    }

    private void RefreshStep3Controls(bool privacy, int fx, bool brk, bool fs)
    {
        step3PrivacyCheck.IsChecked = privacy;
        step3CapText.Text = privacy ? "••" : "K";
        StylePill(step3VibBtn, fx == KeycatCore.KC_FX_VIBRANT);
        StylePill(step3QuietBtn, fx == KeycatCore.KC_FX_QUIET);
        step3BreakCheck.IsChecked = brk;
        step3FullscreenCheck.IsChecked = fs;
    }

    private UIElement BuildFooter()
    {
        var bar = new DockPanel { LastChildFill = true };

        nextBtn.Padding = new Thickness(16, 7, 16, 7);
        nextBtn.FontSize = 12.5;
        nextBtn.FontWeight = FontWeights.Bold;
        nextBtn.Background = Accent;
        nextBtn.BorderBrush = AccentDark;
        nextBtn.Foreground = Brushes.White;
        nextBtn.Cursor = Cursors.Hand;
        nextBtn.Click += delegate
        {
            if (currentStep < 2) SetStep(currentStep + 1);
            else Close();
        };
        DockPanel.SetDock(nextBtn, Dock.Right);
        bar.Children.Add(nextBtn);

        wardrobeBtn.Content = "打开完整衣橱";
        wardrobeBtn.Padding = new Thickness(12, 7, 12, 7);
        wardrobeBtn.Margin = new Thickness(0, 0, 8, 0);
        wardrobeBtn.FontSize = 12;
        wardrobeBtn.Background = BgCard;
        wardrobeBtn.BorderBrush = BorderSoft;
        wardrobeBtn.Foreground = InkDark;
        wardrobeBtn.Cursor = Cursors.Hand;
        wardrobeBtn.Click += delegate
        {
            Close();
            if (ownerPet != null) ownerPet.OpenWardrobeFromGuide();
        };
        DockPanel.SetDock(wardrobeBtn, Dock.Right);
        bar.Children.Add(wardrobeBtn);

        prevBtn.Content = "‹ 上一步";
        prevBtn.Padding = new Thickness(12, 7, 12, 7);
        prevBtn.Margin = new Thickness(0, 0, 8, 0);
        prevBtn.FontSize = 12;
        prevBtn.Background = BgCard;
        prevBtn.BorderBrush = BorderSoft;
        prevBtn.Foreground = InkDark;
        prevBtn.Cursor = Cursors.Hand;
        prevBtn.Click += delegate { if (currentStep > 0) SetStep(currentStep - 1); };
        DockPanel.SetDock(prevBtn, Dock.Right);
        bar.Children.Add(prevBtn);

        bar.Children.Add(new TextBlock
        {
            Text = "可在菜单“新手使用引导…”随时重开",
            FontSize = 11,
            Foreground = InkMuted,
            VerticalAlignment = VerticalAlignment.Center
        });
        return bar;
    }

    private Button ActionPill(string text, RoutedEventHandler handler, Thickness margin)
    {
        var b = new Button
        {
            Content = text,
            Padding = new Thickness(6, 7, 6, 7),
            Margin = margin,
            FontSize = 11.5,
            FontWeight = FontWeights.SemiBold,
            Background = BgWarm,
            BorderBrush = BorderSoft,
            Foreground = InkDark,
            Cursor = Cursors.Hand
        };
        b.Click += handler;
        return b;
    }

    private TextBlock MiniSection(string text)
    {
        return new TextBlock
        {
            Text = text,
            FontSize = 11.5,
            FontWeight = FontWeights.Bold,
            Foreground = InkMuted
        };
    }

    private void StylePill(Button b, bool selected)
    {
        b.Background = selected ? BgWarm : BgPill;
        b.BorderBrush = selected ? Accent : BorderSoft;
        b.Foreground = selected ? AccentDark : InkDark;
        b.FontWeight = selected ? FontWeights.Bold : FontWeights.Regular;
    }
}

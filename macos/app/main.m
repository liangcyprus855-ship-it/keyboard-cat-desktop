#import <Cocoa/Cocoa.h>
#import <ApplicationServices/ApplicationServices.h>
#import "PetView.h"
#import "MacKeys.h"
#import <math.h>

@interface KCPetPanel : NSPanel
@end
@implementation KCPetPanel
- (BOOL)canBecomeKeyWindow { return NO; }
- (BOOL)canBecomeMainWindow { return NO; }
@end

@interface KCController : NSObject <NSApplicationDelegate,NSMenuDelegate,NSWindowDelegate>
- (void)receiveType:(CGEventType)type event:(CGEventRef)event;
- (void)reenableTap;
@end

static CGEventRef Monitor(CGEventTapProxy proxy, CGEventType type, CGEventRef event, void *context) {
    KCController *controller=(__bridge KCController *)context;
    if(type==kCGEventTapDisabledByTimeout || type==kCGEventTapDisabledByUserInput)
        [controller reenableTap];
    else if(event) [controller receiveType:type event:event];
    return event;
}
static double Now(void) { return NSProcessInfo.processInfo.systemUptime; }
static NSInteger StyleDefault(NSString *key, NSInteger fallback, NSInteger maximum) {
    NSInteger value=[NSUserDefaults.standardUserDefaults integerForKey:key];
    return value>=0&&value<=maximum?value:fallback;
}
static double ScaleRatio(id storedRatio) {
    if (![storedRatio isKindOfClass:NSNumber.class]) return 1;
    double ratio=[storedRatio doubleValue];
    return isfinite(ratio) ? fmax(.01, fmin(2.0, ratio)) : 1;
}
static NSSize PetSize(double ratio) {
    return NSMakeSize(306 * ratio, 298 * ratio);
}

@implementation KCController {
    KCPetPanel *_window;
    KCPetView *_pet;
    NSStatusItem *_status;
    NSMenu *_menu;
    NSSlider *_sizeSlider;
    NSTextField *_sizeValue;
    NSTimer *_timer;
    CFMachPortRef _tap;
    CFRunLoopSourceRef _source;
    double _lastConsentCheck, _scale;
    NSPoint _resizeAnchor;
    BOOL _alwaysOnTop, _through, _menuOpen, _permission;
}
- (void)applicationDidFinishLaunching:(NSNotification *)notification {
    NSUserDefaults *defaults=NSUserDefaults.standardUserDefaults;
    [defaults registerDefaults:@{@"cat":@3,@"keyboard":@1,@"mouse":@3,@"labels":@YES,@"alwaysOnTop":@YES,
                                 @"mouseThrough":@NO,@"size":@1.0}];
    _scale=ScaleRatio([defaults objectForKey:@"size"]);
    _through=[defaults boolForKey:@"mouseThrough"];_alwaysOnTop=[defaults boolForKey:@"alwaysOnTop"];
    NSRect frame=(NSRect){NSZeroPoint,PetSize(_scale)};
    _window=[[KCPetPanel alloc] initWithContentRect:frame styleMask:NSWindowStyleMaskBorderless|NSWindowStyleMaskNonactivatingPanel
                                    backing:NSBackingStoreBuffered defer:NO];
    _window.minSize=NSMakeSize(1,1);
    [_window setFrame:frame display:NO];
    _window.title=@"键盘小黑猫";_window.opaque=NO;_window.backgroundColor=NSColor.clearColor;
    _window.hasShadow=NO;_window.hidesOnDeactivate=NO;_window.releasedWhenClosed=NO;
    _window.level=_alwaysOnTop?NSFloatingWindowLevel:NSNormalWindowLevel;
    _window.collectionBehavior=NSWindowCollectionBehaviorCanJoinAllSpaces|NSWindowCollectionBehaviorFullScreenAuxiliary|
                               NSWindowCollectionBehaviorIgnoresCycle;
    _window.ignoresMouseEvents=_through;
    _pet=[[KCPetView alloc] initWithFrame:frame];_pet.catStyle=StyleDefault(@"cat",3,8);_pet.keyboardStyle=StyleDefault(@"keyboard",1,4);
    _pet.mouseStyle=StyleDefault(@"mouse",3,4);_pet.showsLabels=[defaults boolForKey:@"labels"];
    _pet.autoresizingMask=NSViewWidthSizable|NSViewHeightSizable;_window.contentView=_pet;
    _status=[NSStatusBar.systemStatusBar statusItemWithLength:NSSquareStatusItemLength];
    NSImage *icon=[NSImage imageWithSystemSymbolName:@"pawprint.fill" accessibilityDescription:@"键盘小黑猫"];
    if(icon){icon.template=YES;_status.button.image=icon;} else _status.button.title=@"猫";
    _status.button.toolTip=@"键盘小黑猫 · 设置";
    _menu=[[NSMenu alloc] initWithTitle:@"键盘小黑猫"];_menu.autoenablesItems=NO;_menu.delegate=self;
    _status.menu=_menu;_pet.menu=_menu;
    NSString *origin=[defaults stringForKey:@"origin"];BOOL restored=NO;
    if(origin){NSPoint p=NSPointFromString(origin);if(isfinite(p.x)&&isfinite(p.y)){[_window setFrameOrigin:p];restored=[self windowOnScreen];}}
    if(!restored)[self dockToCorner];
    _window.delegate=self;[_window orderFrontRegardless];
    [[NSNotificationCenter defaultCenter] addObserver:self selector:@selector(displayChanged:) name:NSApplicationDidChangeScreenParametersNotification object:nil];
    [self refreshTap];[self rebuildMenu];
    _timer=[NSTimer timerWithTimeInterval:1./40. target:self selector:@selector(tick:) userInfo:nil repeats:YES];
    _timer.tolerance=.004;[[NSRunLoop mainRunLoop] addTimer:_timer forMode:NSRunLoopCommonModes];
    if(!_pet.hasArt){
        NSAlert *alert=[NSAlert new];alert.messageText=@"桌宠图像资源缺失";
        alert.informativeText=@"请从安装包重新安装完整的“键盘小黑猫”应用。";[alert runModal];[NSApp terminate:nil];return;
    }
    if(!_permission && ![defaults boolForKey:@"explainedInput"]){
        [defaults setBool:YES forKey:@"explainedInput"];
        dispatch_async(dispatch_get_main_queue(),^{
            NSAlert *alert=[NSAlert new];alert.messageText=@"让小猫跟着你打字";
            alert.informativeText=@"允许“输入监控”后，小猫能在你使用其他应用时敲键盘。桌宠只使用物理按键编号驱动动画，按键名称短暂显示在本机。你可以随时在菜单栏的猫爪中关闭键帽显示。";
            [alert addButtonWithTitle:@"前往授权"];[alert addButtonWithTitle:@"稍后"];
            if([alert runModal]==NSAlertFirstButtonReturn)[self permissionAction:nil];
        });
    }
}
- (BOOL)windowOnScreen {
    for(NSScreen *s in NSScreen.screens)if(NSIntersectsRect(_window.frame,s.visibleFrame))return YES;
    return NO;
}
- (void)dockToCorner {
    NSScreen *screen=_window.screen?:NSScreen.mainScreen?:NSScreen.screens.firstObject;
    if(!screen)return;NSRect area=screen.visibleFrame;
    [_window setFrameOrigin:NSMakePoint(NSMaxX(area)-_window.frame.size.width-16,NSMinY(area)+12)];
}
- (void)saveOrigin {
    if(!_window || ![self windowOnScreen])return;
    [NSUserDefaults.standardUserDefaults setObject:NSStringFromPoint(_window.frame.origin) forKey:@"origin"];
}
- (void)windowDidMove:(NSNotification *)n {
    [NSObject cancelPreviousPerformRequestsWithTarget:self selector:@selector(saveOrigin) object:nil];
    [self performSelector:@selector(saveOrigin) withObject:nil afterDelay:.25];
}
- (void)displayChanged:(NSNotification *)n {
    if(![self windowOnScreen]){[self dockToCorner];[self saveOrigin];}
}
- (void)disconnectTap {
    if(_source){CFRunLoopRemoveSource(CFRunLoopGetMain(),_source,kCFRunLoopCommonModes);CFRelease(_source);_source=NULL;}
    if(_tap){CFMachPortInvalidate(_tap);CFRelease(_tap);_tap=NULL;}
}
- (void)reenableTap { if(_tap&&_permission&&_window.visible)CGEventTapEnable(_tap,true); }
- (void)refreshTap {
    _lastConsentCheck=Now();_permission=CGPreflightListenEventAccess();
    if(!_permission||!_window.visible){[self disconnectTap];return;}
    if(_tap&&CFMachPortIsValid(_tap)){if(!CGEventTapIsEnabled(_tap))[self reenableTap];return;}
    [self disconnectTap];
    CGEventMask mask=CGEventMaskBit(kCGEventKeyDown)|CGEventMaskBit(kCGEventFlagsChanged)|CGEventMaskBit(NSEventTypeSystemDefined)|
        CGEventMaskBit(kCGEventLeftMouseDown)|CGEventMaskBit(kCGEventLeftMouseUp)|CGEventMaskBit(kCGEventRightMouseDown)|CGEventMaskBit(kCGEventRightMouseUp);
    _tap=CGEventTapCreate(kCGSessionEventTap,kCGHeadInsertEventTap,kCGEventTapOptionListenOnly,mask,Monitor,(__bridge void *)self);
    if(!_tap)return;
    _source=CFMachPortCreateRunLoopSource(kCFAllocatorDefault,_tap,0);
    if(!_source){[self disconnectTap];return;}
    CFRunLoopAddSource(CFRunLoopGetMain(),_source,kCFRunLoopCommonModes);CGEventTapEnable(_tap,true);
}
- (void)receiveType:(CGEventType)type event:(CGEventRef)event {
    if(!_window.visible||_menuOpen||NSApp.modalWindow)return;
    double t=Now();
    if(type==(CGEventType)NSEventTypeSystemDefined) {
        if(_pet.keyboardStyle==4) {
            NSEvent *system=[NSEvent eventWithCGEvent:event];
            NSInteger key=KCSystemArtworkKey(system.subtype,system.data1);
            if(key)[_pet strike:key label:KCKeyLabel(key) at:t];
        }
        return;
    }
    if(type==kCGEventKeyDown||type==kCGEventFlagsChanged){
        CGKeyCode code=(CGKeyCode)CGEventGetIntegerValueField(event,kCGKeyboardEventKeycode);
        if(type==kCGEventFlagsChanged && code!=57) {
            BOOL down=code==63?(CGEventGetFlags(event)&kCGEventFlagMaskSecondaryFn)!=0:
                CGEventSourceKeyState(kCGEventSourceStateCombinedSessionState,code);
            if(!down)return;
        }
        NSInteger vk=KCArtworkKey(code);[_pet strike:vk label:KCKeyLabel(vk) at:t];return;
    }
    if(type==kCGEventLeftMouseDown||type==kCGEventRightMouseDown||type==kCGEventLeftMouseUp||type==kCGEventRightMouseUp){
        NSInteger side=CGEventGetIntegerValueField(event,kCGMouseEventButtonNumber);
        [_pet button:side down:(type==kCGEventLeftMouseDown||type==kCGEventRightMouseDown) at:t];
    }
}
- (void)tick:(NSTimer *)timer {
    if(!_window.visible)return;double t=Now();
    if(t-_lastConsentCheck>3)[self refreshTap];
    NSPoint p=NSEvent.mouseLocation;
    NSPoint windowPoint=[_window convertPointFromScreen:p];NSPoint local=[_pet convertPoint:windowPoint fromView:nil];
    local=NSMakePoint(75+306*local.x/_pet.bounds.size.width,9+298*local.y/_pet.bounds.size.height);
    NSScreen *display=_window.screen?:NSScreen.mainScreen;
    for(NSScreen *s in NSScreen.screens)if(NSPointInRect(p,s.frame)){display=s;break;}
    NSRect f=display.frame;NSPoint norm=NSZeroPoint;
    if(f.size.width>0&&f.size.height>0)norm=NSMakePoint(2*(p.x-NSMinX(f))/f.size.width-1,1-2*(p.y-NSMinY(f))/f.size.height);
    NSUInteger buttons=NSEvent.pressedMouseButtons;
    if(!_menuOpen&&!NSApp.modalWindow) {
        BOOL headInteractive=NO;
        if(_pet.isPerson && !_through && NSPointInRect(p,_window.frame) && [_pet officeHeadAt:local]) {
            NSInteger front=[NSWindow windowNumberAtPoint:p belowWindowWithWindowNumber:0];
            // A pose transition can briefly expose the window underneath before the new frame is displayed.
            headInteractive=front==_window.windowNumber ||
                front==[NSWindow windowNumberAtPoint:p belowWindowWithWindowNumber:_window.windowNumber];
        }
        [_pet samplePointer:local onScreen:norm left:(buttons&1)!=0 right:(buttons&2)!=0 headInteractive:headInteractive at:t];
    }
    [_pet advance:t];
}
- (void)permissionAction:(id)sender {
    if(!CGPreflightListenEventAccess())CGRequestListenEventAccess();
    [self refreshTap];
    if(!_permission){
        NSURL *url=[NSURL URLWithString:@"x-apple.systempreferences:com.apple.preference.security?Privacy_ListenEvent"];
        [[NSWorkspace sharedWorkspace] openURL:url];
    }else if(!_tap&&_window.visible){
        NSAlert *alert=[NSAlert new];alert.messageText=@"请重新打开键盘小黑猫";
        alert.informativeText=@"若系统已允许输入监控，请退出并重开应用，让授权生效。";[alert runModal];
    }
}
- (NSMenuItem *)entry:(NSString *)title action:(SEL)action menu:(NSMenu *)menu {
    NSMenuItem *item=[[NSMenuItem alloc] initWithTitle:title action:action keyEquivalent:@""];
    item.target=self;[menu addItem:item];return item;
}
- (void)styleGroup:(NSString *)title choices:(NSArray<NSString *> *)choices current:(NSInteger)current
             action:(SEL)action resource:(NSString *)format {
    NSMenu *sub=[[NSMenu alloc] initWithTitle:title];
    NSMenuItem *group=[self entry:title action:NULL menu:_menu];group.submenu=sub;
    [choices enumerateObjectsUsingBlock:^(NSString *name,NSUInteger i,BOOL *stop){
        NSMenuItem *item=[self entry:name action:action menu:sub];item.tag=(NSInteger)i;
        item.state=current==(NSInteger)i?NSControlStateValueOn:NSControlStateValueOff;
        NSString *key=[NSString stringWithFormat:format,(long)i];
        item.image=[self->_pet thumbnail:key maxWidth:action==@selector(keyboardAction:)?75:30];
    }];
}
- (void)rebuildMenu {
    [_menu removeAllItems];
    [self styleGroup:@"角色样式" choices:@[@"经典黑猫",@"奶油橘猫",@"日本短尾三花",@"长毛虎斑",@"西装同事",@"发卡同事",@"红结同事",@"发带同事",@"圆镜同事"] current:_pet.catStyle action:@selector(catAction:) resource:@"portrait-%ld"];
    [self styleGroup:@"键盘样式" choices:@[@"经典奶油",@"薄荷打字机",@"冰蓝电竞",@"胡桃木",@"苹果妙控键盘"] current:_pet.keyboardStyle action:@selector(keyboardAction:) resource:@"keyboard-%ld"];
    [self styleGroup:@"鼠标样式" choices:@[@"奶油滚轮",@"雾紫鹅卵石",@"冰蓝电竞",@"酒红轨迹球",@"苹果妙控鼠标"] current:_pet.mouseStyle action:@selector(mouseAction:) resource:@"mouse-%ld-normal"];
    [self entry:@"使用苹果键鼠套装" action:@selector(appleSetup:) menu:_menu].state=
        (_pet.keyboardStyle==4 && _pet.mouseStyle==4)?NSControlStateValueOn:NSControlStateValueOff;
    [self addSizeControl];
    [_menu addItem:NSMenuItem.separatorItem];
    [self entry:@"显示浮动键帽" action:@selector(labelsAction:) menu:_menu].state=_pet.showsLabels?NSControlStateValueOn:NSControlStateValueOff;
    [self entry:@"保持置顶" action:@selector(topAction:) menu:_menu].state=_alwaysOnTop?NSControlStateValueOn:NSControlStateValueOff;
    [self entry:@"鼠标穿透" action:@selector(throughAction:) menu:_menu].state=_through?NSControlStateValueOn:NSControlStateValueOff;
    if(!_permission)[self entry:@"开启键盘联动…" action:@selector(permissionAction:) menu:_menu];
    else if(!_tap&&_window.visible)[self entry:@"重新连接键盘…" action:@selector(permissionAction:) menu:_menu];
    [_menu addItem:NSMenuItem.separatorItem];
    [self entry:@"回到屏幕右下角" action:@selector(cornerAction:) menu:_menu];
    [self entry:_window.visible?@"暂时隐藏":(_pet.isPerson?@"显示角色":@"显示小猫") action:@selector(toggleVisible:) menu:_menu];
    [self entry:_pet.isPerson?@"退出桌宠":@"退出键盘小黑猫" action:@selector(quit:) menu:_menu];
}
- (void)menuNeedsUpdate:(NSMenu *)menu { if(menu==_menu && !_menuOpen)[self rebuildMenu]; }
- (void)menuWillOpen:(NSMenu *)menu {
    _menuOpen=YES;
    [_pet cancelHeadGesture];
    _resizeAnchor=NSMakePoint(NSMaxX(_window.frame),NSMinY(_window.frame));
}
- (void)menuDidClose:(NSMenu *)menu { _menuOpen=NO; }
- (void)catAction:(NSMenuItem *)item { _pet.catStyle=item.tag;[NSUserDefaults.standardUserDefaults setInteger:item.tag forKey:@"cat"];_pet.needsDisplay=YES; }
- (void)keyboardAction:(NSMenuItem *)item { _pet.keyboardStyle=item.tag;[NSUserDefaults.standardUserDefaults setInteger:item.tag forKey:@"keyboard"];_pet.needsDisplay=YES; }
- (void)mouseAction:(NSMenuItem *)item { _pet.mouseStyle=item.tag;[NSUserDefaults.standardUserDefaults setInteger:item.tag forKey:@"mouse"];_pet.needsDisplay=YES; }
- (void)appleSetup:(NSMenuItem *)sender {
    _pet.keyboardStyle=4;
    _pet.mouseStyle=4;
    NSUserDefaults *defaults=NSUserDefaults.standardUserDefaults;
    [defaults setInteger:4 forKey:@"keyboard"];
    [defaults setInteger:4 forKey:@"mouse"];
    _pet.needsDisplay=YES;
}
- (void)addSizeControl {
    NSMenuItem *item=[self entry:_pet.isPerson?@"角色大小":@"小猫大小" action:NULL menu:_menu];
    NSView *row=[[NSView alloc] initWithFrame:NSMakeRect(0,0,254,56)];
    NSTextField *caption=[NSTextField labelWithString:item.title];
    caption.font=[NSFont menuFontOfSize:0];
    caption.frame=NSMakeRect(16,30,120,20);
    [row addSubview:caption];
    _sizeValue=[NSTextField labelWithString:@""];
    _sizeValue.font=[NSFont monospacedDigitSystemFontOfSize:NSFont.smallSystemFontSize weight:NSFontWeightRegular];
    _sizeValue.textColor=NSColor.secondaryLabelColor;
    _sizeValue.alignment=NSTextAlignmentRight;
    _sizeValue.frame=NSMakeRect(171,30,65,19);
    [row addSubview:_sizeValue];
    _sizeSlider=[NSSlider sliderWithValue:_scale*100 minValue:1 maxValue:200 target:self action:@selector(sizeAction:)];
    _sizeSlider.frame=NSMakeRect(14,5,226,23);
    _sizeSlider.continuous=YES;
    _sizeSlider.accessibilityLabel=item.title;
    [row addSubview:_sizeSlider];
    item.view=row;
    [self updateSizeIndicator];
}
- (void)updateSizeIndicator {
    NSInteger tenths=lround(_scale*1000);
    _sizeValue.stringValue=tenths%10
        ? [NSString stringWithFormat:@"%.1f%%",tenths/10.]
        : [NSString stringWithFormat:@"%ld%%",(long)tenths/10];
    _sizeSlider.accessibilityValueDescription=_sizeValue.stringValue;
}
- (void)sizeAction:(NSSlider *)sender {
    double next=ScaleRatio(@(sender.doubleValue/100.));
    if (next==_scale) return;
    _scale=next;
    [self updateSizeIndicator];
    NSRect old=_window.frame;
    NSPoint anchor=_menuOpen?_resizeAnchor:NSMakePoint(NSMaxX(old),NSMinY(old));
    NSSize size=PetSize(_scale);
    [_window setFrame:NSMakeRect(anchor.x-size.width,anchor.y,size.width,size.height) display:YES];
    if(![self windowOnScreen]){
        [self dockToCorner];
        _resizeAnchor=NSMakePoint(NSMaxX(_window.frame),NSMinY(_window.frame));
    }
    [NSUserDefaults.standardUserDefaults setDouble:_scale forKey:@"size"];[self saveOrigin];
}
- (void)labelsAction:(id)sender { _pet.showsLabels=!_pet.showsLabels;[NSUserDefaults.standardUserDefaults setBool:_pet.showsLabels forKey:@"labels"]; }
- (void)topAction:(id)sender { _alwaysOnTop=!_alwaysOnTop;_window.level=_alwaysOnTop?NSFloatingWindowLevel:NSNormalWindowLevel;
    [NSUserDefaults.standardUserDefaults setBool:_alwaysOnTop forKey:@"alwaysOnTop"]; }
- (void)throughAction:(id)sender {
    _through=!_through;_window.ignoresMouseEvents=_through;
    [_pet cancelHeadGesture];
    [NSUserDefaults.standardUserDefaults setBool:_through forKey:@"mouseThrough"];
}
- (void)showPet {
    if(![self windowOnScreen])[self dockToCorner];[_window orderFrontRegardless];[self refreshTap];
}
- (void)cornerAction:(id)sender { [self dockToCorner];[self saveOrigin];[self showPet]; }
- (void)toggleVisible:(id)sender {
    if(_window.visible){[_window orderOut:nil];[_pet clearInput];[self disconnectTap];}else [self showPet];
}
- (BOOL)applicationShouldHandleReopen:(NSApplication *)app hasVisibleWindows:(BOOL)flag { [self showPet];return NO; }
- (void)quit:(id)sender { [NSApp terminate:nil]; }
- (void)applicationWillTerminate:(NSNotification *)n { [_timer invalidate];[self saveOrigin];[self disconnectTap]; }
@end

int main(int argc,const char *argv[]) {
    @autoreleasepool {
        NSApplication *app=NSApplication.sharedApplication;[app setActivationPolicy:NSApplicationActivationPolicyAccessory];
        __attribute__((objc_precise_lifetime)) KCController *controller=[KCController new];
        app.delegate=controller;[app run];
    }
    return 0;
}



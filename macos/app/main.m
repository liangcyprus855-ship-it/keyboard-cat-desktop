#import <Cocoa/Cocoa.h>
#import <ApplicationServices/ApplicationServices.h>
#import "PetView.h"
#import "MacKeys.h"
#import "AutoStart.h"
#import "Updater.h"
#import "keycat_core.h"
#import <math.h>
#include <stdlib.h>

@interface KCPetPanel : NSPanel
@end
@implementation KCPetPanel
- (BOOL)canBecomeKeyWindow { return NO; }
- (BOOL)canBecomeMainWindow { return NO; }
@end

@interface KCController : NSObject <NSApplicationDelegate,NSMenuDelegate,NSWindowDelegate>
- (void)receiveType:(CGEventType)type event:(CGEventRef)event;
- (void)reenableTap;
- (void)cancelMenuForExternalPressAt:(NSTimeInterval)time;
- (BOOL)shouldScheduleStartupUpdateCheck;
- (BOOL)shouldScheduleStartupGuide;
@end

static CGEventRef Monitor(CGEventTapProxy proxy, CGEventType type, CGEventRef event, void *context) {
    KCController *controller=(__bridge KCController *)context;
    if(type==kCGEventTapDisabledByTimeout || type==kCGEventTapDisabledByUserInput)
        [controller reenableTap];
    else if(event) [controller receiveType:type event:event];
    return event;
}

typedef struct {
    CFMachPortRef port;
    CFRunLoopSourceRef source;
} KCEventTap;

static BOOL TapIsActive(KCEventTap tap) {
    return tap.port && CFMachPortIsValid(tap.port) && CGEventTapIsEnabled(tap.port);
}

static void DisconnectTap(KCEventTap *tap) {
    if(tap->source) {
        CFRunLoopRemoveSource(CFRunLoopGetMain(),tap->source,kCFRunLoopCommonModes);
        CFRelease(tap->source);
        tap->source=NULL;
    }
    if(tap->port) {
        CFMachPortInvalidate(tap->port);
        CFRelease(tap->port);
        tap->port=NULL;
    }
}

static void EnableTap(KCEventTap tap) {
    if(tap.port && CFMachPortIsValid(tap.port) && !CGEventTapIsEnabled(tap.port))
        CGEventTapEnable(tap.port,true);
}

static void ConnectTap(KCEventTap *tap, CGEventMask mask, KCController *controller) {
    if(tap->port && CFMachPortIsValid(tap->port)) {
        EnableTap(*tap);
        return;
    }
    DisconnectTap(tap);
    tap->port=CGEventTapCreate(kCGSessionEventTap,kCGHeadInsertEventTap,kCGEventTapOptionListenOnly,
                               mask,Monitor,(__bridge void *)controller);
    if(!tap->port)return;
    tap->source=CFMachPortCreateRunLoopSource(kCFAllocatorDefault,tap->port,0);
    if(!tap->source){DisconnectTap(tap);return;}
    CFRunLoopAddSource(CFRunLoopGetMain(),tap->source,kCFRunLoopCommonModes);
    EnableTap(*tap);
}

static double Now(void) { return NSProcessInfo.processInfo.systemUptime; }
static NSInteger StyleDefault(NSString *key, NSInteger fallback, NSInteger maximum) {
    NSInteger value=[NSUserDefaults.standardUserDefaults integerForKey:key];
    return value>=0&&value<=maximum?value:fallback;
}
static double ScaleRatio(id storedRatio) {
    if (![storedRatio isKindOfClass:NSNumber.class]) return 1;
    double ratio=[storedRatio doubleValue];
    return isfinite(ratio) ? fmax(KC_MIN_SCALE_PERCENT/100., fmin(KC_MAX_SCALE_PERCENT/100., ratio)) : 1;
}
static NSSize PetSize(double ratio) {
    return NSMakeSize(KC_WINDOW_WIDTH * ratio, KC_WINDOW_HEIGHT * ratio);
}

@implementation KCController {
    KCPetPanel *_window;
    KCPetView *_pet;
    NSStatusItem *_status;
    NSMenu *_menu;
    id _outsideMenuMonitor;
    NSTimeInterval _menuOpenedAt;
    NSSlider *_sizeSlider;
    NSTextField *_sizeValue;
    NSTimer *_timer;
    KCEventTap _keyboardTap, _mouseAndModifiersTap, _mediaTap;
    double _lastConsentCheck, _scale, _lastStatsSync;
    NSPoint _resizeAnchor;
    BOOL _alwaysOnTop, _through, _menuOpen, _permission, _checkingUpdates;
    NSString *_statsDate;
    long long _savedKeys;
    NSInteger _savedBursts;
    NSPanel *_wardrobePanel, *_guidePanel;
    KCPetView *_wardrobePreview, *_guidePreview, *_guideStep2Preview;
    NSTextField *_wardrobeStats, *_wardrobeSizeLabel, *_guidePermNote, *_guideLiveMeter, *_guideSubNote, *_guideCapDemo;
    NSButton *_guidePermBtn, *_guidePrevBtn, *_guideWardrobeBtn, *_guideNextBtn, *_gPrivacyChk, *_gBreakChk, *_gVibBtn, *_gQuietBtn;
    NSSegmentedControl *_guideStepper;
    NSView *_guideStepViews[3];
    NSMutableArray<NSButton *> *_gCharBtns, *_gGearBtns, *_gPropBtns;
    NSInteger _guideStep, _guideTryKeys, _guideDemoIdx;
    double _guideBurstUntil, _nextGuideDemoAt;
    id _guideKeyMonitor;
    NSSlider *_wardrobeSizeSlider;
    NSMutableArray<NSButton *> *_catBtns, *_kbdBtns, *_mouseBtns, *_propBtns, *_fxBtns;
    NSButton *_wLabels, *_wPrivacy, *_wBreak, *_wTop, *_wThrough, *_wAutoStart;
}
- (NSString *)todayString {
    static NSDateFormatter *fmt;
    if (!fmt) {
        fmt = [NSDateFormatter new];
        fmt.calendar = [NSCalendar calendarWithIdentifier:NSCalendarIdentifierGregorian];
        fmt.dateFormat = @"yyyy-MM-dd";
    }
    return [fmt stringFromDate:NSDate.date];
}
- (void)ensureTodayStats {
    NSString *today = [self todayString];
    if (![_statsDate isEqualToString:today]) {
        _statsDate = today;
        _pet.todayKeyCount = 0;
        _pet.todayBurstCount = 0;
        _savedKeys = -1;
        _savedBursts = -1;
        [self syncStatsToDefaults:YES];
    }
}
- (void)syncStatsToDefaults:(BOOL)force {
    long long k = _pet.todayKeyCount;
    NSInteger b = _pet.todayBurstCount;
    if (!force && k == _savedKeys && b == _savedBursts) return;
    _savedKeys = k;
    _savedBursts = b;
    NSUserDefaults *defaults = NSUserDefaults.standardUserDefaults;
    [defaults setObject:_statsDate forKey:@"statsDate"];
    [defaults setObject:@(k) forKey:@"todayKeyCount"];
    [defaults setInteger:b forKey:@"todayBurstCount"];
}
- (BOOL)shouldScheduleStartupUpdateCheck {
    const char *disabled=getenv("KEYCAT_NO_AUTO_UPDATE");
    return !(disabled && strcmp(disabled,"1")==0);
}
- (BOOL)shouldScheduleStartupGuide {
    const char *disabled=getenv("KEYCAT_SKIP_GUIDE");
    return !(disabled && strcmp(disabled,"1")==0);
}
- (void)applicationDidFinishLaunching:(NSNotification *)notification {
    NSUserDefaults *defaults=NSUserDefaults.standardUserDefaults;
    [defaults registerDefaults:@{@"cat":@(KC_DEFAULT_CAT),@"keyboard":@(KC_DEFAULT_KEYBOARD),
                                 @"mouse":@(KC_DEFAULT_MOUSE),@"labels":@YES,@"alwaysOnTop":@YES,
                                 @"mouseThrough":@NO,@"size":@1.0,@"autoCheckUpdates":@YES,@"lastUpdateCheck":@0.0,
                                 @"fxLevel":@(KC_FX_VIBRANT),@"deskProp":@(KC_PROP_NONE),
                                 @"privacyMode":@NO,@"breakReminder":@YES,@"guideDone":@NO,
                                 @"statsDate":@"",@"todayKeyCount":@0,@"todayBurstCount":@0}];
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
    _pet=[[KCPetView alloc] initWithFrame:frame];
    _pet.catStyle=StyleDefault(@"cat",KC_DEFAULT_CAT,KC_CAT_STYLE_COUNT-1);
    _pet.keyboardStyle=StyleDefault(@"keyboard",KC_DEFAULT_KEYBOARD,4);
    _pet.mouseStyle=StyleDefault(@"mouse",KC_DEFAULT_MOUSE,4);
    _pet.showsLabels=[defaults boolForKey:@"labels"];
    _pet.fxLevel=StyleDefault(@"fxLevel",KC_FX_VIBRANT,KC_FX_VIBRANT);
    _pet.deskProp=StyleDefault(@"deskProp",KC_PROP_NONE,KC_PROP_MOON);
    _pet.privacyMode=[defaults boolForKey:@"privacyMode"];
    _pet.breakReminderEnabled=[defaults boolForKey:@"breakReminder"];
    _statsDate=[defaults stringForKey:@"statsDate"] ?: @"";
    _pet.todayKeyCount=[[defaults objectForKey:@"todayKeyCount"] longLongValue];
    _pet.todayBurstCount=[defaults integerForKey:@"todayBurstCount"];
    _savedKeys=_pet.todayKeyCount;
    _savedBursts=_pet.todayBurstCount;
    [self ensureTodayStats];
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
    NSString *build=[NSBundle.mainBundle objectForInfoDictionaryKey:@"CFBundleVersion"];
    BOOL showFirstGuide=[self shouldScheduleStartupGuide] && ![defaults boolForKey:@"guideDone"];
    if(showFirstGuide) {
        [defaults setBool:YES forKey:@"guideDone"];
        if(!_permission && build) [defaults setObject:build forKey:@"explainedInputBuild"];
        dispatch_async(dispatch_get_main_queue(),^{ [self openGuideAction:nil]; });
    } else if(!_permission && ![[defaults stringForKey:@"explainedInputBuild"] isEqualToString:build]){
        [defaults setObject:build forKey:@"explainedInputBuild"];
        dispatch_async(dispatch_get_main_queue(),^{
            NSAlert *alert=[NSAlert new];alert.messageText=@"让小猫跟着你打字";
            alert.informativeText=@"请在系统设置的“输入监控”中允许“键盘小黑猫”，然后重新打开桌宠。如果更新后原有开关已开启，请关闭后重新开启。桌宠使用物理键位驱动动画，按键名称短暂显示在本机。";
            [alert addButtonWithTitle:@"前往授权"];[alert addButtonWithTitle:@"稍后"];
            if([alert runModal]==NSAlertFirstButtonReturn)[self permissionAction:nil];
        });
    }
    if([self shouldScheduleStartupUpdateCheck] && [defaults boolForKey:@"autoCheckUpdates"]) {
        double lastCheck=[defaults doubleForKey:@"lastUpdateCheck"];
        double nowEpoch=NSDate.date.timeIntervalSince1970;
        if(nowEpoch-lastCheck>=86400.0) {
            dispatch_after(dispatch_time(DISPATCH_TIME_NOW,(int64_t)(3*NSEC_PER_SEC)),dispatch_get_main_queue(),^{
                [self checkUpdatesSilent:YES];
            });
        }
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
    DisconnectTap(&_keyboardTap);
    DisconnectTap(&_mouseAndModifiersTap);
    DisconnectTap(&_mediaTap);
}
- (void)reenableTap {
    if(!_permission || !_window.visible)return;
    EnableTap(_keyboardTap);
    EnableTap(_mouseAndModifiersTap);
    EnableTap(_mediaTap);
}
- (void)refreshTap {
    _lastConsentCheck=Now();_permission=CGPreflightListenEventAccess();
    if(!_permission||!_window.visible){[self disconnectTap];return;}
    // Quartz can drop individual event bits. A mouse or media port cannot confirm keyboard access.
    ConnectTap(&_keyboardTap,CGEventMaskBit(kCGEventKeyDown),self);
    CGEventMask buttonsAndModifiers=CGEventMaskBit(kCGEventFlagsChanged)|CGEventMaskBit(kCGEventScrollWheel)|
        CGEventMaskBit(kCGEventLeftMouseDown)|CGEventMaskBit(kCGEventLeftMouseUp)|CGEventMaskBit(kCGEventRightMouseDown)|CGEventMaskBit(kCGEventRightMouseUp);
    ConnectTap(&_mouseAndModifiersTap,buttonsAndModifiers,self);
    ConnectTap(&_mediaTap,CGEventMaskBit(NSEventTypeSystemDefined),self);
}
- (void)receiveType:(CGEventType)type event:(CGEventRef)event {
    if(!_window.visible||_menuOpen||NSApp.modalWindow)return;
    double t=Now();
    if(type==kCGEventScrollWheel){[_pet scrollAt:t];return;}
    if(type==(CGEventType)NSEventTypeSystemDefined) {
        if(_pet.keyboardStyle==4) {
            NSEvent *system=[NSEvent eventWithCGEvent:event];
            if(system.type==NSEventTypeSystemDefined) {
                NSInteger key=KCSystemArtworkKey(system.subtype,system.data1);
                if(key)[_pet strike:key label:KCKeyLabel(key) at:t];
            }
        }
        return;
    }
    if(type==kCGEventKeyDown||type==kCGEventFlagsChanged){
        CGKeyCode code=(CGKeyCode)CGEventGetIntegerValueField(event,kCGKeyboardEventKeycode);
        if(type==kCGEventKeyDown && code==4 &&
           CGEventGetIntegerValueField(event,kCGKeyboardEventAutorepeat)==0) {
            CGEventFlags flags=CGEventGetFlags(event);
            if((flags & kCGEventFlagMaskControl) && (flags & kCGEventFlagMaskAlternate)) {
                [self toggleVisible:nil];
                return;
            }
        }
        if(type==kCGEventFlagsChanged && code!=57) {
            BOOL down=code==63?(CGEventGetFlags(event)&kCGEventFlagMaskSecondaryFn)!=0:
                CGEventSourceKeyState(kCGEventSourceStateCombinedSessionState,code);
            if(!down)return;
        }
        NSInteger vk=KCArtworkKey(code);
        if(vk)[_pet strike:vk label:KCKeyLabel(vk) at:t];
        return;
    }
    if(type==kCGEventLeftMouseDown||type==kCGEventRightMouseDown||type==kCGEventLeftMouseUp||type==kCGEventRightMouseUp){
        NSInteger side=CGEventGetIntegerValueField(event,kCGMouseEventButtonNumber);
        [_pet button:side down:(type==kCGEventLeftMouseDown||type==kCGEventRightMouseDown) at:t];
    }
}
- (void)tick:(NSTimer *)timer {
    if(!_window.visible)return;double t=Now();
    if(t-_lastConsentCheck>3)[self refreshTap];
    BOOL effectiveThrough=_through && !_menuOpen && !(NSEvent.modifierFlags & NSEventModifierFlagOption);
    if(_window.ignoresMouseEvents!=effectiveThrough)_window.ignoresMouseEvents=effectiveThrough;
    if(t-_lastStatsSync>2.0){_lastStatsSync=t;[self ensureTodayStats];[self syncStatsToDefaults:NO];}
    if(_wardrobePanel.visible){
        if(_wardrobePreview)[_wardrobePreview advance:t];
        if(_wardrobeStats)_wardrobeStats.stringValue=[NSString stringWithFormat:@"今日战绩：敲击 %lld 下 · 暴走 %ld 次",
                                                       _pet.todayKeyCount,(long)_pet.todayBurstCount];
    }
    if(_guidePanel.visible){
        if(_guideStep==0 && _guidePreview){
            if(_guideBurstUntil>t && t>=_nextGuideDemoAt){
                const NSInteger demoKeys[]={65,83,68,70,74,75,76,32};
                NSInteger vk=demoKeys[_guideDemoIdx%8];
                [self guideStrikeKey:vk label:KCKeyLabel(vk) ?: @"A" at:t];
                _guideDemoIdx++;
                _nextGuideDemoAt=t+0.055;
            }
            [_guidePreview advance:t];
        }
        if(_guideStep==1 && _guideStep2Preview)[_guideStep2Preview advance:t];
        if(_guideStep==2)[self refreshGuidePermission];
    }
    NSPoint p=NSEvent.mouseLocation;
    NSPoint windowPoint=[_window convertPointFromScreen:p];NSPoint local=[_pet convertPoint:windowPoint fromView:nil];
    local=NSMakePoint(KC_SCENE_OFFSET_X+KC_WINDOW_WIDTH*local.x/_pet.bounds.size.width,
                      KC_SCENE_OFFSET_Y+KC_WINDOW_HEIGHT*local.y/_pet.bounds.size.height);
    NSScreen *display=_window.screen?:NSScreen.mainScreen;
    for(NSScreen *s in NSScreen.screens)if(NSPointInRect(p,s.frame)){display=s;break;}
    NSRect f=display.frame;NSPoint norm=NSZeroPoint;
    if(f.size.width>0&&f.size.height>0)norm=NSMakePoint(2*(p.x-NSMinX(f))/f.size.width-1,1-2*(p.y-NSMinY(f))/f.size.height);
    NSUInteger buttons=NSEvent.pressedMouseButtons;
    if(!_menuOpen&&!NSApp.modalWindow) {
        BOOL headInteractive=NO;
        if(_pet.isPerson && !effectiveThrough && NSPointInRect(p,_window.frame) && [_pet officeHeadAt:local]) {
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
    NSURL *url=[NSURL URLWithString:@"x-apple.systempreferences:com.apple.preference.security?Privacy_ListenEvent"];
    [[NSWorkspace sharedWorkspace] openURL:url];
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
- (void)choiceGroup:(NSString *)title choices:(NSArray<NSString *> *)choices current:(NSInteger)current action:(SEL)action {
    NSMenu *sub=[[NSMenu alloc] initWithTitle:title];
    NSMenuItem *group=[self entry:title action:NULL menu:_menu];group.submenu=sub;
    [choices enumerateObjectsUsingBlock:^(NSString *name,NSUInteger i,BOOL *stop){
        NSMenuItem *item=[self entry:name action:action menu:sub];item.tag=(NSInteger)i;
        item.state=current==(NSInteger)i?NSControlStateValueOn:NSControlStateValueOff;
    }];
}
- (NSImage *)sym:(NSString *)name {
    NSImage *img=[NSImage imageWithSystemSymbolName:name accessibilityDescription:nil];
    if(img)img.template=YES;
    return img;
}
- (NSImage *)propMenuIcon:(NSInteger)prop {
    return [NSImage imageWithSize:NSMakeSize(20,20) flipped:YES drawingHandler:^BOOL(NSRect dst){
        if(prop==KC_PROP_NONE){
            [[NSColor secondaryLabelColor] setStroke];
            NSBezierPath *c=[NSBezierPath bezierPathWithOvalInRect:NSMakeRect(4,4,12,12)];
            c.lineWidth=1.4;[c stroke];
            [NSBezierPath strokeLineFromPoint:NSMakePoint(6,14) toPoint:NSMakePoint(14,6)];
        } else if(prop==KC_PROP_COFFEE){
            [[NSColor colorWithSRGBRed:.90 green:.85 blue:.78 alpha:1] setFill];
            [[NSBezierPath bezierPathWithOvalInRect:NSMakeRect(2,14,15,4)] fill];
            [[NSColor colorWithSRGBRed:1. green:.96 blue:.92 alpha:1] setFill];
            [[NSBezierPath bezierPathWithRoundedRect:NSMakeRect(4,6,11,10) xRadius:3 yRadius:3] fill];
            [[NSColor colorWithSRGBRed:.43 green:.24 blue:.13 alpha:1] setFill];
            [[NSBezierPath bezierPathWithOvalInRect:NSMakeRect(5,5,9,3)] fill];
        } else if(prop==KC_PROP_ICED){
            [[NSColor colorWithSRGBRed:.29 green:.64 blue:.48 alpha:1] setStroke];
            NSBezierPath *s=[NSBezierPath bezierPath];s.lineWidth=1.6;[s moveToPoint:NSMakePoint(11,6)];[s lineToPoint:NSMakePoint(14,1.5)];[s stroke];
            [[NSColor colorWithSRGBRed:.61 green:.36 blue:.21 alpha:1] setFill];
            [[NSBezierPath bezierPathWithRoundedRect:NSMakeRect(5,7,9,11) xRadius:1.5 yRadius:1.5] fill];
        } else if(prop==KC_PROP_BRICK){
            [[NSColor colorWithSRGBRed:.72 green:.34 blue:.23 alpha:1] setFill];
            [[NSBezierPath bezierPathWithRoundedRect:NSMakeRect(2,7,16,9) xRadius:1.8 yRadius:1.8] fill];
            [[NSColor colorWithSRGBRed:.85 green:.47 blue:.34 alpha:1] setFill];
            [[NSBezierPath bezierPathWithRoundedRect:NSMakeRect(2,4,16,4.5) xRadius:1.5 yRadius:1.5] fill];
        } else if(prop==KC_PROP_MOON){
            [[NSColor colorWithSRGBRed:.15 green:.19 blue:.26 alpha:1] setFill];
            [[NSBezierPath bezierPathWithRoundedRect:NSMakeRect(3,3,14,14) xRadius:3 yRadius:3] fill];
            [[NSColor colorWithSRGBRed:.97 green:.80 blue:.36 alpha:1] setFill];
            [[NSBezierPath bezierPathWithOvalInRect:NSMakeRect(6.5,6.5,7,7)] fill];
        }
        return YES;
    }];
}
- (void)openFromMenuHeaderAction:(id)sender {
    [_menu cancelTracking];
    [self openWardrobeAction:sender];
}
- (NSView *)menuHeaderView {
    NSView *wrap=[[NSView alloc] initWithFrame:NSMakeRect(0,0,280,68)];
    NSBox *card=[[NSBox alloc] initWithFrame:NSMakeRect(10,4,260,60)];
    card.boxType=NSBoxCustom;card.cornerRadius=10;card.borderWidth=1;
    card.borderColor=[NSColor separatorColor];
    card.fillColor=[NSColor controlBackgroundColor];
    card.titlePosition=NSNoTitle;
    [wrap addSubview:card];
    NSView *cv=card.contentView;
    NSImageView *av=[[NSImageView alloc] initWithFrame:NSMakeRect(8,10,40,40)];
    av.image=[_pet portraitImageForStyle:_pet.catStyle];
    av.wantsLayer=YES;av.layer.cornerRadius=9;av.layer.backgroundColor=[NSColor colorWithSRGBRed:1. green:.95 blue:.90 alpha:1.].CGColor;
    [cv addSubview:av];
    NSArray *roles=@[@"经典黑猫",@"奶油橘猫",@"日本短尾三花",@"长毛虎斑",@"西装同事",@"发卡同事",@"红结同事",@"发带同事",@"圆镜同事",@"耳机同事",@"贝雷帽同事",@"棒球帽同事",@"组长同事",@"柴犬同事",@"腊肠犬",@"柯基犬",@"黑柴犬",@"比熊犬"];
    NSArray *props=@[@"默认桌搭",@"热咖啡 ☕",@"冰美式 🧊",@"搬砖中 🧱",@"勿扰牌 🌙"];
    NSString *roleName=_pet.catStyle>=0&&_pet.catStyle<(NSInteger)roles.count ? roles[_pet.catStyle] : @"桌宠";
    NSString *propTag=(_pet.deskProp>0&&_pet.deskProp<(NSInteger)props.count)?props[_pet.deskProp]:((_pet.keyboardStyle==4&&_pet.mouseStyle==4)?@"苹果妙控套装":@"定制键鼠");
    NSTextField *t=[NSTextField labelWithString:roleName];
    t.frame=NSMakeRect(56,34,132,18);t.font=[NSFont boldSystemFontOfSize:12.5];[cv addSubview:t];
    NSTextField *s=[NSTextField labelWithString:[NSString stringWithFormat:@"%@ · 敲击 %lld · 暴走 %ld",propTag,_pet.todayKeyCount,(long)_pet.todayBurstCount]];
    s.frame=NSMakeRect(56,11,138,16);s.font=[NSFont monospacedDigitSystemFontOfSize:10.5 weight:NSFontWeightRegular];
    s.textColor=NSColor.secondaryLabelColor;[cv addSubview:s];
    NSButton *cta=[NSButton buttonWithTitle:@"衣橱 ›" target:self action:@selector(openFromMenuHeaderAction:)];
    cta.frame=NSMakeRect(194,16,58,26);cta.bezelStyle=NSBezelStyleRounded;cta.font=[NSFont boldSystemFontOfSize:11];
    [cv addSubview:cta];
    return wrap;
}
- (void)rebuildMenu {
    [_menu removeAllItems];
    _menu.minimumWidth=280;
    [self ensureTodayStats];
    [self syncStatsToDefaults:NO];
    NSMenuItem *hero=[self entry:@"今日状态" action:NULL menu:_menu];
    hero.view=[self menuHeaderView];
    NSMenuItem *wardrobe=[self entry:@"✨ 外观与桌搭面板…" action:@selector(openWardrobeAction:) menu:_menu];
    wardrobe.image=[self sym:@"sparkles"];
    [_menu addItem:NSMenuItem.separatorItem];
    [self styleGroup:@"角色样式" choices:@[@"经典黑猫",@"奶油橘猫",@"日本短尾三花",@"长毛虎斑",@"西装同事",@"发卡同事",@"红结同事",@"发带同事",@"圆镜同事",@"耳机同事",@"贝雷帽同事",@"棒球帽同事",@"组长同事",@"柴犬同事",@"腊肠犬",@"柯基犬",@"黑柴犬",@"比熊犬"] current:_pet.catStyle action:@selector(catAction:) resource:@"portrait-%ld"];
    [self styleGroup:@"键盘样式" choices:@[@"经典奶油",@"薄荷打字机",@"冰蓝电竞",@"胡桃木",@"苹果妙控键盘"] current:_pet.keyboardStyle action:@selector(keyboardAction:) resource:@"keyboard-%ld"];
    [self styleGroup:@"鼠标样式" choices:@[@"奶油滚轮",@"雾紫鹅卵石",@"冰蓝电竞",@"酒红轨迹球",@"苹果妙控鼠标"] current:_pet.mouseStyle action:@selector(mouseAction:) resource:@"mouse-%ld-normal"];
    [self choiceGroup:@"桌面小物件" choices:@[@"无桌面物件",@"热咖啡 ☕",@"冰美式 🧊",@"搬砖中 🧱",@"勿扰牌 🌙"] current:_pet.deskProp action:@selector(deskPropAction:)];
    NSMenuItem *propMenu=[_menu itemWithTitle:@"桌面小物件"];
    propMenu.image=[self sym:@"cup.and.saucer"];
    for(NSInteger pi=0;pi<propMenu.submenu.numberOfItems;pi++)
        [propMenu.submenu itemAtIndex:pi].image=[self propMenuIcon:pi];
    [self choiceGroup:@"动效强度" choices:@[@"柔和不晃眼",@"高能解压（默认）"] current:_pet.fxLevel action:@selector(fxLevelAction:)];
    [_menu itemWithTitle:@"动效强度"].image=[self sym:@"waveform.path.ecg"];
    NSMenuItem *appleItem=[self entry:@"使用苹果键鼠套装" action:@selector(appleSetup:) menu:_menu];
    appleItem.state=(_pet.keyboardStyle==4 && _pet.mouseStyle==4)?NSControlStateValueOn:NSControlStateValueOff;
    appleItem.image=[self sym:@"keyboard"];
    [self addSizeControl];
    [_menu addItem:NSMenuItem.separatorItem];
    NSMenuItem *lbl=[self entry:@"显示浮动键帽" action:@selector(labelsAction:) menu:_menu];
    lbl.state=_pet.showsLabels?NSControlStateValueOn:NSControlStateValueOff;lbl.image=[self sym:@"textformat.abc"];
    NSMenuItem *priv=[self entry:@"键帽隐私打码 (••)" action:@selector(privacyAction:) menu:_menu];
    priv.state=_pet.privacyMode?NSControlStateValueOn:NSControlStateValueOff;priv.image=[self sym:@"eye.slash"];
    NSMenuItem *brk=[self entry:@"久坐喝水休息提醒" action:@selector(breakReminderAction:) menu:_menu];
    brk.state=_pet.breakReminderEnabled?NSControlStateValueOn:NSControlStateValueOff;brk.image=[self sym:@"bell"];
    NSMenuItem *top=[self entry:@"保持置顶" action:@selector(topAction:) menu:_menu];
    top.state=_alwaysOnTop?NSControlStateValueOn:NSControlStateValueOff;top.image=[self sym:@"pin"];
    NSMenuItem *throughItem=[self entry:@"鼠标穿透" action:@selector(throughAction:) menu:_menu];
    throughItem.state=_through?NSControlStateValueOn:NSControlStateValueOff;
    throughItem.image=[self sym:@"cursorarrow.click.2"];
    throughItem.toolTip=@"开启后按住 ⌥ (Option) 键可临时拖动或右键打开菜单";
    NSMenuItem *as=[self entry:@"开机自启动" action:@selector(autoStartAction:) menu:_menu];
    as.state=[KCAutoStart isEnabled]?NSControlStateValueOn:NSControlStateValueOff;as.image=[self sym:@"power.circle"];
    if(!_permission)[self entry:@"开启键盘联动…" action:@selector(permissionAction:) menu:_menu].image=[self sym:@"hand.raised"];
    else [self entry:_window.visible&&!TapIsActive(_keyboardTap)?@"重新授权键盘联动…":@"输入监控设置…"
             action:@selector(permissionAction:) menu:_menu].image=[self sym:@"slider.horizontal.3"];
    [_menu addItem:NSMenuItem.separatorItem];
    [self entry:@"📖 新手使用引导…" action:@selector(openGuideAction:) menu:_menu].image=[self sym:@"book"];
    [self entry:@"复制诊断信息" action:@selector(copyDiagnosticsAction:) menu:_menu].image=[self sym:@"doc.on.clipboard"];
    [self entry:@"检查更新…" action:@selector(checkUpdatesAction:) menu:_menu].image=[self sym:@"arrow.triangle.2.circlepath"];
    [self entry:@"回到屏幕右下角" action:@selector(cornerAction:) menu:_menu].image=[self sym:@"arrow.down.right"];
    NSMenuItem *vis=[self entry:_window.visible?@"暂时隐藏":(_pet.isPerson?@"显示角色":@"显示小猫") action:@selector(toggleVisible:) menu:_menu];
    vis.image=[self sym:_window.visible?@"eye.slash":@"eye"];
    [self entry:_pet.isPerson?@"退出桌宠":@"退出键盘小黑猫" action:@selector(quit:) menu:_menu].image=[self sym:@"power"];
}
- (void)menuNeedsUpdate:(NSMenu *)menu { if(menu==_menu && !_menuOpen)[self rebuildMenu]; }
- (void)menuWillOpen:(NSMenu *)menu {
    if(menu!=_menu || _menuOpen)return;
    _menuOpen=YES;
    [_pet cancelHeadGesture];
    _resizeAnchor=NSMakePoint(NSMaxX(_window.frame),NSMinY(_window.frame));
    // The nonactivating pet may open its menu while another app remains active.
    // A global mouse monitor sees only clicks delivered to other applications.
    _menuOpenedAt=Now();
    __weak KCController *controller=self;
    _outsideMenuMonitor=[NSEvent addGlobalMonitorForEventsMatchingMask:
        NSEventMaskLeftMouseDown|NSEventMaskRightMouseDown|NSEventMaskOtherMouseDown handler:^(NSEvent *event) {
            [controller cancelMenuForExternalPressAt:event.timestamp];
        }];
}
- (void)cancelMenuForExternalPressAt:(NSTimeInterval)time {
    // A press from the preceding tracking session may still be queued after a reopen.
    if(_menuOpen && time>=_menuOpenedAt)[_menu cancelTracking];
}
- (void)menuDidClose:(NSMenu *)menu {
    if(menu!=_menu)return;
    _menuOpen=NO;
    if(_outsideMenuMonitor){[NSEvent removeMonitor:_outsideMenuMonitor];_outsideMenuMonitor=nil;}
}
- (void)applicationDidResignActive:(NSNotification *)note {
    if(_menuOpen)[_menu cancelTracking];
}
- (void)catAction:(id)sender {
    NSInteger tag=[sender tag];
    _pet.catStyle=tag;[NSUserDefaults.standardUserDefaults setInteger:tag forKey:@"cat"];_pet.needsDisplay=YES;
    [self refreshWardrobe];
}
- (void)keyboardAction:(id)sender {
    NSInteger tag=[sender tag];
    _pet.keyboardStyle=tag;[NSUserDefaults.standardUserDefaults setInteger:tag forKey:@"keyboard"];_pet.needsDisplay=YES;
    [self refreshWardrobe];
}
- (void)mouseAction:(id)sender {
    NSInteger tag=[sender tag];
    _pet.mouseStyle=tag;[NSUserDefaults.standardUserDefaults setInteger:tag forKey:@"mouse"];_pet.needsDisplay=YES;
    [self refreshWardrobe];
}
- (void)deskPropAction:(id)sender {
    NSInteger tag=[sender tag];
    _pet.deskProp=tag;[NSUserDefaults.standardUserDefaults setInteger:tag forKey:@"deskProp"];_pet.needsDisplay=YES;
    [self refreshWardrobe];
}
- (void)fxLevelAction:(id)sender {
    NSInteger tag=[sender tag];
    _pet.fxLevel=tag;[NSUserDefaults.standardUserDefaults setInteger:tag forKey:@"fxLevel"];_pet.needsDisplay=YES;
    [self refreshWardrobe];
}
- (void)appleSetup:(id)sender {
    _pet.keyboardStyle=4;_pet.mouseStyle=4;
    NSUserDefaults *defaults=NSUserDefaults.standardUserDefaults;
    [defaults setInteger:4 forKey:@"keyboard"];[defaults setInteger:4 forKey:@"mouse"];
    _pet.needsDisplay=YES;[self refreshWardrobe];
}
- (void)addSizeControl {
    NSMenuItem *item=[self entry:_pet.isPerson?@"角色大小":@"小猫大小" action:NULL menu:_menu];
    NSView *row=[[NSView alloc] initWithFrame:NSMakeRect(0,0,280,82)];
    NSTextField *caption=[NSTextField labelWithString:item.title];
    caption.font=[NSFont menuFontOfSize:0];
    caption.frame=NSMakeRect(16,56,120,20);
    [row addSubview:caption];
    _sizeValue=[NSTextField labelWithString:@""];
    _sizeValue.font=[NSFont monospacedDigitSystemFontOfSize:NSFont.smallSystemFontSize weight:NSFontWeightRegular];
    _sizeValue.textColor=NSColor.secondaryLabelColor;
    _sizeValue.alignment=NSTextAlignmentRight;
    _sizeValue.frame=NSMakeRect(197,56,65,19);
    [row addSubview:_sizeValue];
    _sizeSlider=[NSSlider sliderWithValue:_scale*100 minValue:KC_MIN_SCALE_PERCENT maxValue:KC_MAX_SCALE_PERCENT target:self action:@selector(sizeAction:)];
    _sizeSlider.frame=NSMakeRect(14,31,252,23);
    _sizeSlider.continuous=YES;
    _sizeSlider.accessibilityLabel=item.title;
    [row addSubview:_sizeSlider];
    NSArray *presets=@[@75,@100,@125,@150];
    for(NSUInteger i=0;i<presets.count;i++){
        NSInteger pct=[presets[i] integerValue];
        NSButton *pb=[NSButton buttonWithTitle:[NSString stringWithFormat:@"%ld%%",(long)pct] target:self action:@selector(sizePresetAction:)];
        pb.tag=pct;pb.bezelStyle=NSBezelStyleRounded;pb.font=[NSFont systemFontOfSize:10.5];
        pb.frame=NSMakeRect(14+i*64,5,60,22);
        [row addSubview:pb];
    }
    item.view=row;
    [self updateSizeIndicator];
}
- (void)sizePresetAction:(NSButton *)sender {
    _sizeSlider.doubleValue=(double)sender.tag;
    [self sizeAction:_sizeSlider];
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
- (void)labelsAction:(id)sender { _pet.showsLabels=!_pet.showsLabels;[NSUserDefaults.standardUserDefaults setBool:_pet.showsLabels forKey:@"labels"];[self refreshWardrobe]; }
- (void)privacyAction:(id)sender { _pet.privacyMode=!_pet.privacyMode;[NSUserDefaults.standardUserDefaults setBool:_pet.privacyMode forKey:@"privacyMode"];[self refreshWardrobe]; }
- (void)breakReminderAction:(id)sender { _pet.breakReminderEnabled=!_pet.breakReminderEnabled;[NSUserDefaults.standardUserDefaults setBool:_pet.breakReminderEnabled forKey:@"breakReminder"];[self refreshWardrobe]; }
- (void)topAction:(id)sender { _alwaysOnTop=!_alwaysOnTop;_window.level=_alwaysOnTop?NSFloatingWindowLevel:NSNormalWindowLevel;
    [NSUserDefaults.standardUserDefaults setBool:_alwaysOnTop forKey:@"alwaysOnTop"];[self refreshWardrobe]; }
- (void)throughAction:(id)sender {
    _through=!_through;_window.ignoresMouseEvents=_through;
    [_pet cancelHeadGesture];
    [NSUserDefaults.standardUserDefaults setBool:_through forKey:@"mouseThrough"];[self refreshWardrobe];
}
- (void)copyDiagnosticsAction:(id)sender {
    [self ensureTodayStats];[self syncStatsToDefaults:NO];
    NSString *ver=[NSBundle.mainBundle objectForInfoDictionaryKey:@"CFBundleShortVersionString"] ?: @KC_APP_VERSION;
    NSString *info=[NSString stringWithFormat:
        @"KeycatDesktop macOS v%@ | %@\nRole=%ld Keyboard=%ld Mouse=%ld Prop=%ld Fx=%ld Scale=%.0f%% OnTop=%d Through=%d Privacy=%d Break=%d\nInputMonitoring=%d KeyTap=%d MouseTap=%d TodayKeys=%lld TodayBursts=%ld",
        ver, NSProcessInfo.processInfo.operatingSystemVersionString,
        (long)_pet.catStyle, (long)_pet.keyboardStyle, (long)_pet.mouseStyle,
        (long)_pet.deskProp, (long)_pet.fxLevel, _scale*100.,
        _alwaysOnTop, _through, _pet.privacyMode, _pet.breakReminderEnabled,
        _permission, TapIsActive(_keyboardTap), TapIsActive(_mouseAndModifiersTap),
        _pet.todayKeyCount, (long)_pet.todayBurstCount];
    [NSPasteboard.generalPasteboard clearContents];
    [NSPasteboard.generalPasteboard setString:info forType:NSPasteboardTypeString];
}
- (NSButton *)wardrobePill:(NSString *)title tag:(NSInteger)tag action:(SEL)action frame:(NSRect)frame {
    NSButton *b=[[NSButton alloc] initWithFrame:frame];
    b.title=title;b.tag=tag;b.target=self;b.action=action;
    b.bezelStyle=NSBezelStyleRounded;b.buttonType=NSButtonTypePushOnPushOff;
    b.font=[NSFont systemFontOfSize:11];
    return b;
}
- (void)buildWardrobePanel {
    if(_wardrobePanel)return;
    _wardrobePanel=[[NSPanel alloc] initWithContentRect:NSMakeRect(0,0,560,590)
        styleMask:NSWindowStyleMaskTitled|NSWindowStyleMaskClosable backing:NSBackingStoreBuffered defer:NO];
    _wardrobePanel.title=@"键盘小黑猫 · 外观与桌搭面板";
    _wardrobePanel.releasedWhenClosed=NO;
    _wardrobePanel.floatingPanel=YES;
    NSView *root=_wardrobePanel.contentView;
    _wardrobePreview=[[KCPetView alloc] initWithFrame:NSMakeRect(16,426,170,148)];
    [root addSubview:_wardrobePreview];
    _wardrobeStats=[NSTextField labelWithString:@""];
    _wardrobeStats.frame=NSMakeRect(198,536,340,22);
    _wardrobeStats.font=[NSFont boldSystemFontOfSize:13];
    [root addSubview:_wardrobeStats];
    NSTextField *hint=[NSTextField labelWithString:@"快捷键 ⌃⌥H 一键隐身 · 穿透模式下按住 ⌥ 可临时拖动\n右键点击桌宠或顶部爪印图标可快速切换全部设置"];
    hint.frame=NSMakeRect(198,488,342,40);
    hint.font=[NSFont systemFontOfSize:11];hint.textColor=NSColor.secondaryLabelColor;
    [root addSubview:hint];
    NSButton *diagBtn=[NSButton buttonWithTitle:@"复制诊断信息" target:self action:@selector(copyDiagnosticsAction:)];
    diagBtn.frame=NSMakeRect(198,452,110,28);[root addSubview:diagBtn];
    NSButton *appleBtn=[NSButton buttonWithTitle:@"苹果键鼠套装" target:self action:@selector(appleSetup:)];
    appleBtn.frame=NSMakeRect(316,452,110,28);[root addSubview:appleBtn];
    NSButton *guideBtn=[NSButton buttonWithTitle:@"使用引导" target:self action:@selector(openGuideAction:)];
    guideBtn.frame=NSMakeRect(434,452,106,28);[root addSubview:guideBtn];

    _catBtns=[NSMutableArray array];_kbdBtns=[NSMutableArray array];
    _mouseBtns=[NSMutableArray array];_propBtns=[NSMutableArray array];_fxBtns=[NSMutableArray array];
    NSArray *roles=@[@"经典黑猫",@"奶油橘猫",@"短尾三花",@"长毛虎斑",@"西装同事",@"发卡同事",@"红结同事",@"发带同事",@"圆镜同事",@"耳机同事",@"贝雷帽",@"棒球帽",@"组长同事",@"柴犬同事",@"腊肠犬",@"柯基犬",@"黑柴犬",@"比熊犬"];
    CGFloat y=396;
    NSTextField *rTitle=[NSTextField labelWithString:@"角色图鉴（18 位）"];
    rTitle.frame=NSMakeRect(16,y+4,200,18);rTitle.font=[NSFont boldSystemFontOfSize:12];[root addSubview:rTitle];
    for(NSUInteger i=0;i<roles.count;i++){
        CGFloat x=16+(i%6)*88, ry=y-28-(i/6)*30;
        NSButton *b=[self wardrobePill:roles[i] tag:(NSInteger)i action:@selector(catAction:) frame:NSMakeRect(x,ry,84,26)];
        NSImage *img=[_pet portraitImageForStyle:(NSInteger)i];
        if(img){NSImage *ic=[img copy];ic.size=NSMakeSize(16,16);b.image=ic;b.imagePosition=NSImageLeft;}
        [_catBtns addObject:b];[root addSubview:b];
    }
    NSArray *kbds=@[@"经典奶油",@"薄荷",@"冰蓝电竞",@"胡桃木",@"苹果妙控"];
    y=292;[root addSubview:({NSTextField *l=[NSTextField labelWithString:@"键盘"];l.frame=NSMakeRect(16,y,46,20);l.font=[NSFont boldSystemFontOfSize:11.5];l;})];
    for(NSUInteger i=0;i<kbds.count;i++){
        NSButton *b=[self wardrobePill:kbds[i] tag:(NSInteger)i action:@selector(keyboardAction:) frame:NSMakeRect(66+i*94,y-2,90,24)];
        [_kbdBtns addObject:b];[root addSubview:b];
    }
    NSArray *mice=@[@"奶油滚轮",@"雾紫鹅卵石",@"冰蓝鼠标",@"酒红轨迹",@"苹果鼠标"];
    y=260;[root addSubview:({NSTextField *l=[NSTextField labelWithString:@"鼠标"];l.frame=NSMakeRect(16,y,46,20);l.font=[NSFont boldSystemFontOfSize:11.5];l;})];
    for(NSUInteger i=0;i<mice.count;i++){
        NSButton *b=[self wardrobePill:mice[i] tag:(NSInteger)i action:@selector(mouseAction:) frame:NSMakeRect(66+i*94,y-2,90,24)];
        [_mouseBtns addObject:b];[root addSubview:b];
    }
    NSArray *props=@[@"无物件",@"热咖啡 ☕",@"冰美式 🧊",@"搬砖中 🧱",@"勿扰牌 🌙"];
    y=228;[root addSubview:({NSTextField *l=[NSTextField labelWithString:@"桌搭"];l.frame=NSMakeRect(16,y,46,20);l.font=[NSFont boldSystemFontOfSize:11.5];l;})];
    for(NSUInteger i=0;i<props.count;i++){
        NSButton *b=[self wardrobePill:props[i] tag:(NSInteger)i action:@selector(deskPropAction:) frame:NSMakeRect(66+i*94,y-2,90,24)];
        [_propBtns addObject:b];[root addSubview:b];
    }
    NSArray *fxs=@[@"柔和不晃眼",@"高能解压（默认）"];
    y=192;[root addSubview:({NSTextField *l=[NSTextField labelWithString:@"动效"];l.frame=NSMakeRect(16,y,46,20);l.font=[NSFont boldSystemFontOfSize:11.5];l;})];
    for(NSUInteger i=0;i<fxs.count;i++){
        NSButton *b=[self wardrobePill:fxs[i] tag:(NSInteger)i action:@selector(fxLevelAction:) frame:NSMakeRect(66+i*148,y-2,140,24)];
        [_fxBtns addObject:b];[root addSubview:b];
    }
    y=154;[root addSubview:({NSTextField *l=[NSTextField labelWithString:@"尺寸"];l.frame=NSMakeRect(16,y,46,20);l.font=[NSFont boldSystemFontOfSize:11.5];l;})];
    _wardrobeSizeSlider=[[NSSlider alloc] initWithFrame:NSMakeRect(68,y-1,260,22)];
    _wardrobeSizeSlider.minValue=KC_MIN_SCALE_PERCENT;_wardrobeSizeSlider.maxValue=KC_MAX_SCALE_PERCENT;
    _wardrobeSizeSlider.continuous=YES;_wardrobeSizeSlider.target=self;_wardrobeSizeSlider.action=@selector(sizeAction:);
    [root addSubview:_wardrobeSizeSlider];
    _wardrobeSizeLabel=[NSTextField labelWithString:@"100%"];
    _wardrobeSizeLabel.frame=NSMakeRect(336,y,60,20);[root addSubview:_wardrobeSizeLabel];

    _wLabels=[NSButton checkboxWithTitle:@"显示浮动键帽" target:self action:@selector(labelsAction:)];_wLabels.frame=NSMakeRect(16,110,160,22);[root addSubview:_wLabels];
    _wPrivacy=[NSButton checkboxWithTitle:@"键帽隐私打码 (••)" target:self action:@selector(privacyAction:)];_wPrivacy.frame=NSMakeRect(184,110,170,22);[root addSubview:_wPrivacy];
    _wBreak=[NSButton checkboxWithTitle:@"45分钟喝水休息提醒" target:self action:@selector(breakReminderAction:)];_wBreak.frame=NSMakeRect(360,110,180,22);[root addSubview:_wBreak];
    _wTop=[NSButton checkboxWithTitle:@"保持窗口置顶" target:self action:@selector(topAction:)];_wTop.frame=NSMakeRect(16,78,160,22);[root addSubview:_wTop];
    _wThrough=[NSButton checkboxWithTitle:@"鼠标穿透 (⌥临时可拖)" target:self action:@selector(throughAction:)];_wThrough.frame=NSMakeRect(184,78,176,22);[root addSubview:_wThrough];
    _wAutoStart=[NSButton checkboxWithTitle:@"开机自启动" target:self action:@selector(autoStartAction:)];_wAutoStart.frame=NSMakeRect(360,78,160,22);[root addSubview:_wAutoStart];
}
- (void)refreshWardrobe {
    if(!_wardrobePanel)return;
    _wardrobePreview.catStyle=_pet.catStyle;
    _wardrobePreview.keyboardStyle=_pet.keyboardStyle;
    _wardrobePreview.mouseStyle=_pet.mouseStyle;
    _wardrobePreview.deskProp=_pet.deskProp;
    _wardrobePreview.fxLevel=_pet.fxLevel;
    _wardrobePreview.showsLabels=_pet.showsLabels;
    _wardrobePreview.privacyMode=_pet.privacyMode;
    for(NSButton *b in _catBtns)b.state=b.tag==_pet.catStyle?NSControlStateValueOn:NSControlStateValueOff;
    for(NSButton *b in _kbdBtns)b.state=b.tag==_pet.keyboardStyle?NSControlStateValueOn:NSControlStateValueOff;
    for(NSButton *b in _mouseBtns)b.state=b.tag==_pet.mouseStyle?NSControlStateValueOn:NSControlStateValueOff;
    for(NSButton *b in _propBtns)b.state=b.tag==_pet.deskProp?NSControlStateValueOn:NSControlStateValueOff;
    for(NSButton *b in _fxBtns)b.state=b.tag==_pet.fxLevel?NSControlStateValueOn:NSControlStateValueOff;
    _wLabels.state=_pet.showsLabels?NSControlStateValueOn:NSControlStateValueOff;
    _wPrivacy.state=_pet.privacyMode?NSControlStateValueOn:NSControlStateValueOff;
    _wBreak.state=_pet.breakReminderEnabled?NSControlStateValueOn:NSControlStateValueOff;
    _wTop.state=_alwaysOnTop?NSControlStateValueOn:NSControlStateValueOff;
    _wThrough.state=_through?NSControlStateValueOn:NSControlStateValueOff;
    _wAutoStart.state=[KCAutoStart isEnabled]?NSControlStateValueOn:NSControlStateValueOff;
    _wardrobeSizeSlider.doubleValue=_scale*100.;
    _wardrobeSizeLabel.stringValue=[NSString stringWithFormat:@"%ld%%",lround(_scale*100.)];
    _wardrobeStats.stringValue=[NSString stringWithFormat:@"今日战绩：敲击 %lld 下 · 暴走 %ld 次",
                                _pet.todayKeyCount,(long)_pet.todayBurstCount];
}
- (void)openWardrobeAction:(id)sender {
    [self buildWardrobePanel];
    [self refreshWardrobe];
    [_wardrobePanel center];
    [NSApp activateIgnoringOtherApps:YES];
    [_wardrobePanel makeKeyAndOrderFront:nil];
}
- (void)refreshGuidePermission {
    if(!_guidePanel || !_guidePermNote || !_guidePermBtn)return;
    BOOL ok=_permission;
    _guidePermNote.stringValue=ok
        ? @"✓ 输入监控权限已开启：现在全局敲键、鼠标移动都会同步驱动小宠物！"
        : @"提示：macOS 需要在“系统设置 → 隐私与安全性 → 输入监控”勾选“键盘小黑猫”，才会在其他软件打字时响应。";
    _guidePermNote.textColor=ok ? [NSColor systemGreenColor] : [NSColor systemOrangeColor];
    _guidePermBtn.hidden=ok;
}
- (void)guideStrikeKey:(NSInteger)vk label:(NSString *)label at:(double)t {
    [_guidePreview strike:vk label:label at:t];
    _guideTryKeys++;
    BOOL hot=_guideTryKeys>=5;
    _guideLiveMeter.stringValue=[NSString stringWithFormat:@"已实时互动 %ld 次击键%@",(long)_guideTryKeys,hot?@"（触发连击爆气!）":@""];
    _guideSubNote.stringValue=hot
        ? @"手速起飞！连续快打触发星芒冲击波 + 残影鞭腿 + 桌面轻微震动。"
        : @"慢打保持安静克制；只有加快连续手速才会释放解压爆气特效！";
}
- (void)guideDemoBurst:(id)sender {
    if(_guidePreview.isPerson)_guidePreview.catStyle=0;
    _guideBurstUntil=Now()+0.95;_nextGuideDemoAt=Now();
}
- (void)guideDemoSlow:(id)sender {
    _guideBurstUntil=-10;
    double t=Now();
    [_guidePreview clearInput];[_guidePreview strike:74 label:@"J" at:t];
    _guideLiveMeter.stringValue=@"慢速轻敲 · 仅温和抬爪（无炫光爆气）";
    _guideSubNote.stringValue=@"日常慢速写文档时只做柔和按压，不会干扰视线。";
}
- (void)guideDemoHead:(id)sender {
    _guideBurstUntil=-10;
    double t=Now();
    if(!_guidePreview.isPerson)_guidePreview.catStyle=12;
    [_guidePreview samplePointer:NSMakePoint(214,105) onScreen:NSZeroPoint left:NO right:NO headInteractive:YES at:t];
    _guideLiveMeter.stringValue=@"已触发人类同事头顶互动表情！";
    _guideSubNote.stringValue=@"切换到同事角色后，悬停/轻点头部会出现俏皮回应表情。";
}
- (void)guideDemoBreak:(id)sender {
    _guidePreview.deskProp=KC_PROP_COFFEE;
    _guidePreview.breakReminderEnabled=YES;
    _guideLiveMeter.stringValue=@"桌角热咖啡 & 休息提示概念演示";
    _guideSubNote.stringValue=@"连续专注打字满 45 分钟，右上角温柔弹出“休息喝口水 ☕”，点击即可收起。";
}
- (void)guideStarterRole:(NSButton *)sender {
    [self catAction:sender];
    _guidePreview.catStyle=_pet.catStyle;_guideStep2Preview.catStyle=_pet.catStyle;
    [self refreshGuideState];
}
- (void)guideStarterGear:(NSButton *)sender {
    NSInteger kb=4,ms=4;
    if(sender.tag==1){kb=0;ms=0;}
    else if(sender.tag==2){kb=2;ms=2;}
    _pet.keyboardStyle=kb;_pet.mouseStyle=ms;
    [NSUserDefaults.standardUserDefaults setInteger:kb forKey:@"keyboard"];
    [NSUserDefaults.standardUserDefaults setInteger:ms forKey:@"mouse"];
    _guidePreview.keyboardStyle=kb;_guidePreview.mouseStyle=ms;
    _guideStep2Preview.keyboardStyle=kb;_guideStep2Preview.mouseStyle=ms;
    _pet.needsDisplay=YES;[self refreshWardrobe];[self refreshGuideState];
}
- (void)guideStarterProp:(NSButton *)sender {
    [self deskPropAction:sender];
    _guidePreview.deskProp=_pet.deskProp;_guideStep2Preview.deskProp=_pet.deskProp;
    [self refreshGuideState];
}
- (void)guidePrivacyAction:(id)sender { [self privacyAction:sender]; [self refreshGuideState]; }
- (void)guideBreakAction:(id)sender { [self breakReminderAction:sender]; [self refreshGuideState]; }
- (void)guideFxAction:(NSButton *)sender { [self fxLevelAction:sender]; [self refreshGuideState]; }
- (void)refreshGuideState {
    if(!_guidePanel)return;
    NSArray *starterTags=@[@0,@1,@15,@14,@4,@13];
    for(NSUInteger i=0;i<_gCharBtns.count && i<starterTags.count;i++)
        _gCharBtns[i].state=(_pet.catStyle==[starterTags[i] integerValue])?NSControlStateValueOn:NSControlStateValueOff;
    NSInteger gTag=(_pet.keyboardStyle==4&&_pet.mouseStyle==4)?0:((_pet.keyboardStyle==0&&_pet.mouseStyle==0)?1:((_pet.keyboardStyle==2&&_pet.mouseStyle==2)?2:-1));
    for(NSButton *b in _gGearBtns)b.state=b.tag==gTag?NSControlStateValueOn:NSControlStateValueOff;
    for(NSButton *b in _gPropBtns)b.state=b.tag==_pet.deskProp?NSControlStateValueOn:NSControlStateValueOff;
    _gPrivacyChk.state=_pet.privacyMode?NSControlStateValueOn:NSControlStateValueOff;
    _guideCapDemo.stringValue=_pet.privacyMode?@"••":@"K";
    _gBreakChk.state=_pet.breakReminderEnabled?NSControlStateValueOn:NSControlStateValueOff;
    _gVibBtn.state=_pet.fxLevel==KC_FX_VIBRANT?NSControlStateValueOn:NSControlStateValueOff;
    _gQuietBtn.state=_pet.fxLevel==KC_FX_QUIET?NSControlStateValueOn:NSControlStateValueOff;
}
- (void)setGuideStep:(NSInteger)step {
    _guideStep=MAX(0,MIN(2,step));
    _guideStepper.selectedSegment=_guideStep;
    for(int i=0;i<3;i++)_guideStepViews[i].hidden=(i!=_guideStep);
    _guidePrevBtn.hidden=(_guideStep==0);
    _guideWardrobeBtn.hidden=(_guideStep<1);
    _guideNextBtn.title=_guideStep==0?@"下一步：挑个搭子 ›":(_guideStep==1?@"下一步：职场秘籍 ›":@"开始桌面陪伴 ✨");
    [self refreshGuideState];
    [self refreshGuidePermission];
}
- (void)guideStepAction:(NSSegmentedControl *)sender { [self setGuideStep:sender.selectedSegment]; }
- (void)guidePrevAction:(id)sender { [self setGuideStep:_guideStep-1]; }
- (void)guideNextAction:(id)sender {
    if(_guideStep<2)[self setGuideStep:_guideStep+1];
    else [self guideCloseAction:sender];
}
- (void)buildGuidePanel {
    if(_guidePanel)return;
    _guidePanel=[[NSPanel alloc] initWithContentRect:NSMakeRect(0,0,636,566)
        styleMask:NSWindowStyleMaskTitled|NSWindowStyleMaskClosable backing:NSBackingStoreBuffered defer:NO];
    _guidePanel.title=@"键盘小黑猫 · 首次使用交互指南";
    _guidePanel.releasedWhenClosed=NO;_guidePanel.floatingPanel=YES;
    NSView *root=_guidePanel.contentView;

    NSTextField *eyebrow=[NSTextField labelWithString:[NSString stringWithFormat:@"KEYCAT STUDIO · v%s",KC_APP_VERSION]];
    eyebrow.frame=NSMakeRect(20,532,200,16);eyebrow.font=[NSFont boldSystemFontOfSize:10];eyebrow.textColor=[NSColor systemOrangeColor];
    [root addSubview:eyebrow];
    NSTextField *welcome=[NSTextField labelWithString:@"欢迎领养你的桌面键盘搭子"];
    welcome.frame=NSMakeRect(20,504,260,26);welcome.font=[NSFont boldSystemFontOfSize:17];[root addSubview:welcome];

    _guideStepper=[NSSegmentedControl segmentedControlWithLabels:@[@"01 实时试用",@"02 挑个搭子",@"03 职场秘籍"] trackingMode:NSSegmentSwitchTrackingSelectOne target:self action:@selector(guideStepAction:)];
    _guideStepper.frame=NSMakeRect(306,506,310,28);[root addSubview:_guideStepper];

    NSRect stageBounds=NSMakeRect(20,62,596,426);
    for(int i=0;i<3;i++){_guideStepViews[i]=[[NSView alloc] initWithFrame:stageBounds];[root addSubview:_guideStepViews[i]];}

    // Step 0: Interactive Playground
    NSView *s0=_guideStepViews[0];
    _guideSubNote=[NSTextField labelWithString:@"现在直接在键盘敲几个字，或点击下方按钮试试实时互动！"];
    _guideSubNote.frame=NSMakeRect(4,398,370,22);_guideSubNote.font=[NSFont boldSystemFontOfSize:13];[s0 addSubview:_guideSubNote];
    _guideLiveMeter=[NSTextField labelWithString:@"等待你的第一次敲击…"];
    _guideLiveMeter.frame=NSMakeRect(376,398,216,20);_guideLiveMeter.alignment=NSTextAlignmentRight;
    _guideLiveMeter.font=[NSFont systemFontOfSize:11.5 weight:NSFontWeightSemibold];_guideLiveMeter.textColor=[NSColor systemOrangeColor];
    [s0 addSubview:_guideLiveMeter];

    NSBox *spotBox=[[NSBox alloc] initWithFrame:NSMakeRect(0,56,596,332)];
    spotBox.boxType=NSBoxCustom;spotBox.cornerRadius=12;spotBox.borderWidth=1;
    spotBox.borderColor=[NSColor separatorColor];spotBox.fillColor=[NSColor controlBackgroundColor];spotBox.titlePosition=NSNoTitle;
    _guidePreview=[[KCPetView alloc] initWithFrame:NSMakeRect(138,16,320,298)];
    [spotBox.contentView addSubview:_guidePreview];[s0 addSubview:spotBox];

    NSArray *demoTitles=@[@"⚡ 模拟狂敲连击",@"🐢 慢打安静模式",@"👋 摸人类同事头",@"☕ 预览热咖啡桌搭"];
    SEL demoSels[]={@selector(guideDemoBurst:),@selector(guideDemoSlow:),@selector(guideDemoHead:),@selector(guideDemoBreak:)};
    for(int d=0;d<4;d++){
        NSButton *db=[NSButton buttonWithTitle:demoTitles[d] target:self action:demoSels[d]];
        db.frame=NSMakeRect(d*151,10,143,32);db.bezelStyle=NSBezelStyleRounded;[s0 addSubview:db];
    }

    // Step 1: Pick Starter Character, Gear, Prop
    NSView *s1=_guideStepViews[1];
    NSBox *pBox=[[NSBox alloc] initWithFrame:NSMakeRect(0,44,218,366)];
    pBox.boxType=NSBoxCustom;pBox.cornerRadius=12;pBox.borderWidth=1;pBox.borderColor=[NSColor separatorColor];
    pBox.fillColor=[NSColor controlBackgroundColor];pBox.titlePosition=NSNoTitle;
    _guideStep2Preview=[[KCPetView alloc] initWithFrame:NSMakeRect(8,108,200,194)];
    [pBox.contentView addSubview:_guideStep2Preview];
    NSTextField *pNote=[NSTextField wrappingLabelWithString:@"点击右侧任意搭配\n桌面小宠物会同步立刻切换"];
    pNote.frame=NSMakeRect(12,34,194,44);pNote.alignment=NSTextAlignmentCenter;
    pNote.font=[NSFont systemFontOfSize:11.5];pNote.textColor=NSColor.secondaryLabelColor;
    [pBox.contentView addSubview:pNote];[s1 addSubview:pBox];

    _gCharBtns=[NSMutableArray array];_gGearBtns=[NSMutableArray array];_gPropBtns=[NSMutableArray array];
    NSTextField *cLbl=[NSTextField labelWithString:@"1. 挑一位首发桌面伙伴（衣橱里共有 18 位）"];
    cLbl.frame=NSMakeRect(234,386,340,18);cLbl.font=[NSFont boldSystemFontOfSize:12];[s1 addSubview:cLbl];
    NSArray *sNames=@[@"经典黑猫",@"奶油橘猫",@"柯基犬",@"腊肠犬",@"西装同事",@"柴犬同事"];
    NSInteger sIds[]={0,1,15,14,4,13};
    for(int c=0;c<6;c++){
        NSButton *b=[self wardrobePill:sNames[c] tag:sIds[c] action:@selector(guideStarterRole:) frame:NSMakeRect(234+(c%3)*120,336-(c/3)*42,114,34)];
        NSImage *img=[_pet portraitImageForStyle:sIds[c]];
        if(img){NSImage *ic=[img copy];ic.size=NSMakeSize(20,20);b.image=ic;b.imagePosition=NSImageLeft;}
        [_gCharBtns addObject:b];[s1 addSubview:b];
    }
    NSTextField *gLbl=[NSTextField labelWithString:@"2. 选一套键鼠风格"];
    gLbl.frame=NSMakeRect(234,254,280,18);gLbl.font=[NSFont boldSystemFontOfSize:12];[s1 addSubview:gLbl];
    NSArray *gNames=@[@"苹果妙控极简风",@"奶油复古打字机",@"冰蓝电竞桌搭"];
    for(int g=0;g<3;g++){
        NSButton *b=[self wardrobePill:gNames[g] tag:g action:@selector(guideStarterGear:) frame:NSMakeRect(234+g*120,218,114,28)];
        [_gGearBtns addObject:b];[s1 addSubview:b];
    }
    NSTextField *dLbl=[NSTextField labelWithString:@"3. 放个桌角小摆件"];
    dLbl.frame=NSMakeRect(234,174,280,18);dLbl.font=[NSFont boldSystemFontOfSize:12];[s1 addSubview:dLbl];
    NSArray *pNames=@[@"无",@"热咖啡 ☕",@"冰美式 🧊",@"搬砖中 🧱",@"勿扰牌 🌙"];
    for(int p=0;p<5;p++){
        NSButton *b=[self wardrobePill:pNames[p] tag:p action:@selector(guideStarterProp:) frame:NSMakeRect(234+(p%3)*120,138-(p/3)*34,114,28)];
        [_gPropBtns addObject:b];[s1 addSubview:b];
    }

    // Step 2: Privacy, FX, Hotkeys, Input Monitoring
    NSView *s2=_guideStepViews[2];
    NSBox *prefBox=[[NSBox alloc] initWithFrame:NSMakeRect(0,224,596,192)];
    prefBox.boxType=NSBoxCustom;prefBox.cornerRadius=12;prefBox.borderWidth=1;prefBox.borderColor=[NSColor separatorColor];
    prefBox.fillColor=[NSColor controlBackgroundColor];prefBox.titlePosition=NSNoTitle;[s2 addSubview:prefBox];
    NSView *pv=prefBox.contentView;
    NSTextField *pt=[NSTextField labelWithString:@"按你的工作习惯调整隐私与提示开关"];
    pt.frame=NSMakeRect(14,154,340,20);pt.font=[NSFont boldSystemFontOfSize:13];[pv addSubview:pt];
    _gPrivacyChk=[NSButton checkboxWithTitle:@"键帽隐私打码 (••) — 录屏/投屏不泄露输入内容" target:self action:@selector(guidePrivacyAction:)];
    _gPrivacyChk.frame=NSMakeRect(14,122,380,22);[pv addSubview:_gPrivacyChk];
    NSTextField *fxLbl=[NSTextField labelWithString:@"打字动效："];fxLbl.frame=NSMakeRect(14,88,76,20);[pv addSubview:fxLbl];
    _gVibBtn=[self wardrobePill:@"高能解压" tag:KC_FX_VIBRANT action:@selector(guideFxAction:) frame:NSMakeRect(92,84,110,26)];[pv addSubview:_gVibBtn];
    _gQuietBtn=[self wardrobePill:@"柔和不晃眼" tag:KC_FX_QUIET action:@selector(guideFxAction:) frame:NSMakeRect(208,84,116,26)];[pv addSubview:_gQuietBtn];
    _gBreakChk=[NSButton checkboxWithTitle:@"专注满 45 分钟温柔弹出“休息喝口水 ☕”提醒气泡" target:self action:@selector(guideBreakAction:)];
    _gBreakChk.frame=NSMakeRect(14,48,400,22);[pv addSubview:_gBreakChk];

    NSBox *capBox=[[NSBox alloc] initWithFrame:NSMakeRect(428,44,148,118)];
    capBox.boxType=NSBoxCustom;capBox.cornerRadius=10;capBox.borderWidth=1;capBox.borderColor=[NSColor separatorColor];
    capBox.fillColor=[NSColor windowBackgroundColor];capBox.titlePosition=NSNoTitle;
    NSTextField *capTitle=[NSTextField labelWithString:@"浮动键帽预览"];
    capTitle.frame=NSMakeRect(14,86,120,16);capTitle.alignment=NSTextAlignmentCenter;capTitle.font=[NSFont systemFontOfSize:11];
    capTitle.textColor=NSColor.secondaryLabelColor;[capBox.contentView addSubview:capTitle];
    _guideCapDemo=[NSTextField labelWithString:@"K"];
    _guideCapDemo.frame=NSMakeRect(44,36,60,36);_guideCapDemo.alignment=NSTextAlignmentCenter;
    _guideCapDemo.font=[NSFont boldSystemFontOfSize:22];[capBox.contentView addSubview:_guideCapDemo];
    [pv addSubview:capBox];

    _guidePermNote=[NSTextField wrappingLabelWithString:@""];
    _guidePermNote.frame=NSMakeRect(14,10,432,32);_guidePermNote.font=[NSFont systemFontOfSize:11];[pv addSubview:_guidePermNote];
    _guidePermBtn=[NSButton buttonWithTitle:@"前往开启输入监控" target:self action:@selector(permissionAction:)];
    _guidePermBtn.frame=NSMakeRect(454,10,128,28);[pv addSubview:_guidePermBtn];

    NSBox *cheat=[[NSBox alloc] initWithFrame:NSMakeRect(0,48,596,162)];
    cheat.boxType=NSBoxCustom;cheat.cornerRadius=12;cheat.borderWidth=0;
    cheat.fillColor=[NSColor colorWithSRGBRed:.17 green:.15 blue:.13 alpha:1.];cheat.titlePosition=NSNoTitle;
    NSTextField *ct=[NSTextField labelWithString:@"高频快捷键速查（随时都能用）"];
    ct.frame=NSMakeRect(16,124,300,20);ct.font=[NSFont boldSystemFontOfSize:13];ct.textColor=NSColor.whiteColor;[cheat.contentView addSubview:ct];
    NSTextField *cb=[NSTextField wrappingLabelWithString:@"⌃ ⌥ H    一键隐藏或恢复桌面宠物（适合临时共享屏幕）\n按住 ⌥ (Option)    开启鼠标穿透后，按住 Option 即可临时拖动或右键打开菜单\n右键宠物 / 顶部爪印    快速打开精致菜单栏与 18 位角色外观衣橱"];
    cb.frame=NSMakeRect(16,22,564,92);cb.font=[NSFont systemFontOfSize:12];
    cb.textColor=[NSColor colorWithSRGBRed:.92 green:.89 blue:.85 alpha:1.];[cheat.contentView addSubview:cb];
    [s2 addSubview:cheat];

    // Footer Navigation
    NSTextField *footer=[NSTextField labelWithString:@"可在菜单“📖 新手使用引导…”随时重开"];
    footer.frame=NSMakeRect(20,22,250,18);footer.font=[NSFont systemFontOfSize:11];footer.textColor=NSColor.secondaryLabelColor;[root addSubview:footer];
    _guidePrevBtn=[NSButton buttonWithTitle:@"‹ 上一步" target:self action:@selector(guidePrevAction:)];
    _guidePrevBtn.frame=NSMakeRect(272,18,88,30);[root addSubview:_guidePrevBtn];
    _guideWardrobeBtn=[NSButton buttonWithTitle:@"✨ 打开完整衣橱" target:self action:@selector(guideOpenWardrobeAction:)];
    _guideWardrobeBtn.frame=NSMakeRect(366,18,124,30);[root addSubview:_guideWardrobeBtn];
    _guideNextBtn=[NSButton buttonWithTitle:@"下一步 ›" target:self action:@selector(guideNextAction:)];
    _guideNextBtn.keyEquivalent=@"\r";_guideNextBtn.frame=NSMakeRect(496,18,120,30);[root addSubview:_guideNextBtn];
}
- (void)guideCloseAction:(id)sender {
    [NSUserDefaults.standardUserDefaults setBool:YES forKey:@"guideDone"];
    if(_guideKeyMonitor){[NSEvent removeMonitor:_guideKeyMonitor];_guideKeyMonitor=nil;}
    [_guidePanel close];
}
- (void)guideOpenWardrobeAction:(id)sender {
    [self guideCloseAction:sender];
    [self openWardrobeAction:sender];
}
- (void)openGuideAction:(id)sender {
    [self buildGuidePanel];
    for(KCPetView *pv in @[_guidePreview,_guideStep2Preview]){
        pv.catStyle=_pet.catStyle;pv.keyboardStyle=_pet.keyboardStyle;pv.mouseStyle=_pet.mouseStyle;
        pv.deskProp=_pet.deskProp==KC_PROP_NONE?KC_PROP_COFFEE:_pet.deskProp;
        pv.fxLevel=_pet.fxLevel;pv.showsLabels=_pet.showsLabels;pv.privacyMode=_pet.privacyMode;
    }
    _guideTryKeys=0;_guideBurstUntil=-10;_nextGuideDemoAt=Now()+0.3;
    [self setGuideStep:0];
    if(!_guideKeyMonitor){
        __weak KCController *weakSelf=self;
        _guideKeyMonitor=[NSEvent addLocalMonitorForEventsMatchingMask:NSEventMaskKeyDown handler:^NSEvent *(NSEvent *e){
            KCController *s=weakSelf;
            if(!s || !s->_guidePanel.visible || s->_guideStep!=0)return e;
            NSInteger vk=KCArtworkKey(e.keyCode);
            if(vk)[s guideStrikeKey:vk label:KCKeyLabel(vk) ?: @"A" at:Now()];
            return e;
        }];
    }
    [_guidePanel center];[NSApp activateIgnoringOtherApps:YES];[_guidePanel makeKeyAndOrderFront:nil];
}
- (void)autoStartAction:(id)sender {
    BOOL next=![KCAutoStart isEnabled];
    NSError *error=nil;
    if(![KCAutoStart setEnabled:next error:&error]) {
        NSAlert *alert=[NSAlert new];
        alert.messageText=@"无法更改开机自启动设置";
        alert.informativeText=error.localizedDescription ?: @"请检查权限后重试。";
        [alert runModal];
    }
}
- (void)checkUpdatesAction:(id)sender { [self checkUpdatesSilent:NO]; }
- (void)checkUpdatesSilent:(BOOL)silent {
    if(_checkingUpdates) {
        if(!silent) {
            [NSApp activateIgnoringOtherApps:YES];
            NSAlert *busy=[NSAlert new];
            busy.messageText=@"正在检查更新，请稍候";
            [busy runModal];
        }
        return;
    }
    _checkingUpdates=YES;
    [KCUpdater fetchLatestReleaseWithCompletion:^(KCGitHubRelease *release, NSError *error) {
        if(error) {
            self->_checkingUpdates=NO;
            if(!silent) {
                [NSApp activateIgnoringOtherApps:YES];
                NSAlert *alert=[NSAlert new];
                alert.messageText=@"检查更新失败";
                alert.informativeText=[NSString stringWithFormat:@"%@\n\n是否前往 GitHub 发布页面手动下载？", error.localizedDescription ?: @"网络请求失败"];
                [alert addButtonWithTitle:@"前往发布页"];
                [alert addButtonWithTitle:@"取消"];
                if([alert runModal]==NSAlertFirstButtonReturn)
                    [[NSWorkspace sharedWorkspace] openURL:[NSURL URLWithString:[KCUpdater releasesPageURL]]];
            }
            return;
        }
        [NSUserDefaults.standardUserDefaults setDouble:NSDate.date.timeIntervalSince1970 forKey:@"lastUpdateCheck"];
        NSString *current=[NSBundle.mainBundle objectForInfoDictionaryKey:@"CFBundleShortVersionString"] ?: @KC_APP_VERSION;
        if(!release || release.draft || release.prerelease || ![KCUpdater isNewerVersion:release.tagName current:current]) {
            self->_checkingUpdates=NO;
            if(!silent) {
                [NSApp activateIgnoringOtherApps:YES];
                NSAlert *upToDate=[NSAlert new];
                upToDate.messageText=[NSString stringWithFormat:@"当前已是最新版本（v%@）", current];
                [upToDate runModal];
            }
            return;
        }
        [self promptUpdateForRelease:release currentVersion:current];
    }];
}
- (void)promptUpdateForRelease:(KCGitHubRelease *)release currentVersion:(NSString *)current {
    KCGitHubAsset *asset=[KCUpdater selectMacAsset:release];
    KCGitHubAsset *checksum=[KCUpdater selectChecksumAsset:release];
    NSString *body=[release.body ?: @"" stringByTrimmingCharactersInSet:NSCharacterSet.whitespaceAndNewlineCharacterSet];
    if(body.length>300) body=[[body substringToIndex:300] stringByAppendingString:@"..."];
    [NSApp activateIgnoringOtherApps:YES];
    NSAlert *alert=[NSAlert new];
    alert.messageText=[NSString stringWithFormat:@"发现新版本 %@（当前 v%@）", release.tagName, current];
    alert.informativeText=body.length ? [NSString stringWithFormat:@"更新说明：\n%@", body] : @"建议更新到最新版本以获得新功能与稳定性修复。";
    [alert addButtonWithTitle:asset ? @"下载并打开安装包" : @"打开发布页"];
    [alert addButtonWithTitle:@"稍后"];
    if([alert runModal]!=NSAlertFirstButtonReturn) { _checkingUpdates=NO; return; }
    if(!asset) {
        _checkingUpdates=NO;
        [[NSWorkspace sharedWorkspace] openURL:[NSURL URLWithString:release.htmlURL ?: [KCUpdater releasesPageURL]]];
        return;
    }
    [KCUpdater downloadAsset:asset checksumAsset:checksum completion:^(NSURL *fileURL, NSError *dlError) {
        self->_checkingUpdates=NO;
        if(dlError || !fileURL) {
            [NSApp activateIgnoringOtherApps:YES];
            NSAlert *fail=[NSAlert new];
            fail.messageText=@"下载更新失败";
            fail.informativeText=dlError.localizedDescription ?: @"请前往 GitHub 发布页手动下载。";
            [fail addButtonWithTitle:@"前往发布页"];
            [fail addButtonWithTitle:@"关闭"];
            if([fail runModal]==NSAlertFirstButtonReturn)
                [[NSWorkspace sharedWorkspace] openURL:[NSURL URLWithString:release.htmlURL ?: [KCUpdater releasesPageURL]]];
            return;
        }
        [[NSWorkspace sharedWorkspace] openURL:fileURL];
    }];
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
- (void)applicationWillTerminate:(NSNotification *)n {
    [self menuDidClose:_menu];
    if(_guideKeyMonitor){[NSEvent removeMonitor:_guideKeyMonitor];_guideKeyMonitor=nil;}
    [_timer invalidate];[self saveOrigin];[self syncStatsToDefaults:YES];[self disconnectTap];
}
@end

int main(int argc,const char *argv[]) {
    @autoreleasepool {
        NSApplication *app=NSApplication.sharedApplication;[app setActivationPolicy:NSApplicationActivationPolicyAccessory];
        __attribute__((objc_precise_lifetime)) KCController *controller=[KCController new];
        app.delegate=controller;[app run];
    }
    return 0;
}

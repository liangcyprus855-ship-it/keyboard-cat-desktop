// Exercise the actual menu controller with input monitoring disabled for the test session.
#define main KCApplicationMain
#import "../app/main.m"
#undef main
#import <IOKit/hidsystem/IOLLEvent.h>
#import <IOKit/hidsystem/ev_keymap.h>
#include <stdio.h>
#include <stdlib.h>

@interface ScaleTestController : KCController
@end
@implementation ScaleTestController
- (void)refreshTap {}
- (void)tick:(NSTimer *)timer {}
@end

static void Check(BOOL value, NSString *message) {
    if (!value) {
        fprintf(stderr,"FAIL %s\n", message.UTF8String);
        exit(1);
    }
}
static void Near(double actual, double expected, double tolerance, NSString *field) {
    Check(fabs(actual-expected)<=tolerance,
          [NSString stringWithFormat:@"%@ expected %.6f, got %.6f",field,expected,actual]);
}
static void CheckWindow(NSWindow *window, double percent) {
    Near(window.frame.size.width,306*percent/100.,1,@"window width");
    Near(window.frame.size.height,298*percent/100.,1,@"window height");
}
static NSSlider *SizeSlider(NSMenu *menu) {
    NSMenuItem *item=[menu itemWithTitle:@"小猫大小"] ?: [menu itemWithTitle:@"角色大小"];
    for (NSView *child in item.view.subviews)
        if ([child isKindOfClass:NSSlider.class]) return (NSSlider *)child;
    return nil;
}
static void CheckIndicator(NSSlider *slider, NSString *label) {
    Check([slider.accessibilityLabel isEqual:@"小猫大小"],@"slider accessibility label");
    Check([slider.accessibilityValueDescription isEqual:label],@"accessibility percentage");
    for (NSView *child in slider.superview.subviews)
        if ([child isKindOfClass:NSTextField.class] && [[(NSTextField *)child stringValue] isEqual:label]) return;
    Check(NO,@"visible percentage");
}
static NSData *FrameRect(KCPetView *view,NSRect region) {
    NSBitmapImageRep *rep=[view bitmapImageRepForCachingDisplayInRect:region];
    Check(rep && rep.bitmapData && !rep.isPlanar,@"allocated rendered frame");
    [view cacheDisplayInRect:region toBitmapImageRep:rep];
    NSUInteger rowBytes=(rep.pixelsWide*rep.bitsPerPixel+7)/8;
    NSMutableData *data=[NSMutableData dataWithCapacity:rowBytes*rep.pixelsHigh];
    for(NSInteger y=0;y<rep.pixelsHigh;y++)
        [data appendBytes:rep.bitmapData+y*rep.bytesPerRow length:rowBytes];
    return data;
}
static NSData *Frame(KCPetView *view) { return FrameRect(view,view.bounds); }
static NSData *HumanFrame(KCPetView *view) {
    NSRect region=view.bounds;
    region.size.height*=202./298.; // Includes the face and sleeves, above the independently moving desk.
    return FrameRect(view,region);
}
// Test only the pet's local event handlers. Never move the actual pointer or window.
@interface PoseTestPanel : NSPanel
@property(nonatomic) NSUInteger dragCount;
@end
@implementation PoseTestPanel
- (void)performWindowDragWithEvent:(NSEvent *)event { self.dragCount++; }
@end
static NSEvent *LocalMouse(KCPetView *view,NSEventType type,NSPoint scene,double time) {
    NSPoint point=NSMakePoint((scene.x-75)*view.bounds.size.width/306,(scene.y-9)*view.bounds.size.height/298);
    return [NSEvent mouseEventWithType:type location:[view convertPoint:point toView:nil] modifierFlags:0 timestamp:time
        windowNumber:view.window.windowNumber context:nil eventNumber:1 clickCount:1 pressure:1];
}
static void Pointer(KCPetView *view,NSPoint scene,BOOL interactive,double time) {
    [view samplePointer:scene onScreen:NSZeroPoint left:NO right:NO headInteractive:interactive at:time];
    [view advance:time];
}
static void CheckOfficePoses(void) {
    NSRect bounds=NSMakeRect(0,0,306,298);
    PoseTestPanel *panel=[[PoseTestPanel alloc] initWithContentRect:bounds styleMask:NSWindowStyleMaskBorderless
        backing:NSBackingStoreBuffered defer:NO];
    KCPetView *view=[[KCPetView alloc] initWithFrame:bounds];
    panel.contentView=view;view.catStyle=4;
    NSPoint head=NSMakePoint(214,105), ear=NSMakePoint(161,124),away=NSMakePoint(302,280);
    NSPoint clear=NSMakePoint(207,47),lapel=NSMakePoint(214,196);
    Check([view officeHeadAt:head] && [view officeHeadAt:ear] && [view officeHeadAt:NSMakePoint(267,124)],@"face and both ears are interactive");
    Check(![view officeHeadAt:clear] && ![view officeHeadAt:lapel],@"clear hair corner and suit are excluded");
    double t=NSProcessInfo.processInfo.systemUptime;
    Pointer(view,away,NO,t);
    Check(view.officeState==KCOfficeIdle,@"stationary upright idle");
    [view strike:65 label:@"A" at:t+.05];[view advance:t+.1];
    Check(view.officeState==KCOfficeWorking,@"keyboard activates working arms");
    [view advance:t+.84];Check(view.officeState==KCOfficeWorking,@"input remains active before .8 s");
    [view advance:t+.86];Check(view.officeState==KCOfficeIdle,@"input expires after .8 s");
    Pointer(view,ear,YES,t+1);
    Check(view.officeState==KCOfficeHover,@"ear hover overrides movement");
    for(int i=0;i<10;i++)Pointer(view,ear,YES,t+1.1+i*.1);
    Check(view.officeState==KCOfficeHover,@"stable hover at the ear");
    [view button:1 down:YES at:t+2.1];[view advance:t+2.1];
    Check(view.officeState==KCOfficeHover,@"a global right click cannot start a head reaction");
    [view button:1 down:NO at:t+2.12];
    [view mouseDown:LocalMouse(view,NSEventTypeLeftMouseDown,ear,t+2.15)];
    [view mouseUp:LocalMouse(view,NSEventTypeLeftMouseUp,ear,t+2.16)];
    Pointer(view,ear,YES,t+3.65);Check(view.officeState==KCOfficeClick,@"local head click holds for 1.5 seconds");
    Pointer(view,ear,YES,t+3.67);Check(view.officeState==KCOfficeHover,@"expired click returns to hover");
    [view mouseDown:LocalMouse(view,NSEventTypeLeftMouseDown,head,t+4)];
    [view mouseUp:LocalMouse(view,NSEventTypeLeftMouseUp,head,t+4.01)];
    Pointer(view,away,NO,t+4.2);
    Check(view.officeState==KCOfficeClick,@"click survives leaving the head");
    [view strike:65 label:@"A" at:t+5.4];[view advance:t+5.52];
    Check(view.officeState==KCOfficeWorking,@"expired click resumes ongoing typing");
    [view advance:t+6.3];Check(view.officeState==KCOfficeIdle,@"returns to idle away from head");
    [view mouseDown:LocalMouse(view,NSEventTypeLeftMouseDown,head,t+7)];
    [view mouseDragged:LocalMouse(view,NSEventTypeLeftMouseDragged,ear,t+7.02)];
    [view mouseUp:LocalMouse(view,NSEventTypeLeftMouseUp,head,t+7.05)];
    Check(panel.dragCount==1 && view.officeState!=KCOfficeClick,@"drag cannot become a head click");
    [view mouseDown:LocalMouse(view,NSEventTypeLeftMouseDown,head,t+7.1)];[view cancelHeadGesture];
    [view mouseUp:LocalMouse(view,NSEventTypeLeftMouseUp,head,t+7.15)];
    Check(view.officeState!=KCOfficeClick,@"cancelled mouse capture");
    [view mouseDown:LocalMouse(view,NSEventTypeLeftMouseDown,clear,t+7.2)];
    [view mouseUp:LocalMouse(view,NSEventTypeLeftMouseUp,clear,t+7.22)];
    Check(view.officeState!=KCOfficeClick,@"transparent pixel cannot trigger the reaction");
    panel.ignoresMouseEvents=YES;
    [view mouseDown:LocalMouse(view,NSEventTypeLeftMouseDown,head,t+7.3)];
    [view mouseUp:LocalMouse(view,NSEventTypeLeftMouseUp,head,t+7.31)];
    Pointer(view,head,NO,t+7.32);
    Check(view.officeState!=KCOfficeClick && view.officeState!=KCOfficeHover,@"click-through suppresses head reactions");
}
static void CheckOffice(KCPetView *view, NSMenu *menu) {
    NSMenu *roles=[menu itemWithTitle:@"角色样式"].submenu;
    Check(roles.numberOfItems==9,@"nine role choices");
    NSMenuItem *office=[roles itemWithTitle:@"西装同事"];
    Check(office.tag==4 && office.image && office.target,@"office portrait and action");
    NSData *previous=Frame(view);
    [roles performActionForItemAtIndex:[roles indexOfItem:office]];
    Check(view.catStyle==4 && [NSUserDefaults.standardUserDefaults integerForKey:@"cat"]==4,@"character selection");
    Check(![Frame(view) isEqual:previous],@"office character rendered");
    NSInteger keyboard=view.keyboardStyle, mouse=view.mouseStyle;
    [view clearInput];
    Near([[view valueForKey:@"left"] pointValue].x,[[view valueForKey:@"mousePaw"] pointValue].x,.001,@"resting mouse hand");
    Near([[view valueForKey:@"right"] pointValue].y,[[view valueForKey:@"spaceKey"] pointValue].y,.001,@"resting keyboard hand");
    double t=NSProcessInfo.processInfo.systemUptime;
    [view samplePointer:NSMakePoint(120,220) onScreen:NSMakePoint(-.6,.2) left:YES right:NO headInteractive:NO at:t];
    for (int i=1;i<=12;i++) [view advance:t+i*.01];
    NSPoint mouseHand=[[view valueForKey:@"left"] pointValue];
    NSPoint freeHand=[[view valueForKey:@"right"] pointValue];
    Check(mouseHand.x<135,@"anatomical right hand reaches the mouse");
    Check([[view valueForKey:@"leftDown"] boolValue] && ![[view valueForKey:@"rightDown"] boolValue],@"left mouse button");
    NSData *mouseFrame=Frame(view);
    [view button:0 down:NO at:t+.125];
    [view button:1 down:YES at:t+.125];
    [view strike:65 label:@"A" at:t+.13];
    for (int i=14;i<=19;i++) [view advance:t+i*.01];
    NSPoint keyHand=[[view valueForKey:@"right"] pointValue];
    NSPoint contact=[[view valueForKey:@"rightKey"] pointValue];
    Check(hypot(keyHand.x-contact.x,keyHand.y-contact.y)<hypot(freeHand.x-contact.x,freeHand.y-contact.y),@"free hand strikes the key");
    Check([[view valueForKey:@"left"] pointValue].x<135 && [[view valueForKey:@"rightDown"] boolValue] &&
          ![[view valueForKey:@"leftDown"] boolValue],@"simultaneous right mouse and keyboard input");
    Check(![Frame(view) isEqual:mouseFrame],@"simultaneous input changes the rendered frame");
    Check(view.keyboardStyle==keyboard && view.mouseStyle==mouse,@"peripheral preferences preserved");
}
static void CheckPeople(KCPetView *view,NSMenu *menu) {
    NSMenu *roles=[menu itemWithTitle:@"角色样式"].submenu;
    NSArray *names=@[@"西装同事",@"发卡同事",@"红结同事",@"发带同事",@"圆镜同事"];
    NSPoint face=NSMakePoint(214,105);
    for(NSInteger i=0;i<(NSInteger)names.count;i++) {
        NSMenuItem *item=[roles itemWithTitle:names[i]];
        Check(item && item.image && item.tag==4+i,@"human portrait present in menu");
        [roles performActionForItemAtIndex:item.tag];
        [view clearInput];
        Check(view.catStyle==item.tag && view.isPerson && view.hasArt,@"human loads all poses");
        Check([view officeHeadAt:face] && ![view officeHeadAt:NSMakePoint(214,232)],@"head and torso hit discrimination");
        double t=NSProcessInfo.processInfo.systemUptime;
        NSData *idle=HumanFrame(view);
        Pointer(view,face,YES,t);
        NSData *hover=HumanFrame(view);
        Check(view.officeState==KCOfficeHover && ![hover isEqual:idle],@"hover is visible on its first frame");
        [view advance:t+.20];
        Check([hover isEqual:HumanFrame(view)],@"first hover frame equals the held final pose");

        [view mouseDown:LocalMouse(view,NSEventTypeLeftMouseDown,face,t+.21)];
        [view mouseUp:LocalMouse(view,NSEventTypeLeftMouseUp,face,t+.22)];
        NSData *click=HumanFrame(view);
        Check(view.officeState==KCOfficeClick && ![click isEqual:hover],@"click replaces hover immediately");
        [view advance:t+1.71];
        Check([click isEqual:HumanFrame(view)],@"click pose stays unchanged throughout its hold");
        [view advance:t+1.73];
        Check(view.officeState==KCOfficeHover && [hover isEqual:HumanFrame(view)],@"click expiry restores hover in one frame");

        Pointer(view,NSMakePoint(305,260),NO,t+1.8);
        Check(view.officeState==KCOfficeWorking && ![idle isEqual:HumanFrame(view)],@"working arms are visible on the first frame");
        [view advance:t+2.59];
        Check(view.officeState==KCOfficeWorking,@"working pose stays active for .8 seconds");
        [view advance:t+2.61];
        Check(view.officeState==KCOfficeIdle && [idle isEqual:HumanFrame(view)],@"idle has no arms on its first frame");
    }
}
static void Choose(NSMenu *menu,NSString *title) {
    NSInteger index=[menu indexOfItemWithTitle:title];
    Check(menu && index>=0, [@"menu action available: " stringByAppendingString:title]);
    [menu performActionForItemAtIndex:index];
}
static void CheckApple(KCController *controller, KCPetView *view, NSMenu *menu, BOOL restored) {
    NSMenu *keyboards=[menu itemWithTitle:@"键盘样式"].submenu, *mice=[menu itemWithTitle:@"鼠标样式"].submenu;
    NSMenuItem *keyboard=[keyboards itemWithTitle:@"苹果妙控键盘"], *mouse=[mice itemWithTitle:@"苹果妙控鼠标"];
    Check(keyboards.numberOfItems==5 && mice.numberOfItems==5 && keyboard.tag==4 && mouse.tag==4 &&
        keyboard.image && mouse.image,@"five distinct keyboards and mice with Apple thumbnails");
    NSString *kit=@"使用苹果键鼠套装";
    NSUserDefaults *prefs=NSUserDefaults.standardUserDefaults;
    if(restored) {
        Check(view.keyboardStyle==4 && view.mouseStyle==4 && keyboard.state==NSControlStateValueOn &&
            mouse.state==NSControlStateValueOn && [menu itemWithTitle:kit].state==NSControlStateValueOn,@"restored Apple gear and checkmarks");
        [keyboards performActionForItemAtIndex:2];
        [controller menuNeedsUpdate:menu];
        Check(view.keyboardStyle==2 && view.mouseStyle==4 && [menu itemWithTitle:kit].state==NSControlStateValueOff,
            @"one change after restart leaves the other device selected");
        return;
    }
    NSInteger originalCat=view.catStyle;
    Check(view.keyboardStyle==1 && view.mouseStyle==3,@"legacy gear preferences retained");
    Choose(mice,mouse.title);
    Check(view.keyboardStyle==1 && view.mouseStyle==4 && [prefs integerForKey:@"mouse"]==4,@"independent Apple mouse");
    [mice performActionForItemAtIndex:3];
    Choose(keyboards,keyboard.title);
    Check(view.keyboardStyle==4 && view.mouseStyle==3 && [prefs integerForKey:@"keyboard"]==4,@"independent Apple keyboard");
    [keyboards performActionForItemAtIndex:1];
    Choose(menu,kit);
    Check(view.keyboardStyle==4 && view.mouseStyle==4 && view.catStyle==originalCat &&
        [prefs integerForKey:@"keyboard"]==4 && [prefs integerForKey:@"mouse"]==4,@"one click selects and saves both without changing role");
    NSURL *atlasURL=[NSBundle.mainBundle URLForResource:@"atlas" withExtension:@"json" subdirectory:@"Art"];
    NSDictionary *atlas=[NSJSONSerialization JSONObjectWithData:[NSData dataWithContentsOfURL:atlasURL] options:0 error:NULL];
    NSDictionary *apple=atlas[@"keysByStyle"][@"4"];
    Check(apple.count==262,@"Apple layout includes native function row and extended keys");
    double t=NSProcessInfo.processInfo.systemUptime;
    [view clearInput];
    [view button:0 down:YES at:t];[view button:1 down:YES at:t];
    const unsigned short hardware[]={122,120,99,118,96,97,98,100,101,109,103,111,53,51,50,49,0,123,124,125,126,54,55,58,61,59,63};
    const NSInteger keys[]={112,113,114,115,116,117,118,119,120,121,122,123,27,8,192,32,65,37,39,40,38,92,91,164,165,162,256};
    for(NSUInteger i=0;i<sizeof(hardware)/sizeof(hardware[0]);i++) {
        NSInteger key=KCArtworkKey(hardware[i]);Check(key==keys[i],@"physical key translates to the correct keycap");
        [view strike:key label:KCKeyLabel(key) at:t];
        NSArray *xy=apple[@(key).stringValue];
        Check(xy.count==2 && atlas[@"sprites"][[NSString stringWithFormat:@"key-4-%ld",(long)key]],@"matching key highlight exists");
        NSPoint paw=[[view valueForKey:@"rightKey"] pointValue];
        Near(paw.x,[xy[0] doubleValue],.000001,@"Apple key horizontal target");
        Near(paw.y,[xy[1] doubleValue],.000001,@"Apple key vertical target");
    }
    Check([[view valueForKey:@"leftDown"] boolValue] && [[view valueForKey:@"rightDown"] boolValue],@"typing preserves both mouse buttons");
    Check(KCArtworkKey(93)==257 && KCSystemArtworkKey(NX_SUBTYPE_POWER_KEY,0)==261 &&
        atlas[@"sprites"][@"key-4-261"],@"JIS yen and top-right lock remain distinct");
    Check(KCSystemArtworkKey(NX_SUBTYPE_AUX_CONTROL_BUTTONS,(NX_KEYTYPE_SOUND_UP<<16)|(NX_KEYDOWN<<8))==123 &&
        KCSystemArtworkKey(NX_SUBTYPE_AUX_CONTROL_BUTTONS,(NX_KEYTYPE_SOUND_UP<<16)|(NX_KEYUP<<8))==0,
        @"only the system volume press actuates the corresponding F12 position");
    [view strike:112 label:@"F1" at:t];
    [keyboards performActionForItemAtIndex:1];
    NSPoint old=[[view valueForKey:@"rightKey"] pointValue];NSArray *legacy=atlas[@"keys"][@"112"];
    Near(old.x,[legacy[0] doubleValue],.000001,@"in-flight strike restores the old keyboard geometry");
    Check(![legacy isEqual:apple[@"112"]],@"function-key targets differ across layouts");
    Choose(menu,kit);
    [controller menuNeedsUpdate:menu];
    Check([menu itemWithTitle:kit].state==NSControlStateValueOn,@"paired menu checkmark");
}
int main(int argc,const char *argv[]) {
    @autoreleasepool {
        Check(argc==2,@"test scenario required");
        NSString *scenario=@(argv[1]);
        [NSApplication.sharedApplication setActivationPolicy:NSApplicationActivationPolicyAccessory];
        Check(NSScreen.screens.count>0,@"A WindowServer session is required");
        NSUserDefaults *defaults=NSUserDefaults.standardUserDefaults;
        [defaults setBool:YES forKey:@"explainedInput"];
        NSDictionary *fixtures=@{@"fresh":NSNull.null, @"legacy":@1.65, @"low":@.001, @"high":@7,
                                 @"invalid":@"wrong type", @"nan":@(NAN), @"office":@1, @"people":@1, @"apple":@1};
        NSDictionary *expected=@{@"fresh":@100, @"legacy":@165, @"low":@1, @"high":@200,
                                 @"invalid":@100, @"nan":@100, @"restored":@72.5, @"office":@100, @"office-restored":@100,
                                 @"people":@100, @"people-restored":@100, @"apple":@100, @"apple-restored":@100};
        Check(expected[scenario]!=nil,@"unknown test scenario");
        if (fixtures[scenario]) [defaults removeObjectForKey:@"origin"];
        if (fixtures[scenario]==NSNull.null) [defaults removeObjectForKey:@"size"];
        else if (fixtures[scenario]) [defaults setObject:fixtures[scenario] forKey:@"size"];

        ScaleTestController *controller=[ScaleTestController new];
        [controller applicationDidFinishLaunching:
            [NSNotification notificationWithName:NSApplicationDidFinishLaunchingNotification object:NSApp]];
        NSWindow *window=[controller valueForKey:@"window"];
        NSStatusItem *status=[controller valueForKey:@"status"];
        NSMenu *menu=status.menu;
        Check(status.button && menu && window.contentView.menu==menu,@"shared pet and menu-bar controls");
        NSSlider *slider=SizeSlider(menu);
        Check(slider && slider.target==controller && slider.action==@selector(sizeAction:),@"connected size slider");
        Check(slider.continuous && slider.minValue==1 && slider.maxValue==200,@"continuous slider range");
        const double startup=[expected[scenario] doubleValue];
        Near(slider.doubleValue,startup,.000001,@"launch scale");
        CheckWindow(window,startup);
        if ([scenario isEqual:@"apple"] || [scenario isEqual:@"apple-restored"])
            CheckApple(controller,(KCPetView *)window.contentView,menu,[scenario isEqual:@"apple-restored"]);
        if ([scenario isEqual:@"office"]) {
            CheckOffice((KCPetView *)window.contentView,menu);
            CheckOfficePoses();
        }
        if ([scenario isEqual:@"office-restored"]) {
            Check(((KCPetView *)window.contentView).catStyle==4,@"office character restored after restart");
            Check([menu itemWithTitle:@"角色大小"]!=nil,@"office size menu label");
            Check([[menu itemWithTitle:@"角色样式"].submenu itemWithTitle:@"西装同事"].state==NSControlStateValueOn,
                  @"restored character menu checkmark");
        }
        if ([scenario isEqual:@"people"]) CheckPeople((KCPetView *)window.contentView,menu);
        if ([scenario isEqual:@"people-restored"]) {
            Check(((KCPetView *)window.contentView).catStyle==8,@"last new human restored");
            Check([[menu itemWithTitle:@"角色样式"].submenu itemWithTitle:@"圆镜同事"].state==NSControlStateValueOn,
                  @"restored human has the menu checkmark");
        }

        if ([scenario isEqual:@"legacy"]) {
            NSRect initial=window.frame;
            [controller menuWillOpen:menu];
            NSArray *percents=@[@1,@37,@200,@72.5678,@72.5];
            NSArray *labels=@[@"1%",@"37%",@"200%",@"72.6%",@"72.5%"];
            for (NSUInteger n=0;n<percents.count;n++) {
                double percent=[percents[n] doubleValue];
                slider.doubleValue=percent;
                Check([slider sendAction:slider.action to:slider.target],@"native control action");
                [controller menuNeedsUpdate:menu];
                Check(SizeSlider(menu)==slider,@"tracking retains the same slider");
                Check(status.menu==menu && status.button,@"menu-bar recovery at every size");
                Near(slider.doubleValue,percent,.000001,@"thumb value");
                Near([defaults doubleForKey:@"size"],percent/100.,.000001,@"stored ratio");
                CheckIndicator(slider,labels[n]);
                CheckWindow(window,percent);
                Near(NSMaxX(window.frame),NSMaxX(initial),1,@"right anchor");
                Near(NSMinY(window.frame),NSMinY(initial),1,@"bottom anchor");
            }
            [controller menuDidClose:menu];
            [controller menuNeedsUpdate:menu];
            CheckIndicator(SizeSlider(menu),@"72.5%");
        } else if ([scenario isEqual:@"restored"]) {
            NSString *origin=[defaults stringForKey:@"origin"];
            Check(origin!=nil,@"persisted origin");
            NSPoint point=NSPointFromString(origin);
            Near(window.frame.origin.x,point.x,1,@"restored horizontal position");
            Near(window.frame.origin.y,point.y,1,@"restored vertical position");
        }
        [controller applicationWillTerminate:
            [NSNotification notificationWithName:NSApplicationWillTerminateNotification object:NSApp]];
        Check([defaults synchronize],@"test preferences synchronized for next process");
        printf("PASS %s\n", scenario.UTF8String);
    }
    return 0;
}

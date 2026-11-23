// Exercise the actual menu controller with input monitoring disabled for the test session.
#define main KCApplicationMain
#import "../app/main.m"
#undef main
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
static NSData *Frame(KCPetView *view) {
    NSBitmapImageRep *rep=[view bitmapImageRepForCachingDisplayInRect:view.bounds];
    Check(rep && rep.bitmapData,@"allocated rendered frame");
    [view cacheDisplayInRect:view.bounds toBitmapImageRep:rep];
    return [NSData dataWithBytes:rep.bitmapData length:rep.bytesPerRow*rep.pixelsHigh];
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
    Check(roles.numberOfItems==5,@"five role choices");
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
int main(int argc,const char *argv[]) {
    @autoreleasepool {
        Check(argc==2,@"test scenario required");
        NSString *scenario=@(argv[1]);
        [NSApplication.sharedApplication setActivationPolicy:NSApplicationActivationPolicyAccessory];
        Check(NSScreen.screens.count>0,@"A WindowServer session is required");
        NSUserDefaults *defaults=NSUserDefaults.standardUserDefaults;
        [defaults setBool:YES forKey:@"explainedInput"];
        NSDictionary *fixtures=@{@"fresh":NSNull.null, @"legacy":@1.65, @"low":@.001, @"high":@7,
                                 @"invalid":@"wrong type", @"nan":@(NAN), @"office":@1};
        NSDictionary *expected=@{@"fresh":@100, @"legacy":@165, @"low":@1, @"high":@200,
                                 @"invalid":@100, @"nan":@100, @"restored":@72.5, @"office":@100, @"office-restored":@100};
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

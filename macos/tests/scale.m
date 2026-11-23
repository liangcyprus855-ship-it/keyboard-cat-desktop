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
    NSMenuItem *item=[menu itemWithTitle:@"小猫大小"];
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
int main(int argc,const char *argv[]) {
    @autoreleasepool {
        Check(argc==2,@"test scenario required");
        NSString *scenario=@(argv[1]);
        [NSApplication.sharedApplication setActivationPolicy:NSApplicationActivationPolicyAccessory];
        Check(NSScreen.screens.count>0,@"A WindowServer session is required");
        NSUserDefaults *defaults=NSUserDefaults.standardUserDefaults;
        [defaults setBool:YES forKey:@"explainedInput"];
        NSDictionary *fixtures=@{@"fresh":NSNull.null, @"legacy":@1.65, @"low":@.001, @"high":@7,
                                 @"invalid":@"wrong type", @"nan":@(NAN)};
        NSDictionary *expected=@{@"fresh":@100, @"legacy":@165, @"low":@1, @"high":@200,
                                 @"invalid":@100, @"nan":@100, @"restored":@72.5};
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

#import <Cocoa/Cocoa.h>

typedef NS_ENUM(NSInteger, KCOfficeState) { KCOfficeIdle, KCOfficeWorking, KCOfficeHover, KCOfficeClick };

@interface KCPetView : NSView
@property(nonatomic) NSInteger catStyle;
@property(nonatomic,readonly) BOOL isPerson;
@property(nonatomic) NSInteger keyboardStyle;
@property(nonatomic) NSInteger mouseStyle;
@property(nonatomic) BOOL showsLabels;
@property (nonatomic,readonly) BOOL hasArt;
- (void)samplePointer:(NSPoint)point onScreen:(NSPoint)normalized left:(BOOL)left right:(BOOL)right headInteractive:(BOOL)headInteractive at:(double)now;
- (void)cancelHeadGesture;
- (BOOL)officeHeadAt:(NSPoint)scenePoint;
- (KCOfficeState)officeState;
- (double)weightForOfficeState:(KCOfficeState)state;
- (void)button:(NSInteger)side down:(BOOL)down at:(double)now;
- (void)strike:(NSInteger)vk label:(NSString *)label at:(double)now;
- (void)advance:(double)now;
- (void)clearInput;
- (NSImage *)thumbnail:(NSString *)name maxWidth:(CGFloat)width;
@end



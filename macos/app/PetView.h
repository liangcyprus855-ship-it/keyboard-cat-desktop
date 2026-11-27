#import <Cocoa/Cocoa.h>

typedef NS_ENUM(NSInteger, KCOfficeState) { KCOfficeIdle, KCOfficeWorking, KCOfficeHover, KCOfficeClick, KCOfficeSleeping };

@interface KCPetView : NSView
@property(nonatomic) NSInteger catStyle;
@property(nonatomic,readonly) BOOL isPerson;
@property(nonatomic) NSInteger keyboardStyle;
@property(nonatomic) NSInteger mouseStyle;
@property(nonatomic) BOOL showsLabels;
@property(nonatomic) BOOL privacyMode;
@property(nonatomic) NSInteger fxLevel;
@property(nonatomic) NSInteger deskProp;
@property(nonatomic) BOOL breakReminderEnabled;
@property(nonatomic) long long todayKeyCount;
@property(nonatomic) NSInteger todayBurstCount;
@property (nonatomic,readonly) BOOL hasArt;
- (void)samplePointer:(NSPoint)point onScreen:(NSPoint)normalized left:(BOOL)left right:(BOOL)right headInteractive:(BOOL)headInteractive at:(double)now;
- (void)cancelHeadGesture;
- (BOOL)officeHeadAt:(NSPoint)scenePoint;
- (KCOfficeState)officeState;
- (void)button:(NSInteger)side down:(BOOL)down at:(double)now;
- (void)scrollAt:(double)now;
- (void)strike:(NSInteger)vk label:(NSString *)label at:(double)now;
- (void)advance:(double)now;
- (void)clearInput;
- (void)dismissBreak;
- (NSImage *)thumbnail:(NSString *)name maxWidth:(CGFloat)width;
- (NSImage *)portraitImageForStyle:(NSInteger)style;
@end



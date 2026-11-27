#import "PetView.h"
#import "PetArms.h"
#import "OfficeColleague.h"
#import "keycat_core.h"
#import <math.h>

static NSPoint Pair(NSArray *xy,NSPoint fallback) {
    return xy.count==2 ? NSMakePoint([xy[0] doubleValue],[xy[1] doubleValue]) : fallback;
}

@interface KCCap : NSObject
@property(copy) NSString *label;
@property NSPoint start;
@property double time, angle;
@end
@implementation KCCap
@end

@interface KCImpact : NSObject
@property NSPoint start;
@property double time, intensity, seedAngle;
@end
@implementation KCImpact
@end

static NSImage *OfficeImage(NSString *name,NSString *directory) {
    NSURL *url=[NSBundle.mainBundle URLForResource:name withExtension:@"png" subdirectory:directory];
    return url?[[NSImage alloc] initWithContentsOfURL:url]:nil;
}

@implementation KCPetView {
    KCSceneState _state;
    NSDictionary *_sprites, *_dogs, *_keys, *_defaultKeys, *_keysByStyle, *_personStyles;
    KCPersonStyle _look;
    NSURL *_assetDir;
    NSImage *_officeFigure,*_officeWork,*_officeSleep,*_officeHover,*_officeClick,*_headBacking;
    NSData *_headAlpha;
    size_t _alphaWidth,_alphaHeight;
    NSEvent *_headPress;
    NSPoint _pressScreen;
    BOOL _draggingHead;
    BOOL _dirtyRender;
    KCOfficePose _lastRenderedPose;
    KCPoint _lastRenderedMouseOffset;
    NSMutableDictionary<NSString *,NSImage *> *_images;
    NSMutableDictionary<NSNumber *,NSNumber *> *_keysDown;
    NSMutableArray<KCCap *> *_caps;
    NSMutableArray<KCImpact *> *_impacts;
    NSInteger _leftCode,_rightCode;
}

- (instancetype)initWithFrame:(NSRect)frame {
    self=[super initWithFrame:frame]; if(!self)return nil;
    _assetDir=[[[NSBundle mainBundle] resourceURL] URLByAppendingPathComponent:@"Art" isDirectory:YES];
    _personStyles=KCPersonTable();
    NSData *data=[NSData dataWithContentsOfURL:[_assetDir URLByAppendingPathComponent:@"atlas.json"]];
    NSDictionary *atlas=data?[NSJSONSerialization JSONObjectWithData:data options:0 error:NULL]:nil;
    if([atlas isKindOfClass:NSDictionary.class]) {
        _sprites=atlas[@"sprites"];_dogs=atlas[@"dogs"];_defaultKeys=atlas[@"keys"];_keysByStyle=atlas[@"keysByStyle"];_keys=_defaultKeys;
    }
    _images=[NSMutableDictionary dictionary];_keysDown=[NSMutableDictionary dictionary];_caps=[NSMutableArray array];_impacts=[NSMutableArray array];
    kc_scene_init(&_state, NSProcessInfo.processInfo.systemUptime);
    [self syncDeskTargetsWithAtlas:atlas];
    [self clearInput];
    self.showsLabels=YES;
    _dirtyRender=YES;
    return self;
}
- (void)syncDeskTargetsWithAtlas:(NSDictionary *)atlas {
    NSPoint mousePaw=atlas?Pair(atlas[@"mousePaw"],NSMakePoint(104,264)):NSMakePoint(_state.mousePaw.x,_state.mousePaw.y);
    NSPoint space=[self targetForKey:32];
    NSPoint jKey=[self targetForKey:74];
    NSPoint fKey=[self targetForKey:70];
    _state.mousePaw=kc_pt(mousePaw.x,mousePaw.y);
    _state.spaceKey=kc_pt(space.x,space.y);
    _state.jKey=kc_pt(jKey.x,jKey.y);
    _state.fKey=kc_pt(fKey.x,fKey.y);
}
- (NSInteger)catStyle { return _state.catStyle; }
- (NSInteger)keyboardStyle { return _state.keyboardStyle; }
- (NSInteger)mouseStyle { return _state.mouseStyle; }
- (BOOL)showsLabels { return _state.labelsEnabled!=0; }
- (BOOL)privacyMode { return _state.privacyMode!=0; }
- (NSInteger)fxLevel { return _state.fxLevel; }
- (NSInteger)deskProp { return _state.deskProp; }
- (BOOL)breakReminderEnabled { return _state.breakReminderEnabled!=0; }
- (long long)todayKeyCount { return _state.todayKeyCount; }
- (void)setTodayKeyCount:(long long)count { _state.todayKeyCount=count>0?count:0; }
- (NSInteger)todayBurstCount { return _state.todayBurstCount; }
- (void)setTodayBurstCount:(NSInteger)count { _state.todayBurstCount=count>0?(int)count:0; }
- (NSPoint)left { return NSMakePoint(_state.left.x,_state.left.y); }
- (NSPoint)right { return NSMakePoint(_state.right.x,_state.right.y); }
- (NSPoint)spaceKey { return NSMakePoint(_state.spaceKey.x,_state.spaceKey.y); }
- (NSPoint)rightKey { return NSMakePoint(_state.rightKey.x,_state.rightKey.y); }
- (BOOL)leftDown { return _state.leftDown!=0; }
- (BOOL)rightDown { return _state.rightDown!=0; }
- (BOOL)officeLeft { return _state.officeLeft!=0; }
- (BOOL)officeRight { return _state.officeRight!=0; }
- (double)grip { return _state.grip; }

- (BOOL)isPerson { return kc_is_person(_state.catStyle)!=0; }
- (BOOL)hasArt { return _sprites[@"body-3"] && _sprites[@"body-17"] && _sprites[@"dog-ruff-17"] && _sprites[@"dog-seg-l-17-0"] && _sprites[@"dog-paw-r-17"] && _dogs.count==4 && _sprites[@"portrait-17"] && _sprites[@"keyboard-4"] && _sprites[@"mouse-4-both"] && [_keysByStyle[@"4"] count]>=256 && _keys.count>0 && _personStyles.count==10 && (!self.isPerson || (_headAlpha && _officeWork.valid && _officeSleep.valid && _officeHover.valid && _officeClick.valid)); }
- (void)loadPersonArt {
    NSArray *names=@[@"OfficeColleague",@"Hairclip",@"Ribbon",@"Headband",@"Glasses",@"Hoodie",@"Beret",@"Cap",@"Boss",@"Shiba"];
    NSString *name=names[_state.catStyle-4];
    _look=KCOfficeStyle(_state.catStyle-4,_personStyles[name]);
    if(_state.catStyle==4) {
        _officeFigure=OfficeImage(@"office-colleague",nil);
        _officeWork=OfficeImage(@"office-colleague-work",nil);
        _officeSleep=OfficeImage(@"office-colleague-sleep",nil);
        _officeHover=OfficeImage(@"office-colleague-hover",nil);
        _officeClick=OfficeImage(@"office-colleague-click",nil);
    } else {
        NSString *directory=[@"characters" stringByAppendingPathComponent:name.lowercaseString];
        _officeFigure=OfficeImage(@"idle",directory);
        _officeWork=OfficeImage(@"work",directory);
        _officeSleep=OfficeImage(@"sleep",directory);
        _officeHover=OfficeImage(@"hover",directory);
        _officeClick=OfficeImage(@"click",directory);
    }
    _headAlpha=KCOfficeHeadMask(_officeFigure,_look,&_alphaWidth,&_alphaHeight);
    _headBacking=KCOfficeHitBacking(_headAlpha,_alphaWidth);
}
- (BOOL)isFlipped { return YES; }
- (BOOL)isOpaque { return NO; }
- (BOOL)acceptsFirstMouse:(NSEvent *)event { return YES; }
- (BOOL)officeHeadAt:(NSPoint)point {
    return self.isPerson && KCOfficeHitsHead(KCOfficeBounds(_officeFigure,_look),_look,_headAlpha,_alphaWidth,_alphaHeight,point);
}
- (NSPoint)scenePointForEvent:(NSEvent *)event {
    NSPoint p=[self convertPoint:event.locationInWindow fromView:nil];
    return NSMakePoint(KC_SCENE_OFFSET_X+p.x*KC_WINDOW_WIDTH/self.bounds.size.width,
                       KC_SCENE_OFFSET_Y+p.y*KC_WINDOW_HEIGHT/self.bounds.size.height);
}
- (void)cancelHeadGesture {
    _headPress=nil;_state.headHovered=0;_draggingHead=NO;_dirtyRender=YES;
}
- (void)mouseDown:(NSEvent *)event {
    if(!self.window.ignoresMouseEvents && event.type==NSEventTypeLeftMouseDown &&
       !(event.modifierFlags&NSEventModifierFlagControl) &&
       kc_scene_break_opacity(&_state,_state.time)>0.05 &&
       NSPointInRect([self scenePointForEvent:event],NSMakeRect(278,14,98,32))) {
        [self dismissBreak];
        return;
    }
    if(!self.isPerson){[self.window performWindowDragWithEvent:event];return;}
    [self cancelHeadGesture];
    if(self.window.ignoresMouseEvents || event.type!=NSEventTypeLeftMouseDown)return;
    if(event.modifierFlags&NSEventModifierFlagControl) {
        [NSMenu popUpContextMenu:self.menu withEvent:event forView:self];return;
    }
    if([self officeHeadAt:[self scenePointForEvent:event]] && event.clickCount==1) {
        _headPress=event;_state.headHovered=1;
        _pressScreen=[self.window convertPointToScreen:event.locationInWindow];
    } else {
        _draggingHead=YES;
        [self.window performWindowDragWithEvent:event];
    }
}
- (BOOL)pastDragThreshold:(NSEvent *)event {
    NSPoint p=[self.window convertPointToScreen:event.locationInWindow];
    return fabs(p.x-_pressScreen.x)>=4 || fabs(p.y-_pressScreen.y)>=4;
}
- (void)mouseDragged:(NSEvent *)event {
    if(!_headPress || ![self pastDragThreshold:event])return;
    NSEvent *down=_headPress;
    _headPress=nil;_state.headHovered=0;_draggingHead=YES;
    [self.window performWindowDragWithEvent:down];
}
- (void)mouseUp:(NSEvent *)event {
    if(_headPress && _headPress.window==event.window && event.type==NSEventTypeLeftMouseUp &&
       !self.window.ignoresMouseEvents && ![self pastDragThreshold:event] && [self officeHeadAt:[self scenePointForEvent:event]]) {
        kc_scene_click_office_head(&_state,event.timestamp);
        _dirtyRender=YES;
        self.needsDisplay=YES;
    }
    _headPress=nil;_draggingHead=NO;
}
- (void)setCatStyle:(NSInteger)style {
    if(_state.catStyle==style)return;
    [self cancelHeadGesture];
    kc_scene_set_character(&_state,(int)style);
    if(self.isPerson) {
        [self loadPersonArt];
        kc_scene_reset_office(&_state);
        [_impacts removeAllObjects];
    }
    _dirtyRender=YES;
    self.needsDisplay=YES;
}
- (NSPoint)targetForKey:(NSInteger)vk {
    return Pair(_keys[@(vk).stringValue],NSMakePoint(158,258));
}
- (void)setKeyboardStyle:(NSInteger)style {
    _state.keyboardStyle=(int)style;
    _keys=_keysByStyle[@(style).stringValue] ?: _defaultKeys;
    [self syncDeskTargetsWithAtlas:nil];
    if(_leftCode){NSPoint lk=[self targetForKey:_leftCode];_state.leftKey=kc_pt(lk.x,lk.y);}
    if(_rightCode){NSPoint rk=[self targetForKey:_rightCode];_state.rightKey=kc_pt(rk.x,rk.y);}
    if(self.isPerson && self.officeState==KCOfficeIdle)_state.right=_state.spaceKey;
    _dirtyRender=YES;
    self.needsDisplay=YES;
}
- (void)setMouseStyle:(NSInteger)style {
    _state.mouseStyle=(int)style;
    _dirtyRender=YES;
    self.needsDisplay=YES;
}
- (void)clearInput {
    [_keysDown removeAllObjects];[_caps removeAllObjects];[_impacts removeAllObjects];
    _leftCode=_rightCode=0;
    [self cancelHeadGesture];
    kc_scene_reset_input(&_state, NSProcessInfo.processInfo.systemUptime);
    _dirtyRender=YES;
    self.needsDisplay=YES;
}
- (void)setShowsLabels:(BOOL)flag {
    _state.labelsEnabled=flag?1:0;
    if(!flag)[_caps removeAllObjects];
    _dirtyRender=YES;
    self.needsDisplay=YES;
}
- (void)setPrivacyMode:(BOOL)flag {
    _state.privacyMode=flag?1:0;
    if(flag)[_caps removeAllObjects];
    _dirtyRender=YES;
    self.needsDisplay=YES;
}
- (void)setFxLevel:(NSInteger)level {
    _state.fxLevel=(level==KC_FX_QUIET)?KC_FX_QUIET:KC_FX_VIBRANT;
    _dirtyRender=YES;
    self.needsDisplay=YES;
}
- (void)setDeskProp:(NSInteger)prop {
    _state.deskProp=(prop>=KC_PROP_NONE && prop<=KC_PROP_MOON)?(int)prop:KC_PROP_NONE;
    _dirtyRender=YES;
    self.needsDisplay=YES;
}
- (void)setBreakReminderEnabled:(BOOL)flag {
    _state.breakReminderEnabled=flag?1:0;
    if(!flag)kc_scene_dismiss_break(&_state);
    _dirtyRender=YES;
    self.needsDisplay=YES;
}
- (void)dismissBreak {
    kc_scene_dismiss_break(&_state);
    _dirtyRender=YES;
    self.needsDisplay=YES;
}
- (NSImage *)portraitImageForStyle:(NSInteger)style {
    return [self imageOf:[NSString stringWithFormat:@"portrait-%ld",(long)style]];
}
- (void)scrollAt:(double)now {
    kc_scene_mouse_scroll(&_state,now);
    _dirtyRender=YES;
}
- (void)button:(NSInteger)side down:(BOOL)down at:(double)now {
    if(side!=0&&side!=1)return;
    kc_scene_mouse_button(&_state,(int)(side+1),down?1:0,now);
    _dirtyRender=YES;
}
- (NSPoint)headPosition {
    KCHeadPose pose=kc_cat_head_pose(&_state);
    return NSMakePoint(pose.position.x,pose.position.y);
}
- (void)samplePointer:(NSPoint)p onScreen:(NSPoint)normalized left:(BOOL)left right:(BOOL)right headInteractive:(BOOL)headInteractive at:(double)now {
    BOOL hit;
    if(!left)_draggingHead=NO;
    if(self.isPerson) hit=headInteractive && !_draggingHead && [self officeHeadAt:p];
    else {
        NSPoint c=[self headPosition];
        BOOL isDog=_dogs[[NSString stringWithFormat:@"%ld",(long)_state.catStyle]]!=nil;
        double x=(p.x-c.x)/(isDog?66.:56.), y=(p.y-c.y+(isDog?18.:9.))/(isDog?56.:52.);
        hit=x*x+y*y<1;
    }
    kc_scene_pointer(&_state,kc_pt(p.x,p.y),left?1:0,right?1:0,now,kc_pt(normalized.x,normalized.y),hit?1:0);
}
- (KCOfficeState)officeState {
    return (KCOfficeState)kc_scene_office_pose(&_state);
}
- (void)strike:(NSInteger)vk label:(NSString *)label at:(double)now {
    if(!vk || !label)return;
    NSPoint target=[self targetForKey:vk];
    _keysDown[@(vk)]=@(now);
    int left=kc_scene_strike(&_state,(int)vk,kc_pt(target.x,target.y),now);
    if(left)_leftCode=vk;else _rightCode=vk;
    if(!self.isPerson) {
        double intensity=kc_cat_impact_intensity(&_state);
        if(intensity>0.0){
            if(_impacts.count>=7)[_impacts removeObjectAtIndex:0];
            KCImpact *impact=[KCImpact new];
            impact.start=target; impact.time=now; impact.intensity=intensity;
            impact.seedAngle=(double)arc4random_uniform(60);
            [_impacts addObject:impact];
        }
    }
    _dirtyRender=YES;
    if(!_state.labelsEnabled)return;
    if(_caps.count>=9)[_caps removeObjectAtIndex:0];
    KCCap *cap=[KCCap new];cap.label=_state.privacyMode?@"••":label;cap.time=now;
    cap.angle=(int)arc4random_uniform(15)-7;cap.start=NSMakePoint(target.x+(int)arc4random_uniform(11)-5,target.y-14);
    [_caps addObject:cap];
}
- (void)advance:(double)now {
    kc_scene_advance(&_state,now,arc4random_uniform(10000)/10000.0);
    for(NSNumber *key in _keysDown.allKeys)if(now-_keysDown[key].doubleValue>.43)[_keysDown removeObjectForKey:key];
    for(NSInteger i=(NSInteger)_caps.count-1;i>=0;i--){
        KCCap *cap=_caps[(NSUInteger)i];
        KCCapLayout layout=kc_cap_layout(now-cap.time,i==(NSInteger)_caps.count-1?1:0,kc_pt(cap.start.x,cap.start.y),cap.angle);
        if(layout.expired)[_caps removeObjectAtIndex:(NSUInteger)i];
    }
    for(NSInteger i=(NSInteger)_impacts.count-1;i>=0;i--){
        KCImpact *imp=_impacts[(NSUInteger)i];
        KCImpactLayout layout=kc_impact_layout(now-imp.time,imp.intensity,imp.seedAngle);
        if(layout.expired)[_impacts removeObjectAtIndex:(NSUInteger)i];
    }
    KCOfficePose pose=(KCOfficePose)kc_scene_office_pose(&_state);
    BOOL mouseMoving=fabs(_state.mouseOffset.x-_lastRenderedMouseOffset.x)>0.001 ||
                     fabs(_state.mouseOffset.y-_lastRenderedMouseOffset.y)>0.001;
    BOOL needsRedraw=_dirtyRender || !self.isPerson ||
                     pose==KC_POSE_WORKING || pose==KC_POSE_SLEEPING ||
                     pose!=_lastRenderedPose || _caps.count>0 || _impacts.count>0 || _keysDown.count>0 ||
                     _state.leftClickGlow>0.001 || _state.rightClickGlow>0.001 || mouseMoving ||
                     _state.deskProp==KC_PROP_COFFEE || kc_scene_break_opacity(&_state,now)>0.01;
    if(needsRedraw) {
        _lastRenderedPose=pose;
        _lastRenderedMouseOffset=_state.mouseOffset;
        _dirtyRender=NO;
        self.needsDisplay=YES;
    }
}
- (NSImage *)imageOf:(NSString *)name {
    NSDictionary *entry=_sprites[name];NSString *file=entry[@"file"];if(!file)return nil;
    NSImage *image=_images[file];
    if(!image){image=[[NSImage alloc] initWithContentsOfURL:[_assetDir URLByAppendingPathComponent:file]];
        if(image)_images[file]=image;}
    return image;
}
- (NSImage *)thumbnail:(NSString *)name maxWidth:(CGFloat)w {
    NSImage *img=[[self imageOf:name] copy];if(!img)return nil;
    NSDictionary *entry=_sprites[name];double a=[entry[@"w"] doubleValue], b=[entry[@"h"] doubleValue];
    if(a<=0||b<=0)return nil;double scale=fmin(w/a,22/b);img.size=NSMakeSize(a*scale,b*scale);return img;
}
- (void)drawSprite:(NSString *)name alpha:(CGFloat)alpha {
    NSDictionary *r=_sprites[name];NSImage *img=[self imageOf:name];if(!r||!img)return;
    NSRect box=NSMakeRect([r[@"x"] doubleValue],[r[@"y"] doubleValue],[r[@"w"] doubleValue],[r[@"h"] doubleValue]);
    [img drawInRect:box fromRect:NSZeroRect operation:NSCompositingOperationSourceOver fraction:alpha respectFlipped:YES hints:nil];
}
- (void)drawPawWhip:(CGContextRef)ctx paw:(CGPoint)paw side:(int)side whip:(double)whip {
    if(whip<=0.14)return;
    double alpha=kc_clamp(whip,0.,1.), reach=28.+22.*alpha;
    CGContextSaveGState(ctx);CGContextSetAlpha(ctx,alpha*.88);CGContextSetLineCap(ctx,kCGLineCapRound);
    CGContextSetRGBStrokeColor(ctx,1.,1.,1.,220./255.);CGContextSetLineWidth(ctx,3.4);
    CGContextBeginPath(ctx);CGContextMoveToPoint(ctx,paw.x+side*10.,paw.y-reach);
    CGContextAddQuadCurveToPoint(ctx,paw.x+side*(16.+5.*alpha),paw.y-reach*.44,paw.x+side*5.,paw.y-4.);
    CGContextStrokePath(ctx);
    CGContextSetRGBStrokeColor(ctx,1.,212./255.,92./255.,1.);CGContextSetLineWidth(ctx,3.1);
    CGContextBeginPath(ctx);CGContextMoveToPoint(ctx,paw.x+side*10.,paw.y-reach);
    CGContextAddQuadCurveToPoint(ctx,paw.x+side*(16.+5.*alpha),paw.y-reach*.44,paw.x+side*5.,paw.y-4.);
    CGContextStrokePath(ctx);
    CGContextSetRGBStrokeColor(ctx,1.,252./255.,232./255.,1.);CGContextSetLineWidth(ctx,1.45);
    CGContextBeginPath(ctx);CGContextMoveToPoint(ctx,paw.x-side*5.,paw.y-reach*.85);
    CGContextAddQuadCurveToPoint(ctx,paw.x-side*3.,paw.y-reach*.36,paw.x-side*4.,paw.y-5.);
    CGContextStrokePath(ctx);CGContextRestoreGState(ctx);
}
- (void)drawImpacts:(CGContextRef)ctx deskOffset:(CGPoint)deskOffset {
    for(KCImpact *imp in _impacts){
        KCImpactLayout l=kc_impact_layout(_state.time-imp.time,imp.intensity,imp.seedAngle);
        if(l.expired || l.ringOpacity<=0.01)continue;
        CGFloat cx=imp.start.x+deskOffset.x, cy=imp.start.y+deskOffset.y;
        BOOL hot=imp.intensity>=0.58;
        CGContextSaveGState(ctx);CGContextSetAlpha(ctx,kc_clamp(l.ringOpacity,0.,1.));
        CGRect outer=CGRectMake(cx-l.ringRadius,cy-l.ringRadius*.74,l.ringRadius*2.,l.ringRadius*1.48);
        CGContextSetRGBStrokeColor(ctx,1.,1.,1.,200./255.);CGContextSetLineWidth(ctx,3.6);CGContextStrokeEllipseInRect(ctx,outer);
        if(hot)CGContextSetRGBStrokeColor(ctx,1.,107./255.,53./255.,1.);
        else CGContextSetRGBStrokeColor(ctx,1.,193./255.,38./255.,1.);
        CGContextSetLineWidth(ctx,hot?2.3:2.1);CGContextStrokeEllipseInRect(ctx,outer);
        if(hot){
            CGRect inner=CGRectMake(cx-l.innerRadius,cy-l.innerRadius*.74,l.innerRadius*2.,l.innerRadius*1.48);
            CGContextSetRGBStrokeColor(ctx,1.,245./255.,196./255.,1.);CGContextSetLineWidth(ctx,1.5);CGContextStrokeEllipseInRect(ctx,inner);
        }
        double baseRad=l.angleOffsetDeg*M_PI/180., innerR=l.ringRadius*.62, tipR=l.ringRadius+6.5+5.5*l.sparkScale;
        double sideR=(innerR+tipR)*.5, halfSpread=0.095*l.sparkScale;
        CGContextBeginPath(ctx);
        for(int i=0;i<6;i++){
            double a=baseRad+i*(2.*M_PI/6.);
            CGContextMoveToPoint(ctx,cx+cos(a)*innerR,cy+sin(a)*innerR*.76);
            CGContextAddLineToPoint(ctx,cx+cos(a-halfSpread)*sideR,cy+sin(a-halfSpread)*sideR*.76);
            CGContextAddLineToPoint(ctx,cx+cos(a)*tipR,cy+sin(a)*tipR*.76);
            CGContextAddLineToPoint(ctx,cx+cos(a+halfSpread)*sideR,cy+sin(a+halfSpread)*sideR*.76);
            CGContextClosePath(ctx);
        }
        if(hot)CGContextSetRGBFillColor(ctx,1.,148./255.,77./255.,1.);
        else CGContextSetRGBFillColor(ctx,1.,224./255.,112./255.,1.);
        CGContextSetRGBStrokeColor(ctx,214./255.,96./255.,28./255.,1.);CGContextSetLineWidth(ctx,.75);
        CGContextDrawPath(ctx,kCGPathFillStroke);CGContextRestoreGState(ctx);
    }
}
- (void)drawComboAura:(CGContextRef)ctx head:(KCHeadPose)headPose motion:(KCCatMotion)motion {
    if(motion.fury<0.28 || _state.fxLevel==KC_FX_QUIET)return;
    double strength=kc_clamp((motion.fury-.28)/.72,0.,1.), pulse=.78+.22*sin(_state.time*26.);
    double alpha=strength*(.65+.35*motion.slam)*pulse, reach=5.5+5.*strength+2.*motion.slam;
    BOOL wideHead=_dogs[[NSString stringWithFormat:@"%ld",(long)_state.catStyle]]!=nil;
    double sideOffset=wideHead?75.:62., sideY=wideHead?-12.:-6., edgePad=3.5+reach;
    CGContextSaveGState(ctx);CGContextSetAlpha(ctx,kc_clamp(alpha,0.,1.));CGContextSetLineCap(ctx,kCGLineCapRound);
    for(int side=-1;side<=1;side+=2){
        double ax=headPose.position.x+side*(sideOffset+3.*motion.slam), ay=headPose.position.y+sideY;
        ax = side < 0 ? fmax(ax, 88.0 + edgePad) : fmin(ax, 368.0 - edgePad);
        for(int k=-1;k<=1;k++){
            double ang=(k*24.-10.)*M_PI/180., dx=side*cos(ang), dy=sin(ang);
            CGFloat x0=ax+dx*3.5,y0=ay+dy*3.5,x1=ax+dx*(3.5+reach),y1=ay+dy*(3.5+reach);
            CGContextSetRGBStrokeColor(ctx,1.,1.,1.,220./255.);CGContextSetLineWidth(ctx,3.4);
            CGContextBeginPath(ctx);CGContextMoveToPoint(ctx,x0,y0);CGContextAddLineToPoint(ctx,x1,y1);CGContextStrokePath(ctx);
            CGContextSetRGBStrokeColor(ctx,1.,108./255.,47./255.,1.);CGContextSetLineWidth(ctx,1.85);
            CGContextBeginPath(ctx);CGContextMoveToPoint(ctx,x0,y0);CGContextAddLineToPoint(ctx,x1,y1);CGContextStrokePath(ctx);
        }
    }
    if(motion.fury>=0.58){
        double starScale=0.75+0.45*motion.slam;
        CGPoint pts[2]={CGPointMake(fmax(headPose.position.x+(wideHead?-70.:-52.),96.),headPose.position.y+(wideHead?-62.:-46.)),
                        CGPointMake(fmin(headPose.position.x+(wideHead?76.:54.),362.),headPose.position.y+(wideHead?-65.:-42.))};
        double rads[2]={7.*starScale,5.6*starScale};
        for(int s=0;s<2;s++){
            double r=rads[s],in=r*.28;CGPoint c=pts[s];
            CGContextBeginPath(ctx);
            CGContextMoveToPoint(ctx,c.x,c.y-r);CGContextAddLineToPoint(ctx,c.x+in,c.y-in);
            CGContextAddLineToPoint(ctx,c.x+r,c.y);CGContextAddLineToPoint(ctx,c.x+in,c.y+in);
            CGContextAddLineToPoint(ctx,c.x,c.y+r);CGContextAddLineToPoint(ctx,c.x-in,c.y+in);
            CGContextAddLineToPoint(ctx,c.x-r,c.y);CGContextAddLineToPoint(ctx,c.x-in,c.y-in);
            CGContextClosePath(ctx);
            CGContextSetRGBFillColor(ctx,1.,222./255.,89./255.,1.);
            CGContextSetRGBStrokeColor(ctx,224./255.,98./255.,32./255.,1.);CGContextSetLineWidth(ctx,.8);
            CGContextDrawPath(ctx,kCGPathFillStroke);
        }
    }
    CGContextRestoreGState(ctx);
}
- (void)drawCaps:(CGContextRef)ctx motion:(KCCatMotion)motion {
    NSDictionary *attrs=@{NSFontAttributeName:[NSFont boldSystemFontOfSize:13],NSForegroundColorAttributeName:[NSColor colorWithWhite:.11 alpha:1]};
    for(KCCap *cap in _caps){
        KCCapLayout layout=kc_cap_layout(_state.time-cap.time,cap==_caps.lastObject?1:0,kc_pt(cap.start.x,cap.start.y),cap.angle);
        NSSize text=[cap.label sizeWithAttributes:attrs];double w=fmax(25,text.width+9);
        BOOL fiery=!self.isPerson && motion.fury>=0.45 && cap==_caps.lastObject;
        double capScale=fiery?1.+0.14*motion.slam:1.;
        CGContextSaveGState(ctx);CGContextTranslateCTM(ctx,layout.position.x,layout.position.y);
        CGContextRotateCTM(ctx,layout.angleDeg*M_PI/180);
        if(fabs(capScale-1.)>0.001)CGContextScaleCTM(ctx,capScale,capScale);
        CGContextSetAlpha(ctx,layout.opacity);
        NSBezierPath *shape=[NSBezierPath bezierPathWithRoundedRect:NSMakeRect(-w/2,-9.5,w,19) xRadius:4 yRadius:4];
        [[NSColor colorWithRed:.98 green:.97 blue:.94 alpha:1] setFill];[shape fill];
        if(fiery){[[NSColor colorWithSRGBRed:1. green:125./255. blue:41./255. alpha:1] setStroke];shape.lineWidth=1.35;}
        else{[[NSColor colorWithWhite:.30 alpha:1] setStroke];shape.lineWidth=.8;}
        [shape stroke];
        [cap.label drawAtPoint:NSMakePoint(-text.width/2,-text.height/2) withAttributes:attrs];CGContextRestoreGState(ctx);
    }
}
static NSColor *KCSRGB(uint32_t rgb) {
    return [NSColor colorWithSRGBRed:((rgb>>16)&0xff)/255. green:((rgb>>8)&0xff)/255. blue:(rgb&0xff)/255. alpha:1.];
}
- (void)drawMiniTag:(NSString *)text center:(NSPoint)center color:(NSColor *)ink {
    NSDictionary *attrs=@{NSFontAttributeName:[NSFont boldSystemFontOfSize:7.2],NSForegroundColorAttributeName:ink};
    NSSize sz=[text sizeWithAttributes:attrs];
    [text drawAtPoint:NSMakePoint(center.x-sz.width/2.,center.y-sz.height/2.) withAttributes:attrs];
}
- (void)drawDeskProp:(CGContextRef)ctx {
    int prop=_state.deskProp;
    if(prop<=KC_PROP_NONE || prop>KC_PROP_MOON)return;
    const double cx=365.0, cy=272.0;
    if(prop==KC_PROP_COFFEE) {
        NSBezierPath *saucer=[NSBezierPath bezierPathWithOvalInRect:NSMakeRect(cx-11.5,cy+4.5,23.0,8.0)];
        [KCSRGB(0xE6D8C8) setFill];[saucer fill];
        [KCSRGB(0xC9B5A0) setStroke];saucer.lineWidth=1.0;[saucer stroke];
        NSBezierPath *handle=[NSBezierPath bezierPathWithOvalInRect:NSMakeRect(cx+5.0,cy-3.0,6.0,8.0)];
        [KCSRGB(0xC9B5A0) setStroke];handle.lineWidth=3.6;[handle stroke];
        [KCSRGB(0xFFF6EA) setStroke];handle.lineWidth=1.8;[handle stroke];
        NSBezierPath *mug=[NSBezierPath bezierPathWithRoundedRect:NSMakeRect(cx-7.5,cy-6.0,15.0,14.0) xRadius:4.0 yRadius:4.0];
        [KCSRGB(0xFFF6EA) setFill];[mug fill];
        [KCSRGB(0xC9B5A0) setStroke];mug.lineWidth=1.0;[mug stroke];
        NSBezierPath *coffee=[NSBezierPath bezierPathWithOvalInRect:NSMakeRect(cx-6.2,cy-7.5,12.4,4.6)];
        [KCSRGB(0x6E3E22) setFill];[coffee fill];
        [KCSRGB(0xC9B5A0) setStroke];coffee.lineWidth=1.0;[coffee stroke];
        double s0=sin(_state.time*2.3)*1.4, s1=cos(_state.time*2.1)*1.4;
        NSBezierPath *steam=[NSBezierPath bezierPath];
        steam.lineWidth=1.3;steam.lineCapStyle=NSLineCapStyleRound;
        [steam moveToPoint:NSMakePoint(cx-2.2,cy-9.0)];
        [steam curveToPoint:NSMakePoint(cx-1.5-s0*0.6,cy-17.5) controlPoint1:NSMakePoint(cx-3.8+s0,cy-12.5) controlPoint2:NSMakePoint(cx-3.0+s0*0.5,cy-15.0)];
        [steam moveToPoint:NSMakePoint(cx+2.2,cy-9.5)];
        [steam curveToPoint:NSMakePoint(cx+1.5-s1*0.6,cy-18.0) controlPoint1:NSMakePoint(cx+3.8+s1,cy-13.0) controlPoint2:NSMakePoint(cx+3.0+s1*0.5,cy-15.5)];
        [KCSRGB(0xE8DDD1) setStroke];[steam stroke];
    } else if(prop==KC_PROP_ICED) {
        NSBezierPath *straw=[NSBezierPath bezierPath];
        straw.lineWidth=2.0;straw.lineCapStyle=NSLineCapStyleRound;
        [straw moveToPoint:NSMakePoint(cx+1.5,cy-6.0)];[straw lineToPoint:NSMakePoint(cx+5.0,cy-18.0)];
        [KCSRGB(0x4AA37A) setStroke];[straw stroke];
        NSBezierPath *cup=[NSBezierPath bezierPath];
        [cup moveToPoint:NSMakePoint(cx-7.0,cy-7.5)];[cup lineToPoint:NSMakePoint(cx+7.0,cy-7.5)];
        [cup lineToPoint:NSMakePoint(cx+5.0,cy+11.0)];[cup lineToPoint:NSMakePoint(cx-5.0,cy+11.0)];[cup closePath];
        [KCSRGB(0xEAF5FA) setFill];[cup fill];
        [KCSRGB(0x93B1BE) setStroke];cup.lineWidth=0.9;[cup stroke];
        NSBezierPath *liq=[NSBezierPath bezierPath];
        [liq moveToPoint:NSMakePoint(cx-6.1,cy-2.0)];[liq lineToPoint:NSMakePoint(cx+6.1,cy-2.0)];
        [liq lineToPoint:NSMakePoint(cx+4.4,cy+10.0)];[liq lineToPoint:NSMakePoint(cx-4.4,cy+10.0)];[liq closePath];
        [KCSRGB(0x9C5D36) setFill];[liq fill];
        NSBezierPath *i1=[NSBezierPath bezierPathWithRoundedRect:NSMakeRect(cx-3.8,cy-4.5,4.2,4.2) xRadius:0.8 yRadius:0.8];
        NSBezierPath *i2=[NSBezierPath bezierPathWithRoundedRect:NSMakeRect(cx+0.6,cy-3.6,3.8,3.8) xRadius:0.8 yRadius:0.8];
        [KCSRGB(0xF5FBFF) setFill];[i1 fill];[i2 fill];
        [KCSRGB(0x93B1BE) setStroke];i1.lineWidth=0.9;i2.lineWidth=0.9;[i1 stroke];[i2 stroke];
    } else if(prop==KC_PROP_BRICK) {
        NSBezierPath *face=[NSBezierPath bezierPathWithRoundedRect:NSMakeRect(cx-10.5,cy-2.0,21.0,12.0) xRadius:2.0 yRadius:2.0];
        [KCSRGB(0xB8563A) setFill];[face fill];[KCSRGB(0x7B3322) setStroke];face.lineWidth=0.85;[face stroke];
        NSBezierPath *top=[NSBezierPath bezierPathWithRoundedRect:NSMakeRect(cx-10.5,cy-6.5,21.0,6.0) xRadius:2.0 yRadius:2.0];
        [KCSRGB(0xD97856) setFill];[top fill];[KCSRGB(0x7B3322) setStroke];top.lineWidth=0.85;[top stroke];
        NSBezierPath *s1=[NSBezierPath bezierPathWithOvalInRect:NSMakeRect(cx-7.5,cy-4.9,5.0,2.6)];
        NSBezierPath *s2=[NSBezierPath bezierPathWithOvalInRect:NSMakeRect(cx+2.0,cy-4.9,5.0,2.6)];
        [KCSRGB(0xFFF6EA) setFill];[s1 fill];[s2 fill];[KCSRGB(0x7B3322) setStroke];s1.lineWidth=0.85;s2.lineWidth=0.85;[s1 stroke];[s2 stroke];
        [self drawMiniTag:@"搬砖" center:NSMakePoint(cx,cy+4.0) color:KCSRGB(0xF6F7FA)];
    } else if(prop==KC_PROP_MOON) {
        NSBezierPath *stand=[NSBezierPath bezierPath];
        [stand moveToPoint:NSMakePoint(cx-11.0,cy+10.5)];[stand lineToPoint:NSMakePoint(cx-7.5,cy-10.5)];
        [stand lineToPoint:NSMakePoint(cx+7.5,cy-10.5)];[stand lineToPoint:NSMakePoint(cx+11.0,cy+10.5)];[stand closePath];
        [KCSRGB(0x273142) setFill];[stand fill];[KCSRGB(0x161D29) setStroke];stand.lineWidth=0.9;[stand stroke];
        NSBezierPath *moon=[NSBezierPath bezierPathWithOvalInRect:NSMakeRect(cx-3.6,cy-7.8,7.2,7.2)];
        [KCSRGB(0xF7CD5B) setFill];[moon fill];
        NSBezierPath *cut=[NSBezierPath bezierPathWithOvalInRect:NSMakeRect(cx-1.3,cy-7.6,5.6,5.6)];
        [KCSRGB(0x273142) setFill];[cut fill];
        [self drawMiniTag:@"勿扰" center:NSMakePoint(cx,cy+4.8) color:KCSRGB(0xF7CD5B)];
    }
}
- (void)drawBreakBubble:(CGContextRef)ctx {
    double op=kc_scene_break_opacity(&_state,_state.time);
    if(op<=0.01)return;
    double bob=sin(_state.time*2.3)*1.1;
    NSRect rect=NSMakeRect(280.0,17.0+bob,94.0,26.0);
    CGContextSaveGState(ctx);
    CGContextSetAlpha(ctx,kc_clamp(op,0.,1.));
    NSBezierPath *box=[NSBezierPath bezierPathWithRoundedRect:rect xRadius:11.0 yRadius:11.0];
    [KCSRGB(0xFFFDF8) setFill];[box fill];
    [KCSRGB(0xD6C3AF) setStroke];box.lineWidth=1.1;[box stroke];
    NSDictionary *attrs=@{NSFontAttributeName:[NSFont systemFontOfSize:10.5 weight:NSFontWeightSemibold],NSForegroundColorAttributeName:KCSRGB(0x4A3A31)};
    NSString *msg=@"休息喝口水 ☕";
    NSSize sz=[msg sizeWithAttributes:attrs];
    [msg drawAtPoint:NSMakePoint(rect.origin.x+(rect.size.width-sz.width)/2.,rect.origin.y+(rect.size.height-sz.height)/2.-0.5) withAttributes:attrs];
    CGContextRestoreGState(ctx);
}
- (void)drawRect:(NSRect)dirty {
    CGContextRef ctx=NSGraphicsContext.currentContext.CGContext;
    CGContextClearRect(ctx,NSRectToCGRect(dirty));
    CGContextSaveGState(ctx);CGContextScaleCTM(ctx,self.bounds.size.width/(double)KC_WINDOW_WIDTH,self.bounds.size.height/(double)KC_WINDOW_HEIGHT);
    CGContextTranslateCTM(ctx,-KC_SCENE_OFFSET_X,-KC_SCENE_OFFSET_Y);NSGraphicsContext.currentContext.imageInterpolation=NSImageInterpolationHigh;
    KCCatMotion motion=kc_cat_motion(&_state);
    BOOL deskKick=!self.isPerson && (fabs(motion.deskOffsetX)>0.001 || fabs(motion.deskOffsetY)>0.001 || fabs(motion.deskTiltDeg)>0.001);
    if(deskKick){
        CGContextSaveGState(ctx);
        CGContextTranslateCTM(ctx,214.+motion.deskOffsetX,268.+motion.deskOffsetY);
        CGContextRotateCTM(ctx,motion.deskTiltDeg*M_PI/180.);
        CGContextTranslateCTM(ctx,-214.,-268.);
    }
    [self drawSprite:[NSString stringWithFormat:@"keyboard-%ld",(long)_state.keyboardStyle] alpha:1];
    for(NSNumber *key in _keysDown){
        double alpha=1-kc_smooth((_state.time-_keysDown[key].doubleValue)/.43);
        [self drawSprite:[NSString stringWithFormat:@"key-%ld-%@",(long)_state.keyboardStyle,key] alpha:alpha];
    }
    BOOL l=_state.leftDown||_state.time-_state.leftClickAt<.18, r=_state.rightDown||_state.time-_state.rightClickAt<.18;
    NSString *state=l?(r?@"both":@"left"):(r?@"right":@"normal");
    CGContextSaveGState(ctx);CGContextTranslateCTM(ctx,_state.mouseOffset.x,_state.mouseOffset.y);
    [self drawSprite:[NSString stringWithFormat:@"mouse-%ld-%@",(long)_state.mouseStyle,state] alpha:1];CGContextRestoreGState(ctx);
    [self drawDeskProp:ctx];
    if(deskKick)CGContextRestoreGState(ctx);
    if(self.isPerson)[self drawOffice:ctx];
    else {
        [self drawCat:ctx motion:motion];
        [self drawImpacts:ctx deskOffset:CGPointMake(motion.deskOffsetX,motion.deskOffsetY)];
    }
    [self drawCaps:ctx motion:motion];
    [self drawBreakBubble:ctx];
    CGContextRestoreGState(ctx);
}
- (void)drawOffice:(CGContextRef)ctx {
    KCOfficeState state=[self officeState];
    NSImage *image=state==KCOfficeHover?_officeHover:(state==KCOfficeClick?_officeClick:_officeFigure);
    BOOL hands=state==KCOfficeWorking && (_state.officeLeft || _state.officeRight);
    if(hands)image=_officeWork;
    if(state==KCOfficeSleeping)image=_officeSleep;
    if(state==KCOfficeHover || state==KCOfficeClick) {
        // Keep the original ears hittable where this pose is narrower. The window server skips zero-alpha pixels.
        NSRect headRect=KCOfficeBounds(_officeFigure,_look);
        headRect.size.height*=_headBacking.size.height/_alphaHeight;
        [_headBacking drawInRect:headRect fromRect:NSZeroRect operation:NSCompositingOperationSourceOver
                        fraction:1 respectFlipped:YES hints:nil];
    }
    NSRect figure=KCOfficeBounds(image,_look);
    if(state==KCOfficeSleeping) {
        CGFloat nod=.985+cos(_state.time*2.2)*.009;
        CGContextSaveGState(ctx);
        CGContextTranslateCTM(ctx,NSMidX(figure),NSMaxY(figure));
        CGContextScaleCTM(ctx,1,nod);
        CGContextTranslateCTM(ctx,-NSMidX(figure),-NSMaxY(figure));
    }
    if(hands) {
        KCOfficeBodyPose body=kc_office_body_pose(&_state,_look.centerX,NSMaxY(figure),
            kc_pt(_look.leftShoulder,_look.shoulderY+2.0),kc_pt(_look.rightShoulder,_look.shoulderY+2.0));
        CGPoint left=CGPointMake(body.leftShoulder.x,body.leftShoulder.y);
        CGPoint right=CGPointMake(body.rightShoulder.x,body.rightShoulder.y);
        if(!_state.officeLeft)KCOfficeArm(ctx,_look,left,CGPointMake(left.x-8,NSMaxY(figure)+body.offsetY-4),-1,YES);
        if(!_state.officeRight)KCOfficeArm(ctx,_look,right,CGPointMake(right.x+8,NSMaxY(figure)+body.offsetY-4),1,YES);
        if(_state.officeRight)KCOfficeArm(ctx,_look,right,NSMakePoint(_state.right.x,_state.right.y),1,NO);
        if(_state.officeLeft)KCOfficeArm(ctx,_look,left,NSMakePoint(_state.left.x,_state.left.y),-1,NO);

        CGContextSaveGState(ctx);
        CGContextTranslateCTM(ctx,_look.centerX+body.offsetX,NSMaxY(figure)+body.offsetY);
        CGContextRotateCTM(ctx,body.tiltDeg*M_PI/180.);
        CGContextTranslateCTM(ctx,-_look.centerX,-NSMaxY(figure));
        [image drawInRect:figure fromRect:NSZeroRect operation:NSCompositingOperationSourceOver
                 fraction:1 respectFlipped:YES hints:nil];
        CGContextRestoreGState(ctx);

        if(_state.officeRight)KCOfficeHandAt(ctx,_look,right,NSMakePoint(_state.right.x,_state.right.y),1);
        if(_state.officeLeft)KCOfficeHandAt(ctx,_look,left,NSMakePoint(_state.left.x,_state.left.y),-1);
        return;
    }
    [image drawInRect:figure fromRect:NSZeroRect operation:NSCompositingOperationSourceOver
             fraction:1 respectFlipped:YES hints:nil];
    if(state==KCOfficeSleeping) {
        CGContextRestoreGState(ctx);
        [@"Z z" drawAtPoint:NSMakePoint(NSMaxX(figure)-10,NSMinY(figure)+18+sin(_state.time*2)*3)
             withAttributes:@{NSFontAttributeName:[NSFont systemFontOfSize:17 weight:NSFontWeightSemibold],
             NSForegroundColorAttributeName:[NSColor colorWithSRGBRed:105./255 green:108./255 blue:151./255 alpha:1]}];
    }
}
static CGPoint KCDogCubic(CGPoint p0,CGPoint p1,CGPoint p2,CGPoint p3,double u) {
    double z=1.0-u;
    return CGPointMake(z*z*z*p0.x+3*z*z*u*p1.x+3*z*u*u*p2.x+u*u*u*p3.x,
                       z*z*z*p0.y+3*z*z*u*p1.y+3*z*u*u*p2.y+u*u*u*p3.y);
}
static CGPoint KCDogEvalCurve(CGPoint p0,CGPoint p1,CGPoint p2,CGPoint p3,CGPoint dir0,CGPoint dir1,double curveLen,double t) {
    if(t<0.0)return CGPointMake(p0.x+dir0.x*(t*curveLen),p0.y+dir0.y*(t*curveLen));
    if(t>1.0)return CGPointMake(p3.x+dir1.x*((t-1.0)*curveLen),p3.y+dir1.y*((t-1.0)*curveLen));
    return KCDogCubic(p0,p1,p2,p3,t);
}
static CGAffineTransform KCDogBodyTransform(KCPoint leftPaw,KCPoint rightPaw) {
    double lean=kc_smooth((157.0-leftPaw.x)/50.0);
    double rightReach=kc_clamp((rightPaw.x-248.0)/55.0,0.0,0.75);
    double netLean=lean*.85-rightReach*.35;
    double shiftX=-11.0*lean+5.5*rightReach;
    double deg=-2.2*lean+1.5*rightReach;
    double rad=deg*M_PI/180.0;
    CGAffineTransform m=CGAffineTransformMake(1,0,.24*netLean,1,-246.0*.24*netLean,0);
    m=CGAffineTransformConcat(m,CGAffineTransformMakeTranslation(-214.0,-247.0));
    m=CGAffineTransformConcat(m,CGAffineTransformMakeRotation(rad));
    m=CGAffineTransformConcat(m,CGAffineTransformMakeTranslation(214.0+shiftX,247.0));
    return m;
}
- (void)drawDogArm:(CGContextRef)ctx left:(BOOL)left paw:(NSPoint)targetPaw bodyTx:(CGAffineTransform)bodyTx {
    NSDictionary *dog=_dogs[[NSString stringWithFormat:@"%ld",(long)_state.catStyle]];
    if(!dog)return;
    NSPoint shoulder=Pair(dog[left?@"shoulderL":@"shoulderR"],NSMakePoint(0,0));
    NSPoint armRestPaw=Pair(dog[left?@"restPawL":@"restPawR"],NSMakePoint(0,0));
    NSPoint catRestPaw=left?NSMakePoint(192.,233.):NSMakePoint(229.,233.);
    double deltaX=targetPaw.x-catRestPaw.x, deltaY=targetPaw.y-catRestPaw.y;
    NSPoint actualPaw=NSMakePoint(armRestPaw.x+deltaX,armRestPaw.y+deltaY);
    CGPoint curShoulder=CGPointApplyAffineTransform(CGPointMake(shoulder.x,shoulder.y),bodyTx);

    double restDx=armRestPaw.x-shoulder.x,restDy=armRestPaw.y-shoulder.y;
    double restLen=fmax(8.0,hypot(restDx,restDy));
    double restAng=atan2(restDy,restDx);

    double side=left?-1.0:1.0;
    double dx=actualPaw.x-curShoulder.x,dy=actualPaw.y-curShoulder.y;
    double dMag=hypot(deltaX,deltaY)/14.0;
    double active=kc_smooth(dMag);
    double bend=kc_clamp((231.0-actualPaw.y)/42.0,0.0,1.0);

    CGPoint p1=CGPointMake(curShoulder.x+dx*(1.0/3.0*(1.0-active)+.24*active)+active*side*(4.0+7.5*bend),
                           curShoulder.y+dy*(1.0/3.0*(1.0-active)+.36*active)+active*4.5*bend);
    CGPoint p2=CGPointMake(curShoulder.x+dx*(2.0/3.0*(1.0-active)+.78*active)+active*side*(3.0+11.5*bend),
                           actualPaw.y-dy*(1.0/3.0*(1.0-active))+active*(-15.0+23.0*bend));

    double curveLen=0.0; CGPoint prev=curShoulder;
    for(int k=1;k<=8;k++){
        CGPoint pt=KCDogCubic(curShoulder,p1,p2,actualPaw,k/8.0);
        curveLen+=hypot(pt.x-prev.x,pt.y-prev.y); prev=pt;
    }
    curveLen=fmax(8.0,curveLen);
    double d0x=p1.x-curShoulder.x,d0y=p1.y-curShoulder.y,d0n=hypot(d0x,d0y);
    if(d0n<1e-2){d0x=dx;d0y=dy;d0n=fmax(1e-3,hypot(d0x,d0y));}
    CGPoint dir0=CGPointMake(d0x/d0n,d0y/d0n);
    double d1x=actualPaw.x-p2.x,d1y=actualPaw.y-p2.y,d1n=hypot(d1x,d1y);
    if(d1n<1e-2){d1x=dx;d1y=dy;d1n=fmax(1e-3,hypot(d1x,d1y));}
    CGPoint dir1=CGPointMake(d1x/d1n,d1y/d1n);

    double widScale=fmax(.95,fmin(1.06,1.0/pow(curveLen/restLen,.20)));
    NSString *sTag=left?@"l":@"r";
    NSArray *segs=dog[left?@"segsL":@"segsR"];
    for(NSUInteger i=0;i<segs.count;i++){
        NSDictionary *seg=segs[i];
        CGPoint ptA=KCDogEvalCurve(curShoulder,p1,p2,actualPaw,dir0,dir1,curveLen,[seg[@"t0"] doubleValue]);
        CGPoint ptB=KCDogEvalCurve(curShoulder,p1,p2,actualPaw,dir0,dir1,curveLen,[seg[@"t1"] doubleValue]);
        CGPoint ptM=CGPointMake(.5*(ptA.x+ptB.x),.5*(ptA.y+ptB.y));
        double segDx=ptB.x-ptA.x,segDy=ptB.y-ptA.y;
        double chord=fmax(.5,hypot(segDx,segDy));
        double ang=atan2(segDy,segDx);
        double segLenScale=chord/([seg[@"span"] doubleValue]*restLen);

        CGContextSaveGState(ctx);
        CGContextTranslateCTM(ctx,ptM.x,ptM.y);
        CGContextRotateCTM(ctx,ang);
        CGContextScaleCTM(ctx,segLenScale,widScale);
        CGContextRotateCTM(ctx,-restAng);
        [self drawSprite:[NSString stringWithFormat:@"dog-seg-%@-%ld-%lu",sTag,(long)_state.catStyle,(unsigned long)i] alpha:1];
        CGContextRestoreGState(ctx);
    }

    double endAng=atan2(dir1.y,dir1.x);
    double dAng=endAng-restAng;
    while(dAng>M_PI)dAng-=2*M_PI;
    while(dAng<-M_PI)dAng+=2*M_PI;
    CGContextSaveGState(ctx);
    CGContextTranslateCTM(ctx,actualPaw.x,actualPaw.y);
    CGContextRotateCTM(ctx,dAng*.30);
    [self drawSprite:[NSString stringWithFormat:@"dog-paw-%@-%ld",sTag,(long)_state.catStyle] alpha:1];
    CGContextRestoreGState(ctx);
}
- (void)drawCat:(CGContextRef)ctx motion:(KCCatMotion)motion {
    BOOL catActive=fabs(motion.offsetX)>0.001 || fabs(motion.offsetY)>0.001 || fabs(motion.tiltDeg)>0.001 ||
                   fabs(motion.scaleX-1.)>0.001 || fabs(motion.scaleY-1.)>0.001;
    if(catActive){
        CGContextSaveGState(ctx);
        CGContextTranslateCTM(ctx,214.+motion.offsetX,236.+motion.offsetY);
        CGContextRotateCTM(ctx,motion.tiltDeg*M_PI/180.);
        CGContextScaleCTM(ctx,motion.scaleX,motion.scaleY);
        CGContextTranslateCTM(ctx,-214.,-236.);
    }
    NSDictionary *dogInfo=_dogs[[NSString stringWithFormat:@"%ld",(long)_state.catStyle]];
    double tailSpeed=dogInfo?[dogInfo[@"tailSpeed"] doubleValue]:(_state.catStyle<2?1.9:1.8);
    NSInteger phase=((NSInteger)floor(_state.time*tailSpeed/(2*M_PI)*8))%8;
    if(dogInfo){
        CGAffineTransform dogBodyTx=KCDogBodyTransform(_state.left,_state.right);
        CGContextSaveGState(ctx);CGContextConcatCTM(ctx,dogBodyTx);
        NSArray *tailPivotXY=dogInfo[@"tailPivot"];
        NSNumber *wagDegNum=dogInfo[@"wagDeg"];
        if(tailPivotXY.count==2 && wagDegNum){
            CGFloat px=[tailPivotXY[0] doubleValue], py=[tailPivotXY[1] doubleValue];
            double rad=sin(_state.time*tailSpeed)*[wagDegNum doubleValue]*M_PI/180.0;
            CGContextSaveGState(ctx);
            CGContextTranslateCTM(ctx,px,py);
            CGContextRotateCTM(ctx,rad);
            CGContextTranslateCTM(ctx,-px,-py);
            [self drawSprite:[NSString stringWithFormat:@"tail-%ld-0",(long)_state.catStyle] alpha:1];
            CGContextRestoreGState(ctx);
        }else{
            [self drawSprite:[NSString stringWithFormat:@"tail-%ld-%ld",(long)_state.catStyle,(long)phase] alpha:1];
        }
        [self drawSprite:[NSString stringWithFormat:@"body-%ld",(long)_state.catStyle] alpha:1];
        CGContextRestoreGState(ctx);
        [self drawDogArm:ctx left:NO paw:NSMakePoint(_state.right.x,_state.right.y) bodyTx:dogBodyTx];
        [self drawDogArm:ctx left:YES paw:NSMakePoint(_state.left.x,_state.left.y) bodyTx:dogBodyTx];
        CGContextSaveGState(ctx);CGContextConcatCTM(ctx,dogBodyTx);
        [self drawSprite:[NSString stringWithFormat:@"dog-ruff-%ld",(long)_state.catStyle] alpha:1];
        CGContextRestoreGState(ctx);
    }else{
        if(_state.catStyle<2){
            KCDrawCatTail(ctx,_state.catStyle,_state.time);
        }else{
            BOOL tabby=_state.catStyle==3;
            CGFloat px=tabby?187.0:249.0, py=tabby?184.0:207.0;
            double rad=sin(_state.time*1.8)*(tabby?2.1:3.0)*M_PI/180.0;
            CGContextSaveGState(ctx);
            CGContextTranslateCTM(ctx,px,py);
            CGContextRotateCTM(ctx,rad);
            CGContextTranslateCTM(ctx,-px,-py);
            [self drawSprite:[NSString stringWithFormat:@"tail-%ld-0",(long)_state.catStyle] alpha:1];
            CGContextRestoreGState(ctx);
        }
        double lean=kc_cat_lean(&_state),shear=(_state.catStyle<2?.282:.285)*lean;
        CGAffineTransform body=CGAffineTransformMake(1,0,shear,1,-233*shear,0);
        CGContextSaveGState(ctx);CGContextConcatCTM(ctx,body);
        [self drawSprite:[NSString stringWithFormat:@"body-%ld",(long)_state.catStyle] alpha:1];CGContextRestoreGState(ctx);
        KCDrawArm(ctx,_state.catStyle,NO,NSMakePoint(_state.right.x,_state.right.y),lean,_state.rightActivity);
        KCDrawArm(ctx,_state.catStyle,YES,NSMakePoint(_state.left.x,_state.left.y),lean,_state.leftActivity);
        if(_state.catStyle==3){CGContextSaveGState(ctx);CGContextConcatCTM(ctx,body);[self drawSprite:@"ruff-3" alpha:1];CGContextRestoreGState(ctx);}
    }
    KCHeadPose headPose=kc_cat_head_pose(&_state);
    CGContextSaveGState(ctx);CGContextTranslateCTM(ctx,headPose.position.x,headPose.position.y);
    CGContextRotateCTM(ctx,headPose.tiltDeg*M_PI/180.);
    NSInteger ix=(NSInteger)lround(kc_clamp(_state.gazeX,-1,1))+1, iy=(NSInteger)lround(kc_clamp(_state.gazeY,-1,1))+1;
    NSInteger blink=_state.blink>.80?2:(_state.blink>.30?1:0);
    [self drawSprite:[NSString stringWithFormat:@"head-%ld-%ld-%ld-%ld",(long)_state.catStyle,(long)ix,(long)iy,(long)blink] alpha:1];
    CGContextRestoreGState(ctx);
    [self drawPawWhip:ctx paw:CGPointMake(_state.left.x,_state.left.y) side:-1 whip:motion.leftWhip];
    [self drawPawWhip:ctx paw:CGPointMake(_state.right.x,_state.right.y) side:1 whip:motion.rightWhip];
    [self drawComboAura:ctx head:headPose motion:motion];
    if(catActive)CGContextRestoreGState(ctx);
}
@end

#ifndef KC_PET_ARMS_H
#define KC_PET_ARMS_H

#import <Cocoa/Cocoa.h>
#include <math.h>

// Scene coordinates are logical points in a flipped 480 x 320 view.
// The shoulders, curled forearms, toe shapes and tabby bands follow the Windows pose rig.
typedef struct {
    CGPoint shoulder, nearControl, farControl, paw;
    CGFloat bodyWidth;
    int side;
} KCArmCurve;

static inline CGFloat KCArmSmooth(CGFloat x) {
    x=fmax(0,fmin(1,x));
    return x*x*(3-2*x);
}
static inline CGPoint KCArmCenter(KCArmCurve curve,CGFloat t) {
    CGFloat s=1-t;
    return CGPointMake(s*s*s*curve.shoulder.x+3*s*s*t*curve.nearControl.x+3*s*t*t*curve.farControl.x+t*t*t*curve.paw.x,
                       s*s*s*curve.shoulder.y+3*s*s*t*curve.nearControl.y+3*s*t*t*curve.farControl.y+t*t*t*curve.paw.y);
}
static inline CGPoint KCArmNormal(KCArmCurve curve,CGFloat t,BOOL finiteDifference,CGFloat minLengthSquared) {
    CGFloat dx,dy,s=1-t;
    if(finiteDifference){
        CGPoint a=KCArmCenter(curve,fmax(0,t-.01)),b=KCArmCenter(curve,fmin(1,t+.01));
        dx=b.x-a.x;dy=b.y-a.y;
    }else{
        dx=(curve.nearControl.x-curve.shoulder.x)*(3*s*s)+(curve.farControl.x-curve.nearControl.x)*(6*s*t)+(curve.paw.x-curve.farControl.x)*(3*t*t);
        dy=(curve.nearControl.y-curve.shoulder.y)*(3*s*s)+(curve.farControl.y-curve.nearControl.y)*(6*s*t)+(curve.paw.y-curve.farControl.y)*(3*t*t);
    }
    CGFloat length=hypot(dx,dy);
    if(length*length<minLengthSquared)return CGPointMake(-1,0);
    return CGPointMake(-dy/length,dx/length);
}
static inline CGPoint KCArmOffset(CGPoint center,CGPoint n,CGFloat width) {
    return CGPointMake(center.x+n.x*width,center.y+n.y*width);
}
static KCArmCurve KCMakeArmCurve(NSInteger cat,BOOL left,CGPoint paw,CGFloat lean) {
    KCArmCurve c={0};c.side=left?-1:1;c.paw=paw;
    if(cat<2){
        CGFloat raised=KCArmSmooth((221-paw.y)/32);
        c.shoulder=CGPointMake(214+c.side*26-29*lean,140);
        CGFloat dx=paw.x-c.shoulder.x,dy=paw.y-c.shoulder.y;
        c.nearControl=CGPointMake(c.shoulder.x+dx*.25+c.side*(4+8*raised),c.shoulder.y+dy*.37);
        c.farControl=CGPointMake(c.shoulder.x+dx*.79+c.side*(4+20*raised),paw.y-15+raised*41);
    }else{
        c.bodyWidth=cat==2?.92:1.07;
        CGFloat top=cat==2?118:237-119*1.025;
        CGFloat bend=fmax(0,fmin(1,(228-paw.y)/44));
        c.shoulder=CGPointMake(214+c.side*25*c.bodyWidth-29*lean,top+23);
        CGFloat dx=paw.x-c.shoulder.x;
        c.nearControl=CGPointMake(c.shoulder.x+dx*.18+c.side*(3+7*bend),c.shoulder.y+10+bend*9);
        c.farControl=CGPointMake(paw.x-dx*.20+c.side*(2+12*bend),paw.y-16+31*bend);
    }
    return c;
}
static CGPathRef KCArmOutline(KCArmCurve c,NSInteger cat,CGFloat pawRadius) {
    const int n=cat<2?18:20;CGPoint back[21];
    CGMutablePathRef p=CGPathCreateMutable();
    for(int i=0;i<=n;i++){
        CGFloat t=i/(CGFloat)n,width;
        if(cat<2)width=(12.8+(pawRadius-12.8)*KCArmSmooth(t))*(.08+.92*KCArmSmooth(t/.19));
        else width=(12*c.bodyWidth*(1-t)+8.1*t)*(.15+.85*fmin(1,t/.16));
        CGPoint center=KCArmCenter(c,t),normal=KCArmNormal(c,t,cat<2,cat<2?.000001:.01);
        CGPoint first=KCArmOffset(center,normal,width);
        back[i]=KCArmOffset(center,normal,-width);
        if(!i)CGPathMoveToPoint(p,NULL,first.x,first.y);
        else CGPathAddLineToPoint(p,NULL,first.x,first.y);
    }
    for(int i=n;i>=0;i--) CGPathAddLineToPoint(p,NULL,back[i].x,back[i].y);
    CGPathCloseSubpath(p);return p;
}
static inline void KCArmFillColor(CGContextRef ctx,unsigned color) {
    CGContextSetRGBFillColor(ctx,((color>>16)&255)/255.,((color>>8)&255)/255.,(color&255)/255.,1);
}
static inline void KCArmStrokeColor(CGContextRef ctx,unsigned color,CGFloat alpha) {
    CGContextSetRGBStrokeColor(ctx,((color>>16)&255)/255.,((color>>8)&255)/255.,(color&255)/255.,alpha);
}
static CGGradientRef KCArmGradient(int index) {
    // These immutable swatches live as long as the main-thread WPF-equivalent scene.
    static CGGradientRef palette[5];
    static const unsigned colors[5][2]={{0x1e1c1f,0x18171a},{0xefaa67,0xdf9354},{0xf6eddc,0xf6ecda},{0x9e876a,0xb69d7a},{0xf5efdd,0xece3d1}};
    if(!palette[index]){
        CGFloat rgba[8];for(int i=0;i<2;i++){
            unsigned rgb=colors[index][i];
            rgba[i*4]=((rgb>>16)&255)/255.;rgba[i*4+1]=((rgb>>8)&255)/255.;rgba[i*4+2]=(rgb&255)/255.;rgba[i*4+3]=1;
        }
        CGColorSpaceRef space=CGColorSpaceCreateWithName(kCGColorSpaceSRGB);
        palette[index]=CGGradientCreateWithColorComponents(space,rgba,NULL,2);CGColorSpaceRelease(space);
    }
    return palette[index];
}
static void KCArmPaintPath(CGContextRef ctx,CGPathRef p,int index) {
    CGRect b=CGPathGetPathBoundingBox(p);if(b.size.width<=0 || b.size.height<=0)return;
    CGPoint a=index==0?CGPointMake(.2,0):index==1?CGPointMake(.22,0):CGPointMake(.3,.1);
    CGPoint z=index==0?CGPointMake(.9,1):index==1?CGPointMake(.82,1):CGPointMake(.85,1);
    CGContextSaveGState(ctx);CGContextAddPath(ctx,p);CGContextClip(ctx);
    CGContextTranslateCTM(ctx,b.origin.x,b.origin.y);CGContextScaleCTM(ctx,b.size.width,b.size.height);
    CGContextDrawLinearGradient(ctx,KCArmGradient(index),a,z,kCGGradientDrawsBeforeStartLocation|kCGGradientDrawsAfterEndLocation);
    CGContextRestoreGState(ctx);
}
static void KCGingerCuff(CGContextRef ctx,KCArmCurve c,CGPathRef limb) {
    CGFloat dx=c.paw.x-c.farControl.x,dy=c.paw.y-c.farControl.y,length=hypot(dx,dy);
    if(length*length<.001){dx=0;dy=1;}else{dx/=length;dy/=length;}
    CGContextSaveGState(ctx);CGContextAddPath(ctx,limb);CGContextClip(ctx);
    CGContextConcatCTM(ctx,CGAffineTransformMake(dy,-dx,dx,dy,c.paw.x,c.paw.y));
    KCArmFillColor(ctx,0xd08044);
    CGContextBeginPath(ctx);CGContextMoveToPoint(ctx,-15,-27);
    CGContextAddCurveToPoint(ctx,-7,-28,-3,-25,.5,-23.5);CGContextAddQuadCurveToPoint(ctx,-6,-24,-15,-21);
    CGContextClosePath(ctx);CGContextFillPath(ctx);
    KCArmFillColor(ctx,0xffe6c0);
    CGContextBeginPath(ctx);CGContextMoveToPoint(ctx,-9.2,1);CGContextAddCurveToPoint(ctx,-10,-4,-9.9,-9,-7.3,-10.7);
    CGContextAddQuadCurveToPoint(ctx,-5,-12,-2,-10.8);CGContextAddQuadCurveToPoint(ctx,1,-9.3,3.2,-10.7);
    CGContextAddQuadCurveToPoint(ctx,6.1,-12.5,8.3,-10.4);CGContextAddQuadCurveToPoint(ctx,10.3,-7,9.2,1);
    CGContextAddQuadCurveToPoint(ctx,7.2,8,0,9);CGContextAddQuadCurveToPoint(ctx,-7.7,7.9,-9.2,1);
    CGContextClosePath(ctx);CGContextFillPath(ctx);CGContextRestoreGState(ctx);
}
static CGPoint KCTabbyBandPoint(KCArmCurve c,CGFloat t,CGFloat across) {
    return KCArmOffset(KCArmCenter(c,t),KCArmNormal(c,t,NO,.001),across*c.side*(12*c.bodyWidth*(1-t)+8.1*t));
}
static void KCTabbyBand(CGContextRef ctx,KCArmCurve c,CGFloat t,CGFloat breadth,unsigned color) {
    CGPoint a=KCTabbyBandPoint(c,t-.035,-1.35),b=KCTabbyBandPoint(c,t-.040,-.73),e=KCTabbyBandPoint(c,t+.029,-.06),f=KCTabbyBandPoint(c,t+.010,.88);
    KCArmFillColor(ctx,color);CGContextBeginPath(ctx);CGContextMoveToPoint(ctx,a.x,a.y);
    CGContextAddCurveToPoint(ctx,b.x,b.y,e.x,e.y,f.x,f.y);
    b=KCTabbyBandPoint(c,t+.040,.56);e=KCTabbyBandPoint(c,t+.064,.03);f=KCTabbyBandPoint(c,t+breadth*.57,-.38);
    CGContextAddCurveToPoint(ctx,b.x,b.y,e.x,e.y,f.x,f.y);
    b=KCTabbyBandPoint(c,t+.018,-.83);e=KCTabbyBandPoint(c,t+.020,-1.30);
    CGContextAddQuadCurveToPoint(ctx,b.x,b.y,e.x,e.y);CGContextClosePath(ctx);CGContextFillPath(ctx);
}
static inline void KCTabbySock(CGContextRef ctx,CGPoint paw) {
    CGMutablePathRef p=CGPathCreateMutable();
    CGPathMoveToPoint(p,NULL,-8.5,-1);CGPathAddCurveToPoint(p,NULL,-8.9,-4.2,-6.6,-7.3,-3.6,-7.5);
    CGPathAddQuadCurveToPoint(p,NULL,-.8,-9.9,2,-8);CGPathAddQuadCurveToPoint(p,NULL,5,-8.8,7.1,-6.6);
    CGPathAddCurveToPoint(p,NULL,9.6,-4.5,10.2,1.6,8.3,4.6);CGPathAddQuadCurveToPoint(p,NULL,6,8.3,.6,7.1);
    CGPathAddQuadCurveToPoint(p,NULL,-4.3,8.7,-7.3,5.4);CGPathAddQuadCurveToPoint(p,NULL,-9.4,3.3,-8.5,-1);CGPathCloseSubpath(p);
    CGContextSaveGState(ctx);CGContextTranslateCTM(ctx,paw.x,paw.y);KCArmPaintPath(ctx,p,4);CGContextRestoreGState(ctx);
    CGPathRelease(p);
}
static void KCDrawArm(CGContextRef ctx,NSInteger cat,BOOL left,CGPoint paw,double lean,double activity) {
    CGFloat radius=8.4+fmax(0,fmin(1,activity));
    KCArmCurve c=KCMakeArmCurve(cat,left,paw,lean);
    CGPathRef limb=KCArmOutline(c,cat,radius);
    CGContextSaveGState(ctx);CGContextSetLineCap(ctx,kCGLineCapRound);
    KCArmPaintPath(ctx,limb,(int)cat);
    if(cat==3){
        CGContextSaveGState(ctx);CGContextAddPath(ctx,limb);CGContextClip(ctx);
        KCTabbyBand(ctx,c,.26,.100,0x715d47);KCTabbyBand(ctx,c,left?.47:.44,.117,0x6b5642);
        KCTabbyBand(ctx,c,left?.66:.64,.092,0x78654e);KCTabbyBand(ctx,c,.79,.063,0x88745a);
        CGContextRestoreGState(ctx);KCTabbySock(ctx,paw);
    }else{
        CGFloat rx=cat==2?8.65:radius+.35,ry=cat==2?7.4:radius*(.94-.06*activity);
        CGRect box=CGRectMake(paw.x-rx,paw.y-ry,rx*2,ry*2);
        if(cat==0){
            CGPathRef cap=CGPathCreateWithEllipseInRect(box,NULL);KCArmPaintPath(ctx,cap,0);CGPathRelease(cap);
            CGFloat raised=KCArmSmooth((221-paw.y)/32);
            if(raised>.1){
                KCArmStrokeColor(ctx,0x5c595b,raised*.85);CGContextSetLineWidth(ctx,.95);CGContextBeginPath(ctx);
                CGContextMoveToPoint(ctx,paw.x+c.side*7.5,paw.y+.7);
                CGContextAddCurveToPoint(ctx,paw.x+c.side*7.1,paw.y+6.2,paw.x+c.side*2.7,paw.y+8.2,paw.x-c.side*2.3,paw.y+7.7);
                CGContextStrokePath(ctx);
            }
        }else{
            if(cat==1)KCGingerCuff(ctx,c,limb);
            KCArmFillColor(ctx,cat==1?0xffe6c0:0xf4e8d6);CGContextFillEllipseInRect(ctx,box);
        }
    }
    if(cat>=2 && activity<.25){
        KCArmStrokeColor(ctx,cat==2?0xeaddcb:0xbfb6a6,1);CGContextSetLineWidth(ctx,.65);
        CGContextBeginPath(ctx);CGContextMoveToPoint(ctx,paw.x-1.8,paw.y+3.5);CGContextAddLineToPoint(ctx,paw.x-1.9,paw.y+5.9);CGContextStrokePath(ctx);
    }
    CGContextRestoreGState(ctx);CGPathRelease(limb);
}
#endif

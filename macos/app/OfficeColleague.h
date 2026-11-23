#ifndef KC_OFFICE_COLLEAGUE_H
#define KC_OFFICE_COLLEAGUE_H
#import <Cocoa/Cocoa.h>
#include <math.h>

// Shared scene coordinates and sleeve construction are mirrored in OfficeColleagueArt.cs.
static CGRect KCOfficeBounds(NSImage *image) {
    CGFloat width=196*image.size.width/image.size.height;
    return CGRectMake(214-width/2,46,width,196);
}
static CGAffineTransform KCOfficePose(double t,double gx,double gy,double lean,double blink) {
    double shear=.078*lean-.008*gx,stretch=(sin(t*1.7)*.38+gy*.35-blink*1.1)/196;
    return CGAffineTransformMake(1,0,shear,1+stretch,-shear*242,-stretch*242);
}
static BOOL KCOfficeHitsHead(NSImage *image,CGPoint p,CGAffineTransform pose) {
    CGRect body=KCOfficeBounds(image);
    p=CGPointApplyAffineTransform(p,CGAffineTransformInvert(pose));
    double x=(p.x-214)/(body.size.width*.43),y=(p.y-46-196*.32)/(196*.32);
    return x*x+y*y<1;
}
static CGPoint OCShift(CGPoint p,CGPoint vector,CGFloat scale) {
    return CGPointMake(p.x+vector.x*scale,p.y+vector.y*scale);
}
static CGPoint OCVector(CGPoint a,CGPoint b) { return CGPointMake(a.x-b.x,a.y-b.y); }
static CGPoint OCUnit(CGPoint v) {
    double length=hypot(v.x,v.y);
    return length>.001?CGPointMake(v.x/length,v.y/length):CGPointMake(0,1);
}
static CGPoint OCNormal(CGPoint dir) { return CGPointMake(-dir.y,dir.x); }
static void OCMove(CGMutablePathRef path,CGPoint p) { CGPathMoveToPoint(path,NULL,p.x,p.y); }
static void OCLine(CGMutablePathRef path,CGPoint p) { CGPathAddLineToPoint(path,NULL,p.x,p.y); }
static void OCCurve(CGMutablePathRef path,CGPoint a,CGPoint b,CGPoint c) {
    CGPathAddCurveToPoint(path,NULL,a.x,a.y,b.x,b.y,c.x,c.y);
}
static void OCQuad(CGMutablePathRef path,CGPoint a,CGPoint b) { CGPathAddQuadCurveToPoint(path,NULL,a.x,a.y,b.x,b.y); }
static void OCStrokeColor(CGContextRef ctx,unsigned rgb) {
    CGContextSetRGBStrokeColor(ctx,((rgb>>16)&255)/255.,((rgb>>8)&255)/255.,(rgb&255)/255.,1);
}
typedef enum { OCSuit, OCSkin, OCShirt } OCBrush;
static CGGradientRef OCGradient(OCBrush brush) {
    static CGGradientRef cache[3];
    static const unsigned colors[][2]={{0x393b4a,0x30323f},{0xffdbb6,0xffc49c},{0xfffffa,0xdbdce0}};
    if(!cache[brush]) {
        CGFloat channels[8];
        for(int i=0;i<2;i++) {
            unsigned rgb=colors[brush][i];
            channels[4*i]=((rgb>>16)&255)/255.;channels[4*i+1]=((rgb>>8)&255)/255.;
            channels[4*i+2]=(rgb&255)/255.;channels[4*i+3]=1;
        }
        CGColorSpaceRef space=CGColorSpaceCreateWithName(kCGColorSpaceSRGB);
        cache[brush]=CGGradientCreateWithColorComponents(space,channels,NULL,2);
        CGColorSpaceRelease(space);
    }
    return cache[brush];
}
static void OCPaint(CGContextRef ctx,CGPathRef path,OCBrush brush,unsigned edge,CGFloat lineWidth) {
    CGRect b=CGPathGetPathBoundingBox(path);
    CGContextSaveGState(ctx);
    CGContextAddPath(ctx,path);CGContextClip(ctx);
    CGContextTranslateCTM(ctx,b.origin.x,b.origin.y);CGContextScaleCTM(ctx,b.size.width,b.size.height);
    CGContextDrawLinearGradient(ctx,OCGradient(brush),CGPointMake(.1,.1),CGPointMake(.9,.85),
                                kCGGradientDrawsBeforeStartLocation|kCGGradientDrawsAfterEndLocation);
    CGContextRestoreGState(ctx);
    if(lineWidth>0) {
        OCStrokeColor(ctx,edge);CGContextSetLineWidth(ctx,lineWidth);
        CGContextAddPath(ctx,path);CGContextStrokePath(ctx);
    }
}
static CGPathRef OCHand(void) {
    static CGMutablePathRef p;
    if(!p) {
        p=CGPathCreateMutable();
        CGPathMoveToPoint(p,NULL,-4.3,-5.2);
        CGPathAddCurveToPoint(p,NULL,-4.7,-3.3,-5.1,-2,-5.4,-.1);
        CGPathAddCurveToPoint(p,NULL,-5.6,1.4,-5.4,3.7,-5.1,4.9);
        CGPathAddCurveToPoint(p,NULL,-4.8,6,-3.7,6,-3.4,4.9);
        CGPathAddLineToPoint(p,NULL,-3.1,3.5);CGPathAddLineToPoint(p,NULL,-3,6.1);
        CGPathAddCurveToPoint(p,NULL,-2.9,7.4,-1.2,7.7,-.9,6.3);
        CGPathAddLineToPoint(p,NULL,-.7,4.1);CGPathAddLineToPoint(p,NULL,-.4,6.8);
        CGPathAddCurveToPoint(p,NULL,-.2,8.3,1.5,8.2,1.7,6.8);
        CGPathAddLineToPoint(p,NULL,1.9,4.1);CGPathAddLineToPoint(p,NULL,2.2,6.4);
        CGPathAddCurveToPoint(p,NULL,2.4,7.8,4,7.5,4.1,6.1);CGPathAddLineToPoint(p,NULL,4.5,1.6);
        CGPathAddQuadCurveToPoint(p,NULL,4.6,.3,5.1,1);CGPathAddQuadCurveToPoint(p,NULL,5.4,2,6.4,2.1);
        CGPathAddQuadCurveToPoint(p,NULL,7.9,1.9,7.2,.1);CGPathAddLineToPoint(p,NULL,5.8,-2.7);
        CGPathAddQuadCurveToPoint(p,NULL,5.2,-4.1,3.8,-5.2);
        CGPathAddLineToPoint(p,NULL,3.3,-7.8);CGPathAddLineToPoint(p,NULL,-3.5,-7.7);CGPathCloseSubpath(p);
    }
    return p;
}
static void KCOfficeArm(CGContextRef ctx,CGPoint shoulder,CGPoint target,int side) {
    const double handScale=1.30;
    double radians=fmax(-31,fmin(31,-(target.x-shoulder.x)*.34))*M_PI/180;
    CGPoint down=CGPointMake(-sin(radians),cos(radians));
    CGPoint palm=OCShift(target,down,-5.5*handScale),wrist=OCShift(palm,down,-7.4*handScale);
    CGPoint reach=OCVector(wrist,shoulder);
    double bend=fmax(8,fmin(20,29-hypot(reach.x,reach.y)*.15));
    CGPoint elbow=OCShift(OCShift(shoulder,reach,.54),OCNormal(OCUnit(reach)),-side*bend);
    CGPoint upper=OCUnit(OCVector(elbow,shoulder)),lower=OCUnit(OCVector(wrist,elbow));
    CGPoint a=OCNormal(upper),b=OCNormal(OCUnit(OCShift(upper,lower,1))),c=OCNormal(lower);
    CGPoint end=OCShift(wrist,lower,-3.6);
    double radius=fmin(13,fmin(hypot(elbow.x-shoulder.x,elbow.y-shoulder.y),hypot(end.x-elbow.x,end.y-elbow.y))*.38);
    CGPoint before=OCShift(elbow,upper,-radius),after=OCShift(elbow,lower,radius);
    double jointWidth=8.5/fmax(.5,a.x*b.x+a.y*b.y);
    CGMutablePathRef sleeve=CGPathCreateMutable();
    OCMove(sleeve,OCShift(shoulder,a,8));
    OCCurve(sleeve,OCShift(OCShift(shoulder,upper,9),a,9.8),OCShift(OCShift(before,upper,-4),a,8.5),OCShift(before,a,8.5));
    OCQuad(sleeve,OCShift(elbow,b,jointWidth),OCShift(after,c,8.5));
    OCCurve(sleeve,OCShift(OCShift(after,lower,4),c,8.5),OCShift(OCShift(end,lower,-6),c,6.2),OCShift(end,c,5.7));
    OCLine(sleeve,OCShift(end,c,-5.7));
    OCCurve(sleeve,OCShift(OCShift(end,lower,-6),c,-6.2),OCShift(OCShift(after,lower,4),c,-8.5),OCShift(after,c,-8.5));
    OCQuad(sleeve,OCShift(elbow,b,-jointWidth),OCShift(before,a,-8.5));
    OCCurve(sleeve,OCShift(OCShift(before,upper,-4),a,-8.5),OCShift(OCShift(shoulder,upper,9),a,-9.8),OCShift(shoulder,a,-8));
    OCCurve(sleeve,OCShift(OCShift(shoulder,a,-8),upper,-9.3),OCShift(OCShift(shoulder,a,8),upper,-9.3),OCShift(shoulder,a,8));
    CGPathCloseSubpath(sleeve);
    CGContextSaveGState(ctx);CGContextSetLineCap(ctx,kCGLineCapRound);CGContextSetLineJoin(ctx,kCGLineJoinRound);
    OCPaint(ctx,sleeve,OCSuit,0x292a36,.72);CGPathRelease(sleeve);
    CGPoint start=OCShift(elbow,b,side*6.7),finish=OCShift(OCShift(elbow,lower,5),b,side*2.2);
    OCStrokeColor(ctx,0x4c4e5d);CGContextSetLineWidth(ctx,.72);
    CGContextMoveToPoint(ctx,start.x,start.y);CGContextAddLineToPoint(ctx,finish.x,finish.y);CGContextStrokePath(ctx);
    CGPoint handNormal=OCNormal(down),cuffEdge=OCShift(palm,down,-6.5*handScale);
    CGMutablePathRef cuff=CGPathCreateMutable();
    OCMove(cuff,OCShift(end,c,5));OCLine(cuff,OCShift(end,c,-5));
    OCLine(cuff,OCShift(cuffEdge,handNormal,-4*handScale));OCLine(cuff,OCShift(cuffEdge,handNormal,4*handScale));CGPathCloseSubpath(cuff);
    OCPaint(ctx,cuff,OCShirt,0,0);CGPathRelease(cuff);
    CGContextTranslateCTM(ctx,palm.x,palm.y);CGContextRotateCTM(ctx,radians);CGContextScaleCTM(ctx,-side*handScale,handScale);
    OCPaint(ctx,OCHand(),OCSkin,0xe89d70,.62);CGContextRestoreGState(ctx);
}
static void KCOfficeDraw(CGContextRef ctx,NSImage *image,CGAffineTransform pose,CGPoint left,CGPoint right) {
    CGContextSaveGState(ctx);CGContextConcatCTM(ctx,pose);
    [image drawInRect:KCOfficeBounds(image) fromRect:NSZeroRect operation:NSCompositingOperationSourceOver
             fraction:1 respectFlipped:YES hints:nil];
    CGContextRestoreGState(ctx);
    KCOfficeArm(ctx,CGPointApplyAffineTransform(CGPointMake(246,172),pose),right,1);
    KCOfficeArm(ctx,CGPointApplyAffineTransform(CGPointMake(182,172),pose),left,-1);
}
#endif

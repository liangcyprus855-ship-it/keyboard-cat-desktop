#ifndef KC_OFFICE_COLLEAGUE_H
#define KC_OFFICE_COLLEAGUE_H
#import <Cocoa/Cocoa.h>
#include <math.h>
#include "keycat_core.h"

// Bounds, head cutoffs and clothing colors are shared with the Windows renderer.
typedef struct {
    NSInteger index;
    CGFloat centerX,top,height,leftShoulder,rightShoulder,shoulderY,solidHead,bottomHead;
    unsigned sleeveLight,sleeveDark,sleeveEdge,crease,skinLight,skinDark,skinEdge,cuffLight,cuffDark;
} KCPersonStyle;
static NSDictionary *KCPersonTable(void) {
    NSURL *url=[NSBundle.mainBundle URLForResource:@"styles" withExtension:@"tsv" subdirectory:@"characters"];
    NSString *file=url?[NSString stringWithContentsOfURL:url encoding:NSUTF8StringEncoding error:NULL]:nil;
    NSArray *lines=[file componentsSeparatedByCharactersInSet:NSCharacterSet.newlineCharacterSet];
    NSArray *keys=[lines.firstObject componentsSeparatedByString:@"\t"];
    NSMutableDictionary *styles=[NSMutableDictionary dictionary];
    for(NSString *line in lines) {
        if(!line.length || [line isEqual:lines.firstObject])continue;
        NSArray *fields=[line componentsSeparatedByString:@"\t"];
        if(fields.count==keys.count && fields.count)
            styles[fields[0]]=[NSDictionary dictionaryWithObjects:fields forKeys:keys];
    }
    return styles;
}
static unsigned OCColor(NSString *hex) {
    unsigned value=0;
    [[NSScanner scannerWithString:hex ?: @""] scanHexInt:&value];
    return value;
}
static KCPersonStyle KCOfficeStyle(NSInteger index,NSDictionary *row) {
    KCPersonStyle s={index};
    s.centerX=[row[@"centerX"] doubleValue];s.top=[row[@"top"] doubleValue];s.height=[row[@"height"] doubleValue];
    s.leftShoulder=[row[@"leftShoulder"] doubleValue];s.rightShoulder=[row[@"rightShoulder"] doubleValue];
    s.shoulderY=[row[@"shoulderY"] doubleValue];s.solidHead=[row[@"solidHead"] doubleValue];s.bottomHead=[row[@"bottomHead"] doubleValue];
    s.sleeveLight=OCColor(row[@"sleeveLight"]);s.sleeveDark=OCColor(row[@"sleeveDark"]);
    s.sleeveEdge=OCColor(row[@"sleeveEdge"]);s.crease=OCColor(row[@"crease"]);
    s.skinLight=OCColor(row[@"skinLight"]);s.skinDark=OCColor(row[@"skinDark"]);s.skinEdge=OCColor(row[@"skinEdge"]);
    s.cuffLight=OCColor(row[@"cuffLight"]);s.cuffDark=OCColor(row[@"cuffDark"]);
    return s;
}
static CGRect KCOfficeBounds(NSImage *image,KCPersonStyle style) {
    if(!image || image.size.height<=0)return CGRectZero;
    CGFloat width=style.height*image.size.width/image.size.height;
    return CGRectMake(style.centerX-width/2,style.top,width,style.height);
}
static NSData *KCOfficeHeadMask(NSImage *image,KCPersonStyle style,size_t *width,size_t *height) {
    CGImageRef original=[image CGImageForProposedRect:NULL context:nil hints:nil];
    if(!original)return nil;
    *width=CGImageGetWidth(original);*height=CGImageGetHeight(original);
    CGColorSpaceRef space=CGColorSpaceCreateWithName(kCGColorSpaceSRGB);
    NSMutableData *rgba=[NSMutableData dataWithLength:*width * *height * 4];
    CGContextRef context=CGBitmapContextCreate(rgba.mutableBytes,*width,*height,8,4 * *width,space,
                                               kCGBitmapByteOrder32Big|kCGImageAlphaPremultipliedLast);
    CGColorSpaceRelease(space);
    if(!context)return nil;
    CGContextDrawImage(context,CGRectMake(0,0,*width,*height),original);
    CGContextRelease(context);
    if(style.bottomHead<=0 || style.bottomHead>1)return nil;
    size_t rows=(size_t)ceil(style.bottomHead * *height);
    NSMutableData *mask=[NSMutableData dataWithLength:rows * *width];
    const uint8_t *pixels=rgba.bytes;
    uint8_t *out=mask.mutableBytes;
    for(size_t y=0;y<rows;y++)for(size_t x=0;x<*width;x++) {
        size_t i=y * *width+x;
        // At the chin and hair tips, distinguish warm skin and hair from the cool jacket.
        out[i]=pixels[4*i+3]>=32 && (y < style.solidHead * *height || pixels[4*i]>pixels[4*i+2]);
    }
    return mask;
}
static BOOL KCOfficeHitsHead(CGRect body,KCPersonStyle style,NSData *mask,size_t width,size_t height,CGPoint p) {
    if(!mask || !CGRectContainsPoint(body,p) || p.y-body.origin.y>=body.size.height*style.bottomHead)return NO;
    size_t px=(size_t)((p.x-body.origin.x)*width/body.size.width);
    size_t py=(size_t)((p.y-body.origin.y)*height/body.size.height);
    return px<width && py*width+px<mask.length && ((const uint8_t *)mask.bytes)[py*width+px]!=0;
}
static NSImage *KCOfficeHitBacking(NSData *mask,size_t width) {
    if(!width || !mask.length)return nil;
    size_t rows=mask.length/width;
    NSBitmapImageRep *rep=[[NSBitmapImageRep alloc] initWithBitmapDataPlanes:NULL pixelsWide:width pixelsHigh:rows
        bitsPerSample:8 samplesPerPixel:4 hasAlpha:YES isPlanar:NO colorSpaceName:NSDeviceRGBColorSpace
        bytesPerRow:width*4 bitsPerPixel:32];
    if(!rep.bitmapData)return nil;
    memset(rep.bitmapData,0,rows*rep.bytesPerRow);
    const uint8_t *hit=mask.bytes;
    for(size_t i=0;i<mask.length;i++)rep.bitmapData[i*4+3]=hit[i]; // 0 or 1 out of 255.
    NSImage *backing=[[NSImage alloc] initWithSize:NSMakeSize(width,rows)];
    [backing addRepresentation:rep];return backing;
}
static void OCMove(CGMutablePathRef path,KCPoint p) { CGPathMoveToPoint(path,NULL,p.x,p.y); }
static void OCLine(CGMutablePathRef path,KCPoint p) { CGPathAddLineToPoint(path,NULL,p.x,p.y); }
static void OCCurve(CGMutablePathRef path,KCPoint a,KCPoint b,KCPoint c) {
    CGPathAddCurveToPoint(path,NULL,a.x,a.y,b.x,b.y,c.x,c.y);
}
static void OCQuad(CGMutablePathRef path,KCPoint a,KCPoint b) { CGPathAddQuadCurveToPoint(path,NULL,a.x,a.y,b.x,b.y); }
static void OCStrokeColor(CGContextRef ctx,unsigned rgb) {
    CGContextSetRGBStrokeColor(ctx,((rgb>>16)&255)/255.,((rgb>>8)&255)/255.,(rgb&255)/255.,1);
}
typedef enum { OCSuit, OCSkin, OCShirt } OCBrush;
static CGGradientRef OCGradient(OCBrush brush,KCPersonStyle style) {
    static CGGradientRef cache[10][3];
    CGGradientRef *entry=&cache[style.index][brush];
    if(!*entry) {
        const unsigned colors[][2]={{style.sleeveLight,style.sleeveDark},{style.skinLight,style.skinDark},{style.cuffLight,style.cuffDark}};
        CGFloat channels[8];
        for(int i=0;i<2;i++) {
            unsigned rgb=colors[brush][i];
            channels[4*i]=((rgb>>16)&255)/255.;channels[4*i+1]=((rgb>>8)&255)/255.;
            channels[4*i+2]=(rgb&255)/255.;channels[4*i+3]=1;
        }
        CGColorSpaceRef space=CGColorSpaceCreateWithName(kCGColorSpaceSRGB);
        *entry=CGGradientCreateWithColorComponents(space,channels,NULL,2);
        CGColorSpaceRelease(space);
    }
    return *entry;
}
static void OCPaint(CGContextRef ctx,CGPathRef path,KCPersonStyle style,OCBrush brush,unsigned edge,CGFloat lineWidth) {
    CGRect b=CGPathGetPathBoundingBox(path);
    CGContextSaveGState(ctx);
    CGContextAddPath(ctx,path);CGContextClip(ctx);
    CGContextTranslateCTM(ctx,b.origin.x,b.origin.y);CGContextScaleCTM(ctx,b.size.width,b.size.height);
    CGContextDrawLinearGradient(ctx,OCGradient(brush,style),CGPointMake(.1,.1),CGPointMake(.9,.85),
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
        CGPathMoveToPoint(p,NULL,-3.6,-7.5);
        CGPathAddCurveToPoint(p,NULL,-4,-4.1,-5.7,-2.4,-5.6,.8);
        CGPathAddCurveToPoint(p,NULL,-5.6,3.1,-5.1,5.1,-4,5.3);
        CGPathAddQuadCurveToPoint(p,NULL,-3.2,5.5,-2.9,4.4);
        CGPathAddQuadCurveToPoint(p,NULL,-2.6,7,-1.2,7);CGPathAddQuadCurveToPoint(p,NULL,-.2,7,.1,5.3);
        CGPathAddQuadCurveToPoint(p,NULL,.5,7.3,1.7,7.1);CGPathAddQuadCurveToPoint(p,NULL,3,6.9,3.2,4.5);
        CGPathAddQuadCurveToPoint(p,NULL,4.5,5.7,5.1,4);CGPathAddLineToPoint(p,NULL,5.5,.9);
        CGPathAddQuadCurveToPoint(p,NULL,6.8,2.5,7.6,1.4);CGPathAddQuadCurveToPoint(p,NULL,8.3,.3,6.9,-1.7);
        CGPathAddLineToPoint(p,NULL,4,-5.1);CGPathAddLineToPoint(p,NULL,3.5,-7.5);CGPathCloseSubpath(p);
    }
    return p;
}
static void KCOfficeDrawHand(CGContextRef ctx,KCPersonStyle style,KCOfficeArmGeometry arm) {
    CGContextSaveGState(ctx);
    CGContextTranslateCTM(ctx,arm.palm.x,arm.palm.y);
    CGContextRotateCTM(ctx,arm.handAngleDeg*M_PI/180.);
    CGContextScaleCTM(ctx,arm.handScaleX,arm.handScaleY);
    OCPaint(ctx,OCHand(),style,OCSkin,style.skinEdge,.62);
    CGContextRestoreGState(ctx);
}
static void KCOfficeHandAt(CGContextRef ctx,KCPersonStyle style,CGPoint shoulder,CGPoint target,int side) {
    KCOfficeArmGeometry arm=kc_solve_office_arm(kc_pt(shoulder.x,shoulder.y),kc_pt(target.x,target.y),side,0);
    KCOfficeDrawHand(ctx,style,arm);
}
static void KCOfficeArm(CGContextRef ctx,KCPersonStyle style,CGPoint shoulder,CGPoint target,int side,BOOL resting) {
    KCOfficeArmGeometry arm=kc_solve_office_arm(kc_pt(shoulder.x,shoulder.y),kc_pt(target.x,target.y),side,resting?1:0);
    CGMutablePathRef sleeve=CGPathCreateMutable();
    OCMove(sleeve,arm.sleeveStart);
    OCCurve(sleeve,arm.c1a,arm.c1b,arm.before);
    OCQuad(sleeve,arm.elbowJointOuter,arm.after);
    OCCurve(sleeve,arm.c2a,arm.c2b,arm.endOuter);
    OCLine(sleeve,arm.endInner);
    OCCurve(sleeve,arm.c3a,arm.c3b,arm.afterInner);
    OCQuad(sleeve,arm.elbowJointInner,arm.beforeInner);
    OCCurve(sleeve,arm.c4a,arm.c4b,arm.shoulderInner);
    OCCurve(sleeve,arm.c5a,arm.c5b,arm.sleeveStart);
    CGPathCloseSubpath(sleeve);
    CGContextSaveGState(ctx);CGContextSetLineCap(ctx,kCGLineCapRound);CGContextSetLineJoin(ctx,kCGLineJoinRound);
    OCPaint(ctx,sleeve,style,OCSuit,0,0);CGPathRelease(sleeve);
    OCStrokeColor(ctx,style.crease);CGContextSetLineWidth(ctx,.72);
    CGContextMoveToPoint(ctx,arm.creaseStart.x,arm.creaseStart.y);
    CGContextAddLineToPoint(ctx,arm.creaseEnd.x,arm.creaseEnd.y);
    CGContextStrokePath(ctx);
    CGMutablePathRef cuff=CGPathCreateMutable();
    OCMove(cuff,arm.cuff0);OCLine(cuff,arm.cuff1);
    OCLine(cuff,arm.cuff2);OCLine(cuff,arm.cuff3);
    CGPathCloseSubpath(cuff);
    OCPaint(ctx,cuff,style,OCShirt,0,0);CGPathRelease(cuff);
    OCStrokeColor(ctx,style.sleeveEdge);CGContextSetLineWidth(ctx,.72);
    CGContextMoveToPoint(ctx,arm.edgeStart.x,arm.edgeStart.y);
    CGContextAddLineToPoint(ctx,arm.edgeEnd.x,arm.edgeEnd.y);
    CGContextStrokePath(ctx);
    KCOfficeDrawHand(ctx,style,arm);
    CGContextRestoreGState(ctx);
}
#endif

#ifndef KC_OFFICE_COLLEAGUE_H
#define KC_OFFICE_COLLEAGUE_H
#import <Cocoa/Cocoa.h>
#include <math.h>

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
static CGGradientRef OCGradient(OCBrush brush,KCPersonStyle style) {
    static CGGradientRef cache[5][3];
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
static void KCOfficeArm(CGContextRef ctx,KCPersonStyle style,CGPoint shoulder,CGPoint target,int side) {
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
    OCPaint(ctx,sleeve,style,OCSuit,style.sleeveEdge,.72);CGPathRelease(sleeve);
    CGPoint start=OCShift(elbow,b,side*6.7),finish=OCShift(OCShift(elbow,lower,5),b,side*2.2);
    OCStrokeColor(ctx,style.crease);CGContextSetLineWidth(ctx,.72);
    CGContextMoveToPoint(ctx,start.x,start.y);CGContextAddLineToPoint(ctx,finish.x,finish.y);CGContextStrokePath(ctx);
    CGPoint handNormal=OCNormal(down),cuffEdge=OCShift(palm,down,-6.5*handScale);
    CGMutablePathRef cuff=CGPathCreateMutable();
    OCMove(cuff,OCShift(end,c,5));OCLine(cuff,OCShift(end,c,-5));
    OCLine(cuff,OCShift(cuffEdge,handNormal,-4*handScale));OCLine(cuff,OCShift(cuffEdge,handNormal,4*handScale));CGPathCloseSubpath(cuff);
    OCPaint(ctx,cuff,style,OCShirt,0,0);CGPathRelease(cuff);
    CGContextTranslateCTM(ctx,palm.x,palm.y);CGContextRotateCTM(ctx,radians);CGContextScaleCTM(ctx,-side*handScale,handScale);
    OCPaint(ctx,OCHand(),style,OCSkin,style.skinEdge,.62);CGContextRestoreGState(ctx);
}
#endif

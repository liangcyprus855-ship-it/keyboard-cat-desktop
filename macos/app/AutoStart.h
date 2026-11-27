#import <Cocoa/Cocoa.h>

@interface KCAutoStart : NSObject
+ (BOOL)isEnabled;
+ (BOOL)setEnabled:(BOOL)enable error:(NSError **)error;
+ (NSURL *)plistURLInDirectory:(NSURL *)directory bundleIdentifier:(NSString *)bundleID;
+ (BOOL)isEnabledInDirectory:(NSURL *)directory bundleIdentifier:(NSString *)bundleID executablePath:(NSString *)executablePath;
+ (BOOL)setEnabled:(BOOL)enable inDirectory:(NSURL *)directory bundleIdentifier:(NSString *)bundleID executablePath:(NSString *)executablePath error:(NSError **)error;
@end

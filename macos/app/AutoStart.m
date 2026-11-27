#import "AutoStart.h"

@implementation KCAutoStart

+ (NSURL *)defaultLaunchAgentsDirectory {
    NSArray<NSURL *> *urls = [NSFileManager.defaultManager URLsForDirectory:NSLibraryDirectory inDomains:NSUserDomainMask];
    NSURL *library = urls.firstObject ?: [NSURL fileURLWithPath:[NSHomeDirectory() stringByAppendingPathComponent:@"Library"] isDirectory:YES];
    return [library URLByAppendingPathComponent:@"LaunchAgents" isDirectory:YES];
}

+ (NSString *)defaultBundleID {
    return NSBundle.mainBundle.bundleIdentifier ?: @"local.keycat.desktop";
}

+ (NSString *)defaultExecutablePath {
    return NSBundle.mainBundle.executablePath ?: NSProcessInfo.processInfo.arguments.firstObject;
}

+ (NSURL *)plistURLInDirectory:(NSURL *)directory bundleIdentifier:(NSString *)bundleID {
    NSString *name = [NSString stringWithFormat:@"%@.plist", bundleID.length ? bundleID : @"local.keycat.desktop"];
    return [directory URLByAppendingPathComponent:name isDirectory:NO];
}

+ (BOOL)isEnabled {
    return [self isEnabledInDirectory:[self defaultLaunchAgentsDirectory]
                     bundleIdentifier:[self defaultBundleID]
                       executablePath:[self defaultExecutablePath]];
}

+ (BOOL)setEnabled:(BOOL)enable error:(NSError **)error {
    return [self setEnabled:enable
                inDirectory:[self defaultLaunchAgentsDirectory]
           bundleIdentifier:[self defaultBundleID]
             executablePath:[self defaultExecutablePath]
                      error:error];
}

+ (BOOL)isEnabledInDirectory:(NSURL *)directory bundleIdentifier:(NSString *)bundleID executablePath:(NSString *)executablePath {
    NSURL *plistURL = [self plistURLInDirectory:directory bundleIdentifier:bundleID];
    NSDictionary *dict = [NSDictionary dictionaryWithContentsOfURL:plistURL];
    if (![dict isKindOfClass:NSDictionary.class]) return NO;
    if (![dict[@"RunAtLoad"] boolValue]) return NO;
    NSArray *args = dict[@"ProgramArguments"];
    if (![args isKindOfClass:NSArray.class] || args.count == 0) return NO;
    NSString *stored = [args.firstObject isKindOfClass:NSString.class] ? [args.firstObject stringByStandardizingPath] : nil;
    NSString *expected = executablePath.stringByStandardizingPath;
    return stored.length > 0 && expected.length > 0 && [stored isEqualToString:expected];
}

+ (BOOL)setEnabled:(BOOL)enable inDirectory:(NSURL *)directory bundleIdentifier:(NSString *)bundleID executablePath:(NSString *)executablePath error:(NSError **)error {
    NSURL *plistURL = [self plistURLInDirectory:directory bundleIdentifier:bundleID];
    NSFileManager *fm = NSFileManager.defaultManager;
    if (!enable) {
        if (![fm fileExistsAtPath:plistURL.path]) return YES;
        return [fm removeItemAtURL:plistURL error:error];
    }
    if (!executablePath.length) {
        if (error) *error = [NSError errorWithDomain:@"KCAutoStart" code:1 userInfo:@{NSLocalizedDescriptionKey:@"无法定位当前应用可执行文件路径。"}];
        return NO;
    }
    if (![fm createDirectoryAtURL:directory withIntermediateDirectories:YES attributes:nil error:error]) {
        return NO;
    }
    NSDictionary *plist = @{
        @"Label": bundleID.length ? bundleID : @"local.keycat.desktop",
        @"ProgramArguments": @[executablePath.stringByStandardizingPath],
        @"RunAtLoad": @YES,
        @"KeepAlive": @NO,
        @"LimitLoadToSessionType": @"Aqua"
    };
    NSData *data = [NSPropertyListSerialization dataWithPropertyList:plist format:NSPropertyListXMLFormat_v1_0 options:0 error:error];
    if (!data) return NO;
    return [data writeToURL:plistURL options:NSDataWritingAtomic error:error];
}

@end

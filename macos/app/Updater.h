#import <Cocoa/Cocoa.h>

@interface KCGitHubAsset : NSObject
@property(copy) NSString *name;
@property(copy) NSString *downloadURL;
@property long long size;
@end

@interface KCGitHubRelease : NSObject
@property(copy) NSString *tagName;
@property(copy) NSString *name;
@property(copy) NSString *body;
@property(copy) NSString *htmlURL;
@property BOOL draft;
@property BOOL prerelease;
@property(strong) NSArray<KCGitHubAsset *> *assets;
@end

@interface KCUpdater : NSObject
+ (NSString *)latestReleaseAPIURL;
+ (NSString *)releasesPageURL;
+ (BOOL)parseVersion:(NSString *)text major:(int *)major minor:(int *)minor patch:(int *)patch;
+ (BOOL)isNewerVersion:(NSString *)remoteTag current:(NSString *)currentVersion;
+ (KCGitHubRelease *)parseReleaseJSON:(NSData *)data;
+ (KCGitHubAsset *)selectMacAsset:(KCGitHubRelease *)release;
+ (KCGitHubAsset *)selectChecksumAsset:(KCGitHubRelease *)release;
+ (BOOL)verifySHA256OfFile:(NSURL *)fileURL manifest:(NSString *)manifest expectedFileName:(NSString *)fileName;
+ (void)fetchLatestReleaseWithCompletion:(void (^)(KCGitHubRelease *release, NSError *error))completion;
+ (void)downloadAsset:(KCGitHubAsset *)asset checksumAsset:(KCGitHubAsset *)checksumAsset
           completion:(void (^)(NSURL *localFileURL, NSError *error))completion;
@end

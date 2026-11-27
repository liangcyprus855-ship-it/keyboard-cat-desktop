#import "Updater.h"
#import "keycat_core.h"
#import <CommonCrypto/CommonDigest.h>

@implementation KCGitHubAsset
@end

@implementation KCGitHubRelease
@end

@implementation KCUpdater

+ (NSString *)latestReleaseAPIURL {
    return @"https://api.github.com/repos/" @KC_GITHUB_REPO @"/releases/latest";
}

+ (NSString *)releasesPageURL {
    return @"https://github.com/" @KC_GITHUB_REPO @"/releases/latest";
}

+ (BOOL)parseVersion:(NSString *)text major:(int *)major minor:(int *)minor patch:(int *)patch {
    if (![text isKindOfClass:NSString.class]) return NO;
    NSString *trimmed = [text stringByTrimmingCharactersInSet:NSCharacterSet.whitespaceAndNewlineCharacterSet];
    if (trimmed.length == 0) return NO;
    if ([trimmed hasPrefix:@"v"] || [trimmed hasPrefix:@"V"]) trimmed = [trimmed substringFromIndex:1];
    NSRange suffix = [trimmed rangeOfCharacterFromSet:[NSCharacterSet characterSetWithCharactersInString:@"-+"]];
    if (suffix.location != NSNotFound) trimmed = [trimmed substringToIndex:suffix.location];
    NSArray<NSString *> *parts = [trimmed componentsSeparatedByString:@"."];
    if (parts.count < 2) return NO;
    int values[3] = {0, 0, 0};
    for (NSUInteger i = 0; i < 3 && i < parts.count; i++) {
        NSString *part = parts[i];
        if (part.length == 0) return NO;
        NSCharacterSet *nonDigits = NSCharacterSet.decimalDigitCharacterSet.invertedSet;
        if ([part rangeOfCharacterFromSet:nonDigits].location != NSNotFound) return NO;
        values[i] = part.intValue;
    }
    if (major) *major = values[0];
    if (minor) *minor = values[1];
    if (patch) *patch = values[2];
    return YES;
}

+ (BOOL)isNewerVersion:(NSString *)remoteTag current:(NSString *)currentVersion {
    int r0 = 0, r1 = 0, r2 = 0, c0 = 0, c1 = 0, c2 = 0;
    if (![self parseVersion:remoteTag major:&r0 minor:&r1 patch:&r2]) return NO;
    if (![self parseVersion:currentVersion major:&c0 minor:&c1 patch:&c2]) return NO;
    return kc_compare_version_triples(r0, r1, r2, c0, c1, c2) > 0;
}

+ (KCGitHubRelease *)parseReleaseJSON:(NSData *)data {
    if (!data) return nil;
    id obj = [NSJSONSerialization JSONObjectWithData:data options:0 error:NULL];
    if (![obj isKindOfClass:NSDictionary.class]) return nil;
    NSDictionary *dict = obj;
    KCGitHubRelease *release = [KCGitHubRelease new];
    release.tagName = [dict[@"tag_name"] isKindOfClass:NSString.class] ? dict[@"tag_name"] : nil;
    release.name = [dict[@"name"] isKindOfClass:NSString.class] ? dict[@"name"] : nil;
    release.body = [dict[@"body"] isKindOfClass:NSString.class] ? dict[@"body"] : nil;
    release.htmlURL = [dict[@"html_url"] isKindOfClass:NSString.class] ? dict[@"html_url"] : nil;
    release.draft = [dict[@"draft"] boolValue];
    release.prerelease = [dict[@"prerelease"] boolValue];
    NSMutableArray<KCGitHubAsset *> *assets = [NSMutableArray array];
    NSArray *rawAssets = [dict[@"assets"] isKindOfClass:NSArray.class] ? dict[@"assets"] : nil;
    for (id item in rawAssets) {
        if (![item isKindOfClass:NSDictionary.class]) continue;
        NSString *name = item[@"name"];
        NSString *url = item[@"browser_download_url"];
        if (![name isKindOfClass:NSString.class] || ![url isKindOfClass:NSString.class]) continue;
        KCGitHubAsset *asset = [KCGitHubAsset new];
        asset.name = name;
        asset.downloadURL = url;
        asset.size = [item[@"size"] longLongValue];
        [assets addObject:asset];
    }
    release.assets = assets;
    return release;
}

+ (KCGitHubAsset *)selectMacAsset:(KCGitHubRelease *)release {
    KCGitHubAsset *fallbackPkg = nil;
    KCGitHubAsset *archive = nil;
    for (KCGitHubAsset *asset in release.assets) {
        NSString *lower = asset.name.lowercaseString;
        if ([lower hasSuffix:@".pkg"]) {
            if ([lower rangeOfString:@"未签名"].location == NSNotFound) return asset;
            if (!fallbackPkg) fallbackPkg = asset;
        } else if (([lower hasSuffix:@".dmg"] || [lower hasSuffix:@".zip"]) &&
                   ([lower rangeOfString:@"mac"].location != NSNotFound || [lower rangeOfString:@"universal"].location != NSNotFound)) {
            if (!archive) archive = asset;
        }
    }
    return fallbackPkg ?: archive;
}

+ (KCGitHubAsset *)selectChecksumAsset:(KCGitHubRelease *)release {
    for (KCGitHubAsset *asset in release.assets) {
        if ([asset.name.lowercaseString isEqualToString:@"checksums.txt"] ||
            [asset.name.lowercaseString isEqualToString:@"sha256sums.txt"]) {
            return asset;
        }
    }
    return nil;
}

+ (BOOL)verifySHA256OfFile:(NSURL *)fileURL manifest:(NSString *)manifest expectedFileName:(NSString *)fileName {
    if (!manifest.length || !fileName.length) return YES;
    NSString *expectedHash = nil;
    NSArray<NSString *> *lines = [manifest componentsSeparatedByCharactersInSet:NSCharacterSet.newlineCharacterSet];
    for (NSString *raw in lines) {
        NSString *line = [raw stringByTrimmingCharactersInSet:NSCharacterSet.whitespaceCharacterSet];
        if (line.length < 66) continue;
        NSArray<NSString *> *parts = [line componentsSeparatedByCharactersInSet:NSCharacterSet.whitespaceCharacterSet];
        NSMutableArray<NSString *> *nonEmpty = [NSMutableArray array];
        for (NSString *p in parts) if (p.length > 0) [nonEmpty addObject:p];
        if (nonEmpty.count >= 2) {
            NSString *candidateName = [nonEmpty.lastObject stringByTrimmingCharactersInSet:[NSCharacterSet characterSetWithCharactersInString:@"*"]];
            if ([candidateName.lastPathComponent caseInsensitiveCompare:fileName] == NSOrderedSame) {
                expectedHash = nonEmpty.firstObject.lowercaseString;
                break;
            }
        }
    }
    if (!expectedHash) return YES;
    NSData *bytes = [NSData dataWithContentsOfURL:fileURL];
    if (!bytes) return NO;
    unsigned char digest[CC_SHA256_DIGEST_LENGTH];
    CC_SHA256(bytes.bytes, (CC_LONG)bytes.length, digest);
    NSMutableString *hex = [NSMutableString stringWithCapacity:CC_SHA256_DIGEST_LENGTH * 2];
    for (int i = 0; i < CC_SHA256_DIGEST_LENGTH; i++) [hex appendFormat:@"%02x", digest[i]];
    return [hex isEqualToString:expectedHash];
}

+ (NSURLSession *)session {
    NSURLSessionConfiguration *cfg = NSURLSessionConfiguration.ephemeralSessionConfiguration;
    cfg.timeoutIntervalForRequest = 10.0;
    cfg.timeoutIntervalForResource = 120.0;
    return [NSURLSession sessionWithConfiguration:cfg];
}

+ (void)fetchLatestReleaseWithCompletion:(void (^)(KCGitHubRelease *, NSError *))completion {
    NSMutableURLRequest *req = [NSMutableURLRequest requestWithURL:[NSURL URLWithString:[self latestReleaseAPIURL]]];
    [req setValue:@"KeycatDesktop/" @KC_APP_VERSION forHTTPHeaderField:@"User-Agent"];
    [req setValue:@"application/vnd.github+json" forHTTPHeaderField:@"Accept"];
    NSURLSessionDataTask *task = [[self session] dataTaskWithRequest:req completionHandler:^(NSData *data, NSURLResponse *response, NSError *error) {
        if (error) {
            dispatch_async(dispatch_get_main_queue(), ^{ completion(nil, error); });
            return;
        }
        NSHTTPURLResponse *http = [response isKindOfClass:NSHTTPURLResponse.class] ? (NSHTTPURLResponse *)response : nil;
        if (http && (http.statusCode < 200 || http.statusCode >= 300)) {
            NSError *httpErr = [NSError errorWithDomain:@"KCUpdater" code:http.statusCode
                                              userInfo:@{NSLocalizedDescriptionKey:[NSString stringWithFormat:@"服务器返回 HTTP %ld", (long)http.statusCode]}];
            dispatch_async(dispatch_get_main_queue(), ^{ completion(nil, httpErr); });
            return;
        }
        KCGitHubRelease *release = [self parseReleaseJSON:data];
        NSError *parseErr = release ? nil : [NSError errorWithDomain:@"KCUpdater" code:-1
                                                            userInfo:@{NSLocalizedDescriptionKey:@"无法解析更新响应。"}];
        dispatch_async(dispatch_get_main_queue(), ^{ completion(release, parseErr); });
    }];
    [task resume];
}

+ (NSURL *)uniqueDestinationForFileName:(NSString *)fileName {
    NSArray<NSURL *> *downloads = [NSFileManager.defaultManager URLsForDirectory:NSDownloadsDirectory inDomains:NSUserDomainMask];
    NSURL *dir = downloads.firstObject ?: [NSURL fileURLWithPath:NSTemporaryDirectory() isDirectory:YES];
    NSString *safe = fileName.lastPathComponent.length ? fileName.lastPathComponent : @"KeycatUpdate.pkg";
    NSURL *candidate = [dir URLByAppendingPathComponent:safe];
    if (![NSFileManager.defaultManager fileExistsAtPath:candidate.path]) return candidate;
    NSString *stem = safe.stringByDeletingPathExtension;
    NSString *ext = safe.pathExtension;
    for (int i = 1; i < 100; i++) {
        NSString *numbered = ext.length ? [NSString stringWithFormat:@"%@-%d.%@", stem, i, ext] : [NSString stringWithFormat:@"%@-%d", stem, i];
        NSURL *alt = [dir URLByAppendingPathComponent:numbered];
        if (![NSFileManager.defaultManager fileExistsAtPath:alt.path]) return alt;
    }
    return [[NSURL fileURLWithPath:NSTemporaryDirectory() isDirectory:YES] URLByAppendingPathComponent:safe];
}

+ (void)downloadAsset:(KCGitHubAsset *)asset checksumAsset:(KCGitHubAsset *)checksumAsset
           completion:(void (^)(NSURL *, NSError *))completion {
    NSMutableURLRequest *req = [NSMutableURLRequest requestWithURL:[NSURL URLWithString:asset.downloadURL]];
    [req setValue:@"KeycatDesktop/" @KC_APP_VERSION forHTTPHeaderField:@"User-Agent"];
    NSURLSession *session = [self session];
    NSURLSessionDownloadTask *task = [session downloadTaskWithRequest:req completionHandler:^(NSURL *location, NSURLResponse *response, NSError *error) {
        if (error || !location) {
            dispatch_async(dispatch_get_main_queue(), ^{ completion(nil, error); });
            return;
        }
        NSHTTPURLResponse *http = [response isKindOfClass:NSHTTPURLResponse.class] ? (NSHTTPURLResponse *)response : nil;
        if (http && (http.statusCode < 200 || http.statusCode >= 300)) {
            NSError *httpErr = [NSError errorWithDomain:@"KCUpdater" code:http.statusCode
                                              userInfo:@{NSLocalizedDescriptionKey:[NSString stringWithFormat:@"下载失败（HTTP %ld）", (long)http.statusCode]}];
            dispatch_async(dispatch_get_main_queue(), ^{ completion(nil, httpErr); });
            return;
        }
        NSURL *dest = [self uniqueDestinationForFileName:asset.name];
        NSError *moveErr = nil;
        if (![NSFileManager.defaultManager moveItemAtURL:location toURL:dest error:&moveErr]) {
            dispatch_async(dispatch_get_main_queue(), ^{ completion(nil, moveErr); });
            return;
        }
        if (!checksumAsset) {
            dispatch_async(dispatch_get_main_queue(), ^{ completion(dest, nil); });
            return;
        }
        NSMutableURLRequest *sumReq = [NSMutableURLRequest requestWithURL:[NSURL URLWithString:checksumAsset.downloadURL]];
        [sumReq setValue:@"KeycatDesktop/" @KC_APP_VERSION forHTTPHeaderField:@"User-Agent"];
        [[session dataTaskWithRequest:sumReq completionHandler:^(NSData *sumData, NSURLResponse *sumResp, NSError *sumErr) {
            if (!sumErr && sumData) {
                NSString *manifest = [[NSString alloc] initWithData:sumData encoding:NSUTF8StringEncoding];
                if (![self verifySHA256OfFile:dest manifest:manifest expectedFileName:asset.name]) {
                    [NSFileManager.defaultManager removeItemAtURL:dest error:nil];
                    NSError *verifyErr = [NSError errorWithDomain:@"KCUpdater" code:-2
                                                        userInfo:@{NSLocalizedDescriptionKey:@"安装包校验失败（SHA-256 不匹配）。"}];
                    dispatch_async(dispatch_get_main_queue(), ^{ completion(nil, verifyErr); });
                    return;
                }
            }
            dispatch_async(dispatch_get_main_queue(), ^{ completion(dest, nil); });
        }] resume];
    }];
    [task resume];
}

@end

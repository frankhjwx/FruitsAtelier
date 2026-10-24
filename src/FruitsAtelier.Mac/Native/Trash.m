#import <Foundation/Foundation.h>

int fa_trash_item(const char *path)
{
    @autoreleasepool {
        NSString *value = path ? [NSString stringWithUTF8String:path] : nil;
        if (!value) return 0;
        NSURL *url = [NSURL fileURLWithPath:value];
        return [[NSFileManager defaultManager] trashItemAtURL:url resultingItemURL:nil error:nil] ? 1 : 0;
    }
}

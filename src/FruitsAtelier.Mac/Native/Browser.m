#import <AppKit/AppKit.h>
#import <WebKit/WebKit.h>

typedef void (*FATimestampCallback)(const char *, const char *);
static FATimestampCallback fa_timestamp;

@interface FABrowser : NSWindowController <WKNavigationDelegate, WKUIDelegate>
@property WKWebView *web;
@property BOOL failed;
@property NSTextField *address;
@property NSTextField *status;
@property NSDictionary *labels;
@property NSMutableArray<FABrowser *> *children;
- (instancetype)initWithLabels:(NSDictionary *)labels configuration:(WKWebViewConfiguration *)configuration;
@end

static BOOL fa_web_url(NSURL *url) {
    return [url.scheme.lowercaseString isEqualToString:@"https"] || [url.scheme.lowercaseString isEqualToString:@"http"];
}

@implementation FABrowser
- (instancetype)initWithLabels:(NSDictionary *)labels configuration:(WKWebViewConfiguration *)configuration {
    NSWindow *window = [[NSWindow alloc] initWithContentRect:NSMakeRect(0, 0, 1200, 850)
        styleMask:NSWindowStyleMaskTitled | NSWindowStyleMaskClosable | NSWindowStyleMaskMiniaturizable | NSWindowStyleMaskResizable
        backing:NSBackingStoreBuffered defer:NO];
    self = [super initWithWindow:window];
    if (!self) return nil;
    self.labels = labels;
    self.children = [NSMutableArray array];
    window.title = labels[@"title"];
    window.releasedWhenClosed = NO;
    window.minSize = NSMakeSize(640, 480);
    [window center];
    NSView *content = window.contentView;
    NSStackView *toolbar = [[NSStackView alloc] init];
    toolbar.orientation = NSUserInterfaceLayoutOrientationHorizontal;
    toolbar.spacing = 8;
    toolbar.translatesAutoresizingMaskIntoConstraints = NO;
    NSArray *keys = @[@"back", @"forward", @"reload", @"external"];
    SEL actions[] = {@selector(back:), @selector(forward:), @selector(reload:), @selector(external:)};
    NSArray *symbols = @[@"←", @"→", @"↻", @"↗"];
    for (NSUInteger i = 0; i < keys.count; i++) {
        NSButton *button = [NSButton buttonWithTitle:symbols[i] target:self action:actions[i]];
        button.toolTip = labels[keys[i]];
        button.accessibilityLabel = labels[keys[i]];
        [toolbar addArrangedSubview:button];
    }
    self.address = [NSTextField labelWithString:@"https://osu.ppy.sh"];
    self.address.lineBreakMode = NSLineBreakByTruncatingMiddle;
    [self.address setContentCompressionResistancePriority:NSLayoutPriorityDefaultLow forOrientation:NSLayoutConstraintOrientationHorizontal];
    [toolbar addArrangedSubview:self.address];
    self.status = [NSTextField labelWithString:@""];
    self.status.translatesAutoresizingMaskIntoConstraints = NO;
    self.status.lineBreakMode = NSLineBreakByTruncatingTail;
    self.web = [[WKWebView alloc] initWithFrame:NSZeroRect configuration:configuration];
    self.web.translatesAutoresizingMaskIntoConstraints = NO;
    self.web.navigationDelegate = self;
    self.web.UIDelegate = self;
    [content addSubview:toolbar];
    [content addSubview:self.web];
    [content addSubview:self.status];
    [NSLayoutConstraint activateConstraints:@[
        [toolbar.topAnchor constraintEqualToAnchor:content.topAnchor constant:8],
        [toolbar.leadingAnchor constraintEqualToAnchor:content.leadingAnchor constant:8],
        [toolbar.trailingAnchor constraintLessThanOrEqualToAnchor:content.trailingAnchor constant:-8],
        [self.web.topAnchor constraintEqualToAnchor:toolbar.bottomAnchor constant:8],
        [self.web.leadingAnchor constraintEqualToAnchor:content.leadingAnchor],
        [self.web.trailingAnchor constraintEqualToAnchor:content.trailingAnchor],
        [self.web.bottomAnchor constraintEqualToAnchor:self.status.topAnchor constant:-4],
        [self.status.leadingAnchor constraintEqualToAnchor:content.leadingAnchor constant:8],
        [self.status.trailingAnchor constraintEqualToAnchor:content.trailingAnchor constant:-8],
        [self.status.bottomAnchor constraintEqualToAnchor:content.bottomAnchor constant:-4]
    ]];
    return self;
}
- (void)back:(id)sender { if (self.web.canGoBack) [self.web goBack]; }
- (void)forward:(id)sender { if (self.web.canGoForward) [self.web goForward]; }
- (void)reload:(id)sender { [self.web reload]; }
- (void)external:(id)sender { if (fa_web_url(self.web.URL)) [[NSWorkspace sharedWorkspace] openURL:self.web.URL]; }
- (void)webView:(WKWebView *)webView decidePolicyForNavigationAction:(WKNavigationAction *)action decisionHandler:(void (^)(WKNavigationActionPolicy))handler {
    NSURL *url = action.request.URL;
    if (fa_timestamp && action.navigationType == WKNavigationTypeLinkActivated
        && [url.scheme.lowercaseString isEqualToString:@"osu"] && [url.host.lowercaseString isEqualToString:@"edit"]
        && [webView.URL.host.lowercaseString isEqualToString:@"osu.ppy.sh"] && [webView.URL.scheme isEqualToString:@"https"])
        fa_timestamp(url.absoluteString.UTF8String, webView.URL.absoluteString.UTF8String);
    handler(fa_web_url(url) ? WKNavigationActionPolicyAllow : WKNavigationActionPolicyCancel);
}
- (void)webView:(WKWebView *)webView didStartProvisionalNavigation:(WKNavigation *)navigation {
    self.failed = NO;
    self.status.stringValue = self.labels[@"loading"];
}
- (void)webView:(WKWebView *)webView didCommitNavigation:(WKNavigation *)navigation {
    self.address.stringValue = [NSString stringWithFormat:@"%@://%@", webView.URL.scheme ?: @"", webView.URL.host ?: @""];
}
- (void)webView:(WKWebView *)webView didFinishNavigation:(WKNavigation *)navigation { self.status.stringValue = @""; }
- (void)webView:(WKWebView *)webView didFailNavigation:(WKNavigation *)navigation withError:(NSError *)error {
    if (error.code != NSURLErrorCancelled) { self.failed = YES; self.status.stringValue = self.labels[@"failed"]; }
}
- (void)webView:(WKWebView *)webView didFailProvisionalNavigation:(WKNavigation *)navigation withError:(NSError *)error {
    [self webView:webView didFailNavigation:navigation withError:error];
}
- (void)webViewWebContentProcessDidTerminate:(WKWebView *)webView { self.failed = YES; self.status.stringValue = self.labels[@"failed"]; }
- (WKWebView *)webView:(WKWebView *)webView createWebViewWithConfiguration:(WKWebViewConfiguration *)configuration
    forNavigationAction:(WKNavigationAction *)action windowFeatures:(WKWindowFeatures *)features {
    if (action.targetFrame || !fa_web_url(action.request.URL)) return nil;
    FABrowser *child = [[FABrowser alloc] initWithLabels:self.labels configuration:configuration];
    [self.children addObject:child];
    [child showWindow:nil];
    return child.web;
}
- (void)webViewDidClose:(WKWebView *)webView { if (self.window.isVisible) [self close]; }
- (void)closeTree { for (FABrowser *child in self.children) [child closeTree]; [self.children removeAllObjects]; [self.web stopLoading]; [self close]; }
- (NSAlert *)alert:(NSString *)message frame:(WKFrameInfo *)frame {
    NSAlert *alert = [[NSAlert alloc] init];
    alert.messageText = frame.request.URL.host ?: self.labels[@"title"];
    alert.informativeText = message ?: @"";
    [alert addButtonWithTitle:self.labels[@"ok"]];
    return alert;
}
- (void)webView:(WKWebView *)webView runJavaScriptAlertPanelWithMessage:(NSString *)message initiatedByFrame:(WKFrameInfo *)frame completionHandler:(void (^)(void))handler {
    [[self alert:message frame:frame] beginSheetModalForWindow:self.web.window completionHandler:^(NSModalResponse response) { handler(); }];
}
- (void)webView:(WKWebView *)webView runJavaScriptConfirmPanelWithMessage:(NSString *)message initiatedByFrame:(WKFrameInfo *)frame completionHandler:(void (^)(BOOL))handler {
    NSAlert *alert = [self alert:message frame:frame];
    [alert addButtonWithTitle:self.labels[@"cancel"]];
    [alert beginSheetModalForWindow:self.web.window completionHandler:^(NSModalResponse response) { handler(response == NSAlertFirstButtonReturn); }];
}
- (void)webView:(WKWebView *)webView runJavaScriptTextInputPanelWithPrompt:(NSString *)prompt defaultText:(NSString *)text initiatedByFrame:(WKFrameInfo *)frame completionHandler:(void (^)(NSString *))handler {
    NSAlert *alert = [self alert:prompt frame:frame];
    [alert addButtonWithTitle:self.labels[@"cancel"]];
    NSTextField *input = [[NSTextField alloc] initWithFrame:NSMakeRect(0, 0, 320, 24)];
    input.stringValue = text ?: @"";
    alert.accessoryView = input;
    [alert beginSheetModalForWindow:self.web.window completionHandler:^(NSModalResponse response) { handler(response == NSAlertFirstButtonReturn ? input.stringValue : nil); }];
}
@end

static FABrowser *fa_browser;
void *fa_browser_create(const char *labelsJson, FATimestampCallback callback) {
    @autoreleasepool {
        if (![NSThread isMainThread]) return NULL;
        fa_timestamp = callback;
        if (!fa_browser) {
            NSData *data = [[NSString stringWithUTF8String:labelsJson] dataUsingEncoding:NSUTF8StringEncoding];
            NSDictionary *labels = [NSJSONSerialization JSONObjectWithData:data options:0 error:nil];
            WKWebViewConfiguration *configuration = [[WKWebViewConfiguration alloc] init];
            configuration.websiteDataStore = [WKWebsiteDataStore defaultDataStore];
            fa_browser = [[FABrowser alloc] initWithLabels:labels configuration:configuration];
            [fa_browser.web removeFromSuperview];
            fa_browser.web.translatesAutoresizingMaskIntoConstraints = YES;
            fa_browser.web.autoresizingMask = NSViewWidthSizable | NSViewHeightSizable;
        }
        return (__bridge void *)fa_browser.web;
    }
}
void fa_browser_navigate(const char *url) {
    if (![NSThread isMainThread] || !fa_browser) return;
    NSURL *target = [NSURL URLWithString:[NSString stringWithUTF8String:url]];
    if (fa_web_url(target)) [fa_browser.web loadRequest:[NSURLRequest requestWithURL:target]];
}
int fa_browser_state(void) {
    return (fa_browser.web.canGoBack ? 1 : 0) | (fa_browser.web.canGoForward ? 2 : 0)
        | (fa_browser.web.loading ? 4 : 0) | (fa_browser.failed ? 8 : 0);
}
void fa_browser_command(int command, const char *mapUrl) {
    if (![NSThread isMainThread] || !fa_browser) return;
    switch (command) {
        case 0: [fa_browser back:nil]; break;
        case 1: [fa_browser forward:nil]; break;
        case 2: fa_browser_navigate(mapUrl); break;
        case 3: [fa_browser external:nil]; break;
    }
}
const char *fa_browser_url(void) { return fa_browser.web.URL.absoluteString.UTF8String; }
void fa_browser_close(void) {
    if (![NSThread isMainThread]) return;
    [fa_browser.web removeFromSuperview];
    [fa_browser closeTree];
    fa_browser = nil;
    fa_timestamp = NULL;
}
void fa_browser_clear_data(void (*completion)(void)) {
    [[WKWebsiteDataStore defaultDataStore] removeDataOfTypes:[WKWebsiteDataStore allWebsiteDataTypes]
        modifiedSince:[NSDate distantPast] completionHandler:^{ completion(); }];
}

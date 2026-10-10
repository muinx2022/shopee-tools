chrome.runtime.onMessage.addListener((request, sender, sendResponse) => {
    if (request.action === "setCookie") {
        console.log("Setting cookie:", request.cookie);
        let raw = (request.cookie || '').trim();
        let domain = ".shopee.vn";
        let name = "SPC_F";
        let value = "";
        
        if (raw.includes('=')) {
            const parts = raw.split('=');
            if (parts.length === 2) {
                name = parts[0].trim();
                value = parts[1].trim();
            } else if (parts.length >= 3) {
                // e.g. .shopee.vn=SPC_F=xxx
                domain = parts[0].trim() || ".shopee.vn";
                name = parts[1].trim();
                value = parts.slice(2).join('=').trim();
            }
        }
        
        chrome.cookies.set({
            url: "https://shopee.vn",
            name: name,
            value: value,
            domain: domain.startsWith('.') ? domain : ('.' + domain),
            path: "/",
            secure: true,
            httpOnly: false
        }, (cookie) => {
            sendResponse({ success: true, cookie: cookie });
        });
        return true;
    } else if (request.action === "openTabs") {
        console.log("Opening category tabs:", request.urls);
        if (request.urls && Array.isArray(request.urls)) {
            request.urls.forEach(url => {
                chrome.tabs.create({ url: url, active: false });
            });
        }
        sendResponse({ success: true });
    } else if (request.action === "closeCurrentTab") {
        // Disabled: tuyệt đối không đóng tab trong lúc cào
        sendResponse({ success: true });
    }
});

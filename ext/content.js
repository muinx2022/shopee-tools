window.addEventListener('load', async () => {
    // Phiên test: không tự điền login. Xóa automation cũ nếu còn trong profile.
    if (window.location.hash.includes('#shopee_ext_manual=')) {
        await chrome.storage.local.remove(['shopee_automation']);
        history.replaceState(null, null, window.location.pathname + window.location.search);
        return;
    }

    // 1. Process Hash Payload
    if (window.location.hash.includes('#shopee_ext_login=')) {
        const hash = window.location.hash.split('#shopee_ext_login=')[1];
        if (!hash) return;
        
        try {
            const decoded = atob(decodeURIComponent(hash));
            const data = JSON.parse(decoded);
            
            // Save to storage (shared across tabs in extension)
            await chrome.storage.local.set({ 
                shopee_automation: data,
                shopee_backend_port: data.port
            });
            
            // Remove hash to prevent looping
            history.replaceState(null, null, ' ');

            // Proceed with login automation
            await handleAutomation(data);
        } catch (e) { 
            console.error("ShopeeExt Error parsing hash:", e); 
        }
    } else {
        // 2. Check if we have an ongoing automation in storage (login / captcha wait)
        const res = await chrome.storage.local.get(['shopee_automation']);
        if (res.shopee_automation && !res.shopee_automation.opened) {
            await handleAutomation(res.shopee_automation);
        }
    }
});

async function handleAutomation(data) {
    const isLoginPage = window.location.pathname.includes('/buyer/login');
    const isVerifyPage = window.location.pathname.includes('/verify') || window.location.href.includes('/verify');
    
    if (isLoginPage) {
        // We are on login page. Set cookie via background
        chrome.runtime.sendMessage({ action: "setCookie", cookie: data.c }, async () => {
            await simulateHumanLogin(data.u, data.p, data.port, data.wsPort);
        });
    } else if (isVerifyPage) {
        console.log("ShopeeExt: On /verify page, waiting for manual captcha resolution...");
        await waitForCaptchaResolution(data.port, data.wsPort, 120);
    } else {
        // We are already logged in or redirected to home page
        try { chrome.storage.local.remove(['shopee_automation']); } catch (e) {}
        console.log("ShopeeExt: Logged in! Reporting login_success to C# backend...");
        fetch(`http://localhost:${data.port}/status`, { 
            method: 'POST', 
            body: 'login_success' 
        }).catch(()=>{});

        if (data.wsPort) {
            console.log("ShopeeExt: Quay về trang chủ gắn kết nối ?_ss_ws=" + data.wsPort);
            window.location.href = `https://shopee.vn/?_ss_ws=${data.wsPort}#_ss_ws=${data.wsPort}`;
        }
    }
}

async function checkAndScrapeCategory(port) {
    // Disabled: Cào danh mục do extension shopee-search chuyên trách xử lý
    return;
}

async function scrapeCategoryPage(port) {
    // Disabled
    return;
}

function extractCategoryHierarchy() {
    const selectors = [
        '.shopee-breadcrumb',
        '[aria-label="breadcrumb"]',
        'div.flex.items-center.text-xs',
        '.page-category-breadcrumb',
        '.breadcrumb'
    ];
    for (const sel of selectors) {
        const container = document.querySelector(sel);
        if (container) {
            const texts = Array.from(container.querySelectorAll('a, span'))
                .map(el => el.innerText.trim())
                .filter(t => t && t !== '>' && t !== '/' && !t.toLowerCase().includes('shopee'));
            if (texts.length > 0) {
                return texts.join(' > ');
            }
        }
    }

    const titleParts = document.title.split('|')[0].trim();
    if (titleParts && !titleParts.toLowerCase().includes('shopee')) {
        return titleParts;
    }
    return "Danh mục Shopee";
}

function extractProductItems(categoryPath, categoryUrl) {
    const items = [];
    let cards = Array.from(document.querySelectorAll('[data-sqe="item"], .shopee-search-item-result__item, div.col-xs-2-4'));
    
    if (cards.length === 0) {
        const links = Array.from(document.querySelectorAll('a[href*="-i."]'));
        cards = links.map(l => l.closest('div[class*="col"]') || l.parentElement || l);
    }

    const seenIds = new Set();

    for (const card of cards) {
        try {
            const linkEl = card.querySelector('a[href*="-i."]') || (card.tagName === 'A' ? card : null);
            if (!linkEl) continue;

            const href = linkEl.getAttribute('href') || '';
            const match = href.match(/-i\.(\d+)\.(\d+)/);
            if (!match) continue;

            const shopId = match[1];
            const itemId = match[2];
            const uniqueId = `${shopId}_${itemId}`;
            if (seenIds.has(uniqueId)) continue;
            seenIds.add(uniqueId);

            const productUrl = href.startsWith('http') ? href : `https://shopee.vn${href}`;

            // Image
            const imgEl = card.querySelector('img[src*="susercontent.com"], img[src*="shopeemobile.com"], img');
            let imageUrl = "";
            if (imgEl) {
                imageUrl = imgEl.src || imgEl.getAttribute('data-src') || '';
            }

            // Name
            let name = "";
            const nameEl = card.querySelector('div[data-sqe="name"], .line-clamp-2, .truncate');
            if (nameEl) {
                name = nameEl.innerText.trim();
            } else if (imgEl && imgEl.alt) {
                name = imgEl.alt.trim();
            } else {
                name = linkEl.innerText.split('\n')[0].trim();
            }
            if (!name) name = "Sản phẩm Shopee";

            // Price
            let price = 0;
            const fullText = card.innerText;
            const priceMatches = fullText.match(/₫\s*([0-9.,]+)/) || fullText.match(/([0-9]{1,3}(?:\.[0-9]{3})+)\s*₫?/);
            if (priceMatches && priceMatches[1]) {
                const clean = priceMatches[1].replace(/\./g, '').replace(/,/g, '');
                price = parseFloat(clean) || 0;
            }

            // Sold & Monthly Sold
            let historicalSold = 0;
            let monthlySold = 0;
            const soldMatches = fullText.match(/đã\s+bán\s+([0-9.,]+)\s*(k|tr)?(?:\s*\/\s*tháng)?/i) || 
                                fullText.match(/([0-9.,]+)\s*(k|tr)?\s+đã\s+bán/i);
            if (soldMatches) {
                let num = parseFloat(soldMatches[1].replace(',', '.'));
                const unit = (soldMatches[2] || '').toLowerCase();
                if (unit === 'k') num *= 1000;
                else if (unit === 'tr') num *= 1000000;

                if (soldMatches[0].toLowerCase().includes('/tháng') || soldMatches[0].toLowerCase().includes('/ tháng')) {
                    monthlySold = Math.round(num);
                    historicalSold = 0;
                } else {
                    historicalSold = Math.round(num);
                }
            }

            // Rating
            let rating = 0;
            const ratingMatch = fullText.match(/\b([1-5]\.[0-9])\b/);
            if (ratingMatch) {
                rating = parseFloat(ratingMatch[1]);
            }

            // Location
            let location = "";
            const lines = fullText.split('\n').map(l => l.trim()).filter(Boolean);
            if (lines.length > 0) {
                const last = lines[lines.length - 1];
                if (last.length < 30 && !last.includes('₫') && !last.toLowerCase().includes('đã bán') && !last.includes('%')) {
                    location = last;
                }
            }

            items.push({
                itemId: itemId,
                shopId: shopId,
                name: name,
                price: price,
                rating: rating,
                ratingCount: 0,
                historicalSold: historicalSold,
                monthlySold: monthlySold,
                location: location,
                imageUrl: imageUrl,
                categoryPath: categoryPath,
                categoryUrl: categoryUrl,
                productUrl: productUrl,
                scrapedAt: new Date().toISOString()
            });
        } catch (e) {
            console.error("Error parsing product card:", e);
        }
    }
    return items;
}

let _captchaWaitActive = false;
async function waitForCaptchaResolution(port, wsPort, maxWaitSeconds = 120) {
    if (_captchaWaitActive) return false;
    _captchaWaitActive = true;

    console.log(`ShopeeExt: ⚠️ Phát hiện Captcha/trang xác minh! Đang tạm dừng chờ bạn giải Captcha thủ công (tối đa ${maxWaitSeconds}s)...`);
    fetch(`http://localhost:${port}/status`, { method: 'POST', body: 'captcha_waiting' }).catch(()=>{});

    const start = Date.now();
    const maxWaitMs = maxWaitSeconds * 1000;

    while (Date.now() - start < maxWaitMs) {
        await new Promise(r => setTimeout(r, 1500));

        const currentPath = window.location.pathname.toLowerCase();
        const currentUrl = window.location.href.toLowerCase();
        const text = ((document.body ? document.body.innerText : '') || '').toLowerCase();

        // 1. Kiểm tra nếu có báo sai mật khẩu / tài khoản
        const isWrongText =
            text.includes('tài khoản hoặc mật khẩu không đúng') ||
            text.includes('mật khẩu không đúng') ||
            text.includes('tên đăng nhập không đúng') ||
            text.includes('incorrect username or password') ||
            text.includes('sai mật khẩu');
        if (isWrongText) {
            console.log("ShopeeExt: Phát hiện sai mật khẩu trong lúc xác minh!");
            try { chrome.storage.local.remove(['shopee_automation']); } catch (e) {}
            fetch(`http://localhost:${port}/status`, { method: 'POST', body: 'wrong_password' }).catch(()=>{});
            _captchaWaitActive = false;
            return false;
        }

        // 2. Kiểm tra dấu hiệu Captcha/verify
        const stillVerify =
            currentUrl.includes('/verify') ||
            currentPath.startsWith('/verify') ||
            document.querySelector('iframe[src*="verify"]') !== null;

        // 3. Kiểm tra đăng nhập thành công
        const hasUserAvatar = document.querySelector('.navbar__username, .shopee-avatar, a[href*="/user/purchase"]') !== null;
        const isSuccess = hasUserAvatar || (!stillVerify && !currentPath.includes('/buyer/login') && !currentPath.includes('/buyer/signup'));

        if (isSuccess) {
            console.log("ShopeeExt: ✅ Giải Captcha thành công -> Đăng nhập thành công!");
            try { chrome.storage.local.remove(['shopee_automation']); } catch (e) {}
            fetch(`http://localhost:${port}/status`, { method: 'POST', body: 'login_success' }).catch(()=>{});
            if (wsPort) {
                console.log("ShopeeExt: Chuyển về trang chủ gắn kết nối ?_ss_ws=" + wsPort);
                window.location.href = `https://shopee.vn/?_ss_ws=${wsPort}#_ss_ws=${wsPort}`;
            }
            _captchaWaitActive = false;
            return true;
        }
    }

    // Hết 2 phút mà vẫn chưa giải xong
    console.log("ShopeeExt: ❌ Hết 2 phút chờ mà chưa giải xong Captcha.");
    try { chrome.storage.local.remove(['shopee_automation']); } catch (e) {}
    fetch(`http://localhost:${port}/status`, { method: 'POST', body: 'captcha' }).catch(()=>{});
    _captchaWaitActive = false;
    return false;
}

async function checkAndReportLoginStatus(port, wsPort) {
    const text = ((document.body ? document.body.innerText : '') || '').toLowerCase();
    const url = window.location.href.toLowerCase();
    const path = window.location.pathname.toLowerCase();

    // 1. Check Captcha / Verification
    const isCaptcha = 
        url.includes('/verify') ||
        path.startsWith('/verify') ||
        document.querySelector('iframe[src*="verify"]') !== null;

    if (isCaptcha) {
        console.log("ShopeeExt: Phát hiện CAPTCHA / Verification! Bắt đầu chờ giải tay 2 phút...");
        await waitForCaptchaResolution(port, wsPort, 120);
        return true;
    }

    // 2. Check Wrong Password / Account Error
    const isWrongText =
        text.includes('tài khoản hoặc mật khẩu không đúng') ||
        text.includes('mật khẩu không đúng') ||
        text.includes('tên đăng nhập không đúng') ||
        text.includes('incorrect username or password') ||
        text.includes('đăng nhập không thành công') ||
        text.includes('tài khoản của bạn đã bị khóa') ||
        text.includes('tài khoản không tồn tại') ||
        text.includes('sai mật khẩu') ||
        text.includes('mật khẩu chưa chính xác') ||
        text.includes('vui lòng nhập mật khẩu');

    const alertEl = document.querySelector('div[role="alert"], .shopee-form-item__error, .p-input-error');
    const alertText = alertEl ? (alertEl.innerText || '').toLowerCase() : '';
    const hasAlertError = alertText.length > 0 && !alertText.includes('thành công');

    if (isWrongText || hasAlertError) {
        console.log("ShopeeExt: Detected WRONG PASSWORD / LOGIN ERROR!");
        try { chrome.storage.local.remove(['shopee_automation']); } catch (e) {}
        fetch(`http://localhost:${port}/status`, { method: 'POST', body: 'wrong_password' }).catch(()=>{});
        return true;
    }

    const hasUserAvatar = document.querySelector('.navbar__username, .shopee-avatar, a[href*="/user/purchase"]') !== null;
    const isSuccess = (!path.includes('/buyer/login') && !path.includes('/buyer/signup') && !path.includes('/verify') && !url.includes('/verify')) || hasUserAvatar;

    if (isSuccess) {
        console.log("ShopeeExt: Detected LOGIN SUCCESS!");
        try { chrome.storage.local.remove(['shopee_automation']); } catch (e) {}
        fetch(`http://localhost:${port}/status`, { method: 'POST', body: 'login_success' }).catch(()=>{});
        if (wsPort) {
            console.log("ShopeeExt: Chuyển về trang chủ gắn kết nối quét dữ liệu ?_ss_ws=" + wsPort);
            window.location.href = `https://shopee.vn/?_ss_ws=${wsPort}#_ss_ws=${wsPort}`;
        }
        return true;
    }

    return false;
}

async function simulateHumanLogin(username, password, port, wsPort) {
    console.log("ShopeeExt: Simulating human login...");
    await new Promise(r => setTimeout(r, 1500));
    
    // Check if already authenticated or captcha present
    if (await checkAndReportLoginStatus(port, wsPort)) return;

    async function typeText(element, text) {
        element.focus();
        element.click();
        const nativeInputValueSetter = Object.getOwnPropertyDescriptor(window.HTMLInputElement.prototype, "value").set;
        let currentText = "";
        for (let i = 0; i < text.length; i++) {
            currentText += text[i];
            nativeInputValueSetter.call(element, currentText);
            element.dispatchEvent(new Event('input', { bubbles: true }));
            await new Promise(r => setTimeout(r, 30 + Math.random() * 50));
        }
        element.dispatchEvent(new Event('change', { bubbles: true }));
        element.blur();
    }

    const findElement = async (selector, maxRetries = 20) => {
        for(let i = 0; i < maxRetries; i++) {
            const el = document.querySelector(selector);
            if(el) return el;
            if (await checkAndReportLoginStatus(port, wsPort)) return null;
            await new Promise(r => setTimeout(r, 500));
        }
        return null;
    };

    const userField = await findElement('input[name="loginKey"], input[autocomplete="username"], input[placeholder*="Email"], input[placeholder*="đăng nhập"], input[type="text"]');
    const passField = await findElement('input[name="password"], input[type="password"]');

    if (userField && passField) {
        console.log("ShopeeExt: Found inputs, typing...");
        userField.value = '';
        passField.value = '';
        
        await new Promise(r => setTimeout(r, 300));
        await typeText(userField, username);
        
        await new Promise(r => setTimeout(r, 300));
        await typeText(passField, password);
        
        await new Promise(r => setTimeout(r, 800));
        
        const allBtns = Array.from(document.querySelectorAll('button'));
        const loginBtn = allBtns.find(b => {
            const txt = (b.textContent || "").toLowerCase();
            return txt.includes('log in') || txt.includes('đăng nhập');
        });

        if (loginBtn) {
            console.log("ShopeeExt: Clicking login button.");
            loginBtn.removeAttribute('disabled');
            loginBtn.dispatchEvent(new MouseEvent('mousedown', { bubbles: true }));
            loginBtn.dispatchEvent(new MouseEvent('mouseup', { bubbles: true }));
            loginBtn.click();
        } else {
            const form = document.querySelector('form');
            if (form) {
                const b = form.querySelector('button:last-of-type');
                if (b) {
                    b.removeAttribute('disabled');
                    b.click();
                }
            }
        }
        
        console.log("ShopeeExt: Waiting for response/errors...");
        for (let i = 0; i < 40; i++) {
            await new Promise(r => setTimeout(r, 500));
            if (await checkAndReportLoginStatus(port, wsPort)) return;
        }

        // If after 20 seconds still on login page and no explicit error, wait for manual solve
        if (window.location.pathname.includes('/buyer/login')) {
            console.log("ShopeeExt: Still on login page, checking if captcha or waiting for manual action...");
            if (await checkAndReportLoginStatus(port, wsPort)) return;
            await waitForCaptchaResolution(port, wsPort, 100);
        }
    } else {
        console.log("ShopeeExt: Could not find login inputs.");
        if (!(await checkAndReportLoginStatus(port, wsPort))) {
            await waitForCaptchaResolution(port, wsPort, 120);
        }
    }
}

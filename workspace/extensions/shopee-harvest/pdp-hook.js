/**
 * Shopee Harvest — hook bắt JSON chi tiết sản phẩm (PDP) mà CHÍNH TRANG Shopee tự tải.
 *
 * Chạy ở world MAIN, document_start (khai trong manifest): phải gắn TRƯỚC script của trang, vì Shopee hay tải lại
 * trang (vd sau đăng nhập, `is_from_login=true`) — hook gắn muộn sẽ lỡ mất lượt gọi get_pc của document mới.
 *
 * Nguyên tắc: CHỈ ĐỌC. Không sửa request/response, không chặn, không ném lỗi ra trang — mọi thứ trong try/catch.
 * Giảm dấu vết (các việc chi phí thấp, đã làm):
 * - fetch, XMLHttpRequest.prototype.open/send bọc bằng Proxy (giữ name/length, không có prototype, không `new`
 *   được — y hàm gốc). Proxy của V8 KHÔNG giữ toString (ra "function () { [native code] }") nên
 *   Function.prototype.toString cũng bọc Proxy để trả chuỗi hàm gốc. Mọi callback có thể lọt vào tay trang
 *   (callback `then`, `onLoad`, listener REQ) đều đưa vào WeakMap `masked` → toString của chúng cũng trả native.
 * - Chụp sẵn lúc document_start rồi chỉ gọi qua ReflectApply: Reflect.apply, String (chuyển chuỗi), EventTarget
 *   add/dispatch, CustomEvent, JSON parse/stringify, URL + getter URL.prototype.pathname, WeakMap get/has/set,
 *   Promise.prototype.then, Response.prototype.clone/text + getter .url, Request.prototype getter .url,
 *   XMLHttpRequest.prototype getter responseURL/responseType/responseText/response, Array.prototype push/shift/slice,
 *   Date.now, RegExp.prototype.exec (không dùng .test: nó dispatch sang this.exec). item_id lấy bằng regex trên CHUỖI query (không dùng URL.searchParams).
 *   Trang ghi đè các API/thuộc tính đó sau khi hook gắn thì không ảnh hưởng hook.
 * - Mọi object tuỳ chọn truyền cho API trang (CustomEvent init, listener options) dùng { __proto__: null }.
 * - KHÔNG đặt gì lên window: mỗi lượt get_pc gửi một CustomEvent PDP_EVENT lên document (detail = chuỗi JSON)
 *   cho pdp-store.js ở world ISOLATED giữ. Mọi tin có `type` + bộ đếm `seen` (số response get_pc đã thấy) /
 *   `dropped` (số bị bỏ vì không phải JSON hợp lệ hay thiếu itemId):
 *     {type:"ready"}                                   — hook vừa gắn / trả lời yêu cầu phát lại
 *     {type:"pdp",   itemId, url, at, via, body}       — body là CHUỖI GỐC (via "xhr-json" là ngoại lệ:
 *                                                        responseType "json" không còn chuỗi gốc, đành stringify)
 *     {type:"error", itemId, url, at, via, error}      — Shopee từ chối (`error` khác null và khác 0)
 *     {type:"drop"}                                    — chỉ cập nhật bộ đếm
 * - Thứ tự chạy 2 content script document_start khác world KHÔNG được đảm bảo → hook đệm tối đa BUFFER_MAX tin
 *   pdp/error trong closure; pdp-store.js gắn xong listener thì bắn REQ_EVENT, hook phát lại cả đệm + "ready".
 *   Hook gắn sau thì tin "ready" lúc gắn tự tới store (listener đã có).
 *
 * RỦI RO ĐÃ CHẤP NHẬN (Proxy ở world MAIN không che được, không xử ở đây):
 * - Trang dò toString của fetch/XHR từ MỘT REALM KHÁC (vd lấy Function.prototype.toString của iframe rồi .call)
 *   sẽ thấy "function () { [native code] }" của Proxy thay vì mã gốc — ta chỉ che được toString của realm này.
 * - Phép thử vòng qua __proto__ / so sánh tham chiếu sâu có thể lộ việc fetch/XHR không còn là hàm gốc.
 * - Lỗi ném từ trong callback của hook (nếu có) sẽ mang khung stack "chrome-extension://…".
 *   Không thêm all_frames / match_about_blank: get_pc chỉ chạy ở frame chính của trang SP.
 *
 * File này là script thường (content script không import được module) → luật nhận body trùng với
 * pdp-core.js#checkPdpBody; tests/pdp-hook.test.mjs có ca đối chiếu hai bản để không lệch nhau.
 */
(() => {
  // Phải TRÙNG hằng trong pdp-store.js.
  const PDP_EVENT = "shopee-harvest:pdp:7f3a9c1e";
  const REQ_EVENT = "shopee-harvest:pdp:7f3a9c1e:req";
  const BUFFER_MAX = 20;

  let win, doc, A, Str, addEL, dispatch, CE, jsonParse, jsonStringify, URLc, pathnameGetter,
    WM, wmGet, wmHas, wmSet, promiseThen, respClone, respText, arrPush, arrShift, arrSlice,
    dateNow, reExec, ProxyC, getPcRe, digitsRe, itemIdRe,
    respUrlGetter, reqUrlGetter, xhrRespURLGetter, xhrRespTypeGetter, xhrRespTextGetter, xhrResponseGetter;
  try {
    win = window;
    doc = win.document;
    A = win.Reflect.apply;
    Str = win.String;                       // chụp String gốc: trang ghi đè window.String sau này không ảnh hưởng
    addEL = win.EventTarget.prototype.addEventListener;
    dispatch = win.EventTarget.prototype.dispatchEvent;
    CE = win.CustomEvent;
    jsonParse = win.JSON.parse;
    jsonStringify = win.JSON.stringify;
    URLc = win.URL;
    // Lấy getter của một thuộc tính trên prototype; không có getter (engine lạ) → null (getVia tự fallback).
    const desc = (obj, key) => { const d = obj && win.Object.getOwnPropertyDescriptor(obj, key); return d && d.get ? d.get : null; };
    pathnameGetter = desc(win.URL.prototype, "pathname");
    WM = win.WeakMap;
    wmGet = WM.prototype.get;
    wmHas = WM.prototype.has;
    wmSet = WM.prototype.set;
    promiseThen = win.Promise.prototype.then;
    respClone = win.Response.prototype.clone;
    respText = win.Response.prototype.text;
    respUrlGetter = desc(win.Response.prototype, "url");
    reqUrlGetter = win.Request && win.Request.prototype ? desc(win.Request.prototype, "url") : null;
    const xp = win.XMLHttpRequest && win.XMLHttpRequest.prototype;
    xhrRespURLGetter = xp ? desc(xp, "responseURL") : null;
    xhrRespTypeGetter = xp ? desc(xp, "responseType") : null;
    xhrRespTextGetter = xp ? desc(xp, "responseText") : null;
    xhrResponseGetter = xp ? desc(xp, "response") : null;
    arrPush = win.Array.prototype.push;
    arrShift = win.Array.prototype.shift;
    arrSlice = win.Array.prototype.slice;
    dateNow = win.Date.now;
    // Dùng exec (không dùng test): RegExp.prototype.test nội bộ GỌI this.exec — trang ghi đè exec thì test cũng
    // sai dù ta chụp test gốc. exec gốc gọi thẳng thì bỏ qua lối dispatch đó.
    reExec = win.RegExp.prototype.exec;
    ProxyC = win.Proxy;
    getPcRe = /^\/api\/v4\/pdp\/get_pc\/?$/i;
    digitsRe = /^\d+$/;
    itemIdRe = /[?&]item_id=(\d+)/i;
  } catch (_) {
    return;
  }

  const NULLPROTO = () => ({ __proto__: null });
  const now = () => A(dateNow, win.Date, []);
  // Chuyển về chuỗi qua String GỐC đã chụp (không dùng `String` toàn cục / template literal mà trang ghi đè được).
  const toStr = (v) => (typeof v === "string" ? v : A(Str, null, [v]));
  // Lấy getter trên một đối tượng của trang qua ReflectApply; lỗi (getter ném, không phải đúng loại) → fallback.
  const getVia = (getter, obj, fallback) => {
    if (!getter) return fallback;
    try { return A(getter, obj, []); } catch (_) { return fallback; }
  };

  // ── WeakMap che toString của Proxy + callback nội bộ ─────────────────────────────────────────────
  // masked: fn (proxy/callback có thể lọt vào tay trang) → hàm NATIVE dùng cho toString. Proxy fetch/XHR map về
  // chính hàm gốc của nó; callback nội bộ (then/onLoad/listener) không có "hàm gốc" nên map về một native trung
  // tính → toString ra "[native code]" thay vì lộ mã. nativeToString chụp dưới (Function.prototype.toString).
  const masked = new WM();
  let nativeToString = null; // gán ngay dưới; làm mốc native cho callback nội bộ
  const maskFn = (fn, real) => { A(wmSet, masked, [fn, real || nativeToString]); return fn; };

  const wrap = (orig, before) => {
    const handler = NULLPROTO();
    handler.apply = (target, self, args) => {
      let after = null;
      try { after = before(self, args); } catch (_) {}
      const ret = A(target, self, args);
      try { if (after) after(ret); } catch (_) {}
      return ret;
    };
    const proxy = new ProxyC(orig, handler);
    return maskFn(proxy, orig);
  };

  try {
    const FnProto = win.Function.prototype;
    nativeToString = FnProto.toString;
    const handler = NULLPROTO();
    handler.apply = (target, self, args) => {
      const real = A(wmHas, masked, [self]) ? A(wmGet, masked, [self]) : self;
      return A(target, real, args);
    };
    const toStringProxy = new ProxyC(nativeToString, handler);
    maskFn(toStringProxy, nativeToString);
    FnProto.toString = toStringProxy;
  } catch (_) {}

  // ── Luật nhận diện ─────────────────────────────────────────────────────────────────────────────
  const pathOf = (url) => {
    try { return A(pathnameGetter, new URLc(toStr(url), win.location.href), []); } catch (_) { return null; }
  };
  const isGetPcUrl = (url) => {
    if (!url) return false;
    const p = pathOf(url);
    return p != null && A(reExec, getPcRe, [p]) !== null;
  };
  // item_id lấy TRÊN CHUỖI query (không dùng URL.searchParams để không chạm API của trang mỗi request).
  const itemIdFromUrl = (url) => {
    const m = A(reExec, itemIdRe, [toStr(url)]);
    return m ? m[1] : null;
  };
  const asItemId = (id) =>
    (typeof id === "number" || typeof id === "string") && A(reExec, digitsRe, [toStr(id)]) !== null ? toStr(id) : null;

  // Thành công: JSON, `error` null/vắng/0, có data.item.item_id. Từ chối: JSON có `error` khác null và khác 0.
  const classify = (text, url) => {
    if (typeof text !== "string" || !text) return null;
    let parsed;
    try { parsed = A(jsonParse, win.JSON, [text]); } catch (_) { return null; }
    if (!parsed || typeof parsed !== "object") return null;
    const bodyItemId = asItemId(parsed.data?.item?.item_id);
    if (parsed.error !== null && parsed.error !== undefined && parsed.error !== 0) {
      const itemId = bodyItemId || itemIdFromUrl(url);
      return itemId ? { itemId, error: toStr(parsed.error) } : null;
    }
    return bodyItemId ? { itemId: bodyItemId, body: text } : null;
  };

  // ── Gửi sang world ISOLATED (có đệm để phát lại) ──────────────────────────────────────────────
  let seen = 0;
  let dropped = 0;
  const buffer = []; // chuỗi detail của các tin pdp/error gần nhất

  const send = (detail) => {
    try {
      const init = NULLPROTO();
      init.detail = detail;
      A(dispatch, doc, [new CE(PDP_EVENT, init)]);
    } catch (_) {}
  };
  const sendStatus = (type) => {
    const m = NULLPROTO();
    m.type = type; m.seen = seen; m.dropped = dropped;
    send(A(jsonStringify, win.JSON, [m]));
  };

  const report = (url, text, via) => {
    try {
      seen++;
      const c = classify(text, url);
      if (!c) { dropped++; sendStatus("drop"); return; }
      const msg = NULLPROTO();
      msg.itemId = c.itemId; msg.url = url ? toStr(url) : ""; msg.at = now(); msg.via = via; msg.seen = seen; msg.dropped = dropped;
      if (c.body !== undefined) { msg.type = "pdp"; msg.body = c.body; }
      else { msg.type = "error"; msg.error = c.error; }
      A(arrPush, buffer, [A(jsonStringify, win.JSON, [msg])]);
      if (buffer.length > BUFFER_MAX) A(arrShift, buffer, []);
      send(buffer[buffer.length - 1]);
    } catch (_) {}
  };

  try {
    const onReq = maskFn(() => {
      try {
        const snapshot = A(arrSlice, buffer, []);
        for (let i = 0; i < snapshot.length; i++) send(snapshot[i]);
        sendStatus("ready");
      } catch (_) {}
    });
    const opts = NULLPROTO();
    A(addEL, doc, [REQ_EVENT, onReq, opts]);
  } catch (_) {}

  // ── fetch ──────────────────────────────────────────────────────────────────────────────────────
  try {
    const origFetch = win.fetch;
    if (typeof origFetch === "function") {
      win.fetch = wrap(origFetch, (_self, args) => {
        const input = args[0];
        // input: chuỗi, hoặc Request (lấy .url qua getter đã chụp, không chạm getter của trang), hoặc khác → toStr.
        const url = typeof input === "string" ? input
          : (reqUrlGetter ? getVia(reqUrlGetter, input, null) : null) || toStr(input);
        if (!isGetPcUrl(url)) return null;
        return (pending) => {
          if (!pending || typeof pending.then !== "function") return;
          // Nhánh phụ: lỗi mạng (reject) có handler thứ hai nên không thành unhandled rejection;
          // trang vẫn nhận ĐÚNG promise gốc. Mọi callback đưa vào masked để toString không lộ mã.
          const onResp = maskFn((resp) => {
            try {
              const copy = A(respClone, resp, []);
              const textPromise = A(respText, copy, []);
              const respUrl = getVia(respUrlGetter, resp, "") || url;
              A(promiseThen, textPromise, [maskFn((text) => report(respUrl, text, "fetch")), maskFn(() => {})]);
            } catch (_) {}
          });
          A(promiseThen, pending, [onResp, maskFn(() => {})]);
        };
      });
    }
  } catch (_) {}

  // ── XMLHttpRequest ─────────────────────────────────────────────────────────────────────────────
  try {
    const XHR = win.XMLHttpRequest;
    const proto = XHR && XHR.prototype;
    if (proto && typeof proto.open === "function" && typeof proto.send === "function") {
      const openedUrl = new WM(); // URL theo từng xhr — không gắn thuộc tính lên đối tượng của trang

      const onLoad = maskFn(function () {
        try {
          // responseURL/responseType/responseText/response đọc qua getter đã chụp — không chạm getter của trang.
          const url = getVia(xhrRespURLGetter, this, "") || A(wmGet, openedUrl, [this]) || "";
          if (!isGetPcUrl(url)) return;
          const type = getVia(xhrRespTypeGetter, this, "");
          if (type === "" || type === "text") {
            report(url, getVia(xhrRespTextGetter, this, ""), "xhr");
          } else if (type === "json") {
            const r = getVia(xhrResponseGetter, this, null);
            if (r != null) report(url, A(jsonStringify, win.JSON, [r]), "xhr-json");
          }
        } catch (_) {}
      });

      proto.open = wrap(proto.open, (self, args) => {
        A(wmSet, openedUrl, [self, toStr(args[1])]);
        return null;
      });

      proto.send = wrap(proto.send, (self) => {
        if (isGetPcUrl(A(wmGet, openedUrl, [self]))) {
          const opts = NULLPROTO();
          opts.once = true;
          A(addEL, self, ["load", onLoad, opts]);
        }
        return null;
      });
    }
  } catch (_) {}

  sendStatus("ready"); // store đã nghe sẵn thì biết hook đã gắn; chưa thì store sẽ hỏi lại bằng REQ_EVENT
})();

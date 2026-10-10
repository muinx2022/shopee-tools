using Xunit;

// ⚠ TẮT CHẠY SONG SONG cho cả assembly này (vòng 6, 22/08/2026).
//
// Bộ test ở đây điều khiển `OrdersModuleHost` — một lớp `static` giữ TRẠNG THÁI TOÀN CỤC của worker hồ sơ KM:
// `_kmTimer`, `_kmDangThoat`, `_kmDirty`, `_kmLastPull`, `_daBaoTrongNgay`. Mặc định xUnit chạy các LỚP test
// song song, nên `DungNhipHoSoKm()` ở phần dọn dẹp của lớp này bật `_kmDangThoat` đúng lúc lớp kia vừa gọi
// `KmSyncTickAsync` ⇒ nhịp đó quay đầu ở cửa đầu tiên và phép đo của lớp kia đo nhầm một nhịp KHÔNG LÀM GÌ —
// xanh hay đỏ tuỳ thứ tự chạy. Đây không phải rủi ro lý thuyết: từ vòng 6 có test đi qua ĐÚNG nhịp thật để
// khoá chỗ nối "nhịp đầu tiên thấy hub thì backfill máy chủ trì".
//
// Cách khác là gom các lớp ấy vào một `[Collection]`, nhưng assembly này chỉ có vài chục test và mọi lớp Km*
// đều đụng cùng đống static — gom hết cũng bằng tắt hẳn, mà tắt hẳn thì lớp thêm sau không thể quên dán nhãn.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

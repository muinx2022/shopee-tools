using Shopee.Modules.MultiBrave;

namespace OpenMultiBraveLauncherV3;

/// <summary>
/// Các điểm khác nhau giữa extension <c>shopee-scrape</c> và <c>shopee-harvest</c> mà engine phải biết: thư mục nạp,
/// tên trong manifest, cờ + biểu thức probe "SW đã nạp hook". Bản <see cref="Scrape"/> giữ NGUYÊN từng chữ các hằng cũ
/// (có test khẳng định) để app Scrape không đổi hành vi.
/// </summary>
internal sealed record RunnerModeProfile(
    RunnerMode Mode,
    string ExtensionFolder,
    string ExtensionDisplayName,
    string ProbeFlag,
    string ProbeExpression)
{
    // Yêu cầu CẢ hàm bước LẪN applyFormConfig: SW cũ (cache trong profile, thiếu hàm mới) sẽ trượt probe →
    // EnsureRunnerExtensionReadyAsync leo thang chrome.runtime.reload() dựng SW mới.
    public static readonly RunnerModeProfile Scrape = new(
        RunnerMode.Scrape,
        "shopee-scrape",
        "Shopee Data Runner",
        "hasScrapeStep",
        "({ hasScrapeStep: typeof globalThis.__launcherExecuteScrapeStep === 'function' && typeof globalThis.__launcherApplyFormConfig === 'function' })");

    // Cờ RIÊNG hasHarvestStep (extension harvest không trả hasScrapeStep) → hai bên không nhận nhầm SW của nhau.
    public static readonly RunnerModeProfile Harvest = new(
        RunnerMode.Harvest,
        "shopee-harvest",
        "Shopee Harvest Runner",
        "hasHarvestStep",
        "({ hasHarvestStep: typeof globalThis.__launcherExecuteHarvestStep === 'function' && typeof globalThis.__launcherApplyFormConfig === 'function' })");

    public static RunnerModeProfile For(RunnerMode mode) => mode == RunnerMode.Harvest ? Harvest : Scrape;

    /// <summary>Hồ sơ của chế độ process hiện hành (<see cref="MultiBraveRuntime.Mode"/>).</summary>
    public static RunnerModeProfile Current => For(MultiBraveRuntime.Mode);
}

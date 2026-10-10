using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Shopee.Core.Accounts;
using Shopee.Core.Ai;
using Shopee.Core.Browser;
using Shopee.Core.Infrastructure;
using Shopee.Core.Proxy;
using Shopee.Workspace.Services;

namespace Shopee.Workspace.Modules.Settings;

/// <summary>
/// Cài đặt cục bộ cho ShopeeWorkspace: Hiệu năng, AI (API key), Kho tài khoản.
/// Không có Hub — lưu vào AiConfigStore local.
/// </summary>
public sealed partial class LocalSettingsViewModel : ObservableObject
{
    // ---- Hiệu năng ----
    /// <summary>Thu phóng giao diện hiện tại (%).</summary>
    public string ZoomText => UiZoom.PercentText;
    public int CpuCoresMax => BraveFleet.CpuCores;
    public int RamGbMax => BraveFleet.TotalRamGb;
    public string MachineInfo => $"Máy: CPU {BraveFleet.CpuCores} nhân · RAM {BraveFleet.TotalRamGb} GB";
    public string ComputedMaxInfo =>
        $"Tối đa {BraveFleet.WindowsForBudget(UsableCpu, UsableRamGb)} cửa sổ Brave";

    [ObservableProperty] private int _usableCpu;
    [ObservableProperty] private int _usableRamGb;

    partial void OnUsableCpuChanged(int value) => OnPropertyChanged(nameof(ComputedMaxInfo));
    partial void OnUsableRamGbChanged(int value) => OnPropertyChanged(nameof(ComputedMaxInfo));

    [RelayCommand]
    private void ZoomIn() => UiZoom.Step(1);
    [RelayCommand]
    private void ZoomOut() => UiZoom.Step(-1);
    [RelayCommand]
    private void ZoomReset() => UiZoom.Reset();

    [RelayCommand]
    private void SavePerformance()
    {
        PerformanceSettingsStore.Shared.Save(new PerformanceSettings
        {
            UsableCpuCores = UsableCpu,
            UsableRamGb = UsableRamGb,
        });
        BraveFleet.MaxConcurrentWindows = BraveFleet.WindowsForBudget(UsableCpu, UsableRamGb);
        AiStatus = "";
        StoreStatus = ComputedMaxInfo + " — đã lưu.";
    }

    // ---- AI ----
    public string[] Providers { get; } = ["OpenAI", "Anthropic", "Gemini"];
    [ObservableProperty] private string _provider = "OpenAI";
    [ObservableProperty] private string _openAiApiKey = "";
    [ObservableProperty] private string _openAiModel = "";
    [ObservableProperty] private string _anthropicApiKey = "";
    [ObservableProperty] private string _anthropicModel = "";
    [ObservableProperty] private string _geminiApiKey = "";
    [ObservableProperty] private string _geminiModel = "";
    [ObservableProperty] private int _batchSize = 40;
    [ObservableProperty] private string _nameRewritePrompt = "";
    [ObservableProperty] private string _descriptionPrompt = "";
    [ObservableProperty] private string _aiStatus = "";

    public LocalSettingsViewModel()
    {
        var perf = PerformanceSettingsStore.Shared.Current;
        _usableCpu = perf.UsableCpuCores;
        _usableRamGb = perf.UsableRamGb;

        var cfg = AiConfigStore.Shared.Current;
        _provider = string.IsNullOrWhiteSpace(cfg.Provider) ? "OpenAI" : cfg.Provider;
        _openAiApiKey = cfg.OpenAiApiKey;
        _openAiModel = cfg.OpenAiModel;
        _anthropicApiKey = cfg.AnthropicApiKey;
        _anthropicModel = cfg.AnthropicModel;
        _geminiApiKey = cfg.GeminiApiKey;
        _geminiModel = cfg.GeminiModel;
        _batchSize = cfg.BatchSize > 0 ? cfg.BatchSize : 40;
        _nameRewritePrompt = cfg.NameRewritePrompt;
        _descriptionPrompt = cfg.DescriptionPrompt;

        UiZoom.Changed += () => OnPropertyChanged(nameof(ZoomText));
        RefreshStoreStatus();
    }

    [RelayCommand]
    private void SaveAiConfig()
    {
        var cfg = AiConfigStore.Shared.Current;
        cfg.Provider = string.IsNullOrWhiteSpace(Provider) ? "OpenAI" : Provider.Trim();
        cfg.OpenAiApiKey = OpenAiApiKey.Trim();
        cfg.OpenAiModel = string.IsNullOrWhiteSpace(OpenAiModel) ? "gpt-4.1-mini" : OpenAiModel.Trim();
        cfg.AnthropicApiKey = AnthropicApiKey.Trim();
        cfg.AnthropicModel = string.IsNullOrWhiteSpace(AnthropicModel) ? "claude-haiku-4-5" : AnthropicModel.Trim();
        cfg.GeminiApiKey = GeminiApiKey.Trim();
        cfg.GeminiModel = string.IsNullOrWhiteSpace(GeminiModel) ? "gemini-2.5-flash" : GeminiModel.Trim();
        cfg.BatchSize = BatchSize > 0 ? BatchSize : 40;
        cfg.NameRewritePrompt = NameRewritePrompt ?? "";
        cfg.DescriptionPrompt = DescriptionPrompt ?? "";
        AiConfigStore.Shared.Save(cfg);
        AiStatus = "Đã lưu cấu hình AI.";
    }

    // ---- Kho tài khoản ----
    /// <summary>Đường dẫn DB ShopeeBackend dùng chung (chỉ đọc).</summary>
    public string ShopeeDbPath => SuitePaths.ShopeeBackendDbPath;

    [ObservableProperty] private string _storeStatus = "";
    public string? LockStatus => AccountStore.Shared.ReadOnlyReason;

    public void RefreshStoreStatus()
    {
        var when = DateTime.Now.ToString("HH:mm");
        StoreStatus = $"Nạp {AccountStore.Shared.Accounts.Count} tk, {KiotProxyPoolStore.Shared.Count} proxy lúc {when}.";
        OnPropertyChanged(nameof(LockStatus));
    }

    [RelayCommand]
    private void ReloadStore()
    {
        AccountStore.Shared.Reload();
        KiotProxyPoolStore.Shared.Reload();
        RefreshStoreStatus();
    }
}

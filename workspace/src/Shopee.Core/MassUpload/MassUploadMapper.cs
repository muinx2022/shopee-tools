using System.Globalization;

namespace Shopee.Core.MassUpload;

/// <summary>
/// Hàm thuần: <see cref="ShopeeListing"/> → các dòng file Đăng Hàng Loạt (plan GĐ2 bước 2 + "Chốt thiết kế sau GĐ0").
/// Luật "cắt cho vừa, ghi log": mỗi lần cắt sinh một warning, SP vẫn đăng; chỉ <see cref="MassUploadSkip"/> khi không cứu
/// được (tên/mô tả quá ngắn, không còn model, ngành ngoài bản mẫu, ảnh chưa sẵn sàng…). Giới hạn ở
/// <see cref="MassUploadLimits"/>. Ảnh không tự dựng URL: đi qua resolver (id ảnh gốc → <see cref="ImageResolution"/>; bước
/// đệm MD5 ở GĐ2b quyết định URL) — luật Ready / Pending / Failed xem <see cref="Map(ShopeeListing, MassUploadDefaults, TemplateSchema, Func{string, ImageResolution}, AttributeDictionary?, MassUploadLimits?)"/>.
/// </summary>
public static class MassUploadMapper
{
    public const string ColCategory = "ps_category";
    public const string ColName = "ps_product_name";
    public const string ColDescription = "ps_product_description";
    public const string ColParentSku = "ps_sku_parent_short";
    public const string ColVariationGroup = "et_title_variation_integration_no";
    public const string ColTier1Name = "et_title_variation_1";
    public const string ColTier1Option = "et_title_option_for_variation_1";
    public const string ColTier1Image = "et_title_image_per_variation";
    public const string ColTier2Name = "et_title_variation_2";
    public const string ColTier2Option = "et_title_option_for_variation_2";
    public const string ColPrice = "ps_price";
    public const string ColStock = "ps_stock";
    public const string ColSizeChartImage = "et_title_size_chart";
    public const string ColCoverImage = "ps_item_cover_image";
    public const string ColImagePrefix = "ps_item_image_";
    public const string ColWeight = "ps_weight";
    public const string ColLength = "ps_length";
    public const string ColWidth = "ps_width";
    public const string ColHeight = "ps_height";

    /// <summary>Map với resolver kiểu cũ (URL hoặc null): null = ảnh chưa sẵn sàng (<see cref="ImageReadiness.Pending"/>).</summary>
    public static MassUploadMapResult Map(ShopeeListing listing, MassUploadDefaults defaults, TemplateSchema schema,
        Func<string, string?> resolveImageUrl, AttributeDictionary? dictionary = null, MassUploadLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(resolveImageUrl);
        return Map(listing, defaults, schema, id => ImageResolution.FromUrl(resolveImageUrl(id)), dictionary, limits);
    }

    /// <summary>Map một SP. NÉM <see cref="ArgumentException"/> khi cấu hình thiếu khoá ảnh bảng size (lỗi cấu hình của cả
    /// lượt, không phải của SP).
    /// <para>Ảnh qua <paramref name="resolveImage"/> (id ảnh gốc / khoá ảnh bảng size → <see cref="ImageResolution"/>):</para>
    /// <list type="bullet">
    /// <item>Ảnh SP đi theo thứ tự gốc, lấy tối đa <see cref="MassUploadLimits.MaxImages"/> ảnh Ready: ảnh Failed bị bỏ
    /// (warning, ảnh kế tiếp lên thay — kể cả làm bìa); gặp ảnh Pending trước khi đủ → Skip "ảnh chưa sẵn sàng"; không còn
    /// ảnh nào → Skip. Vì ảnh sau có thể lên thay, bước đệm phải nhận MỌI ảnh của SP.</item>
    /// <item>Ảnh lựa chọn: có ảnh Pending → Skip; có ảnh Failed → bỏ cả cột ảnh lựa chọn (all-or-none) + warning.</item>
    /// <item>Ảnh bảng size: Pending hoặc Failed → Skip (Failed = lỗi cấu hình ảnh bảng size).</item>
    /// </list>
    /// <para>Phân loại (GĐ8b): SP thuộc ngành cấp 1 có danh sách chuẩn (<paramref name="variationStandard"/>, mặc định
    /// <see cref="VariationStandard.Default"/>) thì tên nhóm / lựa chọn khớp chuẩn được ghi bằng MÃ; ghi chú "phân loại chuẩn: x/y" vào
    /// <see cref="MassUploadProduct.Notes"/>. Chỉ đổi chuỗi, không đổi thứ tự — dòng và ảnh theo lựa chọn giữ nguyên.</para></summary>
    public static MassUploadMapResult Map(ShopeeListing listing, MassUploadDefaults defaults, TemplateSchema schema,
        Func<string, ImageResolution> resolveImage, AttributeDictionary? dictionary = null, MassUploadLimits? limits = null,
        VariationStandard? variationStandard = null)
    {
        ArgumentNullException.ThrowIfNull(listing);
        ArgumentNullException.ThrowIfNull(defaults);
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(resolveImage);
        ValidateDefaults(defaults);
        dictionary ??= AttributeDictionary.Default;
        var lim = limits ?? MassUploadLimits.Default;
        var id = listing.ItemId;
        var warnings = new List<string>();

        var leaf = listing.LeafCategoryId;
        if (leaf <= 0) return new MassUploadSkip(id, "SP không có ngành hàng (item.categories rỗng)");
        if (schema.Kind == TemplateKind.Advanced && !schema.HasCategory(leaf))
            return new MassUploadSkip(id, $"ngành {leaf} không thuộc bản mẫu (ngành cấp 1 {schema.TopCategoryId})");

        // Tên (làm sạch + NFC TRƯỚC khi đo, để độ dài khớp đúng chữ writer ghi)
        var title = CollapseSpaces(MassUploadText.Clean(listing.Title));
        var name = CutAtWord(title, lim.MaxNameLength);
        if (name.Length < lim.MinNameLength) return new MassUploadSkip(id, $"tên SP dưới {lim.MinNameLength} ký tự: \"{name}\"");
        if (name != title) warnings.Add($"tên dài {title.Length} ký tự → cắt còn {name.Length}");

        // Mô tả
        var description = BuildDescription(listing, title, schema, dictionary, lim, warnings);
        if (description is null)
            return new MassUploadSkip(id, $"mô tả dưới {lim.MinDescriptionLength} ký tự kể cả khi thêm tên + thuộc tính");

        // Ảnh SP
        if (listing.Images.Count == 0) return new MassUploadSkip(id, "SP không có ảnh");

        // Phân loại + giá
        var variation = listing.HasVariations
            ? BuildVariations(listing, lim, warnings, out var skipReason)
            : BuildSingle(listing, lim, out skipReason);
        if (variation is null) return new MassUploadSkip(id, skipReason!);

        // Phân loại khớp danh sách chuẩn của ngành → ghi mã (GĐ8b). Đổi tại chỗ, sau luật cắt / chống trùng.
        var notes = new List<string>();
        var standard = variationStandard ?? VariationStandard.Default;
        if (variation.TierNames.Count > 0 && standard.Covers(listing.TopCategoryId))
        {
            var st = standard.Apply(listing.TopCategoryId, variation.TierNames, variation.OptionNames);
            notes.Add($"phân loại chuẩn: {st.Converted}/{st.Total} giá trị đổi sang mã");
        }

        // Ảnh qua resolver (mỗi khoá hỏi một lần).
        var cache = new Dictionary<string, ImageResolution>(StringComparer.Ordinal);
        ImageResolution Resolve(string key)
        {
            if (!cache.TryGetValue(key, out var r)) cache[key] = r = resolveImage(key);
            return r;
        }

        // Bảng size: phải Ready.
        var sizeChart = Resolve(defaults.SizeChartImageKey);
        if (sizeChart.State == ImageReadiness.Failed)
            return new MassUploadSkip(id, $"ảnh bảng size lỗi ({sizeChart.Reason}) — kiểm cấu hình ảnh bảng size");
        if (sizeChart.State != ImageReadiness.Ready) return new MassUploadSkip(id, $"ảnh chưa sẵn sàng: {defaults.SizeChartImageKey}");

        // Ảnh SP: Failed bỏ (ảnh sau lên thay), Pending trước khi đủ → Skip.
        var imageUrls = new List<string>();
        var taken = 0;
        foreach (var img in listing.Images)
        {
            if (imageUrls.Count == lim.MaxImages) break;
            taken++;
            var r = Resolve(img);
            if (r.State == ImageReadiness.Pending) return new MassUploadSkip(id, $"ảnh chưa sẵn sàng: {img}");
            if (r.State == ImageReadiness.Failed) warnings.Add($"ảnh {img} lỗi ({r.Reason}) → bỏ");
            else imageUrls.Add(r.Url!);
        }
        if (imageUrls.Count == 0) return new MassUploadSkip(id, "SP không còn ảnh dùng được (mọi ảnh đều lỗi)");
        if (taken < listing.Images.Count)
            warnings.Add($"ảnh SP {listing.Images.Count} → {lim.MaxImages} (bìa + {lim.MaxImages - 1})");

        // Ảnh lựa chọn: Pending → Skip; Failed → bỏ cả cột.
        List<string>? tier1Urls = null;
        if (variation.Tier1Images is { } tierImages)
        {
            var res = tierImages.Select(Resolve).ToList();
            var pending = tierImages.Where((_, i) => res[i].State == ImageReadiness.Pending).FirstOrDefault();
            if (pending is not null) return new MassUploadSkip(id, $"ảnh chưa sẵn sàng: {pending}");
            var failed = res.Count(r => r.State == ImageReadiness.Failed);
            if (failed == 0) tier1Urls = res.Select(r => r.Url!).ToList();
            else warnings.Add($"ảnh lựa chọn lỗi {failed}/{res.Count} → bỏ cột ảnh phân loại");
        }

        // Thuộc tính (chỉ bản nâng cao có cột + HiddenCatProps)
        var needsFix = new List<string>();
        var missing = new List<MissingAttributeValue>();
        var attributeCells = schema.Kind == TemplateKind.Advanced
            ? MapAttributes(listing, schema, dictionary, lim, warnings, needsFix, missing)
            : [];

        // Dòng
        var common = new Dictionary<string, MassUploadCell>(StringComparer.Ordinal)
        {
            [ColCategory] = MassUploadCell.Of(leaf),
            [ColName] = MassUploadCell.Of(name),
            [ColDescription] = MassUploadCell.Of(description),
            [ColParentSku] = MassUploadCell.Of(id.ToString(CultureInfo.InvariantCulture)),
            [ColStock] = MassUploadCell.Of(defaults.StockPerVariation),
            [ColSizeChartImage] = MassUploadCell.Of(sizeChart.Url!),
            [ColCoverImage] = MassUploadCell.Of(imageUrls[0]),
            [ColWeight] = MassUploadCell.Of(defaults.WeightGrams),
            [ColLength] = MassUploadCell.Of(defaults.LengthCm),
            [ColWidth] = MassUploadCell.Of(defaults.WidthCm),
            [ColHeight] = MassUploadCell.Of(defaults.HeightCm),
        };
        for (var i = 1; i < imageUrls.Count; i++) common[ColImagePrefix + i] = MassUploadCell.Of(imageUrls[i]);
        foreach (var (code, value) in attributeCells) common[code] = MassUploadCell.Of(value);

        var rows = new List<IReadOnlyDictionary<string, MassUploadCell>>();
        foreach (var r in variation.Rows)
        {
            var row = new Dictionary<string, MassUploadCell>(common, StringComparer.Ordinal)
            {
                [ColPrice] = MassUploadCell.Of(r.Price),
            };
            if (variation.TierNames.Count > 0)
            {
                row[ColVariationGroup] = MassUploadCell.Of(id.ToString(CultureInfo.InvariantCulture));
                row[ColTier1Name] = MassUploadCell.Of(variation.TierNames[0]);
                row[ColTier1Option] = MassUploadCell.Of(variation.OptionNames[0][r.Options[0]]);
                if (tier1Urls is { } ti) row[ColTier1Image] = MassUploadCell.Of(ti[r.Options[0]]);
                if (variation.TierNames.Count > 1)
                {
                    row[ColTier2Name] = MassUploadCell.Of(variation.TierNames[1]);
                    row[ColTier2Option] = MassUploadCell.Of(variation.OptionNames[1][r.Options[1]]);
                }
            }
            rows.Add(row);
        }

        return new MassUploadProduct(id, listing.TopCategoryId, leaf, rows, warnings, needsFix, missing, notes);
    }

    /// <summary>Giá trị mặc định ngoài khoảng Shopee nhận là lỗi cấu hình của cả lượt → NÉM.</summary>
    private static void ValidateDefaults(MassUploadDefaults d)
    {
        if (string.IsNullOrWhiteSpace(d.SizeChartImageKey))
            throw new ArgumentException("Chưa có ảnh bảng size (MassUploadDefaults.SizeChartImageKey)", nameof(d));
        if (d.WeightGrams <= 0)
            throw new ArgumentException($"Cân nặng mặc định phải > 0 g (đang {d.WeightGrams})", nameof(d));
        if (d.StockPerVariation is < 0 or > 999_999)
            throw new ArgumentException($"Tồn kho mỗi phân loại phải trong 0–999.999 (đang {d.StockPerVariation})", nameof(d));
        if (d.LengthCm < 0 || d.WidthCm < 0 || d.HeightCm < 0)
            throw new ArgumentException($"Kích thước không được âm ({d.LengthCm}×{d.WidthCm}×{d.HeightCm} cm)", nameof(d));
        if (d.ProductsPerFile < 1)
            throw new ArgumentException($"Số SP mỗi file phải ≥ 1 (đang {d.ProductsPerFile})", nameof(d));
    }

    // ── Mô tả ──────────────────────────────────────────────────────────────────────────────────────────────────────────

    private static string? BuildDescription(ShopeeListing listing, string title, TemplateSchema schema,
        AttributeDictionary dictionary, MassUploadLimits lim, List<string> warnings)
    {
        var desc = MassUploadText.Clean(listing.Description).Trim();
        if (desc.Length < lim.MinDescriptionLength)
        {
            var before = desc.Length;
            var extras = new List<string> { title };
            extras.AddRange(listing.Attributes
                .Where(a => !string.IsNullOrWhiteSpace(a.Name) && !string.IsNullOrWhiteSpace(a.Value))
                .Select(a => MassUploadText.Clean($"{AttributeLabel(schema, a)}: {AttributeText(dictionary, a)}")));
            var sb = new StringBuilder(desc);
            foreach (var e in extras)
            {
                if (sb.Length >= lim.MinDescriptionLength) break;
                if (sb.Length > 0) sb.Append('\n');
                sb.Append(e);
            }
            desc = sb.ToString().Trim();
            if (desc.Length < lim.MinDescriptionLength) return null;
            warnings.Add($"mô tả {before} ký tự → thêm tên + thuộc tính → {desc.Length}");
        }
        if (desc.Length > lim.MaxDescriptionLength)
        {
            var before = desc.Length;
            var cut = desc[..lim.MaxDescriptionLength];
            var nl = cut.LastIndexOf('\n');
            var sp = cut.LastIndexOf(' ');
            cut = nl >= lim.MinDescriptionLength ? cut[..nl]
                : sp >= lim.MinDescriptionLength ? cut[..sp]
                : HardCut(desc, lim.MaxDescriptionLength);
            desc = cut.TrimEnd();
            warnings.Add($"mô tả {before} ký tự → cắt ở ranh giới đoạn còn {desc.Length}");
        }
        return desc;
    }

    /// <summary>Tên thuộc tính cho dòng bù mô tả: tên cột tiếng Việt (dòng 3 của bản mẫu) nếu bản mẫu có cột đó, không thì
    /// tên EN của JSON.</summary>
    private static string AttributeLabel(TemplateSchema schema, ListingAttribute a) =>
        schema.TryGetColumn(TemplateColumn.AttributePrefix + a.Id.ToString(CultureInfo.InvariantCulture), out var col)
        && !string.IsNullOrWhiteSpace(col.Title)
            ? col.Title
            : a.Name.Trim();

    /// <summary>Giá trị thuộc tính cho dòng bù mô tả: dịch qua từ điển (cả chuỗi, không thì từng phần ", "), phần chưa dịch
    /// giữ EN.</summary>
    private static string AttributeText(AttributeDictionary dictionary, ListingAttribute a)
    {
        var whole = a.Value.Trim();
        if (dictionary.TryTranslate(a.Id, whole, out var all)) return all;
        return string.Join(", ", SplitParts(whole).Select(p => dictionary.TryTranslate(a.Id, p, out var vn) ? vn : p));
    }

    // ── Phân loại ──────────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>Kế hoạch phân loại đã cắt. <see cref="Rows"/>: chỉ số lựa chọn (đã đánh lại) theo từng nhóm + giá.
    /// <see cref="Tier1Images"/>: id ảnh gốc theo lựa chọn nhóm 1, null = không ghi cột ảnh phân loại.</summary>
    private sealed record VariationPlan(
        List<string> TierNames, List<List<string>> OptionNames, List<string>? Tier1Images,
        List<(int[] Options, long Price)> Rows);

    private static VariationPlan? BuildSingle(ShopeeListing listing, MassUploadLimits lim, out string? skipReason)
    {
        skipReason = null;
        if (listing.Models.Count == 0) { skipReason = "SP không có model"; return null; }
        var price = listing.Models[0].Price;
        if (price < lim.MinPrice || price > lim.MaxPrice)
        {
            skipReason = $"giá {price} ngoài khoảng {lim.MinPrice}–{lim.MaxPrice}";
            return null;
        }
        return new VariationPlan([], [], null, [([], price)]);
    }

    private static VariationPlan? BuildVariations(ShopeeListing listing, MassUploadLimits lim, List<string> warnings,
        out string? skipReason)
    {
        skipReason = null;
        var tiers = listing.Tiers;
        if (tiers.Count > 2)
        {
            skipReason = $"SP có {tiers.Count} nhóm phân loại (Shopee nhận tối đa 2)";
            return null;
        }

        // 1. Số lựa chọn: mỗi nhóm ≤ MaxOptionsPerTier; 2 nhóm: bớt cuối nhóm 2 rồi nhóm 1 tới khi tích ≤ trần.
        var keep = tiers.Select(t => Enumerable.Range(0, t.Options.Count).ToList()).ToList();
        for (var k = 0; k < keep.Count; k++)
            if (keep[k].Count > lim.MaxOptionsPerTier)
            {
                warnings.Add($"nhóm {k + 1} \"{tiers[k].Name}\": {keep[k].Count} lựa chọn → {lim.MaxOptionsPerTier}");
                keep[k] = keep[k].Take(lim.MaxOptionsPerTier).ToList();
            }
        if (keep.Count == 2 && keep[0].Count * keep[1].Count > lim.MaxCombinationsTwoTiers)
        {
            var (a, b) = (keep[0].Count, keep[1].Count);
            while (keep[0].Count * keep[1].Count > lim.MaxCombinationsTwoTiers)
            {
                var k = keep[1].Count > 1 ? 1 : 0;
                keep[k].RemoveAt(keep[k].Count - 1);
            }
            warnings.Add($"2 nhóm {a}×{b} = {a * b} tổ hợp → {keep[0].Count}×{keep[1].Count}");
        }

        // 2. Model: bỏ model trỏ lựa chọn đã bỏ / chỉ số hỏng / trùng tổ hợp; bỏ giá ngoài khoảng.
        var keepSets = keep.Select(k => k.ToHashSet()).ToList();
        var selected = new List<(int[] Tier, long Price)>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        int droppedByCut = 0, droppedByPrice = 0, droppedDuplicate = 0;
        foreach (var m in listing.Models)
        {
            var ti = m.TierIndex.ToArray();
            if (ti.Length != tiers.Count || ti.Where((x, k) => !keepSets[k].Contains(x)).Any())
            {
                droppedByCut++;
                continue;
            }
            if (!seen.Add(string.Join(",", ti)))
            {
                droppedDuplicate++;
                continue;
            }
            if (m.Price < lim.MinPrice || m.Price > lim.MaxPrice)
            {
                droppedByPrice++;
                continue;
            }
            selected.Add((ti, m.Price));
        }
        if (droppedByCut > 0) warnings.Add($"bỏ {droppedByCut} model thuộc lựa chọn đã cắt");
        if (droppedDuplicate > 0) warnings.Add($"bỏ {droppedDuplicate} model trùng tổ hợp phân loại (giữ model đầu)");
        if (droppedByPrice > 0) warnings.Add($"bỏ {droppedByPrice} model giá ngoài khoảng {lim.MinPrice}–{lim.MaxPrice}");

        // 3. Tỉ lệ đắt / rẻ: giữ cụm lớn nhất [p, p × trần] (hoà thì cụm rẻ hơn).
        if (selected.Count > 0)
        {
            var prices = selected.Select(s => s.Price).OrderBy(p => p).ToList();
            if (prices[^1] > prices[0] * lim.MaxPriceRatio)
            {
                long bestLo = prices[0];
                var bestCount = 0;
                foreach (var lo in prices.Distinct())
                {
                    var n = prices.Count(p => p >= lo && p <= lo * lim.MaxPriceRatio);
                    if (n > bestCount) (bestCount, bestLo) = (n, lo);
                }
                var before = selected.Count;
                selected = selected.Where(s => s.Price >= bestLo && s.Price <= bestLo * lim.MaxPriceRatio).ToList();
                warnings.Add($"giá đắt/rẻ {prices[^1]}/{prices[0]} > {lim.MaxPriceRatio}: giữ cụm {bestLo}–{bestLo * lim.MaxPriceRatio}, bỏ {before - selected.Count} model");
            }
        }
        if (selected.Count == 0)
        {
            skipReason = "không còn model hợp lệ sau khi lọc phân loại / giá";
            return null;
        }

        // 4. Lựa chọn không còn model thì bỏ.
        for (var k = 0; k < keep.Count; k++)
        {
            var used = selected.Select(s => s.Tier[k]).ToHashSet();
            var removed = keep[k].Where(x => !used.Contains(x)).ToList();
            if (removed.Count > 0)
            {
                warnings.Add($"nhóm {k + 1}: bỏ {removed.Count} lựa chọn không còn model ("
                             + string.Join(", ", removed.Select(x => tiers[k].Options[x])) + ")");
                keep[k] = keep[k].Where(used.Contains).ToList();
            }
        }

        // 4b. 2 nhóm: ma trận phải ĐỦ — mỗi tổ hợp còn lại đều có model (Shopee không nhận tổ hợp trống).
        if (keep.Count == 2 && !CompleteMatrix(tiers, keep, selected, warnings))
        {
            skipReason = "không dựng được ma trận 2 nhóm phân loại đủ tổ hợp";
            return null;
        }
        if (keep.Count == 2)
        {
            var sets = keep.Select(k => k.ToHashSet()).ToList();
            selected = selected.Where(s => sets[0].Contains(s.Tier[0]) && sets[1].Contains(s.Tier[1])).ToList();
        }

        // Đánh lại chỉ số theo lựa chọn còn lại (giữ thứ tự gốc → giá / ảnh / tên đi đúng chỉ số).
        var remap = keep.Select(k => k.Select((orig, idx) => (orig, idx)).ToDictionary(x => x.orig, x => x.idx)).ToList();

        // 5. Tên nhóm ≤ 14, lựa chọn ≤ 20, trùng sau cắt thêm hậu tố.
        var tierNames = UniqueCut(tiers.Select(t => t.Name), lim.MaxTierNameLength, "Phân loại", warnings, "tên nhóm");
        var optionNames = keep.Select((idxs, k) =>
                UniqueCut(idxs.Select(x => tiers[k].Options[x]), lim.MaxOptionLength, "-", warnings, $"lựa chọn nhóm {k + 1}"))
            .ToList();

        // 6. Ảnh lựa chọn (chỉ nhóm 1): đủ cho mọi lựa chọn còn lại mới ghi.
        var images = keep[0].Select(x => tiers[0].Images[x]).ToList();
        List<string>? tier1Images = null;
        if (images.All(x => !string.IsNullOrWhiteSpace(x))) tier1Images = images.Select(x => x!).ToList();
        else warnings.Add($"ảnh lựa chọn thiếu {images.Count(string.IsNullOrWhiteSpace)}/{images.Count} → bỏ cột ảnh phân loại");

        var rows = selected
            .Select(s => (Options: s.Tier.Select((x, k) => remap[k][x]).ToArray(), s.Price))
            .OrderBy(r => r.Options.Length > 0 ? r.Options[0] : 0).ThenBy(r => r.Options.Length > 1 ? r.Options[1] : 0)
            .ToList();
        return new VariationPlan(tierNames, optionNames, tier1Images, rows);
    }

    /// <summary>
    /// Bỏ lựa chọn cho tới khi mọi tổ hợp (nhóm 1 × nhóm 2) còn lại đều có model. Thử bỏ ở nhóm 2 trước: mỗi lần bỏ lựa
    /// chọn thiếu nhiều ô nhất (hoà thì cái cuối). Bỏ nhóm 2 về 0 thì quay lại, làm tương tự với nhóm 1. Sửa
    /// <paramref name="keep"/> tại chỗ; false = không còn ma trận nào (SP bị bỏ).
    /// </summary>
    private static bool CompleteMatrix(IReadOnlyList<ListingTier> tiers, List<List<int>> keep,
        List<(int[] Tier, long Price)> selected, List<string> warnings)
    {
        var present = selected.Select(s => (s.Tier[0], s.Tier[1])).ToHashSet();
        foreach (var drop in new[] { 1, 0 })
        {
            var other = 1 - drop;
            var trial = keep[drop].ToList();
            var removed = new List<(int Option, int Missing)>();
            while (trial.Count > 0)
            {
                int best = -1, bestMissing = 0;
                foreach (var x in trial)
                {
                    var missing = keep[other].Count(y => !present.Contains(drop == 1 ? (y, x) : (x, y)));
                    if (missing > 0 && missing >= bestMissing) (best, bestMissing) = (x, missing); // hoà → cái sau
                }
                if (best < 0) break;
                trial.Remove(best);
                removed.Add((best, bestMissing));
            }
            if (trial.Count == 0) continue; // nhóm này về 0 → thử nhóm kia
            keep[drop] = trial;
            foreach (var (option, missing) in removed)
                warnings.Add($"nhóm {drop + 1}: bỏ lựa chọn \"{tiers[drop].Options[option]}\" vì thiếu {missing} tổ hợp có model");
            return true;
        }
        return false;
    }

    // ── Thuộc tính ─────────────────────────────────────────────────────────────────────────────────────────────────────

    private static Dictionary<string, string> MapAttributes(ShopeeListing listing, TemplateSchema schema,
        AttributeDictionary dictionary, MassUploadLimits lim, List<string> warnings, List<string> needsFix,
        List<MissingAttributeValue> missing)
    {
        var cells = new Dictionary<string, string>(StringComparer.Ordinal);
        var leaf = listing.LeafCategoryId;
        foreach (var col in schema.Columns)
        {
            if (col.AttributeId is not long attrId) continue;
            var req = schema.GetRequirement(leaf, col.Code);
            if (req is not (AttributeRequirement.Mandatory or AttributeRequirement.Optional)) continue;
            var mandatory = req == AttributeRequirement.Mandatory;
            var label = $"{attrId} \"{col.Title}\"";

            var src = listing.Attributes.FirstOrDefault(a => a.Id == attrId && !string.IsNullOrWhiteSpace(a.Value));
            if (src is null)
            {
                if (mandatory) needsFix.Add($"thiếu thuộc tính bắt buộc {label}: JSON không có");
                continue;
            }

            var allowed = schema.GetListValues(leaf, col.Code);
            var isList = allowed.Count > 0 || col.InputKind == AttributeInputKind.List;
            var maxValues = !isList && col.MultiValue ? lim.MaxAttributeValues : 1;
            var whole = MassUploadText.Clean(src.Value).Trim();

            // Giá trị của mẫu cho ngành lá này: danh sách hợp lệ (HiddenAttr), không có thì danh sách của sheet "Ánh xạ giá trị thuộc
            // tính" (hợp lệ / đề xuất). Raw (đã là tiếng Việt khi cào bằng Shopee tiếng Việt) khớp THẲNG một giá trị → ghi đúng chuỗi
            // của mẫu, trước mọi bước dịch. Chỉ khớp đúng (bỏ hoa thường, NFC, khoảng trắng thừa) — không khớp gần đúng.
            var candidates = allowed.Count > 0 ? allowed : schema.GetMappedValues(leaf, col.Code);
            string? Direct(string part)
            {
                var key = AttributeKey(part);
                return key.Length == 0 ? null : candidates.FirstOrDefault(v => AttributeKey(v) == key);
            }

            // Kiểu danh sách: chỉ nhận giá trị có trong HiddenAttr (bản dịch, hoặc chính chữ EN nếu Shopee dùng y vậy).
            // Tùy chỉnh: bản dịch từ điển.
            string? Resolve(string part)
            {
                if (Direct(part) is { } exact) return exact;
                var translated = dictionary.TryTranslate(attrId, part, out var vn) ? MassUploadText.Clean(vn) : null;
                if (!isList) return translated;
                return allowed.FirstOrDefault(v => translated is not null && string.Equals(v, translated, StringComparison.OrdinalIgnoreCase))
                       ?? allowed.FirstOrDefault(v => string.Equals(v, part, StringComparison.OrdinalIgnoreCase));
            }

            // Cả chuỗi giải được → 1 giá trị. Không thì tách ", " — nhưng CHỈ dùng các phần khi MỌI phần đều giải được
            // (dấu phẩy có thể là một phần của giá trị: địa chỉ "Số 10, Đường ABC, Hà Nội").
            var values = new List<string>();
            var unresolved = new List<string>();
            if (Resolve(whole) is { } single) values.Add(single);
            else
            {
                var parts = SplitParts(whole);
                var resolved = parts.Select(Resolve).ToList();
                if (parts.Count > 1 && resolved.All(r => r is not null)) values.AddRange(resolved.Select(r => r!));
                else
                {
                    unresolved.AddRange(parts.Where((_, i) => resolved[i] is null));
                    // Có phần khớp THẲNG danh sách của mẫu ("voan, Ren" → "voan") → giữ phần khớp được, bỏ phần còn lại. Phần chỉ dịch
                    // được qua từ điển không tính (thuộc tính tùy chỉnh chữ EN giữ luật cũ).
                    var partlyMatched = parts.Any(p => Direct(p) is not null);
                    if (isList || partlyMatched)
                    {
                        // Danh sách: ưu tiên phần khớp được ("Hong Kong, China" → "Trung Quốc").
                        values.AddRange(resolved.Where(r => r is not null).Select(r => r!));
                        warnings.Add($"thuộc tính {label}: bỏ phần chưa có trong từ điển/danh sách: {string.Join(", ", unresolved)}");
                    }
                    else if (src.ValId == 0 || mandatory)
                    {
                        // Tùy chỉnh: chữ người bán tự gõ (val_id 0), hoặc thuộc tính bắt buộc → ghi NGUYÊN cả chuỗi.
                        values.Add(whole);
                        warnings.Add(src.ValId == 0
                            ? $"thuộc tính {label}: giá trị tự gõ chưa dịch hết, ghi nguyên cả chuỗi \"{whole}\""
                            : $"thuộc tính bắt buộc {label}: chưa dịch hết, ghi nguyên cả chuỗi EN \"{whole}\"");
                    }
                    else
                        warnings.Add($"thuộc tính {label}: bỏ giá trị chưa có trong từ điển: \"{whole}\"");
                    // Chữ người bán tự gõ ở cột tùy chỉnh không cần từ điển; còn lại ghi để bổ sung.
                    if (isList || src.ValId != 0)
                        foreach (var u in unresolved) missing.Add(new MissingAttributeValue(attrId, src.Name, u));
                }
            }

            // ";" trong một giá trị đổi thành "," để Shopee không hiểu thành nhiều giá trị.
            values = values.Select(v => v.Replace(';', ',').Trim()).Where(v => v.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (values.Count > maxValues)
            {
                warnings.Add($"thuộc tính {label}: {values.Count} giá trị → {maxValues}");
                values = values.Take(maxValues).ToList();
            }
            if (values.Count == 0)
            {
                if (mandatory)
                    needsFix.Add($"thuộc tính bắt buộc {label}: giá trị \"{src.Value}\" chưa có trong từ điển/danh sách");
                continue;
            }
            cells[col.Code] = string.Join(";", values);
        }
        return cells;
    }

    /// <summary>Khoá so giá trị thuộc tính với danh sách của mẫu: làm sạch + NFC (<see cref="MassUploadText.Clean"/>), gộp khoảng
    /// trắng, trim, chữ thường.</summary>
    private static string AttributeKey(string value) => CollapseSpaces(MassUploadText.Clean(value)).Trim().ToLowerInvariant();

    /// <summary>Tách giá trị nhiều phần ngăn "," ("Casual, Elegant").</summary>
    private static List<string> SplitParts(string value) =>
        value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    // ── Chuỗi ──────────────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Cắt tên còn ≤ <paramref name="max"/> (đã làm sạch + NFC) rồi chống trùng (không phân biệt hoa thường). Lần xuất
    /// hiện ĐẦU của một tên giữ nguyên; các lần sau thêm hậu tố " 2", " 3"… chọn sao cho KHÔNG đụng bất kỳ tên đã cắt nào
    /// trong nhóm (["A","A","A 2"] → "A", "A 3", "A 2").
    /// </summary>
    private static List<string> UniqueCut(IEnumerable<string> names, int max, string emptyName, List<string> warnings,
        string label)
    {
        var originals = names.ToList();
        var cut = originals.Select(o =>
        {
            var c = HardCut(CollapseSpaces(MassUploadText.Clean(o)), max);
            return c.Length == 0 ? HardCut(emptyName, max) : c;
        }).ToList();
        var reserved = new HashSet<string>(cut, StringComparer.OrdinalIgnoreCase);
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();
        for (var i = 0; i < cut.Count; i++)
        {
            var c = cut[i];
            if (!used.Add(c))
            {
                for (var k = 2; ; k++)
                {
                    var suffix = " " + k.ToString(CultureInfo.InvariantCulture);
                    var candidate = HardCut(cut[i], max - suffix.Length) + suffix;
                    if (reserved.Contains(candidate) || used.Contains(candidate)) continue;
                    c = candidate;
                    break;
                }
                used.Add(c);
            }
            if (c != CollapseSpaces(MassUploadText.Clean(originals[i]))) warnings.Add($"{label}: \"{originals[i]}\" → \"{c}\"");
            result.Add(c);
        }
        return result;
    }

    /// <summary>Cắt còn ≤ <paramref name="max"/> ở ranh giới từ (khoảng trắng cuối cùng nếu không quá gần đầu).</summary>
    internal static string CutAtWord(string s, int max)
    {
        if (s.Length <= max) return s;
        var cut = HardCut(s, max);
        var sp = cut.LastIndexOf(' ');
        if (sp >= max * 6 / 10) cut = cut[..sp];
        return cut.TrimEnd();
    }

    /// <summary>Cắt cứng còn ≤ <paramref name="max"/> đơn vị UTF-16, không tách đôi cặp surrogate (emoji).</summary>
    internal static string HardCut(string s, int max)
    {
        if (max <= 0) return "";
        if (s.Length <= max) return s.Trim();
        var n = max;
        if (char.IsHighSurrogate(s[n - 1])) n--;
        return s[..n].Trim();
    }

    private static string CollapseSpaces(string s) =>
        string.Join(' ', (s ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
